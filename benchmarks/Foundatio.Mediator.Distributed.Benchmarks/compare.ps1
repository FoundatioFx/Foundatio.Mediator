[CmdletBinding()]
param(
    [switch]$Aws,
    [switch]$Tracking,
    [ValidateRange(1, 1000000)][int]$Messages = 10000,
    [ValidateRange(1, 100)][int]$Repetitions = 3
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$baselineRevision = '89bd6d1504b83d03de156b08f24fb578aa8bc97f'
$baselinePath = Join-Path $repositoryRoot '.dependencies/PR149'
if (!(Test-Path $baselinePath)) {
    & git clone --filter=blob:none --no-checkout https://github.com/FoundatioFx/Foundatio.Mediator.git $baselinePath
    if ($LASTEXITCODE -ne 0) { throw 'Unable to clone the baseline.' }
    & git -C $baselinePath fetch --depth 1 origin $baselineRevision
    if ($LASTEXITCODE -ne 0) { throw 'Unable to fetch the baseline revision.' }
    & git -C $baselinePath checkout --detach $baselineRevision
    if ($LASTEXITCODE -ne 0) { throw 'Unable to check out the baseline revision.' }
}
if ((& git -C $baselinePath rev-parse HEAD) -ne $baselineRevision -or (& git -C $baselinePath status --porcelain)) {
    throw 'The baseline must be clean and match the pinned PR 149 revision.'
}
$baselineProject = Join-Path $repositoryRoot '.dependencies/PR149Benchmark'
New-Item -ItemType Directory -Force $baselineProject | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'comparison/PR149.Program.cs.txt') (Join-Path $baselineProject 'Program.cs')
Copy-Item (Join-Path $PSScriptRoot 'comparison/PR149.csproj.txt') (Join-Path $baselineProject 'PR149Benchmark.csproj')
& dotnet build $PSScriptRoot -c Release -p:GeneratePackageOnBuild=false
if ($LASTEXITCODE -ne 0) { throw 'Native benchmark build failed.' }
& dotnet build $baselineProject -c Release -p:GeneratePackageOnBuild=false "-p:MediatorBaselinePath=$baselinePath"
if ($LASTEXITCODE -ne 0) { throw 'Baseline benchmark build failed.' }
$programs = @{
    native = Join-Path $PSScriptRoot 'bin/Release/net10.0/Foundatio.Mediator.Distributed.Benchmarks.dll'
    pr149 = Join-Path $baselineProject 'bin/Release/net10.0/PR149Benchmark.dll'
}
$outputPath = Join-Path $repositoryRoot "BenchmarkDotNet.Artifacts/native-comparison/$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Force $outputPath | Out-Null
$runArgs = @('--count', "$Messages", '--concurrency', '64')
if ($Aws) { $runArgs += '--aws' }
if ($Tracking) { $runArgs += '--tracking' }
for ($iteration = 0; $iteration -lt $Repetitions; $iteration++) {
    $order = if ($iteration % 2 -eq 0) { @('pr149', 'native') } else { @('native', 'pr149') }
    foreach ($implementation in $order) {
        $output = & dotnet $programs[$implementation] @runArgs
        if ($LASTEXITCODE -ne 0) { throw "$implementation run failed." }
        $result = $output | ConvertFrom-Json
        if ($result.UniqueProcessed -ne $Messages -or $result.Duplicates -ne 0) { throw 'Delivery accounting failed.' }
        $output | Set-Content (Join-Path $outputPath "$implementation-$iteration.json")
        Write-Host "$implementation : $([Math]::Round($result.MessagesPerSecond)) messages/s; $([Math]::Round($result.AllocatedBytesPerMessage)) bytes/message"
    }
}
Write-Host "Results: $outputPath"
