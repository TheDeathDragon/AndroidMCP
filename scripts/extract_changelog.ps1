[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Path = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$lines = Get-Content -LiteralPath $Path
$header = "## [$Version]"
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith($header)) { $start = $i; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG section not found: $header"
    exit 1
}
$end = $lines.Count
for ($j = $start + 1; $j -lt $lines.Count; $j++) {
    if ($lines[$j] -match '^##\s+\[') { $end = $j; break }
}
$body = $lines[($start + 1)..($end - 1)] -join "`n"
$body.Trim()
