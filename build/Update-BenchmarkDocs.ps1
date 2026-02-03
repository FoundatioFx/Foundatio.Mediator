<#
.SYNOPSIS
    Runs benchmarks and updates the performance documentation page.

.DESCRIPTION
    This script runs the Foundatio.Mediator benchmarks using BenchmarkDotNet,
    then parses the CSV results and generates a compact markdown table for
    the docs/guide/performance.md page.

.PARAMETER SkipBenchmarks
    Skip running benchmarks and only update docs from existing results.

.PARAMETER FoundatioOnly
    Run only Foundatio benchmarks and merge with existing comparison results.
    This is much faster than running full comparison benchmarks.

.EXAMPLE
    ./Update-BenchmarkDocs.ps1

.EXAMPLE
    ./Update-BenchmarkDocs.ps1 -SkipBenchmarks

.EXAMPLE
    ./Update-BenchmarkDocs.ps1 -FoundatioOnly
#>

param(
    [switch]$SkipBenchmarks,
    [switch]$FoundatioOnly
)

$ErrorActionPreference = 'Stop'

# Configuration - repo root (1 level up)
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $RepoRoot -or -not (Test-Path $RepoRoot)) {
    $RepoRoot = Get-Location
}

$BenchmarkProject = Join-Path $RepoRoot 'benchmarks/Foundatio.Mediator.Benchmarks'
$CoreResultsFile = Join-Path $BenchmarkProject 'BenchmarkDotNet.Artifacts/results/Foundatio.Mediator.Benchmarks.CoreBenchmarks-report.csv'
$FoundatioResultsFile = Join-Path $BenchmarkProject 'BenchmarkDotNet.Artifacts/results/Foundatio.Mediator.Benchmarks.FoundatioBenchmarks-report.csv'
$PerfDoc = Join-Path $RepoRoot 'docs/guide/performance.md'

# Helper function to convert Mean value string to nanoseconds
function ConvertTo-Nanoseconds {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq 'NA') {
        return [double]::MaxValue
    }
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

# Helper function to convert Allocated value string to bytes
function ConvertTo-Bytes {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq 'NA' -or $Value -eq '-') {
        return $null
    }
    if ($Value -match '^([\d,.]+)\s*B$') {
        return [int64]($Matches[1] -replace ',', '')
    }
    if ($Value -match '^([\d,.]+)\s*KB$') {
        return [int64](([double]($Matches[1] -replace ',', '')) * 1024)
    }
    if ($Value -match '^([\d,.]+)\s*MB$') {
        return [int64](([double]($Matches[1] -replace ',', '')) * 1024 * 1024)
    }
    return $null
}

# Load baseline from existing CSV before running benchmarks (for comparison)
$baseline = @{}
if ($FoundatioOnly -and (Test-Path $FoundatioResultsFile)) {
    Write-Host "Loading baseline from existing results..." -ForegroundColor Cyan
    $baselineCsv = Import-Csv $FoundatioResultsFile
    foreach ($row in $baselineCsv) {
        $baseline[$row.Method] = $row
    }
    Write-Host "Loaded $($baseline.Count) baseline entries" -ForegroundColor Cyan
}

# Run benchmarks if not skipped
if (-not $SkipBenchmarks) {
    if ($FoundatioOnly) {
        Write-Host "Running Foundatio benchmarks..." -ForegroundColor Cyan
        Push-Location $BenchmarkProject
        try {
            dotnet run -c Release -- foundatio --exporters csv
            if ($LASTEXITCODE -ne 0) {
                throw "Benchmark run failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    } else {
        Write-Host "Running full comparison benchmarks..." -ForegroundColor Cyan
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
}

# Determine which results file to use
$ResultsFile = if ($FoundatioOnly) {
    # For Foundatio-only mode, we'll merge results
    $FoundatioResultsFile
} else {
    # For full benchmarks, use CoreBenchmarks results directly
    $CoreResultsFile
}

# Verify results file exists
if (-not (Test-Path $ResultsFile)) {
    throw "Benchmark results file not found: $ResultsFile"
}

Write-Host "Parsing benchmark results..." -ForegroundColor Cyan

# Parse CSV and build rows array
$csv = @()

if ($FoundatioOnly) {
    # Merge Foundatio results with existing CoreBenchmarks results
    Write-Host "Merging Foundatio results with existing comparison benchmarks..." -ForegroundColor Cyan

    # Parse Foundatio CSV results
    $foundatioCsv = Import-Csv $FoundatioResultsFile

    # Create lookup table for Foundatio results
    $foundatioResults = @{}
    foreach ($row in $foundatioCsv) {
        # Map FoundatioBenchmarks method names to CoreBenchmarks naming convention
        $methodName = switch ($row.Method) {
            'Command' { 'Foundatio_Command' }
            'Query' { 'Foundatio_Query' }
            'Publish' { 'Foundatio_Publish' }
            'FullQuery' { 'Foundatio_FullQuery' }
            'CascadingMessages' { 'Foundatio_CascadingMessages' }
            'ShortCircuit' { 'Foundatio_ShortCircuit' }
            'ExecuteMiddleware' { 'Foundatio_ExecuteMiddleware' }
            default { "Foundatio_$($row.Method)" }
        }
        $foundatioResults[$methodName] = $row
    }

    Write-Host "Found $($foundatioResults.Count) Foundatio benchmark results" -ForegroundColor Cyan

    # Display comparison with baseline if available
    if ($baseline.Count -gt 0) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host "  BENCHMARK COMPARISON (Before -> After)" -ForegroundColor Yellow
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host ""
        Write-Host ("{0,-20} {1,15} {2,15} {3,12} {4,15}" -f "Method", "Before", "After", "Change", "Alloc Change") -ForegroundColor White
        Write-Host ("{0,-20} {1,15} {2,15} {3,12} {4,15}" -f "------", "------", "-----", "------", "------------") -ForegroundColor Gray

        foreach ($row in $foundatioCsv) {
            $method = $row.Method
            $newMean = $row.Mean
            $newNs = ConvertTo-Nanoseconds $newMean
            $newAlloc = ConvertTo-Bytes $row.Allocated

            if ($baseline.ContainsKey($method)) {
                $oldMean = $baseline[$method].Mean
                $oldNs = ConvertTo-Nanoseconds $oldMean
                $oldAlloc = ConvertTo-Bytes $baseline[$method].Allocated

                if ($oldNs -gt 0 -and $newNs -lt [double]::MaxValue) {
                    $pctChange = (($newNs - $oldNs) / $oldNs) * 100
                    $changeStr = if ($pctChange -lt 0) {
                        "{0:N1}%" -f $pctChange
                    } else {
                        "+{0:N1}%" -f $pctChange
                    }
                    # Consider both percentage AND absolute time - sub-1ns measurements are noise
                    $isSignificant = ($oldNs -gt 1) -and ($newNs -gt 1)
                    $color = if (-not $isSignificant) { "Gray" }
                             elseif ($pctChange -lt -5) { "Green" }
                             elseif ($pctChange -gt 5) { "Red" }
                             else { "Gray" }

                    # Calculate allocation change
                    $allocChangeStr = "N/A"
                    $allocColor = "Gray"
                    if ($null -ne $oldAlloc -and $null -ne $newAlloc) {
                        $allocDiff = $newAlloc - $oldAlloc
                        if ($allocDiff -eq 0) {
                            $allocChangeStr = "0 B"
                            $allocColor = "Gray"
                        } elseif ($allocDiff -lt 0) {
                            $allocChangeStr = "{0:N0} B" -f $allocDiff
                            $allocColor = "Green"
                        } else {
                            $allocChangeStr = "+{0:N0} B" -f $allocDiff
                            $allocColor = "Red"
                        }
                    } elseif ($null -eq $oldAlloc -and $null -ne $newAlloc) {
                        $allocChangeStr = "+{0:N0} B" -f $newAlloc
                        $allocColor = "Red"
                    } elseif ($null -ne $oldAlloc -and $null -eq $newAlloc) {
                        $allocChangeStr = "-{0:N0} B" -f $oldAlloc
                        $allocColor = "Green"
                    }

                    Write-Host ("{0,-20} {1,15} {2,15} " -f $method, $oldMean, $newMean) -NoNewline
                    Write-Host ("{0,12} " -f $changeStr) -ForegroundColor $color -NoNewline
                    Write-Host ("{0,15}" -f $allocChangeStr) -ForegroundColor $allocColor
                } else {
                    Write-Host ("{0,-20} {1,15} {2,15} {3,12} {4,15}" -f $method, $oldMean, $newMean, "N/A", "N/A") -ForegroundColor Gray
                }
            } else {
                Write-Host ("{0,-20} {1,15} {2,15} {3,12} {4,15}" -f $method, "(new)", $newMean, "NEW", "NEW") -ForegroundColor Cyan
            }
        }
        Write-Host ""
        Write-Host "Legend: " -NoNewline -ForegroundColor White
        Write-Host "Green = improved (>5%), " -NoNewline -ForegroundColor Green
        Write-Host "Red = regressed (>5%), " -NoNewline -ForegroundColor Red
        Write-Host "Gray = within noise" -ForegroundColor Gray
        Write-Host ""
    }

    # Load existing CoreBenchmarks results
    if (Test-Path $CoreResultsFile) {
        Write-Host "Loading existing comparison benchmark results..." -ForegroundColor Cyan
        $existingRows = @(Import-Csv $CoreResultsFile)

        # Merge: Remove old Foundatio entries and add new ones
        foreach ($row in $existingRows) {
            if ($row.Method -notlike 'Foundatio_*') {
                $csv += $row
            }
        }
    }

    # Add new Foundatio results
    foreach ($entry in $foundatioResults.GetEnumerator()) {
        $csv += $entry.Value | Select-Object @{N='Method';E={$entry.Key}}, Mean, Allocated, Gen0, Gen1, Gen2
    }

    Write-Host "Total benchmark entries after merge: $($csv.Count)" -ForegroundColor Cyan
} else {
    # Use CoreBenchmarks results directly
    $csv = Import-Csv $ResultsFile
}
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

# Sort each group by Mean (best performance first)
$groupKeys = @($groups.Keys)
foreach ($key in $groupKeys) {
    $groups[$key] = $groups[$key] | Sort-Object { ConvertTo-Nanoseconds $_.Mean }
}

# Helper function to format allocated bytes with thousand separators
function Format-Allocated {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq 'NA') {
        return 'NA'
    }
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

Process a message with no return value.

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

if ($FoundatioOnly) {
    Write-Host "Performance documentation updated with Foundatio results: $PerfDoc" -ForegroundColor Green
    Write-Host "Foundatio entries have been merged and sorted with existing comparison results." -ForegroundColor Green
} else {
    Write-Host "Performance documentation updated: $PerfDoc" -ForegroundColor Green
}
