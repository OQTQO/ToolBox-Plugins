[CmdletBinding()]
param(
    [switch]$Full,

    [switch]$IncludeDiffStat
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$workspacePath = Join-Path $workspaceRoot 'WORKSPACE.md'

if ($Full -and (Test-Path -LiteralPath $workspacePath)) {
    Write-Host "=== WORKSPACE.md ==="
    Get-Content -LiteralPath $workspacePath -Encoding UTF8
}

Write-Host "Repository: $repoRoot"
Write-Host "`n=== AGENTS.md ==="
Get-Content -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -Encoding UTF8

$activeTask = Join-Path $repoRoot 'docs\active-task.md'
if ($Full) {
    Write-Host "`n=== AI.md ==="
    Get-Content -LiteralPath (Join-Path $repoRoot 'AI.md') -Encoding UTF8
    Write-Host "`n=== active task ==="
    Get-Content -LiteralPath $activeTask -Encoding UTF8
}
elseif (Test-Path -LiteralPath $activeTask) {
    $lines = @(Get-Content -LiteralPath $activeTask -Encoding UTF8)
    $statusPrefix = -join [char[]]@(0x72B6, 0x6001, 0xFF1A)
    $taskHeading = -join [char[]]@(0x4EFB, 0x52A1)
    $nextHeading = -join [char[]]@(0x4E0B, 0x4E00, 0x6B65)
    Write-Host "`n=== active task summary ==="
    $lines | Where-Object { $_.StartsWith($statusPrefix, [System.StringComparison]::Ordinal) } | Select-Object -First 1
    foreach ($heading in @($taskHeading, $nextHeading)) {
        $start = [Array]::IndexOf($lines, "## $heading")
        if ($start -lt 0) { continue }
        Write-Host "`n=== $heading ==="
        for ($index = $start + 1; $index -lt $lines.Count -and $lines[$index] -notmatch '^##\s+'; $index++) {
            if (-not [string]::IsNullOrWhiteSpace($lines[$index])) { $lines[$index] }
        }
    }
}

$compatibility = Join-Path $repoRoot 'docs\compatibility.md'
if ($Full -and (Test-Path -LiteralPath $compatibility)) {
    Write-Host "`n=== compatibility ==="
    Get-Content -LiteralPath $compatibility -Encoding UTF8
}

$softwareDirectoryName = [string]::Concat([char]0x8F6F, [char]0x4EF6)
$softwareRepo = Join-Path $workspaceRoot $softwareDirectoryName
$sdkProjectPath = Join-Path $softwareRepo 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'
if (Test-Path -LiteralPath $sdkProjectPath) {
    Write-Host "`n=== authoritative software ==="
    git -C $softwareRepo log -1 --oneline --decorate
    $softwareReleaseTag = [string](git -C $softwareRepo describe --tags --abbrev=0 2>$null | Select-Object -First 1)
    if (-not [string]::IsNullOrWhiteSpace($softwareReleaseTag)) {
        Write-Host "ToolBox latest Release tag: $softwareReleaseTag"
    }
    [xml]$sdkProject = Get-Content -LiteralPath $sdkProjectPath -Raw -Encoding UTF8
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
