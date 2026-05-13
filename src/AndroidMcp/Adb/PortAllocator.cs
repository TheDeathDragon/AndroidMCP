using System;
using System.Collections.Concurrent;

namespace AndroidMcp.Adb;

// 9500 is the canonical agent port; 9501-9599 are reserved for other tooling.
// We sit one decade higher to avoid `adb forward` collisions.
internal sealed class PortAllocator
{
    private readonly int minPort;
    private readonly int maxPort;
    private readonly ConcurrentDictionary<int, byte> used = new();

    public PortAllocator(int minPort = 9601, int maxPort = 9699)
    {
        if (maxPort < minPort)
        {
            throw new ArgumentException("maxPort < minPort");
        }

        this.minPort = minPort;
        this.maxPort = maxPort;
    }

    public int? TryAllocate()
    {
        for (int port = minPort; port <= maxPort; port++)
        {
            if (used.TryAdd(port, 0))
            {
                return port;
            }
        }
        return null;
    }

    public void Release(int port) => used.TryRemove(port, out _);

    public int InUseCount => used.Count;
}
