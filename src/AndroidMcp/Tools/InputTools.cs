using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class InputClickTool : IToolHandler
{
    private readonly DeviceInput input;
    public ToolDescriptor Descriptor { get; }

    public InputClickTool(DeviceInput input)
    {
        this.input = input;
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

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x = Args.RequireInt(arguments, "x");
        int y = Args.RequireInt(arguments, "y");
        string via = await input.TapAsync(serial, x, y).ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject { ["ok"] = true, ["x"] = x, ["y"] = y, ["via"] = via });
    }
}

internal sealed class InputSwipeTool : IToolHandler
{
    private readonly DeviceInput input;
    public ToolDescriptor Descriptor { get; }

    public InputSwipeTool(DeviceInput input)
    {
        this.input = input;
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

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x1 = Args.RequireInt(arguments, "x1");
        int y1 = Args.RequireInt(arguments, "y1");
        int x2 = Args.RequireInt(arguments, "x2");
        int y2 = Args.RequireInt(arguments, "y2");
        int durationMs = Args.OptionalInt(arguments, "duration_ms", 300);
        string via = await input.SwipeAsync(serial, x1, y1, x2, y2, durationMs).ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["x1"] = x1,
            ["y1"] = y1,
            ["x2"] = x2,
            ["y2"] = y2,
            ["durationMs"] = durationMs,
            ["via"] = via
        });
    }
}

internal sealed class InputTextTool : IToolHandler
{
    private readonly DeviceInput input;
    public ToolDescriptor Descriptor { get; }

    public InputTextTool(DeviceInput input)
    {
        this.input = input;
        Descriptor = new ToolDescriptor
        {
            Name = "input_text",
            Description = "Type text into the focused field. Any Unicode (CJK, emoji) works through the on-device agent; non-ASCII text that the keymap cannot type is appended to the focused editable field.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("text", Schema.String("text to type"), true))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string text = Args.RequireString(arguments, "text");
        string via = await input.TextAsync(serial, text).ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject { ["ok"] = true, ["text"] = text, ["via"] = via });
    }
}

internal sealed class KeyEventTool : IToolHandler
{
    private readonly DeviceInput input;
    public ToolDescriptor Descriptor { get; }

    public KeyEventTool(DeviceInput input)
    {
        this.input = input;
        Descriptor = new ToolDescriptor
        {
            Name = "key_event",
            Description = "Send a single Android keycode. Accepts symbolic names (BACK, HOME, MENU, ENTER, VOLUME_UP, VOLUME_DOWN, POWER, etc.) or numeric KeyEvent codes.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("key", Schema.String("Android keycode name or number, e.g. BACK / HOME / 4 / 26"), true))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string key = Args.RequireString(arguments, "key");
        string via = await input.KeyAsync(serial, key).ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject { ["ok"] = true, ["key"] = key, ["via"] = via });
    }
}
