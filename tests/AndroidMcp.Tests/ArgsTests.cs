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
    public void AcceptsPackageNames(string name) =>
        Assert.Equal(name, Args.RequirePackage(new JsonObject { ["package"] = name }));

    [Theory]
    [InlineData("settings")]
    [InlineData("com.android.settings; reboot")]
    [InlineData("com.a$(id)")]
    [InlineData("com.a b")]
    [InlineData("1com.a")]
    public void RejectsAnythingElse(string name) =>
        Assert.Throws<McpInvalidParamsException>(() => Args.RequirePackage(new JsonObject { ["package"] = name }));
}
