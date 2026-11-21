using AndroidMcp.Tools;
using Xunit;

namespace AndroidMcp.Tests;

public class LaunchOutputTests
{
    [Fact]
    public void SucceedsOnCleanStart()
    {
        (bool ok, string? component) = LaunchAppTool.ParseLaunchOutput(
            "component=com.android.settings/.Settings\r\nStarting: Intent { cmp=com.android.settings/.Settings }\r\nrc=0");
        Assert.True(ok);
        Assert.Equal("com.android.settings/.Settings", component);
    }

    [Fact]
    public void ComponentNamedErrorIsNotAFailure()
    {
        (bool ok, _) = LaunchAppTool.ParseLaunchOutput(
            "component=com.x/.ErrorActivity\nStarting: Intent { cmp=com.x/.ErrorActivity }\nrc=0");
        Assert.True(ok);
    }

    [Theory]
    [InlineData("component=com.x/.A\nException occurred while executing 'start':\njava.lang.SecurityException: denied\nrc=0")]
    [InlineData("component=com.x/.A\nError: Activity not started, unable to resolve Intent\nrc=0")]
    [InlineData("component=com.x/.A\nStarting: Intent { cmp=com.x/.A }\nrc=255")]
    [InlineData("Error: no launcher activity in com.x")]
    public void DetectsFailures(string output) =>
        Assert.False(LaunchAppTool.ParseLaunchOutput(output).Ok);
}
