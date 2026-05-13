# AndroidMCP

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/TheDeathDragon/AndroidMCP)](https://github.com/TheDeathDragon/AndroidMCP/releases)

[English](README.md) | [简体中文](README.zh.md)

适用于 Android 11+ 设备的 MCP 工具。使用 C# .NET 8 实现。

## 安装

PowerShell

```powershell
iwr https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.ps1 | iex
```

或自动写入 `~/.claude.json`：

```powershell
& ([scriptblock]::Create((iwr https://raw.githubusercontent.com/TheDeathDragon/AndroidMCP/main/scripts/install.ps1))) -AutoConfig
```

安装器会下载最新 release，解压到 `%LOCALAPPDATA%\Programs\android-mcp\`，并打印
Claude Code 配置片段（或加 `-AutoConfig` 直接合并）。

装完后 Claude Code 里 `/reload-plugins`，`android-mcp` 工具就可用。

需要配置好 ADB 环境变量。

## Claude Code 配置

加 `-AutoConfig` 安装器会自动写。手动配则把以下加入 `~/.claude.json`
（或 `claude_desktop_config.json`）：

```json
{
  "mcpServers": {
    "android-mcp": {
      "type": "stdio",
      "command": "C:/Users/<你>/AppData/Local/Programs/android-mcp/android-mcp.exe"
    }
  }
}
```

## 工具列表

| 名称                   | 用途                                                 |
| ---------------------- | ---------------------------------------------------- |
| `list_devices`         | 枚举 adb 可见设备                                    |
| `screenshot`           | 端上 agent 截屏 PNG                                  |
| `dump_hierarchy`       | XML 控件树                                           |
| `device_info`          | 设备信息                                             |
| `click`                | 单击 (x, y)                                          |
| `long_press`           | 按住 (x, y) `duration_ms` 毫秒                       |
| `swipe`                | 带持续时间的滑动                                     |
| `input_text`           | 向焦点字段输入 ASCII                                 |
| `key_event`            | KEYCODE 名称或数值                                   |
| `shell`                | 任意 `adb shell`（60 秒超时）                        |
| `push_file`            | host -> device 文件复制                              |
| `pull_file`            | device -> host 文件复制                              |
| `install_apk`          | push + `pm install`                                  |
| `list_packages`        | 列出安装包及元数据                                   |
| `get_package_info`     | 获取应用摘要                                         |
| `launch_app`           | `monkey -p <pkg>` 启动                               |
| `stop_app`             | `am force-stop`                                      |
| `clear_app_data`       | `pm clear`                                           |
| `set_package_enabled`  | `pm enable` / `pm disable-user`                      |
| `get_top_activity`     | 从 `dumpsys activity` 取前台组件                     |
| `screen_size`          | `wm size` 屏幕分辨率                                 |
| `swipe_direction`      | 上下左右整屏滑动，按方向自动算坐标                   |
| `scroll_to_edge`       | 反复滑动直到边缘                                     |
| `find_element`         | 选择器 -> 匹配节点（text/id/desc/xpath/布尔属性）    |
| `tap_element`          | 点击元素中心位置                                     |
| `wait_for_element`     | 轮询 hierarchy 直到命中或超时                        |
| `scroll_until_visible` | 滑动循环，带稳定性 poll + 边界检测                   |
| `press_home`           | KEYCODE_HOME 快捷                                    |
| `press_back`           | KEYCODE_BACK 快捷                                    |
| `press_recents`        | KEYCODE_APP_SWITCH 快捷                              |
| `wake_unlock`          | 点亮屏幕 + `wm dismiss-keyguard`（无密码时直接解锁） |
| `clear_recents`        | 用 `am stack remove` 清掉所有非 home 任务            |

除 `list_devices` 外，每个工具都需要 `serial`。先调用 `list_devices` 拿设备序列号。
设备可能处于息屏 / 锁屏状态时，先调一次 `wake_unlock`（解锁后再调无副作用）。

## Tips

- 优先 `find_element` / `tap_element`，少用 `dump_hierarchy` + `click`。
- `dump_hierarchy` 默认精简 JSON；要原 XML 传 `format=xml`。
- XPath sibling 拿标签后值：`//node[@text='X']/following-sibling::node[1]`。
- `screenshot` 用于肉眼核对，不要拿来抽文字。

`initialize.result.instructions` 里有这几条的浓缩版，支持 MCP 的客户端会自动
注入到模型 system prompt。

## 独立运行

装好的 exe 也可以脱离 Claude Code 跑：

```
%LOCALAPPDATA%\Programs\android-mcp\android-mcp.exe                # stdio
%LOCALAPPDATA%\Programs\android-mcp\android-mcp.exe --http 9460    # HTTP + SSE
```

参数：`--debug`（详细 stderr）、`--quiet`（仅警告）。

## 从源码构建

```
git clone --recursive https://github.com/TheDeathDragon/AndroidMCP.git
cd AndroidMCP
agent\build.bat
publish.bat
```

## 开源协议

[Apache 2.0](LICENSE)
