using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Logging;

namespace AndroidMcp.Adb;

internal sealed class AgentSession : IDisposable
{
    private const string AgentRemotePath = "/data/local/tmp/android-mcp-agent.jar";
    private const int CanonicalAgentPort = 9500;
    private const int HealthTimeoutMs = 10_000;
    private const int HealthIntervalMs = 200;
    private static readonly string AgentLocalPath = Path.Combine(
        AppContext.BaseDirectory, "tools", "agent-server.jar");

    private readonly AdbHub adbHub;
    private readonly string transportId;
    private readonly int localPort;
    private readonly HttpClient http;
    private readonly object startGate = new();
    private volatile bool started;
    private int remotePort;
    private bool weLaunched;

    public string TransportId => transportId;
    public int Port => localPort;
    public int RemotePort => remotePort;
    public bool ReusingExistingAgent => started && !weLaunched;
    public bool IsStarted => started;
    public HttpClient Http => http;

    public AgentSession(AdbHub adbHub, string transportId, int localPort)
    {
        this.adbHub = adbHub;
        this.transportId = transportId;
        this.localPort = localPort;
        // Pool retain-window races with the agent's Connection: close, surfacing
        // as "error sending request" on the first call after a quiet period.
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero
        };
        http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{localPort}"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public bool EnsureStarted()
    {
        if (started && CheckHealth())
        {
            return true;
        }

        lock (startGate)
        {
            if (started && CheckHealth())
            {
                return true;
            }

            started = false;
            return Start();
        }
    }

    private bool Start()
    {
        DeviceData device = adbHub.RequireDeviceByTransportId(transportId);
        AdbClient client = adbHub.Client;
        if (TryReuseCanonicalAgent(client, device))
        {
            return true;
        }

        return LaunchOwnAgent(client, device);
    }

    // UiAutomation is a per-device singleton. If another process already
    // serves the canonical port :9500, launching our own would deadlock on
    // UiAutomation.connect — detect it and forward to the existing one.
    private bool TryReuseCanonicalAgent(AdbClient client, DeviceData device)
    {
        if (!adbHub.CreateForwardByTransportId(transportId, localPort, CanonicalAgentPort))
        {
            Log.Debug($"forward probe failed on transport {transportId}");
            return false;
        }
        if (!CheckHealth())
        {
            adbHub.RemoveForwardByTransportId(transportId, localPort);
            return false;
        }
        remotePort = CanonicalAgentPort;
        weLaunched = false;
        started = true;
        Log.Info($"agent reused on transport {transportId}: forward {localPort} -> :{CanonicalAgentPort}");
        return true;
    }

    private bool LaunchOwnAgent(AdbClient client, DeviceData device)
    {
        if (!File.Exists(AgentLocalPath))
        {
            Log.Error($"agent jar missing: {AgentLocalPath}");
            return false;
        }
        try
        {
            if (!IsBootCompleted(client, device))
            {
                Log.Error($"device transport {transportId} is still booting (sys.boot_completed != 1); agent start refused");
                return false;
            }
            // Launch on the canonical port so this agent is reusable by other tools
            // (a second app_process would deadlock on the UiAutomation singleton);
            // the per-device host port still forwards to it.
            long t0 = Stopwatch.GetTimestamp();
            PushAgent(client, device);
            long t1 = Stopwatch.GetTimestamp();
            KillStaleOnPort(client, device, CanonicalAgentPort);
            long t2 = Stopwatch.GetTimestamp();
            Thread.Sleep(150);
            LaunchServerProcess(client, device, CanonicalAgentPort);
            long t3 = Stopwatch.GetTimestamp();
            adbHub.CreateForwardByTransportId(transportId, localPort, CanonicalAgentPort);
            long t4 = Stopwatch.GetTimestamp();
            if (!WaitForHealth())
            {
                return false;
            }

            long t5 = Stopwatch.GetTimestamp();
            remotePort = CanonicalAgentPort;
            weLaunched = true;
            started = true;
            Log.Info(
                $"agent launched on transport {transportId}: forward {localPort} -> :{CanonicalAgentPort} " +
                $"[push {Ms(t0, t1):0}ms kill {Ms(t1, t2):0}ms launch {Ms(t2, t3):0}ms " +
                $"forward {Ms(t3, t4):0}ms health {Ms(t4, t5):0}ms]");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"agent start failed on transport {transportId}: {ex.Message}");
            return false;
        }
    }

    private static double Ms(long a, long b) =>
        (b - a) * 1000.0 / Stopwatch.Frequency;

    private static bool IsBootCompleted(AdbClient client, DeviceData device)
    {
        try
        {
            ConsoleOutputReceiver receiver = new();
            client.ExecuteRemoteCommand("getprop sys.boot_completed", device, receiver, Encoding.UTF8);
            return receiver.ToString().Trim() == "1";
        }
        catch
        {
            return false;
        }
    }

    private static void PushAgent(AdbClient client, DeviceData device)
    {
        using SyncService sync = new(client, device);
        using FileStream src = new(AgentLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        bool isCancelled = false;
        sync.Push(src, AgentRemotePath, UnixFileStatus.DefaultFileMode, DateTimeOffset.Now,
            callback: null, useV2: false, isCancelled: in isCancelled);
    }

    private static void KillStaleOnPort(AdbClient client, DeviceData device, int port)
    {
        try
        {
            ConsoleOutputReceiver receiver = new();
            client.ExecuteRemoteCommand(
                $"pkill -9 -f \"la.shiro.agent.Server --port {port}\"",
                device, receiver, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void LaunchServerProcess(AdbClient client, DeviceData device, int port)
    {
        string command = $"CLASSPATH={AgentRemotePath} app_process / la.shiro.agent.Server --port {port}";
        Task.Run(() =>
        {
            try
            {
                ConsoleOutputReceiver receiver = new();
                client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
            }
            catch
            {
            }
            started = false;
        });
    }

    private bool WaitForHealth()
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(HealthTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (CheckHealth())
            {
                return true;
            }

            Thread.Sleep(HealthIntervalMs);
        }
        Log.Error($"agent health check timed out for transport {transportId}:{localPort}");
        return false;
    }

    public bool CheckHealth()
    {
        try
        {
            using CancellationTokenSource cts = new(2000);
            string body = http.GetStringAsync("/health", cts.Token).GetAwaiter().GetResult();
            return body == "ok";
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        bool launched = weLaunched;
        int rp = remotePort;
        started = false;
        if (launched)
        {
            try
            {
                using CancellationTokenSource cts = new(1000);
                http.GetStringAsync("/stop", cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
            }
            try
            {
                DeviceData? device = adbHub.FindDeviceByTransportId(transportId);
                if (device is not null)
                {
                    KillStaleOnPort(adbHub.Client, device, rp);
                }
            }
            catch
            {
            }
        }
        try
        {
            adbHub.RemoveForwardByTransportId(transportId, localPort);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        { Stop(); }
        catch { }
        http.Dispose();
    }
}
