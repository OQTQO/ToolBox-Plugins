[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$ExpectedPluginId,

    [Parameter(Mandatory)]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-ZipJson {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Package entry '$EntryName' is missing."
    }

    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream)
    try {
        return $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Read-ZipBytes {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Package entry '$EntryName' is missing."
    }

    $stream = $entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Assert-ExactSet {
    param(
        [Parameter(Mandatory)][string[]]$Actual,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Label
    )

    $difference = @(Compare-Object -ReferenceObject ($Expected | Sort-Object) -DifferenceObject ($Actual | Sort-Object) -CaseSensitive)
    if ($difference.Count -ne 0) {
        throw "$Label does not match the expected file set."
    }
}

$resolvedPackage = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $resolvedPackage -PathType Leaf)) {
    throw "Plugin package is missing: '$resolvedPackage'."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $entryNames = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $caseCollisions = @($entryNames | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1)
    if ($caseCollisions.Count -ne 0) {
        throw 'Package contains duplicate or case-colliding paths.'
    }

    foreach ($entryName in $entryNames) {
        $segments = $entryName.Split('/')
        if ($entryName.StartsWith('/') -or $segments -contains '..' -or $segments -contains '.' -or $segments -contains '') {
            throw "Package contains an unsafe path '$entryName'."
        }
    }

    if ($entryNames | Where-Object { [System.IO.Path]::GetFileName($_) -ieq 'ToolBox.PluginSdk.dll' }) {
        throw 'Package contains a private ToolBox.PluginSdk.dll copy.'
    }

    $manifest = Read-ZipJson -Archive $archive -EntryName 'manifest.json'
    $metadata = Read-ZipJson -Archive $archive -EntryName 'package.json'
    $signatureMetadata = Read-ZipJson -Archive $archive -EntryName 'signature.json'
    if ([int]$manifest.formatVersion -ne 2 -or [int]$metadata.packageFormatVersion -ne 2) {
        throw 'Manifest and package format must both be version 2.'
    }
    if (@($manifest.capabilities).Count -eq 0) {
        throw 'Manifest format 2 requires at least one capability declaration.'
    }
    if ([string]$manifest.id -cne $ExpectedPluginId -or [string]$metadata.pluginId -cne $ExpectedPluginId) {
        throw "Package plugin id does not match '$ExpectedPluginId'."
    }
    if ([string]$manifest.version -cne $ExpectedVersion -or [string]$metadata.pluginVersion -cne $ExpectedVersion) {
        throw "Package version does not match '$ExpectedVersion'."
    }

    $payloadEntries = @($entryNames | Where-Object { $_ -cne 'package.json' -and $_ -cne 'signature.json' })
    $hashedEntries = @($metadata.files | ForEach-Object { [string]$_.path })
    Assert-ExactSet -Actual $hashedEntries -Expected $payloadEntries -Label 'Package hash inventory'

    foreach ($fileHash in @($metadata.files)) {
        $expectedHash = ([string]$fileHash.sha256).ToLowerInvariant()
        if ($expectedHash -cnotmatch '^[a-f0-9]{64}$') {
            throw "Package hash for '$($fileHash.path)' is invalid."
        }

        $entry = $archive.GetEntry([string]$fileHash.path)
        if ($null -eq $entry) {
            throw "Hashed package entry '$($fileHash.path)' is missing."
        }

        $stream = $entry.Open()
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            $actualHash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
            $stream.Dispose()
        }

        if ($actualHash -cne $expectedHash) {
            throw "Package hash mismatch for '$($fileHash.path)'."
        }
    }

    if ([int]$signatureMetadata.schemaVersion -ne 1 `
        -or [string]$signatureMetadata.algorithm -cne 'rsa-sha256' `
        -or [string]$signatureMetadata.payload -cne 'package.json' `
        -or [string]$signatureMetadata.publisherId -cne [string]$manifest.publisher) {
        throw 'Package signature metadata is incompatible or does not match the manifest publisher.'
    }
    $certificateBytes = [Convert]::FromBase64String([string]$signatureMetadata.certificate)
    $signatureBytes = [Convert]::FromBase64String([string]$signatureMetadata.signature)
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateBytes)
    try {
        $now = [DateTime]::UtcNow
        if ($now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) {
            throw 'Package signing certificate is outside its validity period.'
        }
        $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
        if ($null -eq $rsa) {
            throw 'Package signing certificate does not contain an RSA public key.'
        }
        try {
            $packageMetadataBytes = Read-ZipBytes -Archive $archive -EntryName 'package.json'
            if (-not $rsa.VerifyData(
                $packageMetadataBytes,
                $signatureBytes,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
                throw 'Package publisher signature verification failed.'
            }
        }
        finally {
            $rsa.Dispose()
        }
    }
    finally {
        $certificate.Dispose()
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Validated plugin package '$resolvedPackage'." -ForegroundColor Green
