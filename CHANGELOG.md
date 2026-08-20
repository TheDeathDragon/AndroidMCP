# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions
are `YYMM.NNNN` (year-month + zero-padded `git rev-list --count HEAD`,
matching `BuildInfo.Version`).

## [Unreleased]

### Added
- `scripts/install.sh` — Linux one-liner installer (linux-x64 tarball ->
  `~/.local/share/android-mcp/`, `--auto-config` / `--version` /
  `--install-dir`). READMEs document Linux install, config path and build.

### Removed
- Dead `AdbHub.FindDeviceBySerial`, a first-match lookup that ignored the
  transport-id routing added in 2606.0010.

## [2606.0012] - 2026-06-18

### Fixed
- Open the per-device agent's adb forward by transport-id (`adb -t <id> forward`) instead of the AdvancedSharpAdbClient `host-serial:` forward, which fails with "more than one device" when two devices share a serial. Agent-backed tools (screenshot / dump_hierarchy / find_element / scroll) now start and route correctly on devices with duplicate serials; previously only the direct shell/input path worked there.

## [2606.0010] - 2026-06-18

### Fixed
- Route every device operation by adb transport-id instead of serial. Two connected devices that report the same serial (common on clone / low-cost hardware with an unset `ro.serialno`) were collapsed onto one session, leaving the second unreachable; each now gets its own session and routes correctly. `list_devices` exposes `transportId` — pass it as `serial` to target a device whose serial is shared, and an ambiguous serial now errors with that guidance instead of silently hitting the first match.

## [2606.0008] - 2026-06-13

### Changed
- Self-launched on-device agent now starts on the canonical port `9500` instead of the per-device host port, so a PC-side agent and a server-side AndroidMCP can share one `UiAutomation` instance; the per-device host port still forwards to it.

## [2606.0006] - 2026-06-12

### Added
- linux-x64 release artifact (`android-mcp-<version>-linux-x64.tar.gz`).

### Fixed
- Stdio transport drains in-flight requests before exiting on stdin close, so one-shot piped clients no longer lose responses.

## [2606.0003] - 2026-06-11

### Fixed
- Refuse to launch the on-device agent while the device is still booting (`sys.boot_completed != 1`), preventing a guaranteed `UiAutomation.connect` crash right after reboot.

## [2605.0001] - 2026-05-13

### Added
- Initial release.
