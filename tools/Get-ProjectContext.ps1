[CmdletBinding()]
param(
    [switch]$IncludeDiffStat
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$workspacePath = Join-Path $workspaceRoot 'WORKSPACE.md'

if (Test-Path -LiteralPath $workspacePath) {
    Write-Host "=== WORKSPACE.md ==="
    Get-Content -LiteralPath $workspacePath
}

Write-Host "Repository: $repoRoot"
Write-Host "`n=== AI.md ==="
Get-Content -LiteralPath (Join-Path $repoRoot 'AI.md')

$activeTask = Join-Path $repoRoot 'docs\active-task.md'
if (Test-Path -LiteralPath $activeTask) {
    Write-Host "`n=== active task ==="
    Get-Content -LiteralPath $activeTask
}

$compatibility = Join-Path $repoRoot 'docs\compatibility.md'
if (Test-Path -LiteralPath $compatibility) {
    Write-Host "`n=== compatibility ==="
    Get-Content -LiteralPath $compatibility
}

$softwareRepo = Join-Path $workspaceRoot '软件'
$sdkProjectPath = Join-Path $softwareRepo 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'
if (Test-Path -LiteralPath $sdkProjectPath) {
    Write-Host "`n=== authoritative software ==="
    git -C $softwareRepo log -1 --oneline --decorate
    [xml]$sdkProject = Get-Content -LiteralPath $sdkProjectPath -Raw
    $sdkVersion = $sdkProject.Project.PropertyGroup.PackageVersion | Select-Object -First 1
    Write-Host "ToolBox.PluginSdk package version: $sdkVersion"
}

Write-Host "`n=== git status ==="
git -C $repoRoot status --short --branch

Write-Host "`n=== git head ==="
git -C $repoRoot log -1 --oneline --decorate

if ($IncludeDiffStat) {
    Write-Host "`n=== diff stat ==="
    git -C $repoRoot diff --stat
}
