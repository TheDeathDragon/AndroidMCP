using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal interface IToolHandler
{
    public ToolDescriptor Descriptor { get; }
    public Task<ToolCallResult> InvokeAsync(JsonObject arguments);
}
