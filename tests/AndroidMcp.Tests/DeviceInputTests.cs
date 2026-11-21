using AndroidMcp.Adb;
using Xunit;

namespace AndroidMcp.Tests;

public class DeviceInputTests
{
    [Theory]
    [InlineData("hello world", "hello%sworld")]
    [InlineData("a&b;c|d", @"a\&b\;c\|d")]
    [InlineData("$(rm)", @"\$\(rm\)")]
    [InlineData("#tag *.txt ?", @"\#tag%s\*.txt%s\?")]
    [InlineData("~[a]{b}!", @"\~\[a\]\{b\}\!")]
    [InlineData("it's \"x\"", @"it\'s%s\""x\""")]
    public void EscapesShellMetacharactersForInputText(string text, string expected) =>
        Assert.Equal(expected, DeviceInput.EscapeForInputText(text));

    [Theory]
    [InlineData("wifi 123", true)]
    [InlineData("蓝牙", false)]
    [InlineData("😀", false)]
    [InlineData("tab\t", false)]
    public void DetectsTextTheShellCanType(string text, bool expected) =>
        Assert.Equal(expected, DeviceInput.IsAscii(text));
}
