$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$exe = Join-Path $PSScriptRoot '..\publish\win-x64\android-mcp.exe' | Resolve-Path
$serial = (& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' } | ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
if (-not $serial) { throw 'no device' }
Write-Host ("device: {0}`n" -f $serial)

$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '--quiet'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$proc = [Diagnostics.Process]::Start($psi)
$proc.StandardInput.NewLine = "`n"

$reqId = 0
function Call($name, $callArgs) {
    $script:reqId++
    $obj = @{ jsonrpc = '2.0'; id = $script:reqId; method = 'tools/call'; params = @{ name = $name; arguments = $callArgs } }
    $proc.StandardInput.WriteLine(($obj | ConvertTo-Json -Depth 8 -Compress))
    $proc.StandardInput.Flush()
    return $proc.StandardOutput.ReadLine()
}

# warm up: initialize + one of each call so the agent boots and JIT settles
$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"bench","version":"0"}}}')
$proc.StandardInput.Flush()
$null = $proc.StandardOutput.ReadLine()
$null = Call 'key_event' @{ serial = $serial; key = 'WAKEUP' }
$null = Call 'screenshot' @{ serial = $serial; quality = 40 }
$null = Call 'dump_hierarchy' @{ serial = $serial; compressed = $true }

Write-Host ('{0,-28} {1,4}  {2,5}  {3,5}  {4,5}  {5,5}  {6,5}' -f 'Tool', 'N', 'Min', 'P50', 'Avg', 'P95', 'Max')
Write-Host ('-' * 70)

function Bench($label, $name, $callArgs, $n = 20) {
    $samples = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $n; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $null = Call $name $callArgs
        $sw.Stop()
        $samples.Add($sw.Elapsed.TotalMilliseconds)
    }
    $sorted = $samples | Sort-Object
    $avg = ($samples | Measure-Object -Average).Average
    $min = $sorted[0]
    $p50 = $sorted[[int]([math]::Floor($n * 0.50))]
    $p95 = $sorted[[int]([math]::Min($n - 1, [math]::Floor($n * 0.95)))]
    $max = $sorted[-1]
    Write-Host ('{0,-28} {1,4}  {2,5:N0}  {3,5:N0}  {4,5:N0}  {5,5:N0}  {6,5:N0}' -f $label, $n, $min, $p50, $avg, $p95, $max)
}

Bench 'shell echo'                'shell'            @{ serial = $serial; command = 'echo hi' }
Bench 'key_event WAKEUP'          'key_event'        @{ serial = $serial; key = 'WAKEUP' }
Bench 'click (5,5)'               'click'            @{ serial = $serial; x = 5; y = 5 }
Bench 'get_top_activity'          'get_top_activity' @{ serial = $serial }
Bench 'screen_size'               'screen_size'      @{ serial = $serial }
Bench 'find_element (clickable)'  'find_element'     @{ serial = $serial; selector = @{ clickable = $true }; limit = 5 }
Bench 'dump_hierarchy compressed' 'dump_hierarchy'   @{ serial = $serial; compressed = $true }
Bench 'dump_hierarchy full'       'dump_hierarchy'   @{ serial = $serial; compressed = $false }
Bench 'screenshot q=40'           'screenshot'       @{ serial = $serial; quality = 40 } 15
Bench 'screenshot q=70'           'screenshot'       @{ serial = $serial; quality = 70 } 15
Bench 'screenshot q=100'          'screenshot'       @{ serial = $serial; quality = 100 } 10

$proc.StandardInput.Close()
$proc.WaitForExit(2000) | Out-Null
