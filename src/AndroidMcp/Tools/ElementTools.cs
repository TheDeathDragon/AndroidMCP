using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class FindElementTool : IToolHandler
{
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public FindElementTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "find_element",
            Description = "Find UI elements matching a selector in the current hierarchy. Returns all matches (single dump_hierarchy + match — no scrolling). Use scroll_until_visible if the element may be off-screen.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("selector", SelectorSchema.Build(), true),
                ("limit", Schema.Integer("max matches to return, default 10"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        Selector sel = SelectorSchema.Parse(arguments["selector"] as JsonObject);
        int limit = Args.OptionalInt(arguments, "limit", 10);
        AgentSession session = agents.EnsureStarted(serial);
        string xml = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        List<UiNode> all = HierarchyMatcher.FindAll(xml, sel);
        JsonArray hits = new();
        for (int i = 0; i < all.Count && i < limit; i++)
        {
            hits.Add(SelectorSchema.Encode(all[i]));
        }
        return ToolCallResult.Json(new JsonObject
        {
            ["count"] = all.Count,
            ["returned"] = hits.Count,
            ["matches"] = hits
        });
    }
}

internal sealed class TapElementTool : IToolHandler
{
    private readonly AdbHub adb;
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public TapElementTool(AdbHub adb, AgentRegistry agents)
    {
        this.adb = adb;
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "tap_element",
            Description = "Find an element by selector, then tap its center in one round-trip. Errors if the selector matches zero or more than one element (use index=N to disambiguate).",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("selector", SelectorSchema.Build(), true),
                ("index", Schema.Integer("when multiple match, pick this 0-based index; default 0 with strict=true rejects ambiguity"), false),
                ("strict", Schema.Boolean("when true (default), error on >1 match unless index is explicit"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        Selector sel = SelectorSchema.Parse(arguments["selector"] as JsonObject);
        bool indexExplicit = arguments["index"] is not null;
        int index = Args.OptionalInt(arguments, "index", 0);
        bool strict = Args.OptionalBool(arguments, "strict", true);
        AgentSession session = agents.EnsureStarted(serial);
        string xml = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        List<UiNode> all = HierarchyMatcher.FindAll(xml, sel);
        if (all.Count == 0)
        {
            return ToolCallResult.Error($"no element matches {sel.DescribeShort()}");
        }
        if (all.Count > 1 && strict && !indexExplicit)
        {
            return ToolCallResult.Error($"{all.Count} elements match {sel.DescribeShort()}; pass index=N or strict=false");
        }
        if (index < 0 || index >= all.Count)
        {
            return ToolCallResult.Error($"index {index} out of range (0..{all.Count - 1})");
        }
        UiNode hit = all[index];
        DeviceData device = adb.RequireDevice(serial);
        string command = string.Create(CultureInfo.InvariantCulture, $"input tap {hit.CenterX} {hit.CenterY}");
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["matched"] = all.Count,
            ["tapped"] = SelectorSchema.Encode(hit)
        });
    }
}

internal sealed class WaitForElementTool : IToolHandler
{
    private const int PollIntervalMs = 200;

    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public WaitForElementTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "wait_for_element",
            Description = "Block until a selector matches at least one element, or the timeout elapses. Polls the hierarchy every 200 ms. Returns the first match.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("selector", SelectorSchema.Build(), true),
                ("timeout_ms", Schema.Integer("max wait in ms, default 5000"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        Selector sel = SelectorSchema.Parse(arguments["selector"] as JsonObject);
        int timeoutMs = Math.Max(0, Args.OptionalInt(arguments, "timeout_ms", 5000));
        AgentSession session = agents.EnsureStarted(serial);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        int attempts = 0;
        while (true)
        {
            attempts++;
            string xml = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
            UiNode? hit = HierarchyMatcher.FindFirst(xml, sel);
            if (hit is not null)
            {
                return ToolCallResult.Json(new JsonObject
                {
                    ["ok"] = true,
                    ["attempts"] = attempts,
                    ["element"] = SelectorSchema.Encode(hit)
                });
            }
            if (DateTime.UtcNow >= deadline)
            {
                return ToolCallResult.Error($"timeout after {timeoutMs}ms ({attempts} polls); selector {sel.DescribeShort()} not found");
            }
            await Task.Delay(PollIntervalMs).ConfigureAwait(false);
        }
    }
}
