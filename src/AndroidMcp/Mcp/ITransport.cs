using System;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidMcp.Mcp;

internal interface ITransport
{
    public Task RunAsync(Func<string, Task<string?>> handler, CancellationToken ct);
}
