# AndroidMCP

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/TheDeathDragon/AndroidMCP)](https://github.com/TheDeathDragon/AndroidMCP/releases)

[English](README.md) | [简体中文](README.zh.md)

MCP server for Android 11+ devices. C# .NET 8.

## Install

Windows (PowerShell):

```powershell
iwr https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.ps1 | iex
```

Or auto-merge into `~/.claude.json`:

```powershell
& ([scriptblock]::Create((iwr https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.ps1))) -AutoConfig
```

Linux (x86_64):

```bash
curl -fsSL https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.sh | sh
```

Or auto-merge into `~/.claude.json` (needs `python3`):

```bash
curl -fsSL https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.sh | sh -s -- --auto-config
```

Both installers fetch the latest release, extract to
`%LOCALAPPDATA%\Programs\android-mcp\` / `~/.local/share/android-mcp/`, and
print the Claude Code config (or merge it with `-AutoConfig` /
`--auto-config`). Other `install.sh` flags: `--version <YYMM.NNNN>`,
`--install-dir <dir>`.

After install, `/reload-plugins` in Claude Code to load the tools.

Requires `adb` on PATH. The Linux build is self-contained (no .NET runtime
needed) but glibc-based — it does not run on musl distros such as Alpine.

## Claude Code config

`-AutoConfig` / `--auto-config` writes this for you. Manual:

```json
{
  "mcpServers": {
    "android-mcp": {
      "type": "stdio",
      "command": "C:/Users/<you>/AppData/Local/Programs/android-mcp/android-mcp.exe"
    }
  }
}
```

On Linux the command is `/home/<you>/.local/share/android-mcp/android-mcp`.

## Tools

| Name                  | Purpose                                            |
|-----------------------|----------------------------------------------------|
| `list_devices`        | enumerate devices visible to adb                   |
| `screenshot`          | PNG via on-device agent                            |
| `dump_hierarchy`      | XML / lean JSON view tree                          |
| `device_info`         | device info                                        |
| `click`               | tap (x, y)                                         |
| `long_press`          | hold (x, y) for `duration_ms`                      |
| `swipe`               | swipe with duration                                |
| `input_text`          | type ASCII into focused field                      |
| `key_event`           | KEYCODE by name or number                          |
| `shell`               | arbitrary `adb shell` (60 s timeout)               |
| `push_file`           | host -> device file copy                           |
| `pull_file`           | device -> host file copy                           |
| `install_apk`         | push + `pm install`                                |
| `list_packages`       | installed packages with metadata                   |
| `get_package_info`    | single-app summary                                 |
| `launch_app`          | `monkey -p <pkg>` launcher intent                  |
| `stop_app`            | `am force-stop`                                    |
| `clear_app_data`      | `pm clear`                                         |
| `set_package_enabled` | `pm enable` / `pm disable-user`                    |
| `get_top_activity`    | foreground component from `dumpsys activity`       |
| `screen_size`         | display W×H from `wm size`                         |
| `swipe_direction`     | directional swipe sized to the screen              |
| `scroll_to_edge`      | fling to edge                                      |
| `find_element`        | selector → matching nodes                          |
| `tap_element`         | tap element center                                 |
| `wait_for_element`    | poll hierarchy until match or timeout              |
| `scroll_until_visible`| scroll loop with stability + edge detection        |
| `press_home`          | KEYCODE_HOME                                       |
| `press_back`          | KEYCODE_BACK                                       |
| `press_recents`       | KEYCODE_APP_SWITCH                                 |
| `wake_unlock`         | wake screen + `wm dismiss-keyguard` (no PIN)       |
| `clear_recents`       | remove non-home tasks via `am stack remove`        |

Every tool except `list_devices` needs `serial`. Call `list_devices` first.
If the device may be asleep or on the lockscreen, call `wake_unlock` once.

## Tips

- Prefer `find_element` / `tap_element` over `dump_hierarchy` + `click`.
- `dump_hierarchy` returns lean JSON by default; pass `format=xml` for raw.
- XPath siblings read labeled values: `//node[@text='X']/following-sibling::node[1]`.
- `screenshot` is for visual checks, not text extraction.

`initialize.result.instructions` ships a condensed version of these tips
so MCP-aware clients inject them into the model's system prompt.

## Run standalone

```
%LOCALAPPDATA%\Programs\android-mcp\android-mcp.exe                # stdio
%LOCALAPPDATA%\Programs\android-mcp\android-mcp.exe --http 9460    # HTTP + SSE
~/.local/share/android-mcp/android-mcp                             # stdio (Linux)
~/.local/share/android-mcp/android-mcp --http 9460                 # HTTP + SSE (Linux)
```

Flags: `--debug`, `--quiet`.

## Build from source

Windows:

```
git clone --recursive https://github.com/TheDeathDragon/AndroidMCP.git
cd AndroidMCP
agent\build.bat
publish.bat                # win-x64
publish.bat linux-x64      # linux-x64
```

Linux:

```bash
git clone --recursive https://github.com/TheDeathDragon/AndroidMCP.git
cd AndroidMCP/agent && ./gradlew assembleRelease && cd ..
cp agent/build/outputs/apk/release/agent-server-release-unsigned.apk tools/agent-server.jar
dotnet publish src/AndroidMcp/AndroidMcp.csproj -c Release -r linux-x64 -o publish/linux-x64
```

## License

[Apache 2.0](LICENSE)
