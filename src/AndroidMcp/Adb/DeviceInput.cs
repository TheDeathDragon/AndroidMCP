using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using AndroidMcp.Logging;

namespace AndroidMcp.Adb;

// `adb shell input` spawns a JVM per call (~300 ms); the agent injects in-process (~25 ms).
// The shell path stays as a fallback when the agent cannot start or predates /input/*
// (e.g. an older build already serving :9500 that we reuse).
internal sealed class DeviceInput
{
    public const string ViaAgent = "agent";
    public const string ViaShell = "shell";
    private static readonly Regex KeyNameRegex = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);
    // Gestures must finish well inside the 30 s HTTP timeout: a request that times out while
    // the agent is still holding the pointer reports failure for a gesture that lands.
    public const int MaxGestureMs = 10_000;

    private readonly AdbHub adb;
    private readonly AgentRegistry agents;

    public DeviceInput(AdbHub adb, AgentRegistry agents)
    {
        this.adb = adb;
        this.agents = agents;
    }

    public Task<string> TapAsync(string serial, int x, int y) =>
        RunAsync(serial,
            http => http.GetAsync(Inv($"/input/tap?x={x}&y={y}")),
            Inv($"input tap {x} {y}"));

    // easing=linear matches `input swipe` (constant velocity, so lists fling); the agent's
    // default ease-in-out ends near zero velocity. The agent schedules moves on absolute
    // uptime; a batch of moves with relative waits drifts by each move's injection cost
    // (a 150 ms gesture measured ~190 ms). An agent too old for easing would ignore it and
    // ease, so the shell's linear swipe is used there instead.
    public Task<string> SwipeAsync(string serial, int x1, int y1, int x2, int y2, int durationMs)
    {
        RequireGestureDuration(durationMs);
        return RunAsync(serial,
            http => http.GetAsync(Inv($"/input/swipe?x1={x1}&y1={y1}&x2={x2}&y2={y2}&durationMs={durationMs}&easing=linear")),
            Inv($"input swipe {x1} {y1} {x2} {y2} {durationMs}"),
            agentUsable: session => session.SupportsLinearSwipe());
    }

    public Task<string> LongPressAsync(string serial, int x, int y, int durationMs)
    {
        RequireGestureDuration(durationMs);
        return RunAsync(serial,
            http => http.GetAsync(Inv($"/input/tap?x={x}&y={y}&durationMs={durationMs}")),
            Inv($"input swipe {x} {y} {x} {y} {durationMs}"));
    }

    private static void RequireGestureDuration(int durationMs)
    {
        if (durationMs < 0 || durationMs > MaxGestureMs)
        {
            throw new InvalidOperationException($"duration_ms must be 0..{MaxGestureMs}, got {durationMs}");
        }
    }

    // The key reaches a shell command on the fallback path; keycode names and numbers
    // never need anything beyond [A-Za-z0-9_].
    public Task<string> KeyAsync(string serial, string key)
    {
        if (!KeyNameRegex.IsMatch(key))
        {
            throw new InvalidOperationException($"invalid key '{key}': use a keycode name (BACK, KEYCODE_HOME) or number");
        }
        return RunAsync(serial,
            http => http.GetAsync("/input/key?keyCode=" + key),
            "input keyevent " + key);
    }

    // Non-ASCII has no shell fallback: `input text` cannot type it. A JSON body, not the
    // query: the agent caps the request line at 8192 bytes, which percent-encoded CJK
    // (9 bytes per character) exceeds at ~900 characters.
    public Task<string> TextAsync(string serial, string text) =>
        RunAsync(serial,
            http => http.PostAsync("/input/text", new StringContent(
                new JsonObject { ["text"] = text }.ToJsonString(), Encoding.UTF8, "application/json")),
            IsAscii(text) ? "input text " + EscapeForInputText(text) : null,
            notInjectedHint: IsAscii(text) ? null : "no focused editable field; focus an input field first");

    private async Task<string> RunAsync(
        string serial,
        Func<HttpClient, Task<HttpResponseMessage>> agentCall,
        string? shellCommand,
        string? notInjectedHint = null,
        Func<AgentSession, bool>? agentUsable = null)
    {
        AgentSession? session = TryStartAgent(serial);
        if (session is not null && !session.InputEndpointsMissing && (agentUsable?.Invoke(session) ?? true) &&
            await CallAgentAsync(session, agentCall, notInjectedHint).ConfigureAwait(false))
        {
            return ViaAgent;
        }
        if (shellCommand is null)
        {
            throw new InvalidOperationException(session is null
                ? "non-ASCII text needs the on-device agent, which failed to start"
                : "non-ASCII text needs /input/text, which the agent serving this device predates; stop it so a current agent can start");
        }

        DeviceData device = adb.RequireDevice(serial);
        ConsoleOutputReceiver receiver = new();
        adb.Client.ExecuteRemoteCommand(shellCommand, device, receiver, Encoding.UTF8);
        return ViaShell;
    }

    private AgentSession? TryStartAgent(string serial)
    {
        try
        {
            return agents.EnsureStarted(serial);
        }
        catch (InvalidOperationException ex)
        {
            Log.Debug($"agent unavailable for input: {ex.Message}");
            return null;
        }
    }

    // True when injected; false only when the agent predates /input, the one case where the
    // shell is known not to duplicate anything. Every other failure throws without a shell
    // replay: ok:false comes from the same injectInputEvent the shell would call, a 400 means
    // `input` would reject it too (silently), and a 5xx or transport error may land after
    // ACTION_DOWN was delivered, so a replay would perform a second gesture.
    private static async Task<bool> CallAgentAsync(
        AgentSession session,
        Func<HttpClient, Task<HttpResponseMessage>> agentCall,
        string? notInjectedHint)
    {
        using HttpResponseMessage resp = await agentCall(session.Http).ConfigureAwait(false);
        string body = (await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            session.InputEndpointsMissing = true;
            Log.Info($"agent on transport {session.TransportId} has no /input endpoints; using shell input");
            return false;
        }
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"agent input failed (HTTP {(int)resp.StatusCode}): {body}");
        }
        bool ok;
        try
        {
            ok = JsonNode.Parse(body)?["ok"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            ok = false;
        }
        if (!ok)
        {
            throw new InvalidOperationException(notInjectedHint ?? $"agent could not inject the event: {body}");
        }
        return true;
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    internal static bool IsAscii(string text)
    {
        foreach (char c in text)
        {
            if (c > 0x7E || c < 0x20)
            {
                return false;
            }
        }
        return true;
    }

    internal static string EscapeForInputText(string text)
    {
        StringBuilder sb = new(text.Length + 4);
        foreach (char c in text)
        {
            switch (c)
            {
                case ' ':
                    sb.Append("%s");
                    break;
                // Shell metacharacters, globs (`*` would expand against the cwd) and `#`
                // (a leading one comments the whole word out).
                case '\'' or '"' or '\\' or '`' or '$' or '&' or '|' or ';' or '(' or ')' or '<' or '>'
                    or '*' or '?' or '[' or ']' or '{' or '}' or '~' or '#' or '!':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
