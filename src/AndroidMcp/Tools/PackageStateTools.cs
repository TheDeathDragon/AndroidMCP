using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class ClearAppDataTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public ClearAppDataTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "clear_app_data",
            Description = "Reset an app to a fresh-install state via `pm clear`. Wipes preferences, databases, caches, and revokes runtime permissions. The app stays installed.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequireString(arguments, "package");
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand($"pm clear {packageName}", device, receiver, Encoding.UTF8);
        string output = (receiver.ToString() ?? string.Empty).Trim();
        bool ok = output.Contains("Success", StringComparison.Ordinal);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = ok,
            ["package"] = packageName,
            ["output"] = output
        }));
    }
}

internal sealed class SetPackageEnabledTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public SetPackageEnabledTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "set_package_enabled",
            Description = "Enable or disable a package via `pm enable` / `pm disable-user`. Disabled packages stay installed but become invisible to launchers and intent resolution.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name"), true),
                ("enabled", Schema.Boolean("true to enable, false to disable"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequireString(arguments, "package");
        bool enabled = arguments["enabled"]?.GetValue<bool>()
            ?? throw new McpInvalidParamsException("missing 'enabled'");
        DeviceData device = adb.RequireDevice(serial);
        string verb = enabled ? "enable" : "disable-user --user 0";
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand($"pm {verb} {packageName}", device, receiver, Encoding.UTF8);
        string output = (receiver.ToString() ?? string.Empty).Trim();
        bool ok = output.Contains(enabled ? "new state: enabled" : "new state: disabled", StringComparison.Ordinal);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = ok,
            ["package"] = packageName,
            ["enabled"] = enabled,
            ["output"] = output
        }));
    }
}

internal sealed class GetPackageInfoTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public GetPackageInfoTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "get_package_info",
            Description = "Single-package summary: versionName, versionCode, install paths, enabled state, target SDK. Faster than list_packages when you know the package up front.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequireString(arguments, "package");
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand($"dumpsys package {packageName}", device, receiver, Encoding.UTF8);
        string raw = receiver.ToString() ?? string.Empty;
        if (!raw.Contains("Package [", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolCallResult.Error($"package not installed: {packageName}"));
        }
        string? versionName = ExtractField(raw, "versionName=");
        string? versionCode = ExtractField(raw, "versionCode=");
        string? targetSdk = ExtractField(raw, "targetSdk=");
        string? minSdk = ExtractField(raw, "minSdk=");
        string? firstInstall = ExtractField(raw, "firstInstallTime=");
        string? lastUpdate = ExtractField(raw, "lastUpdateTime=");
        string? path = ExtractField(raw, "codePath=");
        string? dataDir = ExtractField(raw, "dataDir=");
        bool enabled = !raw.Contains("enabled=2", StringComparison.Ordinal)
            && !raw.Contains("enabled=3", StringComparison.Ordinal);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["package"] = packageName,
            ["versionName"] = versionName,
            ["versionCode"] = versionCode,
            ["targetSdk"] = targetSdk,
            ["minSdk"] = minSdk,
            ["enabled"] = enabled,
            ["codePath"] = path,
            ["dataDir"] = dataDir,
            ["firstInstallTime"] = firstInstall,
            ["lastUpdateTime"] = lastUpdate
        }));
    }

    private static string? ExtractField(string text, string key)
    {
        int idx = text.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        int start = idx + key.Length;
        int end = start;
        while (end < text.Length && text[end] != ' ' && text[end] != '\n' && text[end] != '\r')
        {
            end++;
        }
        return text.Substring(start, end - start);
    }
}

internal sealed class GetTopActivityTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public GetTopActivityTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "get_top_activity",
            Description = "Return the foreground activity component (package/Activity). Reads `dumpsys activity activities | grep ResumedActivity`.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand("dumpsys activity activities", device, receiver, Encoding.UTF8);
        string raw = receiver.ToString() ?? string.Empty;
        string? component = ParseActivity(raw);
        if (component is null)
        {
            return Task.FromResult(ToolCallResult.Json(new JsonObject
            {
                ["ok"] = false,
                ["reason"] = "no resumed activity found in dumpsys output"
            }));
        }
        int slash = component.IndexOf('/');
        string packageName = slash > 0 ? component.Substring(0, slash) : component;
        string activity = slash > 0 ? component.Substring(slash + 1) : string.Empty;
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["component"] = component,
            ["package"] = packageName,
            ["activity"] = activity
        }));
    }

    private static string? ParseActivity(string raw)
    {
        foreach (string line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("topResumedActivity", StringComparison.Ordinal)
                && !trimmed.StartsWith("mResumedActivity", StringComparison.Ordinal)
                && !trimmed.StartsWith("ResumedActivity:", StringComparison.Ordinal))
            {
                continue;
            }
            int braceIdx = line.IndexOf('{');
            int closeBrace = braceIdx >= 0 ? line.IndexOf('}', braceIdx + 1) : -1;
            if (braceIdx < 0 || closeBrace < 0)
            {
                continue;
            }
            string inside = line.Substring(braceIdx + 1, closeBrace - braceIdx - 1);
            foreach (string part in inside.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int slash = part.IndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }
                if (part.StartsWith("u0", StringComparison.Ordinal))
                {
                    continue;
                }
                return part.TrimEnd(',', '}');
            }
        }
        return null;
    }
}
