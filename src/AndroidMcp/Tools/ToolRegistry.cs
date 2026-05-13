using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using AndroidMcp.Mcp;

namespace AndroidMcp.Tools;

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolHandler> handlers = new();
    private readonly List<ToolDescriptor> descriptors = new();

    public IReadOnlyList<ToolDescriptor> Descriptors => descriptors;

    public void Register(IToolHandler handler)
    {
        handlers[handler.Descriptor.Name] = handler;
        descriptors.Add(handler.Descriptor);
    }

    public bool TryGet(string name, [NotNullWhen(true)] out IToolHandler? handler) =>
        handlers.TryGetValue(name, out handler);
}
