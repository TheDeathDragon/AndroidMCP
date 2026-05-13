using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class InputClickTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public InputClickTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "click",
            Description = "Single-tap at (x, y) in screen pixel coordinates (origin: top-left). Coordinates come from dump_hierarchy bounds — use the centre.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("x", Schema.Integer("x in pixels"), true),
                ("y", Schema.Integer("y in pixels"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x = Args.RequireInt(arguments, "x");
        int y = Args.RequireInt(arguments, "y");
        DeviceData device = adb.RequireDevice(serial);
        string command = string.Create(CultureInfo.InvariantCulture, $"input tap {x} {y}");
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject { ["ok"] = true, ["x"] = x, ["y"] = y }));
    }
}

internal sealed class InputSwipeTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public InputSwipeTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "swipe",
            Description = "Swipe from (x1,y1) to (x2,y2) over `duration_ms` (default 300). Use ~150ms for fling, ~600ms for slow scroll.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("x1", Schema.Integer("start x"), true),
                ("y1", Schema.Integer("start y"), true),
                ("x2", Schema.Integer("end x"), true),
                ("y2", Schema.Integer("end y"), true),
                ("duration_ms", Schema.Integer("swipe duration in ms, default 300"), false))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x1 = Args.RequireInt(arguments, "x1");
        int y1 = Args.RequireInt(arguments, "y1");
        int x2 = Args.RequireInt(arguments, "x2");
        int y2 = Args.RequireInt(arguments, "y2");
        int durationMs = Args.OptionalInt(arguments, "duration_ms", 300);
        DeviceData device = adb.RequireDevice(serial);
        string command = string.Create(CultureInfo.InvariantCulture,
            $"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["x1"] = x1,
            ["y1"] = y1,
            ["x2"] = x2,
            ["y2"] = y2,
            ["durationMs"] = durationMs
        }));
    }
}

internal sealed class InputTextTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public InputTextTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "input_text",
            Description = "Type literal text into the focused field via `adb shell input text`. ASCII only — for non-ASCII (CJK, emoji), use the clipboard route in a follow-up shell call.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("text", Schema.String("text to type"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string text = Args.RequireString(arguments, "text");
        DeviceData device = adb.RequireDevice(serial);
        string escaped = EscapeForInputText(text);
        string command = $"input text {escaped}";
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject { ["ok"] = true, ["text"] = text }));
    }

    private static string EscapeForInputText(string text)
    {
        StringBuilder sb = new(text.Length + 4);
        foreach (char c in text)
        {
            switch (c)
            {
                case ' ':
                    sb.Append("%s");
                    break;
                case '\'':
                    sb.Append("\\'");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '`':
                    sb.Append("\\`");
                    break;
                case '$':
                    sb.Append("\\$");
                    break;
                case '&':
                    sb.Append("\\&");
                    break;
                case '|':
                    sb.Append("\\|");
                    break;
                case ';':
                    sb.Append("\\;");
                    break;
                case '(':
                    sb.Append("\\(");
                    break;
                case ')':
                    sb.Append("\\)");
                    break;
                case '<':
                    sb.Append("\\<");
                    break;
                case '>':
                    sb.Append("\\>");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}

internal sealed class KeyEventTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public KeyEventTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "key_event",
            Description = "Send a single Android keycode via `adb shell input keyevent`. Accepts symbolic names (BACK, HOME, MENU, ENTER, VOLUME_UP, VOLUME_DOWN, POWER, etc.) or numeric KeyEvent codes.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("key", Schema.String("Android keycode name or number, e.g. BACK / HOME / 4 / 26"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string key = Args.RequireString(arguments, "key");
        DeviceData device = adb.RequireDevice(serial);
        string command = $"input keyevent {key}";
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject { ["ok"] = true, ["key"] = key }));
    }
}
