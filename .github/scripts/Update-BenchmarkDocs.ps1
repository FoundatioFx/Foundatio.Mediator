<#
.SYNOPSIS
    Runs benchmarks and updates the performance documentation page.

.DESCRIPTION
    This script runs the Foundatio.Mediator benchmarks using BenchmarkDotNet,
    then parses the CSV results and generates a compact markdown table for
    the docs/guide/performance.md page.

.PARAMETER SkipBenchmarks
    Skip running benchmarks and only update docs from existing results.

.EXAMPLE
    ./Update-BenchmarkDocs.ps1

.EXAMPLE
    ./Update-BenchmarkDocs.ps1 -SkipBenchmarks
#>

param(
    [switch]$SkipBenchmarks
)

$ErrorActionPreference = 'Stop'

# Configuration - .github/scripts -> .github -> repo root (2 levels up)
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $RepoRoot -or -not (Test-Path $RepoRoot)) {
    $RepoRoot = Get-Location
}

$BenchmarkProject = Join-Path $RepoRoot 'benchmarks/Foundatio.Mediator.Benchmarks'
$ResultsFile = Join-Path $BenchmarkProject 'BenchmarkDotNet.Artifacts/results/Foundatio.Mediator.Benchmarks.CoreBenchmarks-report.csv'
$PerfDoc = Join-Path $RepoRoot 'docs/guide/performance.md'

# Run benchmarks if not skipped
if (-not $SkipBenchmarks) {
    Write-Host "Running benchmarks..." -ForegroundColor Cyan
    Push-Location $BenchmarkProject
    try {
        dotnet run -c Release -- --exporters csv
        if ($LASTEXITCODE -ne 0) {
            throw "Benchmark run failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

# Verify results file exists
if (-not (Test-Path $ResultsFile)) {
    throw "Benchmark results file not found: $ResultsFile"
}

Write-Host "Parsing benchmark results..." -ForegroundColor Cyan

# Parse CSV and build markdown table
$csv = Import-Csv $ResultsFile
$currentDate = Get-Date -Format 'yyyy-MM-dd'

# Group benchmarks by operation type
$groups = @{
    'Command' = @()
    'Query' = @()
    'Publish' = @()
    'FullQuery' = @()
    'CascadingMessages' = @()
    'ShortCircuit' = @()
}

# Define the order of implementations
$implOrder = @('Direct', 'MediatorNet', 'MediatR', 'Foundatio', 'Wolverine', 'MassTransit', 'ImmediateHandlers')

foreach ($row in $csv) {
    $method = $row.Method
    if ($method -match 'ShortCircuit') {
        $groups['ShortCircuit'] += $row
    } elseif ($method -match 'CascadingMessages') {
        $groups['CascadingMessages'] += $row
    } elseif ($method -match 'FullQuery') {
        $groups['FullQuery'] += $row
    } elseif ($method -match 'Command') {
        $groups['Command'] += $row
    } elseif ($method -match 'Query') {
        $groups['Query'] += $row
    } elseif ($method -match 'Publish|Event') {
        $groups['Publish'] += $row
    }
}

# Helper function to parse Mean value to nanoseconds for sorting
function Parse-MeanToNs {
    param([string]$Value)
    if ($Value -match '^([\d,.]+)\s*(ns|us|μs|ms|s)$') {
        $num = [double]($Matches[1] -replace ',', '')
        $unit = $Matches[2]
        switch ($unit) {
            'ns' { return $num }
            'us' { return $num * 1000 }
            'μs' { return $num * 1000 }
            'ms' { return $num * 1000000 }
            's'  { return $num * 1000000000 }
            default { return $num }
        }
    }
    return [double]::MaxValue
}

# Sort each group by Mean (best performance first)
$groupKeys = @($groups.Keys)
foreach ($key in $groupKeys) {
    $groups[$key] = $groups[$key] | Sort-Object { Parse-MeanToNs $_.Mean }
}

# Helper function to format allocated bytes with thousand separators
function Format-Allocated {
    param([string]$Value)
    if ($Value -match '^(\d+)\s*B$') {
        $num = [int64]$Matches[1]
        return "{0:N0} B" -f $num
    }
    return $Value
}

# Helper function to build an HTML table for a group
function Build-Table {
    param($Rows)
    $table = @"
<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody>
"@
    foreach ($row in $Rows) {
        $alloc = Format-Allocated $row.Allocated
        $table += "<tr><td style=`"width:100%`"><code>$($row.Method)</code></td><td style=`"text-align:right;white-space:nowrap`">$($row.Mean)</td><td style=`"text-align:right;white-space:nowrap`">$alloc</td></tr>`n"
    }
    $table += "</tbody>`n</table>"
    return $table
}

# Build the markdown content
$markdown = @"
# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** $currentDate

### Commands

Fire-and-forget dispatch with no return value.

$(Build-Table $groups['Command'])

### Queries

Request/response dispatch returning an Order object.

$(Build-Table $groups['Query'])

### Events (Publish)

Notification dispatched to 2 handlers.

$(Build-Table $groups['Publish'])

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

$(Build-Table $groups['FullQuery'])

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

$(Build-Table $groups['CascadingMessages'])

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

$(Build-Table $groups['ShortCircuit'])

## Running Benchmarks Locally

``````bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
``````
"@

# Write the file
$markdown | Out-File -FilePath $PerfDoc -Encoding utf8 -NoNewline

Write-Host "Performance documentation updated: $PerfDoc" -ForegroundColor Green
