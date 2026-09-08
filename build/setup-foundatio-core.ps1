[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$dependencyLock = Get-Content (Join-Path $PSScriptRoot 'foundatio-core.json') -Raw | ConvertFrom-Json
$destination = Join-Path $repositoryRoot '.dependencies/Foundatio'

if (Test-Path $destination) {
    $revision = & git -C $destination rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -ne $dependencyLock.revision) {
        throw "The existing dependency at $destination is not the pinned revision. Preserve or move that checkout before running setup again. For core development, pass -p:FoundatioCorePath=<checkout> to dotnet build."
    }
    $changes = & git -C $destination status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) {
        throw "The pinned dependency has local changes. Use an explicit FoundatioCorePath for development."
    }
} else {
    & git clone --filter=blob:none --no-checkout $dependencyLock.repository $destination
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone Foundatio.' }
    & git -C $destination fetch --depth 1 origin $dependencyLock.revision
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the pinned Foundatio revision.' }
    & git -C $destination checkout --detach $dependencyLock.revision
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the pinned Foundatio revision.' }
}

Write-Host "Foundatio $($dependencyLock.revision) is ready at $destination"
