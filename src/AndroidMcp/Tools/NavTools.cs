using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal abstract class KeyShortcutTool : IToolHandler
{
    private readonly AdbHub adb;
    private readonly string keycode;
    public ToolDescriptor Descriptor { get; }

    protected KeyShortcutTool(AdbHub adb, string name, string keycode, string description)
    {
        this.adb = adb;
        this.keycode = keycode;
        Descriptor = new ToolDescriptor
        {
            Name = name,
            Description = description,
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand($"input keyevent {keycode}", device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject { ["ok"] = true, ["key"] = keycode }));
    }
}

internal sealed class PressHomeTool : KeyShortcutTool
{
    public PressHomeTool(AdbHub adb) : base(adb, "press_home", "HOME",
        "Send HOME keyevent — return to launcher. Convenience wrapper around `key_event` with KEYCODE_HOME.")
    { }
}

internal sealed class PressBackTool : KeyShortcutTool
{
    public PressBackTool(AdbHub adb) : base(adb, "press_back", "BACK",
        "Send BACK keyevent — go back one navigation step. Convenience wrapper.")
    { }
}

internal sealed class PressRecentsTool : KeyShortcutTool
{
    public PressRecentsTool(AdbHub adb) : base(adb, "press_recents", "APP_SWITCH",
        "Send APP_SWITCH keyevent — open the recent-tasks overview. Convenience wrapper.")
    { }
}

internal sealed class WakeUnlockTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public WakeUnlockTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "wake_unlock",
            Description = "Wake the screen and dismiss the keyguard. Sends KEYCODE_WAKEUP then `wm dismiss-keyguard`. Idempotent if already unlocked. Does NOT enter a PIN/pattern/password — secure locks just surface the password prompt. Call this first when scripting starts cold.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver wakeRx = new();
        adb.Client.ExecuteRemoteCommand("input keyevent KEYCODE_WAKEUP", device, wakeRx, Encoding.UTF8);
        Thread.Sleep(200);
        ConsoleOutputReceiver dismissRx = new();
        adb.Client.ExecuteRemoteCommand("wm dismiss-keyguard", device, dismissRx, Encoding.UTF8);
        Thread.Sleep(200);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["wake"] = (wakeRx.ToString() ?? string.Empty).Trim(),
            ["dismiss"] = (dismissRx.ToString() ?? string.Empty).Trim()
        }));
    }
}

internal sealed class ClearRecentsTool : IToolHandler
{
    // Matches "Recent #N: Task{... #<taskId> ... type=<type>". Mirrors AT's
    // KillAllAppsAction; format is stable across Android 11..16.
    private static readonly Regex TaskRegex = new(
        @"Recent\s+#\d+:.*?#(\d+).*?type=(\S+)",
        RegexOptions.Compiled);

    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public ClearRecentsTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "clear_recents",
            Description = "Remove every non-home task from the recents overview via `am stack remove`. Skips `type=home` so the launcher stays alive. Returns the list of removed task ids.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver dump = new();
        adb.Client.ExecuteRemoteCommand("dumpsys activity recents", device, dump, Encoding.UTF8);
        string raw = dump.ToString() ?? string.Empty;
        JsonArray removed = new();
        JsonArray skipped = new();
        foreach (Match m in TaskRegex.Matches(raw))
        {
            string taskId = m.Groups[1].Value;
            string type = m.Groups[2].Value;
            if (string.Equals(type, "home", System.StringComparison.Ordinal))
            {
                skipped.Add(new JsonObject { ["taskId"] = taskId, ["type"] = type });
                continue;
            }
            try
            {
                ConsoleOutputReceiver rmReceiver = new();
                adb.Client.ExecuteRemoteCommand($"am stack remove {taskId}", device, rmReceiver, Encoding.UTF8);
                removed.Add(new JsonObject { ["taskId"] = taskId, ["type"] = type });
            }
            catch
            {
            }
        }
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["removed"] = removed,
            ["skipped"] = skipped
        }));
    }
}
