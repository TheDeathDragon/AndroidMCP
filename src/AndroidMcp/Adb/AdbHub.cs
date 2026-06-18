using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AndroidMcp.Logging;

namespace AndroidMcp.Adb;

internal sealed class AdbHub
{
    private readonly object initGate = new();
    private AdbClient? client;
    private bool initialized;
    private string resolvedAdbPath = string.Empty;

    public AdbClient Client
    {
        get
        {
            EnsureInitialized();
            return client ?? throw new InvalidOperationException("ADB client not available");
        }
    }

    public string ResolvedAdbPath
    {
        get
        {
            EnsureInitialized();
            return resolvedAdbPath;
        }
    }

    public IReadOnlyList<DeviceData> ListDevices()
    {
        EnsureInitialized();
        if (client is null)
        {
            return Array.Empty<DeviceData>();
        }

        return client.GetDevices().ToList();
    }

    public DeviceData? FindDeviceBySerial(string serial)
    {
        EnsureInitialized();
        if (client is null)
        {
            return null;
        }

        foreach (DeviceData d in client.GetDevices())
        {
            if (string.Equals(d.Serial, serial, StringComparison.Ordinal))
            {
                return d;
            }
        }
        return null;
    }

    // Direct-path tools (shell / input / scroll / file transfer) resolve here.
    // Delegating to ResolveDevice gives them the same serial-or-transport-id
    // handling as the agent path, so duplicate serials are disambiguated (or
    // rejected with guidance) everywhere rather than silently hitting the first.
    public DeviceData RequireDevice(string handle) => ResolveDevice(handle);

    public DeviceData? FindDeviceByTransportId(string transportId)
    {
        EnsureInitialized();
        if (client is null)
        {
            return null;
        }

        foreach (DeviceData d in client.GetDevices())
        {
            if (string.Equals(d.TransportId, transportId, StringComparison.Ordinal))
            {
                return d;
            }
        }
        return null;
    }

    public DeviceData RequireDeviceByTransportId(string transportId)
    {
        DeviceData? d = FindDeviceByTransportId(transportId);
        if (d is null)
        {
            throw new InvalidOperationException($"device not connected (transport {transportId})");
        }

        return RequireOnline(d);
    }

    // Resolves a caller handle to exactly one device. The handle is a serial
    // when serials are unique; when two devices share a serial it is ambiguous
    // and the caller must pass the transport-id instead (list_devices surfaces
    // it). adb then addresses the device by host:transport-id, so duplicate
    // serials route correctly once resolved here.
    public DeviceData ResolveDevice(string handle)
    {
        EnsureInitialized();
        if (client is null)
        {
            throw new InvalidOperationException("ADB client not available");
        }

        List<DeviceData> all = client.GetDevices().ToList();
        List<DeviceData> bySerial = all.FindAll(d => string.Equals(d.Serial, handle, StringComparison.Ordinal));
        DeviceData? byTransport = all.Find(d => string.Equals(d.TransportId, handle, StringComparison.Ordinal));
        if (bySerial.Count == 1)
        {
            return RequireOnline(bySerial[0]);
        }
        if (bySerial.Count > 1)
        {
            if (byTransport is not null)
            {
                return RequireOnline(byTransport);
            }
            throw new InvalidOperationException(
                $"serial '{handle}' is ambiguous: {bySerial.Count} connected devices share it; " +
                "pass the transportId from list_devices instead of the serial");
        }
        if (byTransport is not null)
        {
            return RequireOnline(byTransport);
        }
        throw new InvalidOperationException($"device not connected: {handle}");
    }

    private static DeviceData RequireOnline(DeviceData d)
    {
        if (d.State != DeviceState.Online)
        {
            throw new InvalidOperationException($"device {d.Serial} state is {d.State}, expected Online");
        }
        return d;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        lock (initGate)
        {
            if (initialized)
            {
                return;
            }

            string adb = ResolveAdbPath();
            if (string.IsNullOrEmpty(adb))
            {
                throw new InvalidOperationException(
                    "adb executable not found in PATH; install Android platform-tools or set the ADB env var");
            }
            resolvedAdbPath = adb;
            try
            {
                AdbServer server = new();
                StartServerResult result = server.StartServer(adb, restartServerIfNewer: false);
                Log.Info($"adb server: {result} (path: {adb})");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"failed to start adb server via '{adb}': {ex.Message}", ex);
            }
            client = new AdbClient();
            initialized = true;
        }
    }

    private static string ResolveAdbPath()
    {
        string? envOverride = Environment.GetEnvironmentVariable("ADB");
        if (!string.IsNullOrEmpty(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }
        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb";
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return string.Empty;
        }

        char sep = Path.PathSeparator;
        foreach (string dir in pathVar.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }
        return string.Empty;
    }
}
