using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    public static double OptionalDouble(JsonObject args, string name, double fallback)
    {
        JsonNode? node = args[name];
        if (node is null)
        {
            return fallback;
        }

        try
        { return node.GetValue<double>(); }
        catch { throw new McpInvalidParamsException($"'{name}' must be a number"); }
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

    // Package names go into shell commands (`pm clear`, `am force-stop`, ...) and may come
    // from on-screen text; the Android package grammar rules out every shell metacharacter.
    public static string RequirePackage(JsonObject args)
    {
        string name = RequireString(args, "package");
        if (!PackageNameRegex.IsMatch(name))
        {
            throw new McpInvalidParamsException($"'package' is not a valid Android package name: {name}");
        }
        return name;
    }

    private static readonly Regex PackageNameRegex = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$", RegexOptions.Compiled);
}
