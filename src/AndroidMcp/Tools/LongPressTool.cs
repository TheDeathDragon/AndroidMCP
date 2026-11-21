using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class LongPressTool : IToolHandler
{
    private readonly DeviceInput input;
    public ToolDescriptor Descriptor { get; }

    public LongPressTool(DeviceInput input)
    {
        this.input = input;
        Descriptor = new ToolDescriptor
        {
            Name = "long_press",
            Description = "Long-press at (x, y), holding for `duration_ms`.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("x", Schema.Integer("x in pixels"), true),
                ("y", Schema.Integer("y in pixels"), true),
                ("duration_ms", Schema.Integer("hold duration, default 800"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x = Args.RequireInt(arguments, "x");
        int y = Args.RequireInt(arguments, "y");
        int durationMs = Args.OptionalInt(arguments, "duration_ms", 800);
        string via = await input.LongPressAsync(serial, x, y, durationMs).ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["x"] = x,
            ["y"] = y,
            ["durationMs"] = durationMs,
            ["via"] = via
        });
    }
}
