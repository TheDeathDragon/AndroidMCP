[CmdletBinding()]
param(
    [string]$Version,
    [switch]$AutoConfig,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\android-mcp')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'TheDeathDragon/AndroidMCP'
$apiBase = "https://api.github.com/repos/$repo"
$rel = if ($Version) {
    $tag = $Version -replace '^v', ''
    Invoke-RestMethod -Headers @{ 'User-Agent' = 'android-mcp-installer' } "$apiBase/releases/tags/v$tag"
} else {
    Invoke-RestMethod -Headers @{ 'User-Agent' = 'android-mcp-installer' } "$apiBase/releases/latest"
}

$asset = $rel.assets | Where-Object { $_.name -like 'android-mcp-*-win-x64.zip' } | Select-Object -First 1
if (-not $asset) { throw "no win-x64 zip in release $($rel.tag_name)" }

Write-Host "AndroidMCP $($rel.tag_name)  ($([math]::Round($asset.size / 1MB, 1)) MB)"
Write-Host "  install dir: $InstallDir"

if (Test-Path $InstallDir) {
    Get-ChildItem $InstallDir -Force | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

$zipPath = Join-Path $env:TEMP $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath
Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
Remove-Item $zipPath -Force

$exe = Join-Path $InstallDir 'android-mcp.exe'
if (-not (Test-Path $exe)) { throw "installation failed: $exe not produced" }

$exePosix = $exe -replace '\\', '/'
$snippet = @"
{
  "mcpServers": {
    "android-mcp": {
      "type": "stdio",
      "command": "$exePosix"
    }
  }
}
"@

Write-Host ""
Write-Host "Installed: $exe"

if ($AutoConfig) {
    $configPath = Join-Path $env:USERPROFILE '.claude.json'
    if (Test-Path $configPath) {
        $cfg = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } else {
        $cfg = [pscustomobject]@{}
    }
    if (-not $cfg.PSObject.Properties.Match('mcpServers').Count) {
        $cfg | Add-Member -MemberType NoteProperty -Name mcpServers -Value ([pscustomobject]@{})
    }
    $entry = [pscustomobject]@{ type = 'stdio'; command = $exePosix }
    if ($cfg.mcpServers.PSObject.Properties.Match('android-mcp').Count) {
        $cfg.mcpServers.'android-mcp' = $entry
    } else {
        $cfg.mcpServers | Add-Member -MemberType NoteProperty -Name 'android-mcp' -Value $entry
    }
    ($cfg | ConvertTo-Json -Depth 32) | Set-Content -Path $configPath -Encoding UTF8
    Write-Host "Updated $configPath"
    Write-Host "Run /reload-plugins in Claude Code to load the server."
} else {
    Write-Host ""
    Write-Host "Add this to ~/.claude.json (or claude_desktop_config.json) under mcpServers:"
    Write-Host ""
    Write-Host $snippet
    Write-Host ""
    Write-Host "Or rerun with -AutoConfig to merge it automatically."
}
