$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$exe = Join-Path $PSScriptRoot '..\publish\win-x64\android-mcp.exe' | Resolve-Path
$serial = (& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' } | ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
if (-not $serial) { throw 'no adb device connected' }
Write-Host "device: $serial`n"

$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '--quiet'
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$proc = [Diagnostics.Process]::Start($psi)
$proc.StandardInput.NewLine = "`n"

$reqId = 0
function Invoke-Tool($name, $callArgs) {
    $script:reqId++
    $obj = @{ jsonrpc = '2.0'; id = $script:reqId; method = 'tools/call'; params = @{ name = $name; arguments = $callArgs } }
    $proc.StandardInput.WriteLine(($obj | ConvertTo-Json -Depth 8 -Compress))
    $proc.StandardInput.Flush()
    $line = $proc.StandardOutput.ReadLine()
    $resp = $line | ConvertFrom-Json
    if ($resp.error) { throw "$name: $($resp.error.message)" }
    if ($resp.result.isError) {
        $msg = $resp.result.content[0].text
        throw "$name: $msg"
    }
    $text = $resp.result.content[0].text
    return $text | ConvertFrom-Json
}

$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"demo","version":"0"}}}')
$proc.StandardInput.Flush()
$null = $proc.StandardOutput.ReadLine()

Write-Host '[1] press home'
$null = Invoke-Tool 'key_event' @{ serial = $serial; key = 'HOME' }
Start-Sleep -Milliseconds 400

Write-Host '[2] launch Settings'
$null = Invoke-Tool 'launch_app' @{ serial = $serial; package = 'com.android.settings' }
Start-Sleep -Milliseconds 1500

Write-Host '[3] scroll until "About phone" visible'
$r = Invoke-Tool 'scroll_until_visible' @{
    serial = $serial
    selector = @{ text = 'About'; partial = $true }
    direction = 'up'
    max_scrolls = 8
}
Write-Host ("    scrolled $($r.scrollCount) time(s); found: $($r.element.text)")

Write-Host '[4] tap the About row'
$null = Invoke-Tool 'tap_element' @{
    serial = $serial
    selector = @{ text = 'About'; partial = $true }
    strict = $false
    index = 0
}
Start-Sleep -Milliseconds 1500

Write-Host '[5] dump full About-phone hierarchy and extract version + patch'
$xml = $null
$resp = $null
# Use a raw shell call to avoid pulling 100KB JSON-encoded XML through demo;
# we already have dump_hierarchy. Use it for parity.
$dump = (& {
    $script:reqId++
    $obj = @{ jsonrpc = '2.0'; id = $script:reqId; method = 'tools/call'; params = @{ name = 'dump_hierarchy'; arguments = @{ serial = $serial; compressed = $true } } }
    $proc.StandardInput.WriteLine(($obj | ConvertTo-Json -Depth 8 -Compress))
    $proc.StandardInput.Flush()
    ($proc.StandardOutput.ReadLine() | ConvertFrom-Json).result.content[0].text
})

# Find labels of interest. We use find_element to ask for text containing key markers
$wantedLabels = @(
    'Android version',
    'Android 版本',
    'Build number',
    '版本号',
    'Security update',
    '安全补丁',
    'Security patch'
)
Write-Host '    --- labels probe ---'
foreach ($label in @('Android version', 'Build number', 'Security')) {
    try {
        $hits = Invoke-Tool 'find_element' @{ serial = $serial; selector = @{ text = $label; partial = $true }; limit = 3 }
        if ($hits.count -gt 0) {
            foreach ($m in $hits.matches) {
                Write-Host ("    [{0}] @ ({1},{2}) -> '{3}'  rid={4}" -f $label, $m.bounds.centerX, $m.bounds.centerY, $m.text, $m.resourceId)
            }
        } else {
            Write-Host ("    [{0}] no match" -f $label)
        }
    } catch {
        Write-Host ("    [{0}] error: {1}" -f $label, $_.Exception.Message)
    }
}

# A more robust pattern on most Settings: the value is the next sibling TextView.
# We can read every TextView with non-empty text and let the user eyeball it.
Write-Host '    --- all visible text on this screen ---'
$xpath = "//node[@class='android.widget.TextView' and string-length(@text) > 0]"
$hits = Invoke-Tool 'find_element' @{ serial = $serial; selector = @{ xpath = $xpath }; limit = 50 }
foreach ($m in $hits.matches) {
    Write-Host ("    {0,4},{1,4}  {2}" -f $m.bounds.centerX, $m.bounds.centerY, $m.text)
}

Write-Host '[6] back to home'
$null = Invoke-Tool 'key_event' @{ serial = $serial; key = 'BACK' }
Start-Sleep -Milliseconds 300
$null = Invoke-Tool 'key_event' @{ serial = $serial; key = 'HOME' }

$proc.StandardInput.Close()
$proc.WaitForExit(3000) | Out-Null
Write-Host "`ndone."
