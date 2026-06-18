using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AdvancedSharpAdbClient.Models;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class ListDevicesTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public ListDevicesTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "list_devices",
            Description = "Enumerate Android devices currently visible to adb. Returns serial, state, model, product and transportId for each. Always call this before any other tool to discover the device serial to pass. If two devices share the same serial, pass the per-device transportId (unique) as the 'serial' argument to target one unambiguously.",
            InputSchema = Schema.Object()
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        IReadOnlyList<DeviceData> devices = adb.ListDevices();
        JsonArray array = new();
        foreach (DeviceData d in devices)
        {
            array.Add(new JsonObject
            {
                ["serial"] = d.Serial,
                ["state"] = d.State.ToString(),
                ["model"] = d.Model,
                ["product"] = d.Product,
                ["transportId"] = d.TransportId
            });
        }
        JsonObject payload = new()
        {
            ["count"] = devices.Count,
            ["devices"] = array
        };
        return Task.FromResult(ToolCallResult.Json(payload));
    }
}
