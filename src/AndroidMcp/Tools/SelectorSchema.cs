using System.Text.Json.Nodes;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal static class SelectorSchema
{
    public static JsonObject Build()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Element matcher; non-null fields AND together. Provide at least one of text / resourceId / contentDesc / className / xpath.",
            ["properties"] = new JsonObject
            {
                ["text"] = Schema.String("exact text by default; combine with partial=true for substring"),
                ["resourceId"] = Schema.String("Android resource-id (e.g. com.android.settings:id/search)"),
                ["contentDesc"] = Schema.String("accessibility content-desc"),
                ["className"] = Schema.String("widget class, e.g. android.widget.Button"),
                ["package"] = Schema.String("package filter"),
                ["xpath"] = Schema.String("XPath; when set, the text/id/desc/class/package fields are ignored"),
                ["partial"] = Schema.Boolean("when true, text and contentDesc use Contains() instead of equality"),
                ["clickable"] = Schema.Boolean("require clickable=<value>"),
                ["scrollable"] = Schema.Boolean("require scrollable=<value>"),
                ["enabled"] = Schema.Boolean("require enabled=<value>"),
                ["focused"] = Schema.Boolean("require focused=<value>"),
                ["selected"] = Schema.Boolean("require selected=<value>"),
                ["checked"] = Schema.Boolean("require checked=<value>"),
                ["checkable"] = Schema.Boolean("require checkable=<value>"),
                ["focusable"] = Schema.Boolean("require focusable=<value>"),
                ["longClickable"] = Schema.Boolean("require longClickable=<value>"),
                ["password"] = Schema.Boolean("require password=<value>")
            },
            ["additionalProperties"] = false
        };
    }

    public static Selector Parse(JsonObject? obj)
    {
        if (obj is null)
        {
            throw new McpInvalidParamsException("missing 'selector'");
        }
        Selector sel = new()
        {
            Text = OptString(obj, "text"),
            ResourceId = OptString(obj, "resourceId"),
            ContentDesc = OptString(obj, "contentDesc"),
            ClassName = OptString(obj, "className"),
            Package = OptString(obj, "package"),
            Xpath = OptString(obj, "xpath"),
            Partial = OptBool(obj, "partial") ?? false,
            Clickable = OptBool(obj, "clickable"),
            Scrollable = OptBool(obj, "scrollable"),
            Enabled = OptBool(obj, "enabled"),
            Focused = OptBool(obj, "focused"),
            Selected = OptBool(obj, "selected"),
            Checked = OptBool(obj, "checked"),
            Checkable = OptBool(obj, "checkable"),
            Focusable = OptBool(obj, "focusable"),
            LongClickable = OptBool(obj, "longClickable"),
            Password = OptBool(obj, "password")
        };
        if (sel.IsEmpty)
        {
            throw new McpInvalidParamsException("selector is empty — provide at least one matching field");
        }
        return sel;
    }

    public static JsonObject Encode(UiNode node)
    {
        return new JsonObject
        {
            ["text"] = node.Text,
            ["resourceId"] = node.ResourceId,
            ["contentDesc"] = node.ContentDesc,
            ["className"] = node.ClassName,
            ["bounds"] = new JsonObject
            {
                ["left"] = node.Left,
                ["top"] = node.Top,
                ["right"] = node.Right,
                ["bottom"] = node.Bottom,
                ["centerX"] = node.CenterX,
                ["centerY"] = node.CenterY,
                ["width"] = node.Width,
                ["height"] = node.Height
            }
        };
    }

    private static string? OptString(JsonObject obj, string name)
    {
        JsonNode? node = obj[name];
        if (node is null)
        { return null; }
        try
        { return node.GetValue<string>(); }
        catch { throw new McpInvalidParamsException($"selector.{name} must be a string"); }
    }

    private static bool? OptBool(JsonObject obj, string name)
    {
        JsonNode? node = obj[name];
        if (node is null)
        { return null; }
        try
        { return node.GetValue<bool>(); }
        catch { throw new McpInvalidParamsException($"selector.{name} must be a boolean"); }
    }
}
