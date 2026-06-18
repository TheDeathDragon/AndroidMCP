using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace AndroidMcp.Tools;

internal static class Schema
{
    public static JsonObject Object(params (string name, JsonObject prop, bool required)[] properties)
    {
        JsonObject schema = new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["additionalProperties"] = false
        };
        JsonObject props = (JsonObject)schema["properties"]!;
        JsonArray required = new();
        foreach ((string name, JsonObject prop, bool isRequired) in properties)
        {
            props[name] = prop;
            if (isRequired)
            {
                required.Add(name);
            }
        }
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    public static JsonObject String(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    public static JsonObject Integer(string description) =>
        new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Boolean(string description) =>
        new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject Enum(string description, params string[] values)
    {
        JsonArray arr = new();
        foreach (string v in values)
        {
            arr.Add(v);
        }

        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = arr
        };
    }

    public const string SerialDescription =
        "device serial from list_devices. If two connected devices report the same serial, " +
        "pass that device's transportId here instead (list_devices returns it) so the correct one is targeted";
}
