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

# Group benchmarks by operation type (Command, Query, Event/Publish, QueryWithDependencies)
$groups = @{
    'Command' = @()
    'Query' = @()
    'Publish' = @()
    'QueryWithDependencies' = @()
}

# Define the order of implementations
$implOrder = @('Direct', 'Foundatio', 'MediatR', 'MassTransit')

foreach ($row in $csv) {
    $method = $row.Method
    if ($method -match 'QueryWithDependencies') {
        $groups['QueryWithDependencies'] += $row
    } elseif ($method -match 'Command') {
        $groups['Command'] += $row
    } elseif ($method -match 'Query') {
        $groups['Query'] += $row
    } elseif ($method -match 'Publish|Event') {
        $groups['Publish'] += $row
    }
}

# Sort each group by implementation order
foreach ($key in $groups.Keys) {
    $groups[$key] = $groups[$key] | Sort-Object {
        $method = $_.Method
        for ($i = 0; $i -lt $implOrder.Count; $i++) {
            if ($method -match "^$($implOrder[$i])_") { return $i }
        }
        return 999
    }
}

# Build the markdown content
$markdown = @"
# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** $currentDate

### Commands

| Method | Mean | Allocated |
|:-------|-----:|----------:|
"@

foreach ($row in $groups['Command']) {
    $markdown += "`n| $($row.Method) | $($row.Mean) | $($row.Allocated) |"
}

$markdown += @"


### Queries

| Method | Mean | Allocated |
|:-------|-----:|----------:|
"@

foreach ($row in $groups['Query']) {
    $markdown += "`n| $($row.Method) | $($row.Mean) | $($row.Allocated) |"
}

$markdown += @"


### Events (Publish)

| Method | Mean | Allocated |
|:-------|-----:|----------:|
"@

foreach ($row in $groups['Publish']) {
    $markdown += "`n| $($row.Method) | $($row.Mean) | $($row.Allocated) |"
}

$markdown += @"


### Queries with Dependencies

| Method | Mean | Allocated |
|:-------|-----:|----------:|
"@

foreach ($row in $groups['QueryWithDependencies']) {
    $markdown += "`n| $($row.Method) | $($row.Mean) | $($row.Allocated) |"
}

$markdown += @"


## Running Benchmarks Locally

``````bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
``````
"@

# Write the file
$markdown | Out-File -FilePath $PerfDoc -Encoding utf8 -NoNewline

Write-Host "Performance documentation updated: $PerfDoc" -ForegroundColor Green
