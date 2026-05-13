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

internal sealed class LongPressTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public LongPressTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "long_press",
            Description = "Long-press at (x, y). Implemented as a zero-distance `input swipe` with the requested duration (Android's `input` has no native long-press).",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("x", Schema.Integer("x in pixels"), true),
                ("y", Schema.Integer("y in pixels"), true),
                ("duration_ms", Schema.Integer("hold duration, default 800"), false))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int x = Args.RequireInt(arguments, "x");
        int y = Args.RequireInt(arguments, "y");
        int durationMs = Args.OptionalInt(arguments, "duration_ms", 800);
        DeviceData device = adb.RequireDevice(serial);
        string command = string.Create(CultureInfo.InvariantCulture, $"input swipe {x} {y} {x} {y} {durationMs}");
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["x"] = x,
            ["y"] = y,
            ["durationMs"] = durationMs
        }));
    }
}
