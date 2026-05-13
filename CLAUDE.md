# AndroidMCP

MCP server for Android 11+ devices. C# .NET 8 + Kotlin agent (submodule).
Talks ADB protocol directly via AdvancedSharpAdbClient; pushes an
`app_process` agent for `UiAutomation` operations.

License: Apache 2.0.

## Install

End users install via:

```powershell
iwr https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.ps1 | iex
```

`scripts/install.ps1` resolves the latest GitHub Release, downloads the
win-x64 zip, extracts to `%LOCALAPPDATA%\Programs\android-mcp\`, and prints
the Claude Code config snippet (or merges with `-AutoConfig`).

## Build

```
git submodule update --init --recursive
agent\build.bat
dotnet build  AndroidMcp.slnx
dotnet test   AndroidMcp.slnx
publish.bat
```

Agent compile needs an unstripped `android.jar` at
`$ANDROID_HOME/platforms/android-36/android.jar` (use
[Reginer/aosp-android-jar](https://github.com/Reginer/aosp-android-jar)).
CI overlays this automatically.

## Layout

```
src/AndroidMcp/
├── Program.cs              # entry, transport selection, tool registration
├── Adb/                    # ADB layer, agent management, selector matching
├── Mcp/                    # JSON-RPC protocol + stdio/HTTP transports
├── Tools/                  # one file per cluster (Screen/Input/Shell/App/Element/Scroll/Nav/...)
└── Logging/Log.cs          # stderr-only logger
agent/                      # submodule -> github.com/TheDeathDragon/AndroidAgent
tools/agent-server.jar      # built by agent/build.bat
scripts/                    # install / publish / smoke / extract_changelog
.github/workflows/          # ci.yml + release.yml
```

## Architecture

- **Transport**: stdio (default) or HTTP+SSE on loopback.
- **Multi-device**: every tool except `list_devices` requires `serial`. No
  active-device state. `AgentRegistry` lazily creates one `AgentSession`
  per serial; each session gets a host port from `PortAllocator` (9601-9699).
- **Agent reuse**: `AgentSession.TryReuseCanonicalAgent` first probes
  `tcp:9500` (`UiAutomation` is a per-device singleton). If a healthy agent
  already runs there, MCP forwards instead of pushing its own jar.
- **Selector matching**: `HierarchyMatcher.FindAll` walks the agent's
  accessibility XML. AT-tuned constants live in
  `ScrollTools.ScrollGeometry` (40 px edge inset, 60 % default span,
  150-900 ms stability window) — don't tweak without retesting.
- **Lean hierarchy**: `HierarchyMatcher.ToLean` collapses XML into compact
  JSON (~5× smaller). `dump_hierarchy` defaults to `format=lean`; CJK
  emitted raw via `UnsafeRelaxedJsonEscaping`.
- **Initialize instructions**: `Mcp/Protocol.cs` `ServerInstructions.Text`
  ships via `initialize.result.instructions`. Clients inject into the LLM's
  system prompt once per session.
- **Versioning**: `YYMM.NNNN` (year-month + zero-padded
  `git rev-list --count HEAD`). MSBuild `GenerateBuildInfo` target writes
  `BuildInfo.g.cs` before compile. Tag releases as `v<YYMM.NNNN>`.

## Conventions

- Open-source terseness: no narrative comments, no obvious-WHAT comments,
  only why-comments for non-obvious decisions. CHANGELOG entries are
  1-line bullets. README "Tips" are bare bullet lists with no preamble.
- `dotnet format --verify-no-changes` must pass before commit (CI enforces).
- `private static readonly` and `private const` use PascalCase; instance
  fields camelCase (`.editorconfig` enforces).
- No Chinese in code or commits; READMEs are bilingual.
- stdio safety: never `Console.WriteLine` from production paths. Use
  `AndroidMcp.Logging.Log` (writes stderr).
- AdbClient calls always need `DeviceData device`; obtain via
  `AdbHub.RequireDevice(serial)`.

## Release flow

1. Land changes on `main` (CI green).
2. Edit `CHANGELOG.md`: rename `[Unreleased]` to `[YYMM.NNNN]` where NNNN
   is the commit count **after** the release commit you're about to make.
3. `git commit -m "release: <version>"` and `git tag v<version>`.
4. `git push origin main --tags`. `release.yml` builds + extracts the
   CHANGELOG section + creates the GitHub Release.

## Dependencies

| Package                | Purpose                          |
|------------------------|----------------------------------|
| AdvancedSharpAdbClient | ADB protocol client              |
| xunit                  | tests                            |
