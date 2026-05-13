using System.Collections.Generic;
using AndroidMcp.Adb;
using Xunit;

namespace AndroidMcp.Tests;

public class PortAllocatorTests
{
    [Fact]
    public void AllocatesDistinctPortsInRange()
    {
        PortAllocator allocator = new(9601, 9605);
        HashSet<int> seen = new();
        for (int i = 0; i < 5; i++)
        {
            int? port = allocator.TryAllocate();
            Assert.NotNull(port);
            Assert.InRange(port!.Value, 9601, 9605);
            Assert.True(seen.Add(port.Value), "port reused");
        }
        Assert.Null(allocator.TryAllocate());
    }

    [Fact]
    public void ReleasedPortIsReusable()
    {
        PortAllocator allocator = new(9601, 9602);
        int p1 = allocator.TryAllocate()!.Value;
        int p2 = allocator.TryAllocate()!.Value;
        Assert.Null(allocator.TryAllocate());
        allocator.Release(p1);
        int p3 = allocator.TryAllocate()!.Value;
        Assert.Equal(p1, p3);
    }
}
