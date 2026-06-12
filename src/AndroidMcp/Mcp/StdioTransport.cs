using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndroidMcp.Logging;

namespace AndroidMcp.Mcp;

internal sealed class StdioTransport : ITransport
{
    public async Task RunAsync(Func<string, Task<string?>> handler, CancellationToken ct)
    {
        using TextReader reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using TextWriter writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        SemaphoreSlim writeGate = new(1, 1);
        List<Task> pending = new();
        Log.Info("stdio transport listening");
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                Log.Info("stdin closed; transport exiting");
                break;
            }
            if (line.Length == 0)
            {
                continue;
            }

            pending.RemoveAll(static t => t.IsCompleted);
            pending.Add(Task.Run(async () =>
            {
                try
                {
                    string? response = await handler(line).ConfigureAwait(false);
                    if (response is null)
                    {
                        return;
                    }

                    await writeGate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeGate.Release();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Error($"stdio dispatch error: {ex.Message}");
                }
            }, ct));
        }
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
}
