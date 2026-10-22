#Requires -Version 5.1
# Build with .\build.ps1; add -Test to run the checks after publishing.
[CmdletBinding()]
param([switch]$Test)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'AudioToggle must be built on Windows.'
}

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $dotnet) {
    throw 'Install the .NET 10 SDK or newer. The Desktop Runtime alone cannot build the app.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    $sdks = @(& $dotnet.Source --list-sdks)
    if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 })) {
        throw 'Install the .NET 10 SDK or newer. The Desktop Runtime alone cannot build the app.'
    }

    $project = Join-Path $PSScriptRoot 'AudioToggle\AudioToggle.csproj'
    $testProject = Join-Path $PSScriptRoot 'AudioToggle.Tests\AudioToggle.Tests.csproj'
    $output = Join-Path $PSScriptRoot 'publish'
    $exe = Join-Path $output 'AudioToggle.exe'
    if (-not (Test-Path -LiteralPath $project) -or ($Test -and -not (Test-Path -LiteralPath $testProject))) {
        throw 'Keep build.ps1 beside the AudioToggle and AudioToggle.Tests source folders.'
    }
    $running = @(Get-Process -Name AudioToggle -ErrorAction SilentlyContinue | Where-Object { $Test -or $_.Path -eq $exe })
    if ($running.Count -gt 0) {
        throw 'Close AudioToggle using Tray > Exit before building or running the checks.'
    }

    & $dotnet.Source publish $project -c Release -o $output --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)." }

    if ($Test) {
        & $dotnet.Source run --project $testProject -c Release --disable-build-servers -p:UseSharedCompilation=false -- --exe $exe
        if ($LASTEXITCODE -ne 0) { throw "Checks failed (exit code $LASTEXITCODE)." }
    }
    Write-Host "Built: $exe"
}
finally { Pop-Location }
