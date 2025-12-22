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

# Build the markdown content
$markdown = @"
# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** $currentDate
> 🔧 **Generated automatically by [GitHub Actions](https://github.com/FoundatioFx/Foundatio.Mediator/actions/workflows/benchmarks.yml)**

| Method | Mean | Allocated |
|:-------|-----:|----------:|
"@

foreach ($row in $csv) {
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
