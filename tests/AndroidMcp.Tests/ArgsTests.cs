using System.Text.Json.Nodes;
using AndroidMcp.Mcp;
using AndroidMcp.Tools;
using Xunit;

namespace AndroidMcp.Tests;

public class ArgsTests
{
    [Theory]
    [InlineData("com.android.settings")]
    [InlineData("la.shiro.agent")]
    [InlineData("com.google.android.apps.messaging_x1")]
    [InlineData("android")]
    public void AcceptsPackageNames(string name) =>
        Assert.Equal(name, Args.RequirePackage(new JsonObject { ["package"] = name }));

    [Theory]
    [InlineData("com.android.settings; reboot")]
    [InlineData("com.a$(id)")]
    [InlineData("com.a b")]
    [InlineData("1com.a")]
    [InlineData("com.a.")]
    [InlineData("com.x\n")]
    [InlineData("com.x\nreboot")]
    public void RejectsAnythingElse(string name) =>
        Assert.Throws<McpInvalidParamsException>(() => Args.RequirePackage(new JsonObject { ["package"] = name }));
}
