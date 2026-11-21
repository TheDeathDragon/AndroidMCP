using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;

namespace AndroidMcp.Adb;

internal readonly record struct ScreenSize(int Width, int Height)
{
    private static readonly Regex SizeRegex = new(@"(\d+)x(\d+)", RegexOptions.Compiled);
    private static readonly Regex HierarchyRootRegex = new(
        @"<hierarchy\b[^>]*?\bwidth=""(\d+)""[^>]*?\bheight=""(\d+)""", RegexOptions.Compiled);

    // The agent's hierarchy root carries Display.getRealSize, which follows the current
    // rotation; `wm size` reports the natural (portrait) size, so landscape gestures
    // computed from it land in the wrong place. It also costs a shell round trip.
    public static ScreenSize? FromHierarchy(string xml)
    {
        Match m = HierarchyRootRegex.Match(xml);
        if (!m.Success)
        {
            return null;
        }
        return new ScreenSize(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    // /info is ~1 ms on the device; a hierarchy dump walks the whole tree and slows down
    // further while a list is still flinging. Agents older than /info's display fields fall
    // back to the hierarchy root, then to `wm size`.
    public static async Task<ScreenSize> ReadAsync(AgentSession session, AdbClient client, DeviceData device, string? hierarchyXml = null)
    {
        if (hierarchyXml is null)
        {
            ScreenSize? fromInfo = FromInfo(await session.Http.GetStringAsync("/info").ConfigureAwait(false));
            if (fromInfo is not null)
            {
                return fromInfo.Value;
            }
            hierarchyXml = await session.Http.GetStringAsync("/hierarchy").ConfigureAwait(false);
        }
        return FromHierarchy(hierarchyXml) ?? Read(client, device);
    }

    public static ScreenSize? FromInfo(string json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(json);
            int? width = node?["width"]?.GetValue<int>();
            int? height = node?["height"]?.GetValue<int>();
            return width > 0 && height > 0 ? new ScreenSize(width.Value, height.Value) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

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
