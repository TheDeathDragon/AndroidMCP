using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndroidMcp.Logging;

namespace AndroidMcp.Mcp;

// GET /sse — server-sent event stream of JSON-RPC responses.
// POST /message — single JSON-RPC request, response pushed back over /sse.
// Loopback-only by design.
internal sealed class HttpSseTransport : ITransport
{
    private readonly int port;
    private HttpListenerResponse? sseClient;
    private readonly object sseGate = new();

    public HttpSseTransport(int port)
    {
        this.port = port;
    }

    public async Task RunAsync(Func<string, Task<string?>> handler, CancellationToken ct)
    {
        HttpListener listener = new();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://[::1]:{port}/");
        listener.Start();
        Log.Info($"http+sse transport listening on http://127.0.0.1:{port}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                _ = Task.Run(() => HandleAsync(ctx, handler, ct), ct);
            }
        }
        finally
        {
            try
            { listener.Stop(); }
            catch { }
            try
            { listener.Close(); }
            catch { }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, Func<string, Task<string?>> handler, CancellationToken ct)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            string method = ctx.Request.HttpMethod;
            if (method == "GET" && path == "/sse")
            {
                await HoldSseAsync(ctx, ct).ConfigureAwait(false);
                return;
            }
            if (method == "POST" && path == "/message")
            {
                await HandleMessageAsync(ctx, handler, ct).ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Log.Warn($"http transport handler error: {ex.Message}");
            try
            { ctx.Response.StatusCode = 500; ctx.Response.Close(); }
            catch { }
        }
    }

    private async Task HoldSseAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.Add("Cache-Control", "no-cache");
        ctx.Response.Headers.Add("Connection", "keep-alive");
        ctx.Response.SendChunked = true;
        lock (sseGate)
        {
            try
            { sseClient?.OutputStream.Close(); }
            catch { }
            sseClient = ctx.Response;
        }
        Log.Info("sse client connected");
        try
        {
            // Keep the response stream alive until the client disconnects or we shut down.
            byte[] heartbeat = Encoding.UTF8.GetBytes(": keep-alive\n\n");
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(15_000, ct).ConfigureAwait(false);
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(heartbeat, ct).ConfigureAwait(false);
                    await ctx.Response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }
        finally
        {
            lock (sseGate)
            {
                if (sseClient == ctx.Response)
                {
                    sseClient = null;
                }
            }
            try
            { ctx.Response.Close(); }
            catch { }
            Log.Info("sse client disconnected");
        }
    }

    private async Task HandleMessageAsync(HttpListenerContext ctx, Func<string, Task<string?>> handler, CancellationToken ct)
    {
        string body;
        using (StreamReader reader = new(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
        {
            body = (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim();
        }
        ctx.Response.StatusCode = 202;
        ctx.Response.ContentLength64 = 0;
        ctx.Response.Close();
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        string? response = await handler(body).ConfigureAwait(false);
        if (response is null)
        {
            return;
        }

        await PushSseAsync(response, ct).ConfigureAwait(false);
    }

    private async Task PushSseAsync(string payload, CancellationToken ct)
    {
        HttpListenerResponse? target;
        lock (sseGate)
        {
            target = sseClient;
        }
        if (target is null)
        {
            Log.Warn("dropping response — no sse client connected");
            return;
        }
        // SSE frame format: "data: <line>\n\n". Embed the JSON as one line.
        byte[] frame = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
        try
        {
            await target.OutputStream.WriteAsync(frame, ct).ConfigureAwait(false);
            await target.OutputStream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"sse push failed: {ex.Message}");
            lock (sseGate)
            {
                if (sseClient == target)
                {
                    sseClient = null;
                }
            }
        }
    }
}
