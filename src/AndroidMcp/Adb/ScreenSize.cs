using System;
using System.Text;
using System.Text.RegularExpressions;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;

namespace AndroidMcp.Adb;

internal readonly record struct ScreenSize(int Width, int Height)
{
    private static readonly Regex SizeRegex = new(@"(\d+)x(\d+)", RegexOptions.Compiled);

    public static ScreenSize Read(AdbClient client, DeviceData device)
    {
        ConsoleOutputReceiver receiver = new();
        client.ExecuteRemoteCommand("wm size", device, receiver, Encoding.UTF8);
        string raw = receiver.ToString() ?? string.Empty;
        // Override line wins when the user has set a custom resolution; fall
        // back to Physical size otherwise.
        int over = raw.IndexOf("Override size:", StringComparison.Ordinal);
        int phys = raw.IndexOf("Physical size:", StringComparison.Ordinal);
        int start = over >= 0 ? over : phys;
        if (start < 0)
        {
            throw new InvalidOperationException($"unexpected `wm size` output: {raw.Trim()}");
        }
        Match m = SizeRegex.Match(raw, start);
        if (!m.Success)
        {
            throw new InvalidOperationException($"could not parse screen size from: {raw.Trim()}");
        }
        return new ScreenSize(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }
}
