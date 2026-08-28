[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'ToolBox-Plugins.sln'
$nugetConfigPath = Join-Path $repositoryRoot 'NuGet.config'

if (-not (Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'sdk') -Filter 'ToolBox.PluginSdk.*.nupkg' -File -ErrorAction SilentlyContinue)) {
    throw "ToolBox.PluginSdk package is missing from '$repositoryRoot\sdk'. Download it from the ToolBox GitHub Release first."
}

Push-Location $repositoryRoot
try {
    dotnet restore $solutionPath --configfile $nugetConfigPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    dotnet build $solutionPath --configuration $Configuration --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    dotnet test $solutionPath --configuration $Configuration --no-build --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Host 'Plugin repository validation passed.' -ForegroundColor Green
