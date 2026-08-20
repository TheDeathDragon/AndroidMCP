#!/usr/bin/env sh
set -eu

REPO='TheDeathDragon/AndroidMCP'
VERSION=''
AUTO_CONFIG=0
INSTALL_DIR="${ANDROID_MCP_DIR:-$HOME/.local/share/android-mcp}"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="${2#v}"; shift 2 ;;
        --install-dir) INSTALL_DIR="$2"; shift 2 ;;
        --auto-config) AUTO_CONFIG=1; shift ;;
        -h|--help)
            echo "usage: install.sh [--version <YYMM.NNNN>] [--install-dir <dir>] [--auto-config]"
            exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 1 ;;
    esac
done

case "$(uname -s)" in
    Linux) ;;
    *) echo "this installer targets Linux; on Windows use scripts/install.ps1" >&2; exit 1 ;;
esac

case "$(uname -m)" in
    x86_64|amd64) ;;
    *) echo "no prebuilt binary for $(uname -m); build from source (publish.bat linux-arm64)" >&2; exit 1 ;;
esac

for tool in curl tar; do
    command -v "$tool" >/dev/null 2>&1 || { echo "$tool is required" >&2; exit 1; }
done

if [ -n "$VERSION" ]; then
    api="https://api.github.com/repos/$REPO/releases/tags/v$VERSION"
else
    api="https://api.github.com/repos/$REPO/releases/latest"
fi
rel=$(curl -fsSL -H 'User-Agent: android-mcp-installer' "$api")

# one JSON field per line so grep/sed can pick values without a jq dependency
tag=$(printf '%s' "$rel" | tr ',' '\n' | grep -m1 '"tag_name"' | sed 's/.*: *"\(.*\)".*/\1/')
url=$(printf '%s' "$rel" | tr ',' '\n' | grep 'browser_download_url' | grep -m1 'linux-x64\.tar\.gz' | sed 's/.*"\(https[^"]*\)".*/\1/')
[ -n "$url" ] || { echo "no linux-x64 tarball in release ${tag:-?}" >&2; exit 1; }

echo "AndroidMCP $tag"
echo "  install dir: $INSTALL_DIR"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fsSL "$url" -o "$tmp/android-mcp.tar.gz"

mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALL_DIR/android-mcp" "$INSTALL_DIR/tools"
tar -xzf "$tmp/android-mcp.tar.gz" -C "$INSTALL_DIR"

bin="$INSTALL_DIR/android-mcp"
[ -f "$bin" ] || { echo "installation failed: $bin not produced" >&2; exit 1; }
chmod +x "$bin"

echo ""
echo "Installed: $bin"
command -v adb >/dev/null 2>&1 || echo "warning: adb is not on PATH"

if [ "$AUTO_CONFIG" -eq 1 ] && command -v python3 >/dev/null 2>&1; then
    python3 - "$HOME/.claude.json" "$bin" <<'PY'
import json, os, sys

path, command = sys.argv[1], sys.argv[2]
cfg = {}
if os.path.exists(path):
    with open(path, encoding='utf-8') as fh:
        cfg = json.load(fh)
cfg.setdefault('mcpServers', {})['android-mcp'] = {'type': 'stdio', 'command': command}
with open(path, 'w', encoding='utf-8') as fh:
    json.dump(cfg, fh, indent=2, ensure_ascii=False)
PY
    echo "Updated $HOME/.claude.json"
    echo "Run /reload-plugins in Claude Code to load the server."
    exit 0
fi

if [ "$AUTO_CONFIG" -eq 1 ]; then
    echo "python3 not found, skipping --auto-config"
fi

cat <<EOF

Add this to ~/.claude.json (or claude_desktop_config.json) under mcpServers:

{
  "mcpServers": {
    "android-mcp": {
      "type": "stdio",
      "command": "$bin"
    }
  }
}

Or rerun with --auto-config to merge it automatically.
EOF
