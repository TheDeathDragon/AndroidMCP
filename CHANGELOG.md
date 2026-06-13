# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions
are `YYMM.NNNN` (year-month + zero-padded `git rev-list --count HEAD`,
matching `BuildInfo.Version`).

## [Unreleased]

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
