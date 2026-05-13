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
            Description = "Launch an app by package name. Uses `monkey -p <pkg> 1` which resolves the launcher activity automatically — works whether or not you know the entry component.",
            InputSchema = Schema.Object(
                ("serial", Schema.String(Schema.SerialDescription), true),
                ("package", Schema.String("Android package name, e.g. com.android.settings"), true))
        };
    }

    public Task<ToolCallResult> InvokeAsync(JsonObject arguments)
    {
        string serial = Args.RequireSerial(arguments);
        string packageName = Args.RequireString(arguments, "package");
        DeviceData device = adb.RequireDevice(serial);
        string command = $"monkey -p {packageName} -c android.intent.category.LAUNCHER 1";
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
        string output = receiver.ToString() ?? string.Empty;
        bool ok = !output.Contains("No activities found", System.StringComparison.Ordinal);
        return Task.FromResult(ToolCallResult.Json(new JsonObject
        {
            ["ok"] = ok,
            ["package"] = packageName,
            ["output"] = output
        }));
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
        string packageName = Args.RequireString(arguments, "package");
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
