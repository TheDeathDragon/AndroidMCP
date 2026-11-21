using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Adb;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class ListPackagesTool : IToolHandler
{
    private readonly AgentRegistry agents;
    public ToolDescriptor Descriptor { get; }

    public ListPackagesTool(AgentRegistry agents)
    {
        this.agents = agents;
        Descriptor = new ToolDescriptor
        {
            Name = "list_packages",
            Description = "List installed packages with metadata (label, version, signature, install times, system flag) via the on-device agent. Set user_only=true to skip system apps.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("user_only", Schema.Boolean("when true, skip system apps; default false"), false))
        };
    }

    public async Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        bool userOnly = Args.OptionalBool(arguments, "user_only", false);
        AgentSession session = agents.EnsureStarted(serial);
        string url = userOnly ? "/packages?user_only=true" : "/packages";
        string json = await session.Http.GetStringAsync(url).ConfigureAwait(false);
        return ToolCallResult.Text(json);
    }
}

internal sealed class LaunchAppTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public LaunchAppTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "launch_app",
            Description = "Launch an app by package name: resolves its launcher activity and starts it the way the home screen does (an already running app comes back to the front).",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name, e.g. com.android.settings"), true))
        };
    }

    // `monkey -p <pkg> 1` spends ~3 s starting its own runtime; resolving the launcher
    // activity and `am start`-ing it takes ~0.4 s. Flags 0x10200000 are the launcher's
    // NEW_TASK | RESET_TASK_IF_NEEDED, so a running app resumes instead of stacking.
    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequirePackage(arguments);
        DeviceData device = adb.RequireDevice(serial);
        const string launcherIntent = "-a android.intent.action.MAIN -c android.intent.category.LAUNCHER";
        string command =
            $"c=$(cmd package query-activities --brief {launcherIntent} {packageName} | grep / | head -n 1 | tr -d ' '); " +
            $"if [ -n \"$c\" ]; then echo \"component=$c\"; am start {launcherIntent} -f 0x10200000 -n \"$c\"; echo \"rc=$?\"; " +
            $"else echo 'Error: no launcher activity in {packageName}'; fi";
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        string output = (receiver.ToString() ?? string.Empty).Trim();
        (bool ok, string? component) = ParseLaunchOutput(output);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = ok,
            ["package"] = packageName,
            ["component"] = component,
            ["output"] = output
        }));
    }

    // Judged on `am start`'s own exit status and messages only: a component may itself be
    // named like ".ErrorActivity", and some failures print an Exception without "Error".
    internal static (bool Ok, string? Component) ParseLaunchOutput(string output)
    {
        string? component = null;
        bool exitOk = false;
        bool failed = false;
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("component=", System.StringComparison.Ordinal))
            {
                component = line["component=".Length..];
            }
            else if (line.StartsWith("rc=", System.StringComparison.Ordinal))
            {
                exitOk = line == "rc=0";
            }
            // "Starting: Intent { cmp=... }" echoes the component, which may be named like
            // ".ExceptionViewer"; only am's own failure lines count.
            else if (line.StartsWith("Error", System.StringComparison.Ordinal)
                || line.StartsWith("Exception", System.StringComparison.Ordinal)
                || line.Contains("Exception:", System.StringComparison.Ordinal))
            {
                failed = true;
            }
        }
        return (component is not null && exitOk && !failed, component);
    }
}

internal sealed class StopAppTool : IToolHandler
{
    private readonly AdbHub adb;
    public ToolDescriptor Descriptor { get; }

    public StopAppTool(AdbHub adb)
    {
        this.adb = adb;
        Descriptor = new ToolDescriptor
        {
            Name = "stop_app",
            Description = "Force-stop a package via `am force-stop`. Tears down all processes and pending alarms for the app.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequirePackage(arguments);
        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand($"am force-stop {packageName}", device, receiver, Encoding.UTF8);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = true,
            ["package"] = packageName
        }));
    }
}
