using System;
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

// Production-tuned constants borrowed from AT's ScrollUntilVisibleAction —
// touching them risks reintroducing mid-animation false negatives and
// gesture-zone swallowed swipes.
internal static class ScrollGeometry
{
    public const int EdgeMarginPx = 40;
    public const int StableMinMs = 150;
    public const int StableMaxMs = 900;
    public const int StablePollIntervalMs = 80;
    public const int DefaultDistancePercent = 60;
    public const int DefaultDurationMs = 300;
    public const int DefaultMaxScrolls = 10;
    public const int DefaultEdgeMaxScrolls = 20;

    public static (int x1, int y1, int x2, int y2) Compute(
        string direction, int distPct, ScreenSize screen, UiNode? container)
    {
        int left, top, right, bottom;
        if (container is not null)
        {
            left = container.Left;
            top = container.Top;
            right = container.Right;
            bottom = container.Bottom;
        }
        else
        {
            left = 0;
            top = 0;
            right = screen.Width;
            bottom = screen.Height;
        }
        left += EdgeMarginPx;
        top += EdgeMarginPx;
        right -= EdgeMarginPx;
        bottom -= EdgeMarginPx;
        if (right <= left)
        { left = 0; right = screen.Width; }
        if (bottom <= top)
        { top = 0; bottom = screen.Height; }
        int cx = (left + right) / 2;
        int cy = (top + bottom) / 2;
        int rangeX = right - left;
        int rangeY = bottom - top;
        int radiusX = rangeX * distPct / 200;
        int radiusY = rangeY * distPct / 200;
        return direction switch
        {
            "up" => (cx, cy + radiusY, cx, cy - radiusY),
            "down" => (cx, cy - radiusY, cx, cy + radiusY),
            "left" => (cx + radiusX, cy, cx - radiusX, cy),
            "right" => (cx - radiusX, cy, cx + radiusX, cy),
            _ => (cx, cy, cx, cy)
        };
    }
}

internal sealed class ScreenSizeTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public ScreenSizeTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "screen_size",
            Description = "Return the device's current display size in pixels (Override size if set, otherwise Physical size).",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ScreenSize size = ScreenSize.Read(adb.Client, device);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["width"] = size.Width,
            ["height"] = size.Height
        }));
    }
}

internal sealed class SwipeDirectionTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public SwipeDirectionTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "swipe_direction",
            Description = "One-shot directional swipe based on screen size — caller doesn't compute coordinates. `up` means content scrolls up (finger moves up, revealing content below).",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("direction", Schema.Enum("swipe direction", "up", "down", "left", "right"), true),
                ("distance_percent", Schema.Integer("swipe span as percent of screen, 10..95, default 60"), false),
                ("duration_ms", Schema.Integer("swipe duration, default 300"), false))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string direction = Args.RequireString(arguments, "direction");
        int distPct = Math.Clamp(Args.OptionalInt(arguments, "distance_percent", ScrollGeometry.DefaultDistancePercent), 10, 95);
        int durationMs = Math.Max(50, Args.OptionalInt(arguments, "duration_ms", ScrollGeometry.DefaultDurationMs));
        DeviceData device = adb.RequireDevice(serial);
        ScreenSize size = ScreenSize.Read(adb.Client, device);
        (int x1, int y1, int x2, int y2) = ScrollGeometry.Compute(direction, distPct, size, container: null);
        string command = string.Create(CultureInfo.InvariantCulture, $"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["direction"] = direction,
            ["from"] = new JsonObject { ["x"] = x1, ["y"] = y1 },
            ["to"] = new JsonObject { ["x"] = x2, ["y"] = y2 }
        }));
    }
}

internal sealed class ScrollToEdgeTool : IToolHandler
{
    private readonly AdbHub adb;
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public ScrollToEdgeTool(AdbHub adb, AgentRegistry agents)
    {
        this.adb = adb;
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "scroll_to_edge",
            Description = "Fling repeatedly in `direction` until the UI hierarchy stops changing (boundary reached) or max_steps is hit. Use this to jump to the top/bottom/leftmost/rightmost of a scrollable view in one call.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("direction", Schema.Enum("which edge", "up", "down", "left", "right"), true),
                ("max_steps", Schema.Integer("safety cap, default 20"), false),
                ("distance_percent", Schema.Integer("swipe span, 10..95, default 60"), false),
                ("duration_ms", Schema.Integer("swipe duration, default 300"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string direction = Args.RequireString(arguments, "direction");
        int maxSteps = Math.Max(1, Args.OptionalInt(arguments, "max_steps", ScrollGeometry.DefaultEdgeMaxScrolls));
        int distPct = Math.Clamp(Args.OptionalInt(arguments, "distance_percent", ScrollGeometry.DefaultDistancePercent), 10, 95);
        int durationMs = Math.Max(50, Args.OptionalInt(arguments, "duration_ms", ScrollGeometry.DefaultDurationMs));
        DeviceData device = adb.RequireDevice(serial);
        AgentSession session = agents.EnsureStarted(serial);
        ScreenSize size = ScreenSize.Read(adb.Client, device);
        string xml = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        string prevFingerprint = HierarchyMatcher.Fingerprint(xml);
        int steps = 0;
        for (int i = 0; i < maxSteps; i++)
        {
            (int x1, int y1, int x2, int y2) = ScrollGeometry.Compute(direction, distPct, size, container: null);
            string command = string.Create(CultureInfo.InvariantCulture, $"input swipe {x1} {y1} {x2} {y2} {durationMs}");
            ConsoleOutputReceiver receiver = new();
            adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
            steps++;
            xml = await WaitStableAsync(session).ConfigureAwait(false);
            string post = HierarchyMatcher.Fingerprint(xml);
            if (string.Equals(prevFingerprint, post, StringComparison.Ordinal))
            {
                return ToolCallResult.Json(new JsonObject
                {
                    ["ok"] = true,
                    ["direction"] = direction,
                    ["steps"] = steps,
                    ["reachedEdge"] = true
                });
            }
            prevFingerprint = post;
        }
        return ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["direction"] = direction,
            ["steps"] = steps,
            ["reachedEdge"] = false
        });
    }

    internal static async Task<string> WaitStableAsync(AgentSession session)
    {
        await Task.Delay(ScrollGeometry.StableMinMs).ConfigureAwait(false);
        string? prev = null;
        long startMs = Environment.TickCount64;
        string latest = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        while (true)
        {
            string fp = HierarchyMatcher.Fingerprint(latest);
            if (prev is not null && string.Equals(prev, fp, StringComparison.Ordinal))
            {
                return latest;
            }
            if (Environment.TickCount64 - startMs >= ScrollGeometry.StableMaxMs)
            {
                return latest;
            }
            prev = fp;
            await Task.Delay(ScrollGeometry.StablePollIntervalMs).ConfigureAwait(false);
            latest = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        }
    }
}

internal sealed class ScrollUntilVisibleTool : IToolHandler
{
    private readonly AdbHub adb;
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public ScrollUntilVisibleTool(AdbHub adb, AgentRegistry agents)
    {
        this.adb = adb;
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "scroll_until_visible",
            Description = "Scroll in `direction` until `selector` matches a visible element, or until the list hits its edge, or max_scrolls is exhausted. Uses hierarchy-fingerprint stability polling so it doesn't return mid-animation. One call replaces an LLM dump/swipe loop.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("selector", SelectorSchema.Build(), true),
                ("direction", Schema.Enum("scroll direction", "up", "down", "left", "right"), true),
                ("max_scrolls", Schema.Integer("safety cap, default 10"), false),
                ("distance_percent", Schema.Integer("swipe span as percent, 10..95, default 60"), false),
                ("duration_ms", Schema.Integer("swipe duration, default 300"), false),
                ("container", SelectorSchema.Build(), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        Selector target = SelectorSchema.Parse(arguments["selector"] as JsonObject);
        string direction = Args.RequireString(arguments, "direction");
        int maxScrolls = Math.Max(1, Args.OptionalInt(arguments, "max_scrolls", ScrollGeometry.DefaultMaxScrolls));
        int distPct = Math.Clamp(Args.OptionalInt(arguments, "distance_percent", ScrollGeometry.DefaultDistancePercent), 10, 95);
        int durationMs = Math.Max(50, Args.OptionalInt(arguments, "duration_ms", ScrollGeometry.DefaultDurationMs));
        Selector? container = null;
        if (arguments["container"] is JsonObject containerObj && containerObj.Count > 0)
        {
            try
            { container = SelectorSchema.Parse(containerObj); }
            catch (McpInvalidParamsException) { container = null; }
        }
        DeviceData device = adb.RequireDevice(serial);
        AgentSession session = agents.EnsureStarted(serial);
        ScreenSize size = ScreenSize.Read(adb.Client, device);
        string xml = await session.Http.GetStringAsync("/hierarchy?compressed=false").ConfigureAwait(false);
        UiNode? hit = HierarchyMatcher.FindFirst(xml, target);
        if (hit is not null)
        {
            return ToolCallResult.Json(new JsonObject
            {
                ["ok"] = true,
                ["scrollCount"] = 0,
                ["element"] = SelectorSchema.Encode(hit)
            });
        }
        UiNode? containerNode = container is not null ? HierarchyMatcher.FindFirst(xml, container) : null;
        string prevFingerprint = HierarchyMatcher.Fingerprint(xml);
        for (int i = 1; i <= maxScrolls; i++)
        {
            (int x1, int y1, int x2, int y2) = ScrollGeometry.Compute(direction, distPct, size, containerNode);
            string command = string.Create(CultureInfo.InvariantCulture, $"input swipe {x1} {y1} {x2} {y2} {durationMs}");
            ConsoleOutputReceiver receiver = new();
            adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
            xml = await ScrollToEdgeTool.WaitStableAsync(session).ConfigureAwait(false);
            hit = HierarchyMatcher.FindFirst(xml, target);
            if (hit is not null)
            {
                return ToolCallResult.Json(new JsonObject
                {
                    ["ok"] = true,
                    ["scrollCount"] = i,
                    ["element"] = SelectorSchema.Encode(hit)
                });
            }
            string post = HierarchyMatcher.Fingerprint(xml);
            if (string.Equals(prevFingerprint, post, StringComparison.Ordinal))
            {
                return ToolCallResult.Error($"reached edge after {i} scroll(s); {target.DescribeShort()} not found");
            }
            prevFingerprint = post;
            if (container is not null)
            {
                UiNode? refreshed = HierarchyMatcher.FindFirst(xml, container);
                if (refreshed is not null)
                { containerNode = refreshed; }
            }
        }
        return ToolCallResult.Error($"after {maxScrolls} scrolls, {target.DescribeShort()} not visible");
    }
}
