using System;
using System.Globalization;
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
            Description = "Capture the current screen via the on-device agent as JPEG (faster than PNG and about a tenth of its size). Vision tokens scale with pixel count, so pass `scale` (e.g. 0.5) when full resolution is not needed; `format=png` for lossless.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("format", Schema.Enum("image format, default jpeg", "jpeg", "png"), false),
                ("scale", Schema.Number("downscale factor 0.1..1.0, default 1.0"), false),
                ("quality", Schema.Integer("JPEG quality 1..100, default 80; ignored for png"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string format = Args.OptionalString(arguments, "format") ?? "jpeg";
        if (format is not ("png" or "jpeg"))
        {
            throw new McpInvalidParamsException("format must be png or jpeg");
        }
        double scale = Math.Clamp(Args.OptionalDouble(arguments, "scale", 1.0), 0.1, 1.0);
        int quality = Math.Clamp(Args.OptionalInt(arguments, "quality", 80), 1, 100);
        AgentSession session = agents.EnsureStarted(serial);
        string url = format == "jpeg"
            ? string.Create(CultureInfo.InvariantCulture, $"/screenshot?format=jpeg&quality={quality}&scale={scale}")
            : string.Create(CultureInfo.InvariantCulture, $"/screenshot?scale={scale}");
        using HttpResponseMessage resp = await session.Http.GetAsync(url).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        byte[] image = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        // Agents that predate format/scale ignore them and still answer PNG.
        string mimeType = resp.Content.Headers.ContentType?.MediaType ?? "image/png";
        return ToolCallResult.Image(Convert.ToBase64String(image), mimeType);
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
