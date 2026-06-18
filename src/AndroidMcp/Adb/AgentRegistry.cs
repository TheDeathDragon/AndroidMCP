using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AdvancedSharpAdbClient.Models;
using AndroidMcp.Logging;

namespace AndroidMcp.Adb;

internal sealed class AgentRegistry : IDisposable
{
    private readonly AdbHub adbHub;
    private readonly PortAllocator portAllocator;
    private readonly ConcurrentDictionary<string, AgentSession> sessions = new();

    public AgentRegistry(AdbHub adbHub, PortAllocator portAllocator)
    {
        this.adbHub = adbHub;
        this.portAllocator = portAllocator;
    }

    // Sessions are keyed by transport-id, not serial: two devices can report
    // the same serial, but each USB connection has a unique transport-id, so
    // this is what disambiguates them (and what adb routes by).
    public AgentSession GetOrCreate(string handle)
    {
        if (string.IsNullOrEmpty(handle))
        {
            throw new ArgumentException("device handle required", nameof(handle));
        }

        DeviceData device = adbHub.ResolveDevice(handle);
        string transportId = device.TransportId;
        return sessions.GetOrAdd(transportId, tid =>
        {
            int? port = portAllocator.TryAllocate();
            if (port is null)
            {
                throw new InvalidOperationException(
                    "agent port pool exhausted (9601-9699); release some sessions first");
            }
            Log.Info($"creating agent session for {device.Serial} (transport {tid}) on port {port}");
            return new AgentSession(adbHub, tid, port.Value);
        });
    }

    public AgentSession EnsureStarted(string handle)
    {
        AgentSession session = GetOrCreate(handle);
        if (!session.EnsureStarted())
        {
            throw new InvalidOperationException(
                $"failed to start agent for transport {session.TransportId} on port {session.Port}");
        }
        return session;
    }

    public bool TryDrop(string handle)
    {
        string key;
        try
        { key = adbHub.ResolveDevice(handle).TransportId; }
        catch
        { key = handle; }
        if (!sessions.TryRemove(key, out AgentSession? session))
        {
            return false;
        }

        try
        { session.Dispose(); }
        catch { }
        portAllocator.Release(session.Port);
        return true;
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, AgentSession> kv in sessions)
        {
            try
            { kv.Value.Dispose(); }
            catch { }
            portAllocator.Release(kv.Value.Port);
        }
        sessions.Clear();
    }
}
