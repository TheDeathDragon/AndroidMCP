using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class PushFileTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public PushFileTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "push_file",
            Description = "Upload a file from the host filesystem to the device. Useful for deploying configs, test fixtures, screenshots back to the device. Use install_apk for APKs.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("local_path", Schema.String("absolute path on the host"), true),
                ("remote_path", Schema.String("absolute destination path on the device, e.g. /sdcard/Download/foo.txt"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string localPath = Args.RequireString(arguments, "local_path");
        string remotePath = Args.RequireString(arguments, "remote_path");
        if (!File.Exists(localPath))
        {
            return Task.FromResult(ToolCallResult.Error($"local file not found: {localPath}"));
        }

        DeviceData device = adb.RequireDevice(serial);
        long t0 = Stopwatch.GetTimestamp();
        bool isCancelled = false;
        long size;
        using (SyncService sync = new(adb.Client, device))
        using (FileStream src = new(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            size = src.Length;
            sync.Push(src, remotePath, UnixFileStatus.DefaultFileMode, DateTimeOffset.Now,
                callback: null, useV2: false, isCancelled: in isCancelled);
        }
        long elapsedMs = (long)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["bytes"] = size,
            ["elapsedMs"] = elapsedMs,
            ["remotePath"] = remotePath
        }));
    }
}

internal sealed class PullFileTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public PullFileTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "pull_file",
            Description = "Download a file from the device to the host filesystem. Useful for collecting logs, screenshots, app data exports.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("remote_path", Schema.String("absolute path on the device, e.g. /sdcard/log.txt"), true),
                ("local_path", Schema.String("absolute destination path on the host"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string remotePath = Args.RequireString(arguments, "remote_path");
        string localPath = Args.RequireString(arguments, "local_path");
        DeviceData device = adb.RequireDevice(serial);
        string? dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        long t0 = Stopwatch.GetTimestamp();
        bool isCancelled = false;
        long size;
        using (SyncService sync = new(adb.Client, device))
        using (FileStream dst = new(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            sync.Pull(remotePath, dst, callback: null, useV2: false, isCancelled: in isCancelled);
            size = dst.Length;
        }
        long elapsedMs = (long)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["bytes"] = size,
            ["elapsedMs"] = elapsedMs,
            ["localPath"] = localPath
        }));
    }
}
