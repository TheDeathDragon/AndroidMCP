# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions
are `YYMM.NNNN` (year-month + zero-padded `git rev-list --count HEAD`,
matching `BuildInfo.Version`).

## [Unreleased]

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
