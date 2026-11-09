<#
.SYNOPSIS
Gateway benchmarks over the exchange stub: REST, WebSocket API and streams, for our client, Binance.Net and the Rust competitors.

.DESCRIPTION
REST and WebSocket API: BenchmarkDotNet for our client and Binance.Net, a second BenchmarkDotNet run of our client
compiled with Native AOT, the Rust binaries for binance-sdk and Nautilus (Nautilus has no WebSocket API run).
Streams: pace 1 measures latency, max measures throughput; axitor-aot is our client from a Native AOT build of the harness.
Each suite starts its own stub. Results go to bench\Results\<suite>-<time>.md.
Windows only. The stub replays bench\Data, recorded first with bench\tools\recorder.cs; signed benchmarks need data recorded with API keys.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\bench\run-bench.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\bench\run-bench.ps1 -Suite streams -Pace 1 -Streams depth,book

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\bench\run-bench.ps1 -Suite rest,wsapi -Clients axitor-aot,binance-sdk,nautilus
#>
param(
    [string[]] $Suite = @('all'),
    [ValidateSet('1', 'max', 'all')]
    [string] $Pace = 'all',
    [string[]] $Streams = @(),
    [string[]] $Clients = @(),
    [int] $Repeat = 0,
    [string] $Repo = '',
    [int] $Port = 5100,
    [int] $LatencyWindowSeconds = 120,
    [int] $WarmupMessages = 50000
)

$ErrorActionPreference = 'Stop'

# The script lives in bench\, one level under the repository root.
if (-not $Repo) {
    $Repo = Split-Path -Parent $PSScriptRoot
}

$stubDir = Join-Path $Repo 'bench\Axitor.Trading.Gateway.Benchmarks.ExchangeStub'
$benchDir = Join-Path $Repo 'bench\Axitor.Trading.Gateway.Benchmarks'
$dataDir = Join-Path $Repo 'bench\Data'
$rustDir = Join-Path $Repo 'competitors\rust'
$aotDir = Join-Path $Repo 'artifacts\aot\benchmarks'
$aotExe = Join-Path $aotDir 'Axitor.Trading.Gateway.Benchmarks.exe'
$resultsDir = Join-Path $Repo 'bench\Results'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

$rustExe = @{
    'binance-sdk' = Join-Path $rustDir 'binance-sdk\target\release\binance-sdk-bench.exe'
    'nautilus' = Join-Path $rustDir 'nautilus\target\release\nautilus-bench.exe'
}

# Built from its code point: Windows PowerShell reads a script without a BOM in the ANSI code page.
$micro = [char]0x00B5
$streamHeader = "| Client | Stream | Msgs | Msg/s | p50 ${micro}s | p90 ${micro}s | p99 ${micro}s | p99.9 ${micro}s | Max ${micro}s | B/msg | Gen0 | Gen1 | Gen2 | GC pause ms |"
$streamSeparator = '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|'
$requestHeader = "| Client | Benchmark | Mean ${micro}s | Error ${micro}s | StdDev ${micro}s | Allocated B/op | Allocs/op |"
$requestSeparator = '|---|---|---:|---:|---:|---:|---:|'

function Split-List([string[]] $values) {
    # powershell -File passes "depth,book" as one string, so the names are split here.
    return @($values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
}

$wantedSuites = Split-List $Suite
$wantedStreams = Split-List $Streams
$wantedClients = Split-List $Clients

foreach ($name in $wantedSuites) {
    if ($name -notin 'all', 'rest', 'wsapi', 'streams') {
        throw "Unknown suite $name. Use rest, wsapi, streams or all."
    }
}

function Test-Suite([string] $name) {
    return $wantedSuites -contains 'all' -or $wantedSuites -contains $name
}

function Test-Client([string] $client) {
    return $wantedClients.Count -eq 0 -or $wantedClients -contains $client
}

function New-Run([string] $client, [string] $stream, [object[]] $extra) {
    [pscustomobject]@{ Client = $client; Stream = $stream; Extra = $extra }
}

# Pace 1: latency on every stream. The high-performance mode of Binance.Net carries no event time, so it is left out.
# Busy streams get a fixed window; aggTrade and kline are thin, so they run to the end of the pass for enough samples.
# Only the .NET clients build a local order book. axitor-aot is our client from the Native AOT build of the harness.
$latencyRuns = foreach ($stream in 'bookticker', 'aggtrade', 'depth', 'depth20', 'kline', 'book') {
    $extra = @('--warmup-msgs', $WarmupMessages)

    if ($stream -notin 'aggtrade', 'kline') {
        $extra += @('--window', $LatencyWindowSeconds)
    }

    foreach ($client in 'axitor', 'axitor-aot', 'binance-net', 'binance-sdk', 'nautilus') {
        if ($stream -eq 'book' -and $client -in 'binance-sdk', 'nautilus') {
            continue
        }

        New-Run $client $stream $extra
    }
}

# Max: throughput on the busy streams, a whole fresh pass after a warmup pass. aggTrade and kline are too short at this pace.
# raw reads and drops whole messages on the same transport: the floor under every client.
$throughputRuns = foreach ($stream in 'bookticker', 'depth', 'depth20', 'book') {
    foreach ($client in 'axitor', 'axitor-aot', 'binance-net', 'binance-net-perf', 'raw', 'binance-sdk', 'nautilus') {
        if ($stream -eq 'book' -and $client -notin 'axitor', 'axitor-aot', 'binance-net') {
            continue
        }

        New-Run $client $stream @('--warmup-pass')
    }
}

function Select-Runs($runs) {
    return @($runs | Where-Object { ($wantedStreams.Count -eq 0 -or $_.Stream -in $wantedStreams) -and (Test-Client $_.Client) })
}

function ConvertTo-Number([string] $text) {
    $value = 0.0

    if ([double]::TryParse($text, [System.Globalization.NumberStyles]::Float, $invariant, [ref] $value)) {
        return $value
    }

    # Rust has no GC: its GC cells hold a dash.
    return $null
}

function Format-Median([object[]] $values, [string] $format) {
    $numbers = @($values | Where-Object { $null -ne $_ } | Sort-Object)

    if ($numbers.Count -eq 0) {
        return '-'
    }

    $middle = [math]::Floor($numbers.Count / 2)
    $median = if ($numbers.Count % 2 -eq 1) { $numbers[$middle] } else { ($numbers[$middle - 1] + $numbers[$middle]) / 2 }
    return ([double] $median).ToString($format, $invariant)
}

function Invoke-Build {
    # Build tools write progress to stderr; only the exit code decides.
    $ErrorActionPreference = 'Continue'

    foreach ($dir in $stubDir, $benchDir) {
        & dotnet build $dir -c Release --nologo -v q

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed: $dir"
        }
    }

    # Native AOT build of the harness for axitor-aot. The trim warnings of Binance.Net and BenchmarkDotNet are not errors here:
    # their code is in the exe but never runs in stream mode.
    if (Test-Client 'axitor-aot') {
        & dotnet publish $benchDir -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=false -o $aotDir --nologo -v q

        if ($LASTEXITCODE -ne 0) {
            throw "Native AOT publish failed: $benchDir"
        }
    }

    foreach ($client in $rustExe.Keys) {
        if (-not (Test-Client $client)) {
            continue
        }

        $manifest = Join-Path $rustDir "$client\Cargo.toml"
        & cargo build --release --manifest-path $manifest

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed: $manifest"
        }
    }
}

function Start-Stub([string] $pace, [string] $log) {
    $arguments = "run -c Release --no-build -- `"$dataDir`" $Port $pace"
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $stubDir `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru

    $deadline = (Get-Date).AddSeconds(60)

    while ($true) {
        if ($process.HasExited) {
            throw "The stub exited, see $log"
        }

        try {
            Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/fapi/v1/time" -TimeoutSec 2 | Out-Null
            return $process
        }
        catch {
            # Not listening yet.
        }

        if ((Get-Date) -gt $deadline) {
            Stop-Stub $process
            throw "The stub did not start in 60 s, see $log"
        }

        Start-Sleep -Milliseconds 500
    }
}

function Stop-Stub($process) {
    # dotnet run starts the app as a child process: the whole tree goes.
    if (-not $process.HasExited) {
        & taskkill /PID $process.Id /T /F | Out-Null
    }
}

# Runs a tool, shows its output as it comes and returns the lines.
function Invoke-Tool([string] $file, [object[]] $arguments, [string] $log) {
    # Tools may write to stderr; that must not stop the series.
    $ErrorActionPreference = 'Continue'

    $output = & $file @arguments 2>&1 | ForEach-Object {
        $line = "$_"
        Write-Host $line
        $line
    }

    $script:toolExitCode = $LASTEXITCODE
    $output | Add-Content -Path $log -Encoding UTF8
    return @($output)
}

function Invoke-StreamRun($run, [string] $log, [int] $round) {
    Write-Host ''
    Write-Host "== round $round, $($run.Client) $($run.Stream)"

    $harnessArguments = @('streams', $run.Client, $run.Stream) + $run.Extra

    if ($run.Client -eq 'axitor-aot') {
        # The AOT harness knows our client as axitor; its row is renamed below.
        $output = Invoke-Tool $aotExe (@('streams', 'axitor', $run.Stream) + $run.Extra) $log
    }
    elseif ($rustExe.ContainsKey($run.Client)) {
        $output = Invoke-Tool $rustExe[$run.Client] $harnessArguments $log
    }
    else {
        $output = Invoke-Tool 'dotnet' (@('run', '-c', 'Release', '--no-build', '--project', $benchDir, '--') + $harnessArguments) $log
    }

    $exitCode = $script:toolExitCode
    $row = $output | Where-Object { $_ -like '| *' -and $_ -notlike '| Client*' } | Select-Object -Last 1
    $note = $output | Where-Object { $_ -like 'Warmup: *' } | Select-Object -Last 1
    $cells = $null

    if ($exitCode -ne 0 -or -not $row) {
        $reason = $output | Select-Object -Last 1
        $row = "| $($run.Client) | $($run.Stream) | failed: $reason |"
        $note = "exit code $exitCode"
    }
    else {
        if ($run.Client -eq 'axitor-aot') {
            $row = $row -replace '^\| axitor \|', '| axitor-aot |'
        }

        $cells = @($row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
    }

    [pscustomobject]@{
        Client = $run.Client
        Stream = $run.Stream
        Row = $row
        Cells = $cells
        Note = "- round $round, $($run.Client) $($run.Stream): $note"
    }
}

function Get-SummaryLines($results, [int] $rounds) {
    $lines = @(
        "## Median of $rounds runs",
        '',
        '| Client | Stream | Runs | Msg/s median | Msg/s min | Msg/s max | B/msg median | Gen0 median | GC pause ms median |',
        '|---|---|---:|---:|---:|---:|---:|---:|---:|'
    )

    $groups = $results | Where-Object { $_.Cells -and $_.Cells.Count -ge 14 } | Group-Object { "$($_.Client)|$($_.Stream)" }

    foreach ($group in $groups) {
        $first = $group.Group[0]
        $rates = @($group.Group | ForEach-Object { ConvertTo-Number $_.Cells[3] })
        $bytes = @($group.Group | ForEach-Object { ConvertTo-Number $_.Cells[9] })
        $gen0 = @($group.Group | ForEach-Object { ConvertTo-Number $_.Cells[10] })
        $pause = @($group.Group | ForEach-Object { ConvertTo-Number $_.Cells[13] })
        $range = $rates | Where-Object { $null -ne $_ } | Measure-Object -Minimum -Maximum

        $lines += "| $($first.Client) | $($first.Cells[1]) | $($group.Count) | " +
            "$(Format-Median $rates 'F0') | $($range.Minimum.ToString('F0', $invariant)) | $($range.Maximum.ToString('F0', $invariant)) | " +
            "$(Format-Median $bytes 'F0') | $(Format-Median $gen0 'F0') | $(Format-Median $pause 'F1') |"
    }

    return $lines + @('')
}

function Invoke-StreamSeries([string] $pace, $runs) {
    if ($runs.Count -eq 0) {
        Write-Host ''
        Write-Host "#### Streams, pace $pace`: no runs match the stream and client filters"
        return
    }

    $rounds = if ($Repeat -gt 0) { $Repeat } elseif ($pace -eq '1') { 1 } else { 5 }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $name = if ($pace -eq '1') { "streams-pace1-$stamp" } else { "streams-max-$stamp" }
    $table = Join-Path $resultsDir "$name.md"
    $log = Join-Path $resultsDir "$name.log"
    $stubLog = Join-Path $resultsDir "$name-stub.log"

    Write-Host ''
    Write-Host "#### Streams, pace $pace, $($runs.Count) runs x $rounds"

    $stub = Start-Stub $pace $stubLog
    $results = @()

    try {
        # Rounds outside, so each client meets the machine in the same state across the series.
        foreach ($round in 1..$rounds) {
            foreach ($run in $runs) {
                $results += Invoke-StreamRun $run $log $round

                # Let the previous connections close before the next run subscribes.
                Start-Sleep -Seconds 2
            }
        }
    }
    finally {
        Stop-Stub $stub
    }

    $lines = @(
        "# Streams, pace $pace",
        '',
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm'), $($runs.Count) runs x $rounds, 20 symbols.",
        ''
    )

    if ($rounds -gt 1) {
        $lines += Get-SummaryLines $results $rounds
        $lines += @('## All runs', '')
    }

    $lines += @($streamHeader, $streamSeparator)
    $lines += $results | ForEach-Object { $_.Row }
    $lines += @('', '## Notes', '')
    $lines += $results | ForEach-Object { $_.Note }
    $lines += '- axitor-aot is our client from the Native AOT build of the harness.'
    $lines += '- Rust clients have no GC; their GC columns hold a dash. Their B/msg counts every allocation, freed or not.'

    Set-Content -Path $table -Value $lines -Encoding UTF8
    Write-Host ''
    Write-Host "Saved $table"
}

# BenchmarkDotNet on a request suite. Returns the report lines.
# JIT: our client and Binance.Net, one run per class, so each class keeps its own baseline and Ratio.
# -Aot: only our client's methods with Native AOT, joined into one run, so the AOT build happens once.
function Invoke-BenchmarkDotNet([string] $suite, [string] $name, [string] $log, [switch] $Aot) {
    $group = if ($suite -eq 'rest') { '*.Rest.*' } else { '*.WsApi.*' }
    $filter = if ($Aot) { "$($group)Axitor*" } else { $group }
    $artifacts = if ($Aot) { Join-Path $resultsDir "$name-bdn-aot" } else { Join-Path $resultsDir "$name-bdn" }

    Write-Host ''
    Write-Host "== BenchmarkDotNet $filter$(if ($Aot) { ' (Native AOT)' })"

    $arguments = @('run', '-c', 'Release', '--no-build', '--project', $benchDir, '--', '--filter', $filter, '--artifacts', $artifacts)

    if ($Aot) {
        $arguments += @('--join', '--aot')
    }

    # BenchmarkDotNet finds the benchmark project from the current folder, so it runs from the project folder.
    Push-Location $benchDir

    try {
        $output = Invoke-Tool 'dotnet' $arguments $log
    }
    finally {
        Pop-Location
    }

    # The last count is the global one.
    $executed = 0

    foreach ($line in $output) {
        if ($line -match 'executed benchmarks: (\d+)') {
            $executed = [int] $Matches[1]
        }
    }

    if ($executed -eq 0) {
        return @("BenchmarkDotNet ran no benchmarks (exit code $script:toolExitCode), see $log")
    }

    $reports = @(Get-ChildItem -Path (Join-Path $artifacts 'results') -Filter '*-report-github.md' -ErrorAction SilentlyContinue |
        Sort-Object Name)

    if ($reports.Count -eq 0) {
        return @("BenchmarkDotNet left no report (exit code $script:toolExitCode), see $log")
    }

    # Every report starts with the same environment block: it is taken once, from the first report.
    $lines = @()

    foreach ($line in Get-Content -Path $reports[0].FullName -Encoding UTF8) {
        if ($line -like '|*') {
            break
        }

        $lines += $line
    }

    foreach ($report in $reports) {
        if ($reports.Count -gt 1) {
            # The report name is the full benchmark class name with a suffix; the heading is the bare class name.
            $className = ($report.Name -replace '-report-github\.md$', '').Split('.')[-1]
            $lines += @("### $className", '')
        }

        $lines += @(Get-Content -Path $report.FullName -Encoding UTF8 | Where-Object { $_ -like '|*' })
        $lines += ''
    }

    # A benchmark that failed shows NA instead of its numbers.
    if ($lines -match '\|\s*NA\s*\|') {
        $lines = @("Some benchmarks failed and show NA, see $log", '') + $lines
    }

    return $lines
}

# One Rust binary on a request suite. Returns its table rows.
function Invoke-RustRequests([string] $client, [string] $suite, [string] $log) {
    Write-Host ''
    Write-Host "== $client $suite"

    $output = Invoke-Tool $rustExe[$client] @($suite) $log
    $rows = @($output | Where-Object { $_ -like "| $client |*" })

    if ($rows.Count -eq 0) {
        $rows = @("| $client | failed: exit code $script:toolExitCode, $($output | Select-Object -Last 1) |")
    }

    return $rows
}

function Invoke-RequestSeries([string] $suite) {
    $title = if ($suite -eq 'rest') { 'REST' } else { 'WebSocket API' }
    $rustClients = @(@(if ($suite -eq 'rest') { 'binance-sdk', 'nautilus' } else { 'binance-sdk' }) | Where-Object { Test-Client $_ })
    $hasDotnet = (Test-Client 'axitor') -or (Test-Client 'binance-net')
    $hasAot = Test-Client 'axitor-aot'

    if (-not $hasDotnet -and -not $hasAot -and $rustClients.Count -eq 0) {
        Write-Host ''
        Write-Host "#### $title`: no clients match the client filter"
        return
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $name = "$suite-$stamp"
    $table = Join-Path $resultsDir "$name.md"
    $log = Join-Path $resultsDir "$name.log"
    $stubLog = Join-Path $resultsDir "$name-stub.log"

    Write-Host ''
    Write-Host "#### $title"

    # The stream pace does not touch REST or the WebSocket API.
    $stub = Start-Stub '1' $stubLog
    $dotnetLines = @()
    $aotLines = @()
    $rustRows = @()

    try {
        if ($hasDotnet) {
            $dotnetLines = Invoke-BenchmarkDotNet $suite $name $log
        }

        if ($hasAot) {
            $aotLines = Invoke-BenchmarkDotNet $suite $name $log -Aot
        }

        foreach ($client in $rustClients) {
            $rustRows += Invoke-RustRequests $client $suite $log
        }
    }
    finally {
        Stop-Stub $stub
    }

    $lines = @("# $title", '', "$(Get-Date -Format 'yyyy-MM-dd HH:mm'), BTCUSDT.", '')

    if ($hasDotnet) {
        $lines += @('## Axitor and Binance.Net (BenchmarkDotNet)', '') + $dotnetLines + @('')
    }

    if ($hasAot) {
        $lines += @(
            '## Axitor, Native AOT (BenchmarkDotNet)',
            '',
            'One joined run, so the AOT build happens once. Its Ratio columns compare across classes and mean nothing.',
            ''
        )
        $lines += $aotLines + @('')
    }

    if ($rustClients.Count -gt 0) {
        $lines += @('## Rust', '', $requestHeader, $requestSeparator) + $rustRows + @('')
        $lines += @(
            '## Notes',
            '',
            '- Rust: 20 measured iterations of about 250 ms after 5 warmup ones; Error is half the 99.9% interval, as in BenchmarkDotNet.',
            '- Rust allocations count every allocation on every thread during the measured iterations, freed or not.'
        )

        if ($rustClients -contains 'nautilus' -and $suite -eq 'rest') {
            $lines += '- Nautilus always applies its own Binance limits, so each of its benchmarks makes at most 2300 calls (new order 290) inside one burst and waits 61 s before the next.'
        }

        if ($suite -eq 'wsapi') {
            $lines += '- Nautilus has no WebSocket API run: its client only places and cancels orders, under a fixed 20 per second quota.'
        }
    }

    Set-Content -Path $table -Value $lines -Encoding UTF8
    Write-Host ''
    Write-Host "Saved $table"
}

# The harness prints µ: read its output as UTF-8.
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Without access to nuget.org the package audit fails the restore of BenchmarkDotNet's generated project.
$env:NuGetAudit = 'false'

try {
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    Invoke-Build

    if (Test-Suite 'rest') {
        Invoke-RequestSeries 'rest'
    }

    if (Test-Suite 'wsapi') {
        Invoke-RequestSeries 'wsapi'
    }

    if (Test-Suite 'streams') {
        if ($Pace -in '1', 'all') {
            Invoke-StreamSeries '1' (Select-Runs $latencyRuns)
        }

        if ($Pace -in 'max', 'all') {
            Invoke-StreamSeries 'max' (Select-Runs $throughputRuns)
        }
    }
}
finally {
    [Console]::OutputEncoding = $previousEncoding
}