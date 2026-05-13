using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AndroidMcp.Logging;
using AndroidMcp.Tools;

namespace AndroidMcp.Mcp;

internal sealed class McpServer
{
    private readonly ToolRegistry tools;

    public McpServer(ToolRegistry tools)
    {
        this.tools = tools;
    }

    public async Task<string?> HandleAsync(string rawRequest)
    {
        JsonRpcRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<JsonRpcRequest>(rawRequest, JsonOpts.Default);
        }
        catch (Exception ex)
        {
            return Serialize(ErrorResponse(null, JsonRpcError.ParseError, $"parse error: {ex.Message}"));
        }
        if (req is null || string.IsNullOrEmpty(req.Method))
        {
            return Serialize(ErrorResponse(req?.Id, JsonRpcError.InvalidRequest, "missing method"));
        }
        bool isNotification = req.Id is null;
        Log.Debug($"-> {req.Method}{(isNotification ? " (notification)" : "")}");
        try
        {
            JsonNode? result = req.Method switch
            {
                "initialize" => HandleInitialize(),
                "initialized" => null,
                "notifications/initialized" => null,
                "ping" => new JsonObject(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(req.Params).ConfigureAwait(false),
                _ => throw new McpMethodNotFoundException(req.Method)
            };
            if (isNotification)
            {
                return null;
            }

            return Serialize(new JsonRpcResponse { Id = req.Id, Result = result ?? new JsonObject() });
        }
        catch (McpMethodNotFoundException ex)
        {
            if (isNotification)
            {
                return null;
            }

            return Serialize(ErrorResponse(req.Id, JsonRpcError.MethodNotFound, ex.Message));
        }
        catch (McpInvalidParamsException ex)
        {
            if (isNotification)
            {
                return null;
            }

            return Serialize(ErrorResponse(req.Id, JsonRpcError.InvalidParams, ex.Message));
        }
        catch (Exception ex)
        {
            Log.Error($"handler crashed for {req.Method}: {ex}");
            if (isNotification)
            {
                return null;
            }

            return Serialize(ErrorResponse(req.Id, JsonRpcError.InternalError, ex.Message));
        }
    }

    private static JsonNode HandleInitialize()
    {
        InitializeResult result = new();
        return JsonSerializer.SerializeToNode(result, JsonOpts.Default)!;
    }

    private JsonNode HandleToolsList()
    {
        JsonArray array = new();
        foreach (ToolDescriptor d in tools.Descriptors)
        {
            array.Add(JsonSerializer.SerializeToNode(d, JsonOpts.Default));
        }
        return new JsonObject { ["tools"] = array };
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? paramsNode)
    {
        if (paramsNode is not JsonObject obj)
        {
            throw new McpInvalidParamsException("params must be an object");
        }
        string? name = obj["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            throw new McpInvalidParamsException("missing 'name' field");
        }
        JsonObject args = obj["arguments"] as JsonObject ?? new JsonObject();
        if (!tools.TryGet(name, out IToolHandler? handler))
        {
            ToolCallResult err = ToolCallResult.Error($"unknown tool: {name}");
            return JsonSerializer.SerializeToNode(err, JsonOpts.Default)!;
        }
        ToolCallResult result;
        try
        {
            result = await handler!.InvokeAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"tool '{name}' threw: {ex.Message}");
            result = ToolCallResult.Error(ex.Message);
        }
        return JsonSerializer.SerializeToNode(result, JsonOpts.Default)!;
    }

    private static JsonRpcResponse ErrorResponse(JsonNode? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };

    private static string Serialize(JsonRpcResponse response) =>
        JsonSerializer.Serialize(response, JsonOpts.Default);
}

internal sealed class McpMethodNotFoundException : Exception
{
    public McpMethodNotFoundException(string method) : base($"method not found: {method}") { }
}

internal sealed class McpInvalidParamsException : Exception
{
    public McpInvalidParamsException(string message) : base(message) { }
}
