using System.Text.Json.Nodes;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal static class Args
{
    public static string RequireString(JsonObject args, string name)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            throw new McpInvalidParamsException($"missing '{name}'");
        }

        try
        { return node.GetValue<string>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be a string"); }
    }

    public static string? OptionalString(JsonObject args, string name)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            return null;
        }

        try
        { return node.GetValue<string>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be a string"); }
    }

    public static int RequireInt(JsonObject args, string name)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            throw new McpInvalidParamsException($"missing '{name}'");
        }

        try
        { return node.GetValue<int>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be an integer"); }
    }

    public static int OptionalInt(JsonObject args, string name, int fallback)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            return fallback;
        }

        try
        { return node.GetValue<int>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be an integer"); }
    }

    public static bool OptionalBool(JsonObject args, string name, bool fallback)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            return fallback;
        }

        try
        { return node.GetValue<bool>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be a boolean"); }
    }

    public static string RequireSerial(JsonObject args) => RequireString(args, "serial");
}
