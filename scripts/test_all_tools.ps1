$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$exe = Join-Path $PSScriptRoot '..\publish\win-x64\android-mcp.exe' | Resolve-Path
$serial = (& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' } | ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
if (-not $serial) { throw 'no adb device connected' }
Write-Host "device: $serial`n"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '--quiet'
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$proc = [System.Diagnostics.Process]::Start($psi)
$writer = $proc.StandardInput
$writer.NewLine = "`n"

$reqId = 0
$results = New-Object System.Collections.Generic.List[object]

function Send-Request($method, $params) {
    $script:reqId++
    $obj = @{ jsonrpc = '2.0'; id = $script:reqId; method = $method }
    if ($params) { $obj.params = $params }
    $writer.WriteLine(($obj | ConvertTo-Json -Depth 8 -Compress))
    $writer.Flush()
    return $proc.StandardOutput.ReadLine()
}

function Test-Tool($name, $callArgs, [scriptblock]$check, [string]$note = '') {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Send-Request 'tools/call' @{ name = $name; arguments = $callArgs }
        $sw.Stop()
        $obj = $resp | ConvertFrom-Json
        if ($obj.error) {
            $script:results.Add([pscustomobject]@{ Tool = $name; Status = 'ERROR'; Ms = $sw.ElapsedMilliseconds; Detail = $obj.error.message })
            return $null
        }
        $isErr = $obj.result.isError
        $first = $obj.result.content[0]
        $text = if ($first.type -eq 'text') { $first.text } else { "<$($first.type) $($first.data.Length) bytes>" }
        $detail = if ($text.Length -gt 80) { $text.Substring(0, 80) + '...' } else { $text }
        if ($isErr) {
            $script:results.Add([pscustomobject]@{ Tool = $name; Status = 'FAIL'; Ms = $sw.ElapsedMilliseconds; Detail = $detail })
            return $null
        }
        $verdict = if ($check) { & $check $first $text } else { $true }
        $status = if ($verdict) { 'PASS' } else { 'CHECK' }
        $extra = if ($note) { " [$note]" } else { '' }
        $script:results.Add([pscustomobject]@{ Tool = $name; Status = $status; Ms = $sw.ElapsedMilliseconds; Detail = "$detail$extra" })
        return $first
    } catch {
        $sw.Stop()
        $script:results.Add([pscustomobject]@{ Tool = $name; Status = 'THROW'; Ms = $sw.ElapsedMilliseconds; Detail = $_.Exception.Message })
        return $null
    }
}

# initialize
$null = Send-Request 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'test-sweep'; version = '0.1' } }

Test-Tool 'list_devices' @{} { param($c, $t) $t -match 'serial' } | Out-Null
Test-Tool 'screenshot' @{ serial = $serial; quality = 40 } { param($c, $t) $c.type -eq 'image' -and $c.data.Length -gt 1000 } | Out-Null
Test-Tool 'dump_hierarchy' @{ serial = $serial; compressed = $true } { param($c, $t) $t.StartsWith('<?xml') -or $t.StartsWith('<hierarchy') } | Out-Null
Test-Tool 'device_info' @{ serial = $serial } { param($c, $t) $t -match 'brand' -and $t -match 'model' } | Out-Null
Test-Tool 'shell' @{ serial = $serial; command = 'id' } { param($c, $t) $t -match 'uid=' } | Out-Null
Test-Tool 'get_top_activity' @{ serial = $serial } { param($c, $t) $t -match 'component' } | Out-Null
Test-Tool 'list_packages' @{ serial = $serial; user_only = $true } { param($c, $t) $t -match 'packageName' } | Out-Null
Test-Tool 'get_package_info' @{ serial = $serial; package = 'com.android.settings' } { param($c, $t) $t -match 'versionName' } | Out-Null
Test-Tool 'key_event' @{ serial = $serial; key = 'WAKEUP' } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'click' @{ serial = $serial; x = 540; y = 1200 } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'long_press' @{ serial = $serial; x = 540; y = 1200; duration_ms = 500 } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'swipe' @{ serial = $serial; x1 = 540; y1 = 1500; x2 = 540; y2 = 800; duration_ms = 300 } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'input_text' @{ serial = $serial; text = 'mcp_smoke' } { param($c, $t) $t -match '"ok":true' } 'visible only when a text field is focused' | Out-Null
Test-Tool 'launch_app' @{ serial = $serial; package = 'com.android.settings' } { param($c, $t) $t -match '"ok":true' } | Out-Null
Start-Sleep -Milliseconds 800
Test-Tool 'stop_app' @{ serial = $serial; package = 'com.android.settings' } { param($c, $t) $t -match '"ok":true' } | Out-Null

$tmp = New-TemporaryFile
('mcp test marker ' + (Get-Date -Format o)) | Out-File -FilePath $tmp -Encoding utf8 -NoNewline
$remote = '/data/local/tmp/mcp-smoke.txt'
Test-Tool 'push_file' @{ serial = $serial; local_path = $tmp.FullName; remote_path = $remote } { param($c, $t) $t -match '"ok":true' } | Out-Null

$pullDst = "$($tmp.FullName).pulled"
Test-Tool 'pull_file' @{ serial = $serial; remote_path = $remote; local_path = $pullDst } { param($c, $t) $t -match '"ok":true' } | Out-Null
$roundtripOk = (Test-Path $pullDst) -and ((Get-Content $pullDst -Raw) -match 'mcp test marker')
Write-Host ("pull_file roundtrip ok: " + $roundtripOk)
Remove-Item $tmp, $pullDst -ErrorAction SilentlyContinue

Test-Tool 'clear_app_data' @{ serial = $serial; package = 'com.android.shell' } { param($c, $t) $t -match 'ok' } 'pm refuses for protected packages; wiring check' | Out-Null
Test-Tool 'set_package_enabled' @{ serial = $serial; package = 'com.android.settings'; enabled = $true } { param($c, $t) $t -match '"ok":true' } | Out-Null

$apkPath = (Resolve-Path "$PSScriptRoot\..\tools\agent-server.jar").Path
Test-Tool 'install_apk' @{ serial = $serial; apk_path = $apkPath; replace = $true } { param($c, $t) $t -match '"ok":true' } 'agent-server.jar is an APK underneath' | Out-Null
$null = Send-Request 'tools/call' @{ name = 'shell'; arguments = @{ serial = $serial; command = 'pm uninstall la.shiro.agent' } }

# screen_size
Test-Tool 'screen_size' @{ serial = $serial } { param($c, $t) $t -match '"width"' -and $t -match '"height"' } | Out-Null

# launch settings to give us a known scrollable surface
$null = Send-Request 'tools/call' @{ name = 'launch_app'; arguments = @{ serial = $serial; package = 'com.android.settings' } }
Start-Sleep -Milliseconds 1500

# swipe_direction
Test-Tool 'swipe_direction' @{ serial = $serial; direction = 'up'; distance_percent = 50; duration_ms = 250 } { param($c, $t) $t -match '"ok":true' } | Out-Null
Start-Sleep -Milliseconds 500

# scroll_to_edge — fling to bottom of Settings (cap at 6 to keep test brisk)
Test-Tool 'scroll_to_edge' @{ serial = $serial; direction = 'up'; max_steps = 6 } { param($c, $t) $t -match '"ok":true' -and $t -match 'steps' } | Out-Null

# Reset to top first via scroll_to_edge down (= move content down = fling top)
$null = Send-Request 'tools/call' @{ name = 'scroll_to_edge'; arguments = @{ serial = $serial; direction = 'down'; max_steps = 8 } }
Start-Sleep -Milliseconds 500

# find_element — try to find any clickable in the current Settings UI
Test-Tool 'find_element' @{ serial = $serial; selector = @{ clickable = $true }; limit = 5 } { param($c, $t) $t -match '"count"' } | Out-Null

# wait_for_element — wait for any android.widget.FrameLayout (always present)
Test-Tool 'wait_for_element' @{ serial = $serial; selector = @{ className = 'android.widget.FrameLayout' }; timeout_ms = 3000 } { param($c, $t) $t -match '"ok":true' } | Out-Null

# scroll_until_visible — scroll Settings looking for something likely off-screen, e.g. "About"
Test-Tool 'scroll_until_visible' @{ serial = $serial; selector = @{ text = 'About'; partial = $true }; direction = 'up'; max_scrolls = 8 } { param($c, $t) $t -match '"ok":true' -or $t -match 'edge|not visible' } 'success or graceful no-result both prove the loop runs' | Out-Null

# tap_element — pick first clickable in current view (no-strict so duplicates are fine)
Test-Tool 'tap_element' @{ serial = $serial; selector = @{ clickable = $true }; strict = $false; index = 0 } { param($c, $t) $t -match '"ok":true' } | Out-Null

# back out
$null = Send-Request 'tools/call' @{ name = 'key_event'; arguments = @{ serial = $serial; key = 'BACK' } }
$null = Send-Request 'tools/call' @{ name = 'stop_app'; arguments = @{ serial = $serial; package = 'com.android.settings' } }

# Convenience nav wrappers
Test-Tool 'press_home' @{ serial = $serial } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'press_back' @{ serial = $serial } { param($c, $t) $t -match '"ok":true' } | Out-Null
Test-Tool 'press_recents' @{ serial = $serial } { param($c, $t) $t -match '"ok":true' } | Out-Null
Start-Sleep -Milliseconds 500

# clear_recents — should remove non-home tasks; OK if removed array is empty
Test-Tool 'clear_recents' @{ serial = $serial } { param($c, $t) $t -match '"ok":true' -and $t -match 'removed' } | Out-Null

# wake_unlock — idempotent on unlocked device
Test-Tool 'wake_unlock' @{ serial = $serial } { param($c, $t) $t -match '"ok":true' } | Out-Null

$null = Send-Request 'tools/call' @{ name = 'press_home'; arguments = @{ serial = $serial } }

$writer.Close()
$proc.WaitForExit(3000) | Out-Null

Write-Host "`n=== Summary ==="
$results | Format-Table -AutoSize
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -in 'FAIL', 'THROW', 'ERROR').Count
$check = ($results | Where-Object Status -eq 'CHECK').Count
Write-Host "`npass=$pass  fail=$fail  check=$check  total=$($results.Count)"
