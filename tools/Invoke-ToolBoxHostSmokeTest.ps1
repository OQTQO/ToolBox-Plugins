[CmdletBinding()]
param(
    [string]$SoftwareRepository,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pluginRepository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SoftwareRepository = if ([string]::IsNullOrWhiteSpace($SoftwareRepository)) {
    Join-Path $pluginRepository '..\软件'
} else {
    $SoftwareRepository
}
$softwareRoot = [System.IO.Path]::GetFullPath($SoftwareRepository)
$hostProject = Join-Path $softwareRoot 'src\ToolBox.Host\ToolBox.Host.csproj'
$workerProject = Join-Path $softwareRoot 'src\ToolBox.PluginWorker\ToolBox.PluginWorker.csproj'
$pluginSolution = Join-Path $pluginRepository 'ToolBox-Plugins.sln'
$nugetConfig = Join-Path $pluginRepository 'NuGet.config'
$packageScript = Join-Path $PSScriptRoot 'New-PluginPackage.ps1'
$tempBase = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'ToolBoxCrossRepoSmoke'))
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase ([Guid]::NewGuid().ToString('N'))))

$plugins = @(
    [PSCustomObject]@{
        Project = 'plugins\KeyboardMouse\KeyboardTest.csproj'
        Manifest = 'plugins\KeyboardMouse\manifest.json'
        AssemblyName = 'KeyboardTest.dll'
    },
    [PSCustomObject]@{
        Project = 'plugins\AudioRelay\AudioRelay.csproj'
        Manifest = 'plugins\AudioRelay\manifest.json'
        AssemblyName = 'AudioRelay.dll'
    }
)

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Get-SingleArtifact {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$FileName,
        [string]$ProjectDirectoryName
    )

    $matches = @(Get-ChildItem -LiteralPath $Root -Filter $FileName -File -Recurse | Where-Object {
        [string]::IsNullOrWhiteSpace($ProjectDirectoryName) -or
        $_.FullName -match "[\\/]bin[\\/]$([regex]::Escape($ProjectDirectoryName))[\\/]"
    })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$FileName' artifact below '$Root', found $($matches.Count)."
    }

    return $matches[0].FullName
}

if (-not (Test-Path -LiteralPath $hostProject -PathType Leaf)) {
    throw "ToolBox software repository is missing the Host project: '$hostProject'."
}
if (-not (Test-Path -LiteralPath $pluginSolution -PathType Leaf)) {
    throw "Plugin solution is missing: '$pluginSolution'."
}
if (-not $runRoot.StartsWith($tempBase + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a smoke-test directory outside '$tempBase'."
}

$softwareArtifacts = Join-Path $runRoot 'software-artifacts'
$hostPublishRoot = Join-Path $runRoot 'host-publish'
$workerPublishRoot = Join-Path $runRoot 'worker-publish'
$pluginArtifacts = Join-Path $runRoot 'plugin-artifacts'
$packageRoot = Join-Path $runRoot 'packages'
$hostWorkingRoot = Join-Path $runRoot 'host-working'
$resultPath = Join-Path $runRoot 'host-smoke-result.json'

try {
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

    Invoke-DotNet -Arguments @(
        'restore', $hostProject,
        '--artifacts-path', $softwareArtifacts
    )
    Invoke-DotNet -Arguments @(
        'publish', $hostProject,
        '--configuration', $Configuration,
        '--artifacts-path', $softwareArtifacts,
        '--output', $hostPublishRoot,
        '--no-restore',
        '-warnaserror',
        '--disable-build-servers'
    )
    Invoke-DotNet -Arguments @(
        'publish', $workerProject,
        '--configuration', $Configuration,
        '--artifacts-path', $softwareArtifacts,
        '--output', $workerPublishRoot,
        '--no-restore',
        '-warnaserror',
        '--disable-build-servers'
    )

    Invoke-DotNet -Arguments @(
        'restore', $pluginSolution,
        '--configfile', $nugetConfig,
        '--artifacts-path', $pluginArtifacts,
        '-p:NuGetAudit=false'
    )
    Invoke-DotNet -Arguments @(
        'build', $pluginSolution,
        '--configuration', $Configuration,
        '--artifacts-path', $pluginArtifacts,
        '--no-restore',
        '--no-incremental',
        '-warnaserror',
        '-p:NuGetAudit=false',
        '--disable-build-servers'
    )

    $hostExecutable = Join-Path $hostPublishRoot 'ToolBox.Host.exe'
    $workerExecutable = Join-Path $workerPublishRoot 'ToolBox.PluginWorker.exe'
    if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
        throw "Published ToolBox Host executable is missing: '$hostExecutable'."
    }
    if (-not (Test-Path -LiteralPath $workerExecutable -PathType Leaf)) {
        throw "Published ToolBox PluginWorker executable is missing: '$workerExecutable'."
    }
    $packagePaths = @()
    $signingCertificatePath = Join-Path $runRoot 'smoke-signing.cer'
    $signingPrivateKeyPath = Join-Path $runRoot 'smoke-signing.pk8'
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=ToolBox Cross Repository Smoke',
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddDays(-1),
            [DateTimeOffset]::UtcNow.AddDays(7))
        try {
            [System.IO.File]::WriteAllBytes($signingCertificatePath, $certificate.Export(
                [System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
            [System.IO.File]::WriteAllBytes($signingPrivateKeyPath, $rsa.ExportPkcs8PrivateKey())
        }
        finally {
            $certificate.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }

    foreach ($plugin in $plugins) {
        $projectPath = Join-Path $pluginRepository $plugin.Project
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
        $assemblyPath = Get-SingleArtifact -Root $pluginArtifacts -FileName $plugin.AssemblyName -ProjectDirectoryName $projectName
        $manifestPath = Join-Path $pluginRepository $plugin.Manifest
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $packageName = "$($manifest.id)-$($manifest.version).tpk"
        & $packageScript `
            -RuntimeDirectory (Split-Path -Parent $assemblyPath) `
            -ManifestPath $manifestPath `
            -OutputDirectory $packageRoot `
            -PackageName $packageName `
            -SigningCertificatePath $signingCertificatePath `
            -SigningPrivateKeyPath $signingPrivateKeyPath
        $packagePaths += Join-Path $packageRoot $packageName
    }

    $hostArguments = @(
        '--smoke-test-worker', $workerExecutable,
        '--smoke-test-root', $hostWorkingRoot,
        '--smoke-test-result', $resultPath
    )
    foreach ($packagePath in $packagePaths) {
        $hostArguments += @('--smoke-test-package', $packagePath)
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $hostArguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $hostProcess = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $hostProcess) {
        throw "ToolBox Host smoke-test process could not be started."
    }
    try {
        $hostProcess.WaitForExit()
        $hostExitCode = $hostProcess.ExitCode
    }
    finally {
        $hostProcess.Dispose()
    }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "ToolBox Host did not produce the smoke-test result '$resultPath' (exit code $hostExitCode)."
    }

    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($hostExitCode -ne 0 -or -not $result.success) {
        throw "ToolBox Host smoke test failed (exit code $hostExitCode): $($result | ConvertTo-Json -Depth 10 -Compress)"
    }
    if (@($result.packages).Count -ne $plugins.Count) {
        throw "ToolBox Host smoke test returned $(@($result.packages).Count) package results; expected $($plugins.Count)."
    }

    foreach ($package in @($result.packages)) {
        if (-not ($package.installed -and $package.enabled -and $package.disabled -and $package.uninstalled)) {
            throw "Plugin '$($package.pluginId)' did not complete every Host lifecycle stage."
        }

        Write-Host "Host smoke passed: $($package.pluginId) $($package.version) (install -> enable -> disable -> uninstall)" -ForegroundColor Green
    }
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
        if (-not $resolvedRunRoot.StartsWith($tempBase + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a smoke-test directory outside '$tempBase'."
        }
        try {
            Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        }
        catch {
            Write-Warning "Could not clean smoke-test directory '$resolvedRunRoot': $($_.Exception.Message)"
        }
    }
}

Write-Host 'Cross-repository ToolBox Host smoke test passed.' -ForegroundColor Green
