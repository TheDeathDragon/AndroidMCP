using System;
using System.Threading;
using System.Threading.Tasks;
using AndroidMcp.Adb;
using AndroidMcp.Logging;
using AndroidMcp.Mcp;
using AndroidMcp.Tools;

namespace AndroidMcp;

internal static class Program
{
    private const int DefaultHttpPort = 9460;

    public static async Task<int> Main(string[] args)
    {
        TransportKind kind = TransportKind.Stdio;
        int httpPort = DefaultHttpPort;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--stdio":
                    kind = TransportKind.Stdio;
                    break;
                case "--http":
                    kind = TransportKind.Http;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
                    {
                        httpPort = p;
                        i++;
                    }
                    break;
                case "--debug":
                    Log.MinLevel = LogLevel.Debug;
                    break;
                case "--quiet":
                    Log.MinLevel = LogLevel.Warn;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                default:
                    Log.Warn($"unknown arg: {a}");
                    break;
            }
        }
        AdbHub adb = new();
        PortAllocator portAllocator = new();
        AgentRegistry agents = new(adb, portAllocator);
        ToolRegistry tools = new();
        tools.Register(new ListDevicesTool(adb));
        tools.Register(new ScreenshotTool(agents));
        tools.Register(new DumpHierarchyTool(agents));
        tools.Register(new DeviceInfoTool(agents));
        tools.Register(new InputClickTool(adb));
        tools.Register(new InputSwipeTool(adb));
        tools.Register(new InputTextTool(adb));
        tools.Register(new KeyEventTool(adb));
        tools.Register(new ShellTool(adb));
        tools.Register(new InstallApkTool(adb));
        tools.Register(new ListPackagesTool(agents));
        tools.Register(new LaunchAppTool(adb));
        tools.Register(new StopAppTool(adb));
        tools.Register(new ClearAppDataTool(adb));
        tools.Register(new SetPackageEnabledTool(adb));
        tools.Register(new GetPackageInfoTool(adb));
        tools.Register(new GetTopActivityTool(adb));
        tools.Register(new LongPressTool(adb));
        tools.Register(new PushFileTool(adb));
        tools.Register(new PullFileTool(adb));
        tools.Register(new ScreenSizeTool(adb));
        tools.Register(new SwipeDirectionTool(adb));
        tools.Register(new ScrollToEdgeTool(adb, agents));
        tools.Register(new FindElementTool(agents));
        tools.Register(new TapElementTool(adb, agents));
        tools.Register(new WaitForElementTool(agents));
        tools.Register(new ScrollUntilVisibleTool(adb, agents));
        tools.Register(new PressHomeTool(adb));
        tools.Register(new PressBackTool(adb));
        tools.Register(new PressRecentsTool(adb));
        tools.Register(new WakeUnlockTool(adb));
        tools.Register(new ClearRecentsTool(adb));
        McpServer server = new(tools);
        ITransport transport = kind switch
        {
            TransportKind.Http => new HttpSseTransport(httpPort),
            _ => new StdioTransport()
        };
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Log.Info($"android-mcp v{BuildInfo.Version} ({BuildInfo.GitHash}) starting (transport={kind}, tools={tools.Descriptors.Count})");
        try
        {
            await transport.RunAsync(server.HandleAsync, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            agents.Dispose();
        }
        Log.Info("android-mcp stopped");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: android-mcp [--stdio | --http <port>] [--debug | --quiet]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --stdio          JSON-RPC over stdin/stdout (default)");
        Console.Error.WriteLine("  --http <port>    HTTP+SSE transport on 127.0.0.1:<port> (default 9460)");
        Console.Error.WriteLine("  --debug          verbose logging to stderr");
        Console.Error.WriteLine("  --quiet          warnings only");
    }
}

internal enum TransportKind
{
    Stdio,
    Http
}
