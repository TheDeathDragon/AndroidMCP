using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private const int HealthIntervalMs = 50;
    private static readonly string AgentLocalPath = Path.Combine(
        AppContext.BaseDirectory, "tools", "agent-server.jar");

    private readonly AdbHub adbHub;
    private readonly string transportId;
    private int localPort;
    private volatile HttpClient http;
    private readonly object startGate = new();
    private volatile bool started;
    private int remotePort;
    private bool weLaunched;
    private int launchGeneration;

    public string TransportId => transportId;
    public int Port => localPort;
    public int RemotePort => remotePort;
    public bool ReusingExistingAgent => started && !weLaunched;
    public bool IsStarted => started;
    public HttpClient Http => http;
    public bool InputEndpointsMissing { get; set; }
    // -1 unknown, 0 no, 1 yes. Agents without "swipe-easing" in /info's features silently
    // ignore easing=linear and ease. Only a definitive /info answer is cached: a transient
    // failure must not pin every later swipe of this agent to the slower shell path.
    private int linearSwipe = -1;

    public bool SupportsLinearSwipe()
    {
        int known = Volatile.Read(ref linearSwipe);
        if (known >= 0)
        {
            return known == 1;
        }
        try
        {
            using CancellationTokenSource cts = new(2000);
            string info = http.GetStringAsync("/info", cts.Token).GetAwaiter().GetResult();
            bool yes = System.Text.Json.Nodes.JsonNode.Parse(info)?["features"] is System.Text.Json.Nodes.JsonArray features
                && features.Any(f => f?.GetValueKind() == System.Text.Json.JsonValueKind.String && (string?)f == "swipe-easing");
            Volatile.Write(ref linearSwipe, yes ? 1 : 0);
            return yes;
        }
        catch (Exception ex)
        {
            Log.Debug($"swipe capability probe failed on transport {transportId}: {ex.Message}");
            return false;
        }
    }

    public AgentSession(AdbHub adbHub, string transportId)
    {
        this.adbHub = adbHub;
        this.transportId = transportId;
        http = CreateHttpClient(0);
    }

    // Idle timeout stays under the agent's 15 s keep-alive so the client never reuses a
    // socket the agent is closing, yet spans the usual gap between an LLM's tool calls. Agents older than keep-alive answer
    // Connection: close, which the handler honours by not pooling. The connection cap
    // leaves room in the agent's 16 keep-alive sockets for other hosts sharing :9500.
    private static HttpClient CreateHttpClient(int port)
    {
        SocketsHttpHandler handler = new()
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 4
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    // Pooled sockets ride the old adb forward and the old agent process; after a
    // restart they would tunnel to a dead or different agent. The old client is disposed
    // only after the request timeout: other tool calls may still be awaiting it, and
    // cancelling a request whose gesture was already injected invites a retry that
    // performs it twice.
    private void ResetHttpClient()
    {
        HttpClient old = http;
        http = CreateHttpClient(localPort);
        RetireLater(old);
    }

    private static void RetireLater(HttpClient client) =>
        _ = Task.Delay(TimeSpan.FromSeconds(35)).ContinueWith(_ => client.Dispose(), TaskScheduler.Default);

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
            InputEndpointsMissing = false;
            return Start();
        }
    }

    private bool Start()
    {
        DeviceData device = adbHub.RequireDeviceByTransportId(transportId);
        AdbClient client = adbHub.Client;
        // `adb forward` is global to the adb server: a fixed host port is silently taken over
        // by whichever process (another android-mcp, AndroidTest, a CLI) forwards it last, and
        // requests then reach another phone. tcp:0 lets the adb server hand out a free port.
        RemoveForward();
        int? port = adbHub.CreateEphemeralForward(transportId, CanonicalAgentPort);
        if (port is null)
        {
            Log.Error($"adb forward failed on transport {transportId}");
            return false;
        }
        localPort = port.Value;
        ResetHttpClient();
        bool ok = TryReuseCanonicalAgent(client, device, out bool outdatedButHealthy)
            || LaunchOwnAgent(client, device)
            // Replacing an outdated agent failed before it was killed (no jar, push
            // failure, boot not completed): keep using it rather than fail every tool.
            || (outdatedButHealthy && CheckHealth() && MarkReused("outdated agent kept; replacement failed"));
        if (!ok)
        {
            RemoveForward();
            return false;
        }
        Volatile.Write(ref linearSwipe, -1);
        return true;
    }

    private bool MarkReused(string why)
    {
        remotePort = CanonicalAgentPort;
        weLaunched = false;
        started = true;
        Log.Info($"agent reused on transport {transportId}: forward {localPort} -> :{CanonicalAgentPort} ({why})");
        return true;
    }

    // UiAutomation is a per-device singleton. If another process already
    // serves the canonical port :9500, launching our own would deadlock on
    // UiAutomation.connect — detect it and forward to the existing one.
    private bool TryReuseCanonicalAgent(AdbClient client, DeviceData device, out bool outdatedButHealthy)
    {
        outdatedButHealthy = false;
        if (!CheckHealth())
        {
            return false;
        }
        // An outdated agent pushed by an older android-mcp (orphaned by a crash, or a second
        // install still running) would otherwise be reused forever, pinning input to the shell
        // fallback. Replace only our own jar; an agent another tool launched stays untouched.
        if (!HasInputEndpoints() && IsOwnAgentJar(client, device))
        {
            Log.Info($"replacing outdated android-mcp agent on transport {transportId}");
            outdatedButHealthy = true;
            return false;
        }
        return MarkReused("healthy");
    }

    // /input/tap without coordinates is a 400 on agents that have it and a 404 on older ones;
    // it injects nothing either way.
    private bool HasInputEndpoints()
    {
        try
        {
            using CancellationTokenSource cts = new(2000);
            using HttpResponseMessage resp = http.GetAsync("/input/tap", cts.Token).GetAwaiter().GetResult();
            return resp.StatusCode != System.Net.HttpStatusCode.NotFound;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsOwnAgentJar(AdbClient client, DeviceData device)
    {
        try
        {
            ConsoleOutputReceiver receiver = new();
            client.ExecuteRemoteCommand(
                $"for p in $(pgrep -f 'la.shiro.agent.Server --port {CanonicalAgentPort}'); do " +
                $"grep -q '{AgentRemotePath}' /proc/$p/environ 2>/dev/null && echo own; done",
                device, receiver, Encoding.UTF8);
            foreach (string line in receiver.ToString().Split('\n'))
            {
                if (line.Trim() == "own")
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
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
            if (!WaitForHealth())
            {
                return false;
            }

            long t4 = Stopwatch.GetTimestamp();
            remotePort = CanonicalAgentPort;
            weLaunched = true;
            started = true;
            Log.Info(
                $"agent launched on transport {transportId}: forward {localPort} -> :{CanonicalAgentPort} " +
                $"[push {Ms(t0, t1):0}ms kill {Ms(t1, t2):0}ms launch {Ms(t2, t3):0}ms " +
                $"health {Ms(t3, t4):0}ms]");
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
        // The shell stream ends when this agent process dies; a previous launch's stream can
        // end after a relaunch already succeeded, and must not mark the new agent dead.
        int generation = Interlocked.Increment(ref launchGeneration);
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
            if (Volatile.Read(ref launchGeneration) == generation)
            {
                started = false;
            }
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
        lock (startGate)
        {
            StopLocked();
        }
    }

    private void StopLocked()
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
        RemoveForward();
    }

    private void RemoveForward()
    {
        if (localPort == 0)
        {
            return;
        }
        try
        {
            adbHub.RemoveForwardByTransportId(transportId, localPort);
        }
        catch
        {
        }
        localPort = 0;
    }

    public void Dispose()
    {
        try
        { Stop(); }
        catch { }
        RetireLater(http);
    }
}
