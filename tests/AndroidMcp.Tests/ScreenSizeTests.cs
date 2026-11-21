using AndroidMcp.Adb;
using Xunit;

namespace AndroidMcp.Tests;

public class ScreenSizeTests
{
    [Fact]
    public void ReadsCurrentRotationSizeFromHierarchyRoot()
    {
        const string xml = "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>" +
            "<hierarchy rotation=\"1\" width=\"1640\" height=\"720\"><node width=\"1\" height=\"2\"/></hierarchy>";
        Assert.Equal(new ScreenSize(1640, 720), ScreenSize.FromHierarchy(xml));
    }

    [Fact]
    public void ReturnsNullWhenRootHasNoSize()
    {
        Assert.Null(ScreenSize.FromHierarchy("<?xml version=\"1.0\"?><hierarchy rotation=\"0\"/>"));
    }

    [Fact]
    public void ReadsSizeFromInfoWhenPresent()
    {
        Assert.Equal(new ScreenSize(720, 1640),
            ScreenSize.FromInfo("{\"brand\":\"x\",\"width\":720,\"height\":1640,\"rotation\":0}"));
        Assert.Null(ScreenSize.FromInfo("{\"brand\":\"x\",\"sdk\":30}"));
        Assert.Null(ScreenSize.FromInfo("not json"));
    }
}
