using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    public AgentSession GetOrCreate(string serial)
    {
        if (string.IsNullOrEmpty(serial))
        {
            throw new ArgumentException("serial required", nameof(serial));
        }

        return sessions.GetOrAdd(serial, s =>
        {
            int? port = portAllocator.TryAllocate();
            if (port is null)
            {
                throw new InvalidOperationException(
                    "agent port pool exhausted (9501-9599); release some sessions first");
            }
            Log.Info($"creating agent session for {s} on port {port}");
            return new AgentSession(adbHub, s, port.Value);
        });
    }

    public AgentSession EnsureStarted(string serial)
    {
        AgentSession session = GetOrCreate(serial);
        if (!session.EnsureStarted())
        {
            throw new InvalidOperationException($"failed to start agent for {serial} on port {session.Port}");
        }
        return session;
    }

    public bool TryDrop(string serial)
    {
        if (!sessions.TryRemove(serial, out AgentSession? session))
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
