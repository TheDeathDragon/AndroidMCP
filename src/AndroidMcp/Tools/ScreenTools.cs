using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class ScreenshotTool : IToolHandler
{
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public ScreenshotTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "screenshot",
            Description = "Capture the current screen via the on-device agent (UiAutomation API — much faster than `adb shell screencap`). Returns a PNG image content block.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("quality", Schema.Integer("PNG quality 1..100, default 100. Lower values shrink the image; 70 is a sane default for LLM consumption."), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        int quality = Args.OptionalInt(arguments, "quality", 100);
        AgentSession session = agents.EnsureStarted(serial);
        string url = quality != 100 ? $"/screenshot?quality={quality}" : "/screenshot";
        byte[] png = await session.Http.GetByteArrayAsync(url).ConfigureAwait(false);
        string base64 = Convert.ToBase64String(png);
        return ToolCallResult.Image(base64);
    }
}

internal sealed class DumpHierarchyTool : IToolHandler
{
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public DumpHierarchyTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "dump_hierarchy",
            Description = "Dump the active window's accessibility tree. Default `format=lean` returns compact JSON (5–12× fewer tokens than XML); pass `format=xml` for the raw agent output. `interactive_only=true` further drops pure layout containers. If you already know what to tap, prefer `find_element` — single-element results are tiny.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("format", Schema.Enum("output format", "lean", "xml"), false),
                ("compressed", Schema.Boolean("agent-side filter for decoration nodes; default true"), false),
                ("interactive_only", Schema.Boolean("lean only — drop nodes with no text / contentDesc / resource-id / clickable / scrollable / focused; default false"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string format = Args.OptionalString(arguments, "format") ?? "lean";
        bool compressed = Args.OptionalBool(arguments, "compressed", true);
        bool interactiveOnly = Args.OptionalBool(arguments, "interactive_only", false);
        AgentSession session = agents.EnsureStarted(serial);
        string url = compressed ? "/hierarchy" : "/hierarchy?compressed=false";
        string xml = await session.Http.GetStringAsync(url).ConfigureAwait(false);
        if (format == "xml")
        {
            return ToolCallResult.Text(xml);
        }
        if (format != "lean")
        {
            return ToolCallResult.Error($"unknown format '{format}'; expected 'lean' or 'xml'");
        }
        return ToolCallResult.Text(HierarchyMatcher.ToLean(xml, interactiveOnly));
    }
}

internal sealed class DeviceInfoTool : IToolHandler
{
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public DeviceInfoTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "device_info",
            Description = "Detailed device profile: build / display / cpu / gpu / sensors / cameras / network / sim / treble / features / properties. Returns a single JSON document from the agent's /device-info endpoint.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        AgentSession session = agents.EnsureStarted(serial);
        string json = await session.Http.GetStringAsync("/device-info").ConfigureAwait(false);
        return ToolCallResult.Text(json);
    }
}
