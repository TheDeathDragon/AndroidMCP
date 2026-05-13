using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AndroidMcp.Mcp;

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonNode? Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("params")] public JsonNode? Params { get; set; }
}

internal sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonNode? Id { get; set; }
    [JsonPropertyName("result")] public JsonNode? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}

internal sealed class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("data")] public JsonNode? Data { get; set; }

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

internal sealed class ToolDescriptor
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("inputSchema")] public JsonNode InputSchema { get; set; } = new JsonObject();
}

internal sealed class ToolCallResult
{
    [JsonPropertyName("content")] public List<ToolContent> Content { get; set; } = new();
    [JsonPropertyName("isError")] public bool IsError { get; set; }

    public static ToolCallResult Text(string text) => new()
    {
        Content = { new ToolContent { Type = "text", Text = text } }
    };

    public static ToolCallResult Json(JsonNode payload) => new()
    {
        Content = { new ToolContent { Type = "text", Text = payload.ToJsonString(JsonOpts.Default) } }
    };

    public static ToolCallResult Image(string base64Png) => new()
    {
        Content = { new ToolContent { Type = "image", Data = base64Png, MimeType = "image/png" } }
    };

    public static ToolCallResult Error(string message) => new()
    {
        Content = { new ToolContent { Type = "text", Text = message } },
        IsError = true
    };
}

internal sealed class ToolContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
}

internal sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "2024-11-05";
    [JsonPropertyName("capabilities")] public ServerCapabilities Capabilities { get; set; } = new();
    [JsonPropertyName("serverInfo")] public ServerInfo ServerInfo { get; set; } = new();
    [JsonPropertyName("instructions")] public string Instructions { get; set; } = ServerInstructions.Text;
}

internal static class ServerInstructions
{
    public const string Text = """
        You are driving Android devices via this MCP. Token economy and a few selector quirks matter:

        1. Multi-device by design — every tool except `list_devices` requires a `serial`. Call `list_devices` first if you don't know it.

        2. Cold start: if the device might be asleep or on the lockscreen, call `wake_unlock` once at the start. It is idempotent on an unlocked device.

        3. Hierarchy vs screenshot:
           - `dump_hierarchy` returns compact JSON by default (`format=lean`), ~5× smaller than the raw XML. Pass `format=xml` only when you need the full attribute set; `interactive_only=true` further drops pure layout containers.
           - `screenshot` consumes vision tokens (~1.5k @ 720p, ~3.5k @ 1080p). Use it for VISUAL verification (animations, charts, custom-rendered UIs), not for extracting text — `find_element` is far cheaper.

        4. Prefer composed tools over manual loops:
           - `tap_element` (selector → tap) instead of `dump_hierarchy` + `click`.
           - `scroll_until_visible` (selector + direction) instead of repeated `swipe` + dump.
           - `wait_for_element` instead of polling `dump_hierarchy`.
           - `find_element` with XPath sibling expressions reads "the value next to label X" cheaply, e.g. `//node[@text='Build number']/following-sibling::node[1]`.

        5. Selector trap in dense lists (Settings rows, RecyclerView items): the text usually lives on an inner TextView while clickable=true is on the parent container. **Do not combine `text=...` with `clickable=true`** in one selector. Match by text only — `tap_element` taps the centroid, which lands inside the clickable parent.

        6. Coordinates and bounds are in pixels; origin top-left. Use `screen_size` once if you need to compute absolute regions, then `swipe_direction` / `scroll_to_edge` handle directional gestures without you doing the math.
        """;
}

internal sealed class ServerCapabilities
{
    [JsonPropertyName("tools")] public ToolsCapability Tools { get; set; } = new();
}

internal sealed class ToolsCapability
{
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; } = false;
}

internal sealed class ServerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "android-mcp";
    [JsonPropertyName("version")] public string Version { get; set; } = BuildInfo.Version;
}
