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
$packageScript = Join-Path $PSScriptRoot 'New-PluginPackage.ps1'
$packageValidationScript = Join-Path $PSScriptRoot 'Test-PluginPackage.ps1'
$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxPluginBuild\$([Guid]::NewGuid().ToString('N'))"
$validationArtifacts = Join-Path $validationRoot 'artifacts'
$plugins = @(
    [PSCustomObject]@{
        Project = 'plugins\KeyboardMouse\KeyboardTest.csproj'
        Manifest = 'plugins\KeyboardMouse\manifest.json'
        TargetFramework = 'net10.0'
        AssemblyName = 'KeyboardTest'
    },
    [PSCustomObject]@{
        Project = 'plugins\AudioRelay\AudioRelay.csproj'
        Manifest = 'plugins\AudioRelay\manifest.json'
        TargetFramework = 'net10.0-windows10.0.19041.0'
        AssemblyName = 'AudioRelay'
    }
)

function Get-PluginVersion {
    param([Parameter(Mandatory)][object]$Plugin)

    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot $Plugin.Project) -Raw
    $projectVersion = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
    $manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot $Plugin.Manifest) -Raw | ConvertFrom-Json
    $manifestVersion = [string]$manifest.version
    if ($projectVersion -cne $manifestVersion) {
        throw "Plugin project '$($Plugin.Project)' version '$projectVersion' does not match manifest version '$manifestVersion'."
    }

    return $projectVersion
}

function Assert-PluginAssemblyVersion {
    param(
        [Parameter(Mandatory)][object]$Plugin,
        [Parameter(Mandatory)][string]$ExpectedVersion
    )

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($Plugin.Project)
    $assemblyPath = Join-Path $validationArtifacts "bin\$projectName\$($Configuration.ToLowerInvariant())\$($Plugin.AssemblyName).dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Built plugin assembly is missing: '$assemblyPath'."
    }

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString(3)
    if ($assemblyVersion -cne $ExpectedVersion) {
        throw "Plugin assembly '$assemblyPath' version '$assemblyVersion' does not match '$ExpectedVersion'."
    }
}

if (-not (Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'sdk') -Filter 'ToolBox.PluginSdk.*.nupkg' -File -ErrorAction SilentlyContinue)) {
    throw "ToolBox.PluginSdk package is missing from '$repositoryRoot\sdk'. Download it from the ToolBox GitHub Release first."
}

Push-Location $repositoryRoot
try {
    dotnet restore $solutionPath --configfile $nugetConfigPath --artifacts-path $validationArtifacts -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    dotnet build $solutionPath --configuration $Configuration --artifacts-path $validationArtifacts --no-restore --no-incremental -warnaserror --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    foreach ($plugin in $plugins) {
        $pluginVersion = Get-PluginVersion -Plugin $plugin
        Assert-PluginAssemblyVersion -Plugin $plugin -ExpectedVersion $pluginVersion
    }

    dotnet test $solutionPath --configuration $Configuration --artifacts-path $validationArtifacts --no-build --no-restore --disable-build-servers --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

    $packageValidationRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxPluginValidation\$([Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $packageValidationRoot -Force | Out-Null
        $signingCertificatePath = Join-Path $packageValidationRoot 'validation-signing.cer'
        $signingPrivateKeyPath = Join-Path $packageValidationRoot 'validation-signing.pk8'
        $rsa = [System.Security.Cryptography.RSA]::Create(2048)
        try {
            $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=ToolBox Plugin Validation',
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
            $pluginVersion = Get-PluginVersion -Plugin $plugin
            $manifestPath = Join-Path $repositoryRoot $plugin.Manifest
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $projectName = [System.IO.Path]::GetFileNameWithoutExtension($plugin.Project)
            $runtimeDirectory = Join-Path $validationArtifacts "bin\$projectName\$($Configuration.ToLowerInvariant())"
            $packageName = "$($manifest.id)-$pluginVersion.tpk"
            $firstOutput = Join-Path $packageValidationRoot 'first'
            $secondOutput = Join-Path $packageValidationRoot 'second'

            & $packageScript -RuntimeDirectory $runtimeDirectory -ManifestPath $manifestPath -PackageName $packageName -OutputDirectory $firstOutput -SigningCertificatePath $signingCertificatePath -SigningPrivateKeyPath $signingPrivateKeyPath
            & $packageScript -RuntimeDirectory $runtimeDirectory -ManifestPath $manifestPath -PackageName $packageName -OutputDirectory $secondOutput -SigningCertificatePath $signingCertificatePath -SigningPrivateKeyPath $signingPrivateKeyPath
            $firstPackage = Join-Path $firstOutput $packageName
            $secondPackage = Join-Path $secondOutput $packageName
            & $packageValidationScript -PackagePath $firstPackage -ExpectedPluginId ([string]$manifest.id) -ExpectedVersion $pluginVersion

            $firstHash = (Get-FileHash -LiteralPath $firstPackage -Algorithm SHA256).Hash
            $secondHash = (Get-FileHash -LiteralPath $secondPackage -Algorithm SHA256).Hash
            if ($firstHash -cne $secondHash) {
                throw "Plugin package '$packageName' is not reproducible."
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $packageValidationRoot) {
            Remove-Item -LiteralPath $packageValidationRoot -Recurse -Force
        }
    }
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}

Write-Host 'Plugin repository validation passed.' -ForegroundColor Green
