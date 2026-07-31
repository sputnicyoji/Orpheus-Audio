# Builds the published UPM tarball from an allowlisted public package surface.
#
# Dot-sourced by test-public-tarball.ps1 in the public repository and by
# build-public-package-tarball.ps1 in the private repository. Both layouts keep
# OrpheusPublicPackageValidation.ps1 as a sibling of this file.
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "OrpheusPublicPackageValidation.ps1")

$script:OrpheusUpmTarballSchemaVersion = 1
$script:OrpheusUpmPackageDirectory = "package"
$script:OrpheusUpmTemporaryPrefix = ".orpheus-upm-tarball-"
$script:OrpheusUpmOwnerMarker = ".orpheus-owned-temporary"

function Get-OrpheusUpmTarballFileName {
    param(
        [Parameter(Mandatory)]
        [string] $PackageName,

        [Parameter(Mandatory)]
        [string] $PackageVersion
    )

    return "$PackageName-$PackageVersion.tgz"
}

function Assert-OrpheusPackedFileSet {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $Expected,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $Reported,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $reportedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $Reported) {
        if (-not $reportedSet.Add($path)) {
            throw "$Label contains a duplicate path: $path"
        }
    }

    $expectedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $Expected) {
        if (-not $expectedSet.Add($path)) {
            throw "Expected package file set contains a duplicate path: $path"
        }
    }

    # Report the ordinally first divergence so the message is stable across runs.
    [string[]] $missing = @($Expected | Where-Object { -not $reportedSet.Contains($_) })
    if ($missing.Count -gt 0) {
        [System.Array]::Sort($missing, [System.StringComparer]::Ordinal)
        throw "$Label is missing an expected package file: $($missing[0])"
    }

    [string[]] $unexpected = @($Reported | Where-Object { -not $expectedSet.Contains($_) })
    if ($unexpected.Count -gt 0) {
        [System.Array]::Sort($unexpected, [System.StringComparer]::Ordinal)
        throw "$Label contains an unexpected file: $($unexpected[0])"
    }
}

function Assert-OrpheusArchiveEntryLayout {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $EntryPath
    )

    if ($EntryPath.Count -eq 0) {
        throw "UPM archive contains no entries."
    }

    $prefix = "$script:OrpheusUpmPackageDirectory/"
    $separator = [char] 47
    $backslash = [char] 92
    foreach ($path in $EntryPath) {
        if (-not $path.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            throw "UPM archive entry is not inside the package directory: $path"
        }
        if ($path.IndexOf($backslash) -ge 0) {
            throw "UPM archive entry must use forward slashes: $path"
        }

        $relativePath = $path.Substring($prefix.Length)
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            throw "UPM archive entry is not inside the package directory: $path"
        }
        foreach ($segment in $relativePath.Split($separator)) {
            if (
                [string]::IsNullOrEmpty($segment) -or
                [string]::Equals($segment, ".", [System.StringComparison]::Ordinal) -or
                [string]::Equals($segment, "..", [System.StringComparison]::Ordinal)
            ) {
                throw "UPM archive entry is not a normalized package path: $path"
            }
        }
    }
}

function Test-OrpheusBytesEqual {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Left,

        [Parameter(Mandatory)]
        [byte[]] $Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }

    return $true
}

function New-OrpheusOwnedTemporaryDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Purpose
    )

    [System.IO.Directory]::CreateDirectory($Parent) | Out-Null
    $name = "$script:OrpheusUpmTemporaryPrefix$Purpose-$([Guid]::NewGuid().ToString('N'))"
    $path = Join-Path $Parent $name
    if ([System.IO.Directory]::Exists($path) -or [System.IO.File]::Exists($path)) {
        throw "Owned temporary path already exists: $name"
    }

    [System.IO.Directory]::CreateDirectory($path) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $path $script:OrpheusUpmOwnerMarker),
        "",
        [System.Text.UTF8Encoding]::new($false)
    )

    return $path
}

function Remove-OrpheusOwnedTemporaryDirectory {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not [System.IO.Directory]::Exists($Path)) {
        return
    }

    # Only ever delete a directory this module created and marked. A directory that
    # merely matches the naming prefix belongs to someone else and stays untouched.
    if (-not [System.IO.File]::Exists((Join-Path $Path $script:OrpheusUpmOwnerMarker))) {
        return
    }

    [System.IO.Directory]::Delete($Path, $true)
}

function Get-OrpheusApplicationPath {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $command = @(
        Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue
    ) | Select-Object -First 1
    if ($null -eq $command) {
        throw "$Name is required to build the UPM tarball but was not found on PATH."
    }

    return [string] $command.Source
}

function Invoke-OrpheusToolProcess {
    param(
        [Parameter(Mandatory)]
        [string] $CommandPath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [string] $DiagnosticsDirectory,

        [Parameter(Mandatory)]
        [string] $Label
    )

    # npm is a .cmd shim on Windows, which CreateProcess cannot launch directly, so
    # ProcessStartInfo is not usable here. PowerShell's native invocation builds the
    # argument vector itself, so no shell command string is ever assembled. Streams
    # go to separate files so a tool warning cannot corrupt the JSON result.
    # A function-scoped override keeps the caller's Stop preference from turning a
    # non-zero exit into a generic error before the specific message is built.
    $PSNativeCommandUseErrorActionPreference = $false

    $standardOutputPath = Join-Path $DiagnosticsDirectory "$Label.out"
    $standardErrorPath = Join-Path $DiagnosticsDirectory "$Label.err"
    & $CommandPath @Arguments 1> $standardOutputPath 2> $standardErrorPath
    $exitCode = $LASTEXITCODE

    $standardError = if ([System.IO.File]::Exists($standardErrorPath)) {
        [System.IO.File]::ReadAllText($standardErrorPath).Trim()
    }
    else {
        ""
    }
    if ($exitCode -ne 0) {
        throw ("$Label failed with exit code $exitCode. $standardError").Trim()
    }

    return [System.IO.File]::ReadAllText($standardOutputPath)
}

function Get-OrpheusToolVersion {
    param(
        [Parameter(Mandatory)]
        [string] $CommandPath,

        [Parameter(Mandatory)]
        [string] $DiagnosticsDirectory,

        [Parameter(Mandatory)]
        [string] $Label
    )

    return (
        Invoke-OrpheusToolProcess `
            -CommandPath $CommandPath `
            -Arguments @("--version") `
            -DiagnosticsDirectory $DiagnosticsDirectory `
            -Label "$Label-version"
    ).Trim()
}

function Invoke-OrpheusNpmPack {
    param(
        [Parameter(Mandatory)]
        [string] $NpmPath,

        [Parameter(Mandatory)]
        [string] $StagingRoot,

        [Parameter(Mandatory)]
        [string] $PackDestination,

        [Parameter(Mandatory)]
        [string] $DiagnosticsDirectory
    )

    $standardOutput = Invoke-OrpheusToolProcess `
        -CommandPath $NpmPath `
        -Arguments @(
            "pack",
            "--ignore-scripts",
            "--json",
            "--pack-destination", $PackDestination,
            $StagingRoot
        ) `
        -DiagnosticsDirectory $DiagnosticsDirectory `
        -Label "npm-pack"

    try {
        $results = @(ConvertFrom-Json -InputObject $standardOutput -ErrorAction Stop)
    }
    catch {
        throw "npm pack did not return parsable JSON."
    }
    if ($results.Count -ne 1) {
        throw "npm pack must report exactly one packed result but reported $($results.Count)."
    }

    return $results[0]
}

function Get-OrpheusUpmArchiveEntryPath {
    param(
        [Parameter(Mandatory)]
        [string] $TarballPath
    )

    $names = [System.Collections.Generic.List[string]]::new()
    $file = [System.IO.File]::OpenRead($TarballPath)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $file,
            [System.IO.Compression.CompressionMode]::Decompress
        )
        try {
            $reader = [System.Formats.Tar.TarReader]::new($gzip)
            try {
                while ($null -ne ($entry = $reader.GetNextEntry())) {
                    if ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory) {
                        continue
                    }
                    if (
                        $entry.EntryType -ne [System.Formats.Tar.TarEntryType]::RegularFile -and
                        $entry.EntryType -ne [System.Formats.Tar.TarEntryType]::V7RegularFile
                    ) {
                        throw "UPM archive contains an unsupported entry type: $($entry.Name)"
                    }
                    $names.Add([string] $entry.Name)
                }
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }

    return $names.ToArray()
}

function Expand-OrpheusUpmArchive {
    param(
        [Parameter(Mandatory)]
        [string] $TarballPath,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    $file = [System.IO.File]::OpenRead($TarballPath)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $file,
            [System.IO.Compression.CompressionMode]::Decompress
        )
        try {
            [System.Formats.Tar.TarFile]::ExtractToDirectory($gzip, $Destination, $true)
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }
}

function New-OrpheusUpmTarball {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $PublicRepositoryRoot,

        [Parameter(Mandatory)]
        [string] $OutputDirectory
    )

    $root = Get-OrpheusNormalizedRoot -Path $PublicRepositoryRoot
    $content = Invoke-OrpheusPackageContentValidation `
        -PackageRoot $root `
        -PublicRepositoryLayout
    $fileName = Get-OrpheusUpmTarballFileName `
        -PackageName $content.packageName `
        -PackageVersion $content.packageVersion

    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $outputRoot = Get-OrpheusNormalizedRoot -Path $OutputDirectory
    $artifactPath = Join-Path $outputRoot $fileName

    [string[]] $expectedPaths = @(
        foreach ($file in Get-OrpheusPackageFiles -PackageRoot $root -PublicRepositoryLayout) {
            Get-OrpheusRelativePath -Root $root -Path $file.FullName
        }
    )

    $stagingRoot = ""
    $packRoot = ""
    $verifyRoot = ""
    try {
        # The staging root carries an ownership marker, so package bytes go into a
        # nested directory. npm must never see the marker as a package file.
        $stagingRoot = New-OrpheusOwnedTemporaryDirectory -Parent $outputRoot -Purpose "stage"
        $packageStage = Join-Path $stagingRoot $script:OrpheusUpmPackageDirectory
        [System.IO.Directory]::CreateDirectory($packageStage) | Out-Null
        foreach ($relativePath in $expectedPaths) {
            $source = Join-Path $root $relativePath
            $destination = [System.IO.Path]::Combine($packageStage, $relativePath)
            $parent = [System.IO.Path]::GetDirectoryName($destination)
            if ($parent) {
                [System.IO.Directory]::CreateDirectory($parent) | Out-Null
            }
            [System.IO.File]::Copy($source, $destination, $false)
        }

        $packRoot = New-OrpheusOwnedTemporaryDirectory -Parent $outputRoot -Purpose "pack"
        $npmPath = Get-OrpheusApplicationPath -Name "npm"
        $nodePath = Get-OrpheusApplicationPath -Name "node"
        $npmVersion = Get-OrpheusToolVersion `
            -CommandPath $npmPath `
            -DiagnosticsDirectory $packRoot `
            -Label "npm"
        $nodeVersion = Get-OrpheusToolVersion `
            -CommandPath $nodePath `
            -DiagnosticsDirectory $packRoot `
            -Label "node"

        $packed = Invoke-OrpheusNpmPack `
            -NpmPath $npmPath `
            -StagingRoot $packageStage `
            -PackDestination $packRoot `
            -DiagnosticsDirectory $packRoot

        if (-not [string]::Equals([string] $packed.name, $content.packageName, [System.StringComparison]::Ordinal)) {
            throw "npm pack reported package name $($packed.name) instead of $($content.packageName)."
        }
        if (-not [string]::Equals([string] $packed.version, $content.packageVersion, [System.StringComparison]::Ordinal)) {
            throw "npm pack reported package version $($packed.version) instead of $($content.packageVersion)."
        }
        if (-not [string]::Equals([string] $packed.filename, $fileName, [System.StringComparison]::Ordinal)) {
            throw "npm pack reported artifact name $($packed.filename) instead of $fileName."
        }

        [string[]] $reportedPaths = @(
            foreach ($entry in @($packed.files)) {
                [string] $entry.path
            }
        )
        Assert-OrpheusPackedFileSet `
            -Expected $expectedPaths `
            -Reported $reportedPaths `
            -Label "npm pack output"

        $packedArtifact = Join-Path $packRoot $fileName
        if (-not [System.IO.File]::Exists($packedArtifact)) {
            throw "npm pack did not create $fileName."
        }

        [string[]] $entryPaths = @(Get-OrpheusUpmArchiveEntryPath -TarballPath $packedArtifact)
        Assert-OrpheusArchiveEntryLayout -EntryPath $entryPaths

        # Validate the archive that actually exists, not the staging tree that
        # produced it. This is the guard that catches a packer that rewrites bytes.
        $verifyRoot = New-OrpheusOwnedTemporaryDirectory -Parent $outputRoot -Purpose "verify"
        Expand-OrpheusUpmArchive -TarballPath $packedArtifact -Destination $verifyRoot
        $extractedPackage = Join-Path $verifyRoot $script:OrpheusUpmPackageDirectory
        $extracted = Invoke-OrpheusPackageContentValidation -PackageRoot $extractedPackage
        if (
            -not [string]::Equals(
                $extracted.packageTreeSha256,
                $content.packageTreeSha256,
                [System.StringComparison]::Ordinal
            )
        ) {
            throw "Extracted UPM package fingerprint does not match the validated public package."
        }
        if ($extracted.fileCount -ne $content.fileCount) {
            throw "Extracted UPM package file count does not match the validated public package."
        }

        [byte[]] $artifactBytes = [System.IO.File]::ReadAllBytes($packedArtifact)
        $sha256 = [System.Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($artifactBytes)
        ).ToLowerInvariant()

        if ([System.IO.File]::Exists($artifactPath)) {
            [byte[]] $existingBytes = [System.IO.File]::ReadAllBytes($artifactPath)
            if (-not (Test-OrpheusBytesEqual -Left $existingBytes -Right $artifactBytes)) {
                throw "$fileName already exists with different bytes in the output directory."
            }
        }
        else {
            [System.IO.File]::Copy($packedArtifact, $artifactPath, $false)
        }

        return [pscustomobject][ordered]@{
            schemaVersion = $script:OrpheusUpmTarballSchemaVersion
            packageName = $content.packageName
            packageVersion = $content.packageVersion
            fileName = $fileName
            sizeBytes = $artifactBytes.Length
            sha256 = $sha256
            packageTreeSha256 = $content.packageTreeSha256
            fileCount = $content.fileCount
            nodeVersion = $nodeVersion
            npmVersion = $npmVersion
        }
    }
    finally {
        Remove-OrpheusOwnedTemporaryDirectory -Path $verifyRoot
        Remove-OrpheusOwnedTemporaryDirectory -Path $packRoot
        Remove-OrpheusOwnedTemporaryDirectory -Path $stagingRoot
    }
}
