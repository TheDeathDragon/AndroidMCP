using System;
using System.Diagnostics;
using System.IO;
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

internal sealed class ShellTool : IToolHandler
{
    private const int MaxCommandLength = 4096;
    private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(60);

    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public ShellTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "shell",
            Description = "Run an `adb shell` command on the device and return stdout (max 60s). For long-running commands, prefer launching them with `&` and polling. Quote arguments with single quotes when they contain spaces.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("command", Schema.String("shell command to execute, e.g. `pm list packages -3`"), true))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string command = Args.RequireString(arguments, "command");
        if (command.Length > MaxCommandLength)
        {
            return ToolCallResult.Error($"command exceeds {MaxCommandLength}-char limit");
        }
        DeviceData device = adb.RequireDevice(serial);
        Task<string> work = Task.Run(() =>
        {
            ConsoleOutputReceiver receiver = new();
            adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
            return receiver.ToString() ?? string.Empty;
        });
        Task done = await Task.WhenAny(work, Task.Delay(ShellTimeout)).ConfigureAwait(false);
        if (done != work)
        {
            return ToolCallResult.Error($"shell timed out after {(int)ShellTimeout.TotalSeconds}s");
        }
        string output = await work.ConfigureAwait(false);
        return ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["command"] = command,
            ["output"] = output
        });
    }
}

internal sealed class InstallApkTool : IToolHandler
{
    private const string StagingPath = "/data/local/tmp/android-mcp-install.apk";

    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public InstallApkTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "install_apk",
            Description = "Push an APK from the host filesystem to the device and install it. Set `replace=true` (default) to upgrade an existing install. Returns pm install output.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("apk_path", Schema.String("absolute path to the APK on the host"), true),
                ("replace", Schema.Boolean("upgrade existing app (pm install -r), default true"), false),
                ("grant_permissions", Schema.Boolean("grant runtime permissions (pm install -g), default false"), false))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string apkPath = Args.RequireString(arguments, "apk_path");
        bool replace = Args.OptionalBool(arguments, "replace", true);
        bool grant = Args.OptionalBool(arguments, "grant_permissions", false);
        if (!File.Exists(apkPath))
        {
            return Task.FromResult(ToolCallResult.Error($"apk not found: {apkPath}"));
        }

        DeviceData device = adb.RequireDevice(serial);
        long pushMs;
        long installMs;
        long t0 = Stopwatch.GetTimestamp();
        bool isCancelled = false;
        using (SyncService sync = new(adb.Client, device))
        using (FileStream src = new(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            sync.Push(src, StagingPath, UnixFileStatus.DefaultFileMode, DateTimeOffset.Now,
                callback: null, useV2: false, isCancelled: in isCancelled);
        }
        long t1 = Stopwatch.GetTimestamp();
        StringBuilder cmd = new("pm install");
        if (replace)
        {
            cmd.Append(" -r");
        }

        if (grant)
        {
            cmd.Append(" -g");
        }

        cmd.Append(' ');
        cmd.Append(StagingPath);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(cmd.ToString(), device, receiver, Encoding.UTF8);
        long t2 = Stopwatch.GetTimestamp();
        try
        {
            ConsoleOutputReceiver rmReceiver = new();
            adb.Client.ExecuteRemoteCommand($"rm -f {StagingPath}", device, rmReceiver, Encoding.UTF8);
        }
        catch
        {
        }
        pushMs = (long)((t1 - t0) * 1000.0 / Stopwatch.Frequency);
        installMs = (long)((t2 - t1) * 1000.0 / Stopwatch.Frequency);
        string output = receiver.ToString() ?? string.Empty;
        bool ok = output.Contains("Success", StringComparison.Ordinal);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = ok,
            ["pushMs"] = pushMs,
            ["installMs"] = installMs,
            ["output"] = output
        }));
    }
}
