$script:OrpheusPackageDirectories = [string[]]@(
    "Documentation~",
    "Editor",
    "Runtime",
    "Samples~",
    "Tests"
)

$script:OrpheusPackageRootFiles = [string[]]@(
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "CONTRIBUTING.md",
    "CONTRIBUTING.md.meta",
    "Documentation~.meta",
    "Editor.meta",
    "LICENSE.md",
    "LICENSE.md.meta",
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "Runtime.meta",
    "Samples~.meta",
    "SECURITY.md",
    "SECURITY.md.meta",
    "Tests.meta"
)

$script:OrpheusGovernanceFiles = [string[]]@(
    ".gitattributes",
    ".gitignore",
    ".github/CODEOWNERS",
    ".github/dependabot.yml",
    ".github/ISSUE_TEMPLATE/bug_report.yml",
    ".github/ISSUE_TEMPLATE/config.yml",
    ".github/ISSUE_TEMPLATE/feature_request.yml",
    ".github/PULL_REQUEST_TEMPLATE.md",
    ".github/scripts/OrpheusPublicPackageValidation.ps1",
    ".github/scripts/OrpheusUpmTarball.ps1",
    ".github/scripts/test-public-package.ps1",
    ".github/scripts/test-public-tarball.ps1",
    ".github/workflows/unity-compatibility.yml",
    ".github/workflows/verify.yml"
)

$script:OrpheusTextExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@(
        ".asmdef", ".asset", ".cs", ".gitattributes", ".gitignore", ".json",
        ".md", ".meta", ".mixer", ".prefab", ".ps1", ".psm1", ".shader",
        ".txt", ".unity", ".uss", ".uxml", ".yaml", ".yml"
    ),
    [System.StringComparer]::OrdinalIgnoreCase
)

$script:OrpheusBinaryExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@(".wav"),
    [System.StringComparer]::OrdinalIgnoreCase
)

function Get-OrpheusNormalizedRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not [System.IO.Directory]::Exists($Path)) {
        throw "Directory does not exist: $Path"
    }

    return [System.IO.Path]::TrimEndingDirectorySeparator(
        [System.IO.Path]::GetFullPath($Path)
    )
}

function Get-OrpheusRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Path
    )

    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace(
        [System.IO.Path]::DirectorySeparatorChar,
        [char] "/"
    )
}

function Test-OrpheusPackageRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    foreach ($rootFile in $script:OrpheusPackageRootFiles) {
        if ([string]::Equals($RelativePath, $rootFile, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    foreach ($directory in $script:OrpheusPackageDirectories) {
        $prefix = "$directory/"
        if ($RelativePath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Test-OrpheusGovernanceRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    foreach ($governanceFile in $script:OrpheusGovernanceFiles) {
        if ([string]::Equals($RelativePath, $governanceFile, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Get-OrpheusRepositoryFiles {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        if (
            [string]::Equals($relativePath, ".git", [System.StringComparison]::Ordinal) -or
            $relativePath.StartsWith(".git/", [System.StringComparison]::Ordinal)
        ) {
            continue
        }

        $files.Add($file)
    }

    return $files.ToArray()
}

function Get-OrpheusPackageFiles {
    param(
        [Parameter(Mandatory)]
        [string] $PackageRoot,

        [switch] $PublicRepositoryLayout
    )

    $root = Get-OrpheusNormalizedRoot -Path $PackageRoot
    if (-not $PublicRepositoryLayout) {
        return @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)
    }

    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($file in Get-OrpheusRepositoryFiles -RepositoryRoot $root) {
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        if (Test-OrpheusPackageRelativePath -RelativePath $relativePath) {
            $files.Add($file)
        }
    }

    return $files.ToArray()
}

function Assert-OrpheusNoLinks {
    param(
        [Parameter(Mandatory)]
        [string] $Root
    )

    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Symbolic links and reparse points are not allowed: $($item.FullName)"
        }
    }
}

function Get-OrpheusPackageTreeSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $PackageRoot,

        [switch] $PublicRepositoryLayout
    )

    $root = Get-OrpheusNormalizedRoot -Path $PackageRoot
    Assert-OrpheusNoLinks -Root $root

    $fileMap = [System.Collections.Generic.Dictionary[string, System.IO.FileInfo]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($file in Get-OrpheusPackageFiles -PackageRoot $root -PublicRepositoryLayout:$PublicRepositoryLayout) {
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        if (-not $fileMap.TryAdd($relativePath, $file)) {
            throw "Duplicate normalized package path: $relativePath"
        }
    }

    [string[]] $relativePaths = @($fileMap.Keys)
    [System.Array]::Sort($relativePaths, [System.StringComparer]::Ordinal)

    $aggregate = [System.IO.MemoryStream]::new()
    try {
        foreach ($relativePath in $relativePaths) {
            $bytes = [System.IO.File]::ReadAllBytes($fileMap[$relativePath].FullName)
            $fileHashBytes = [System.Security.Cryptography.SHA256]::HashData($bytes)
            $fileHash = [System.Convert]::ToHexString($fileHashBytes).ToLowerInvariant()
            $entry = [System.Text.Encoding]::UTF8.GetBytes("$relativePath`0$fileHash`n")
            $aggregate.Write($entry, 0, $entry.Length)
        }

        $treeHashBytes = [System.Security.Cryptography.SHA256]::HashData($aggregate.ToArray())
        return [System.Convert]::ToHexString($treeHashBytes).ToLowerInvariant()
    }
    finally {
        $aggregate.Dispose()
    }
}

function Read-OrpheusJson {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Label
    )

    try {
        return Get-Content -LiteralPath $Path -Raw -ErrorAction Stop |
            ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "$Label does not parse as JSON: $Path"
    }
}

function Assert-OrpheusExactUtf8FileContent {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Expected,
        [Parameter(Mandatory)][string] $Label
    )

    [byte[]] $actualBytes = [System.IO.File]::ReadAllBytes($Path)
    [byte[]] $expectedBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Expected)
    if ($actualBytes.Length -ne $expectedBytes.Length) {
        throw "$Label content must be exact."
    }
    for ($index = 0; $index -lt $actualBytes.Length; $index++) {
        if ($actualBytes[$index] -ne $expectedBytes[$index]) {
            throw "$Label content must be exact."
        }
    }
}

function Assert-OrpheusPublicLayout {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    $present = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($file in Get-OrpheusRepositoryFiles -RepositoryRoot $root) {
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        $null = $present.Add($relativePath)
        $isPackageFile = Test-OrpheusPackageRelativePath -RelativePath $relativePath
        if (
            -not $isPackageFile -and
            -not (Test-OrpheusGovernanceRelativePath -RelativePath $relativePath) -and
            -not [string]::Equals($relativePath, ".orpheus-export.json", [System.StringComparison]::Ordinal)
        ) {
            throw "Unknown public file: $relativePath"
        }
        if ($isPackageFile) {
            $extension = if ([string]::IsNullOrEmpty($file.Extension)) {
                $file.Name
            }
            else {
                $file.Extension
            }
            if (
                -not $script:OrpheusTextExtensions.Contains($extension) -and
                -not $script:OrpheusBinaryExtensions.Contains($extension)
            ) {
                throw "Unsupported public package file extension: $relativePath"
            }
        }
    }

    foreach ($governanceFile in $script:OrpheusGovernanceFiles) {
        if (-not $present.Contains($governanceFile)) {
            throw "Required public governance file is missing: $governanceFile"
        }
    }

    foreach ($rootFile in $script:OrpheusPackageRootFiles) {
        if (-not $present.Contains($rootFile)) {
            throw "Required package root file is missing: $rootFile"
        }
    }

    if (-not $present.Contains(".orpheus-export.json")) {
        throw "Required export manifest is missing: .orpheus-export.json"
    }
    Assert-OrpheusExactUtf8FileContent `
        -Path (Join-Path $root ".gitattributes") `
        -Expected "* -text`n" `
        -Label ".gitattributes"
    $slash = [string] [char] 47
    Assert-OrpheusExactUtf8FileContent `
        -Path (Join-Path $root ".github/CODEOWNERS") `
        -Expected "${slash}.github/** @sputnicyoji`n${slash}.orpheus-export.json @sputnicyoji`n" `
        -Label "CODEOWNERS"
}

function Assert-OrpheusPackageManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $packagePath = Join-Path $RepositoryRoot "package.json"
    $package = Read-OrpheusJson -Path $packagePath -Label "package.json"

    if (-not [string]::Equals([string] $package.name, "com.orpheus.audio", [System.StringComparison]::Ordinal)) {
        throw "package.json name must be com.orpheus.audio."
    }

    if (-not [string]::Equals([string] $package.license, "MIT", [System.StringComparison]::Ordinal)) {
        throw "package.json license must be MIT."
    }

    if (
        -not [string]::Equals([string] $package.unity, "2022.3", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $package.unityRelease, "62f2c1", [System.StringComparison]::Ordinal)
    ) {
        throw "package.json must declare Unity 2022.3.62f2c1."
    }

    if ([string] $package.version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
        throw "package.json version is invalid."
    }

    # Tarball creation packs with --ignore-scripts, so a declared lifecycle script
    # would be dead weight in the published artifact and live code for any consumer
    # that installs through npm directly. Reject the property even when it is empty.
    if ($null -ne $package.PSObject.Properties["scripts"]) {
        throw "package.json must not declare npm lifecycle scripts."
    }

    $samples = @($package.samples)
    if ($samples.Count -ne 2) {
        throw "package.json must declare exactly two valid samples."
    }

    $expectedSamplePaths = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@("Samples~/Minimal Setup", "Samples~/Playable One Shot"),
        [System.StringComparer]::Ordinal
    )
    foreach ($sample in $samples) {
        $samplePath = [string] $sample.path
        if (
            [string]::IsNullOrWhiteSpace([string] $sample.displayName) -or
            [string]::IsNullOrWhiteSpace([string] $sample.description) -or
            -not $expectedSamplePaths.Remove($samplePath) -or
            -not [System.IO.Directory]::Exists((Join-Path $RepositoryRoot $samplePath))
        ) {
            throw "package.json must declare exactly two valid samples."
        }
    }

    foreach ($urlProperty in @("licensesUrl", "documentationUrl", "changelogUrl")) {
        $url = [string] $package.$urlProperty
        if ($url -notmatch "Orpheus-Audio/blob/v$([regex]::Escape([string] $package.version))/") {
            throw "package.json $urlProperty must use package version $($package.version)."
        }
    }

    return $package
}

function Assert-OrpheusJsonAndAssemblyDefinitions {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [switch] $PublicRepositoryLayout
    )

    $packageFiles = Get-OrpheusPackageFiles `
        -PackageRoot $RepositoryRoot `
        -PublicRepositoryLayout:$PublicRepositoryLayout
    $assemblyNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $assemblyDefinitions = [System.Collections.Generic.List[object]]::new()

    foreach ($file in $packageFiles) {
        if (
            [string]::Equals($file.Extension, ".json", [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($file.Extension, ".asmdef", [System.StringComparison]::OrdinalIgnoreCase)
        ) {
            $value = Read-OrpheusJson -Path $file.FullName -Label $file.Name
            if ([string]::Equals($file.Extension, ".asmdef", [System.StringComparison]::OrdinalIgnoreCase)) {
                if ([string]::IsNullOrWhiteSpace([string] $value.name)) {
                    throw "Assembly definition has no name: $($file.FullName)"
                }
                if (-not $assemblyNames.Add([string] $value.name)) {
                    throw "Duplicate assembly definition name: $($value.name)"
                }
                $assemblyDefinitions.Add([pscustomobject]@{ File = $file; Value = $value })
            }
        }
    }

    $guidToAssembly = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($definition in $assemblyDefinitions) {
        $metaPath = "$($definition.File.FullName).meta"
        if (-not [System.IO.File]::Exists($metaPath)) {
            continue
        }
        $metaText = Get-Content -LiteralPath $metaPath -Raw
        $match = [regex]::Match($metaText, "(?m)^guid:\s*([0-9a-f]{32})\s*$")
        if ($match.Success) {
            $null = $guidToAssembly.Add($match.Groups[1].Value)
        }
    }

    foreach ($definition in $assemblyDefinitions) {
        foreach ($referenceValue in @($definition.Value.references)) {
            $reference = [string] $referenceValue
            if ($reference.StartsWith("GUID:", [System.StringComparison]::Ordinal)) {
                $guid = $reference.Substring(5)
                if (-not $guidToAssembly.Contains($guid)) {
                    throw "Unresolved asmdef reference $reference in $($definition.File.Name)."
                }
            }
            elseif (-not $assemblyNames.Contains($reference)) {
                throw "Unresolved asmdef reference $reference in $($definition.File.Name)."
            }
        }
    }
}

function Assert-OrpheusUnityMetadata {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [switch] $PublicRepositoryLayout
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    $packageFiles = Get-OrpheusPackageFiles `
        -PackageRoot $root `
        -PublicRepositoryLayout:$PublicRepositoryLayout

    foreach ($file in $packageFiles) {
        if (
            -not [string]::Equals($file.Extension, ".meta", [System.StringComparison]::Ordinal) -and
            -not [System.IO.File]::Exists("$($file.FullName).meta")
        ) {
            $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
            throw "Unity metadata sibling is missing: $relativePath.meta"
        }
    }

    foreach ($directoryName in $script:OrpheusPackageDirectories) {
        $directory = Join-Path $root $directoryName
        foreach ($childDirectory in @(
            Get-Item -LiteralPath $directory
            Get-ChildItem -LiteralPath $directory -Recurse -Directory -Force
        )) {
            if (-not [System.IO.File]::Exists("$($childDirectory.FullName).meta")) {
                $relativePath = Get-OrpheusRelativePath -Root $root -Path $childDirectory.FullName
                throw "Unity metadata sibling is missing: $relativePath.meta"
            }
        }
    }

    $guids = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($metaFile in $packageFiles | Where-Object Extension -EQ ".meta") {
        $targetPath = $metaFile.FullName.Substring(0, $metaFile.FullName.Length - 5)
        if (-not [System.IO.File]::Exists($targetPath) -and -not [System.IO.Directory]::Exists($targetPath)) {
            $relativePath = Get-OrpheusRelativePath -Root $root -Path $metaFile.FullName
            throw "Orphan metadata: $relativePath"
        }

        $metaText = Get-Content -LiteralPath $metaFile.FullName -Raw
        $matches = [regex]::Matches($metaText, "(?m)^guid:\s*([0-9a-f]{32})\s*$")
        if ($matches.Count -ne 1) {
            $relativePath = Get-OrpheusRelativePath -Root $root -Path $metaFile.FullName
            throw "Unity metadata GUID is invalid: $relativePath"
        }

        $guid = $matches[0].Groups[1].Value
        $relativeMetaPath = Get-OrpheusRelativePath -Root $root -Path $metaFile.FullName
        if ($guids.ContainsKey($guid)) {
            throw "Duplicate GUID $guid in $relativeMetaPath and $($guids[$guid])."
        }
        $guids.Add($guid, $relativeMetaPath)
    }
}

function Assert-OrpheusCoreIsolation {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $coreRoot = Join-Path $RepositoryRoot "Runtime/Core"
    foreach ($asmdefPath in Get-ChildItem -LiteralPath $coreRoot -Recurse -Filter "*.asmdef" -File) {
        $asmdef = Read-OrpheusJson -Path $asmdefPath.FullName -Label $asmdefPath.Name
        $includePlatformsProperty = $asmdef.PSObject.Properties["includePlatforms"]
        $includePlatforms = @(
            if ($null -ne $includePlatformsProperty) {
                $includePlatformsProperty.Value
            }
        )
        $referencesProperty = $asmdef.PSObject.Properties["references"]
        $references = @(
            if ($null -ne $referencesProperty) {
                $referencesProperty.Value
            }
        )
        $noEngineReferencesProperty = $asmdef.PSObject.Properties["noEngineReferences"]
        $noEngineReferences = if ($null -ne $noEngineReferencesProperty) {
            $noEngineReferencesProperty.Value
        }
        else {
            $false
        }
        if (
            $noEngineReferences -ne $true -or
            $references.Count -ne 0 -or
            $includePlatforms -contains "Editor"
        ) {
            throw "Runtime/Core must have no Unity runtime or Editor dependency: $($asmdefPath.Name)"
        }
    }

    foreach ($sourcePath in Get-ChildItem -LiteralPath $coreRoot -Recurse -Filter "*.cs" -File) {
        $source = Get-Content -LiteralPath $sourcePath.FullName -Raw
        if ($source -match "(?m)^\s*using\s+Unity(?:Engine|Editor)\b" -or $source -match "\bUnity(?:Engine|Editor)\.") {
            throw "Runtime/Core must have no Unity runtime or Editor dependency: $($sourcePath.Name)"
        }
    }
}

function Resolve-OrpheusCaseSensitivePath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $current = Get-OrpheusNormalizedRoot -Path $Root
    $segments = $RelativePath.Replace("\", "/").Split(
        [char[]]@("/"),
        [System.StringSplitOptions]::RemoveEmptyEntries
    )
    foreach ($segment in $segments) {
        if ([string]::Equals($segment, ".", [System.StringComparison]::Ordinal)) {
            continue
        }
        if ([string]::Equals($segment, "..", [System.StringComparison]::Ordinal)) {
            $current = [System.IO.Directory]::GetParent($current).FullName
            continue
        }

        if (-not [System.IO.Directory]::Exists($current)) {
            return $null
        }

        $match = $null
        foreach ($child in [System.IO.Directory]::EnumerateFileSystemEntries($current)) {
            if ([string]::Equals([System.IO.Path]::GetFileName($child), $segment, [System.StringComparison]::Ordinal)) {
                $match = $child
                break
            }
        }
        if ($null -eq $match) {
            return $null
        }
        $current = $match
    }

    return $current
}

function Assert-OrpheusMarkdownTarget {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $MarkdownPath,
        [Parameter(Mandatory)][string] $RawTarget
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    $rootPrefix = "$root$([System.IO.Path]::DirectorySeparatorChar)"
    $rawTarget = $RawTarget.Trim()
    if ($rawTarget.StartsWith("<", [System.StringComparison]::Ordinal)) {
        $closing = $rawTarget.IndexOf(">", [System.StringComparison]::Ordinal)
        if ($closing -lt 0) {
            throw "Invalid Markdown link in $MarkdownPath`: $rawTarget"
        }
        $rawTarget = $rawTarget.Substring(1, $closing - 1)
    }
    else {
        $rawTarget = ($rawTarget -split "\s+[`"']")[0]
    }

    if (
        [string]::IsNullOrWhiteSpace($rawTarget) -or
        $rawTarget.StartsWith("#", [System.StringComparison]::Ordinal) -or
        $rawTarget -match "^(?:https?|mailto|tel|data):"
    ) {
        return
    }

    $targetWithoutFragment = ($rawTarget -split "[?#]", 2)[0]
    $targetWithoutFragment = [System.Uri]::UnescapeDataString($targetWithoutFragment)
    if ([System.IO.Path]::IsPathRooted($targetWithoutFragment)) {
        throw "Markdown link must be repository-local: $rawTarget"
    }

    $baseDirectory = [System.IO.Path]::GetDirectoryName($MarkdownPath)
    $absoluteTarget = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($baseDirectory, $targetWithoutFragment)
    )
    if (
        -not $absoluteTarget.StartsWith($rootPrefix, [System.StringComparison]::Ordinal) -and
        -not [string]::Equals($absoluteTarget, $root, [System.StringComparison]::Ordinal)
    ) {
        throw "Markdown link escapes the public repository: $rawTarget"
    }

    $relativeTarget = Get-OrpheusRelativePath -Root $root -Path $absoluteTarget
    if ($null -eq (Resolve-OrpheusCaseSensitivePath -Root $root -RelativePath $relativeTarget)) {
        $source = Get-OrpheusRelativePath -Root $root -Path $MarkdownPath
        throw "Markdown link does not resolve case-sensitively: $source -> $rawTarget"
    }
}

function Assert-OrpheusMarkdownLinks {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    foreach ($markdownFile in Get-ChildItem -LiteralPath $root -Recurse -Filter "*.md" -File -Force) {
        if ($markdownFile.FullName.StartsWith((Join-Path $root ".git"), [System.StringComparison]::Ordinal)) {
            continue
        }

        $text = Get-Content -LiteralPath $markdownFile.FullName -Raw
        foreach ($match in [regex]::Matches($text, "!?\[[^\]]*\]\((?<target>[^)]+)\)")) {
            Assert-OrpheusMarkdownTarget `
                -RepositoryRoot $root `
                -MarkdownPath $markdownFile.FullName `
                -RawTarget $match.Groups["target"].Value
        }
        foreach ($match in [regex]::Matches($text, "(?m)^\s{0,3}\[[^\]]+\]\s*:\s*(?<target><[^>]+>|\S+)")) {
            Assert-OrpheusMarkdownTarget `
                -RepositoryRoot $root `
                -MarkdownPath $markdownFile.FullName `
                -RawTarget $match.Groups["target"].Value
        }
    }
}

function Assert-OrpheusVersionSurfaces {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [switch] $PublicRepositoryLayout
    )

    $versionPattern = "(?<![0-9A-Za-z.])v?(?<version>[0-9]+\.[0-9]+\.[0-9]+)(?![0-9A-Za-z.])"
    $changelog = Get-Content -LiteralPath (Join-Path $RepositoryRoot "CHANGELOG.md") -Raw
    $changelogVersion = [regex]::Match($changelog, "(?m)^## \[([0-9]+\.[0-9]+\.[0-9]+)\]").Groups[1].Value
    if (-not [string]::Equals($changelogVersion, $PackageVersion, [System.StringComparison]::Ordinal)) {
        throw "Current changelog value does not match package.json $PackageVersion."
    }

    $historicalFiles = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($relativePath in @("CHANGELOG.md", "Documentation~/upgrading.md")) {
        $allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $text = Get-Content -LiteralPath (Join-Path $RepositoryRoot $relativePath) -Raw
        foreach ($heading in [regex]::Matches($text, "(?m)^## .+$")) {
            foreach ($match in [regex]::Matches($heading.Value, $versionPattern)) {
                $null = $allowed.Add($match.Groups["version"].Value)
            }
        }
        $null = $allowed.Add($PackageVersion)
        $historicalFiles.Add($relativePath, $allowed)
    }
    $packageAllowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $null = $packageAllowed.Add($PackageVersion)
    $packageDocument = Get-Content -LiteralPath (Join-Path $RepositoryRoot "package.json") -Raw |
        ConvertFrom-Json
    if ($null -ne $packageDocument.dependencies) {
        foreach ($dependency in $packageDocument.dependencies.PSObject.Properties) {
            if ([string] $dependency.Value -match "^[0-9]+\.[0-9]+\.[0-9]+$") {
                $null = $packageAllowed.Add([string] $dependency.Value)
            }
        }
    }
    $historicalFiles.Add("package.json", $packageAllowed)

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    foreach ($file in Get-OrpheusRepositoryFiles -RepositoryRoot $root) {
        $extension = if ([string]::IsNullOrEmpty($file.Extension)) { $file.Name } else { $file.Extension }
        if (-not $script:OrpheusTextExtensions.Contains($extension)) {
            continue
        }
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        $allowedHistorical = if ($historicalFiles.ContainsKey($relativePath)) {
            $historicalFiles[$relativePath]
        }
        else {
            $null
        }
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, $versionPattern)) {
            $found = $match.Groups["version"].Value
            $prefixStart = [Math]::Max(0, $match.Index - 24)
            $prefix = $text.Substring($prefixStart, $match.Index - $prefixStart)
            $externalToolVersion = $prefix -match '(?i)\bGit\s+`?$'
            $futureContractBoundary = (
                [string]::Equals(
                    $relativePath,
                    "Documentation~/upgrading.md",
                    [System.StringComparison]::Ordinal
                ) -and
                $prefix -match '(?i)\bbefore\s+`?$'
            )
            if (
                -not $externalToolVersion -and
                -not $futureContractBoundary -and
                -not [string]::Equals($found, $PackageVersion, [System.StringComparison]::Ordinal) -and
                ($null -eq $allowedHistorical -or -not $allowedHistorical.Contains($found))
            ) {
                throw "Versioned public value $found in $relativePath does not match package.json $PackageVersion."
            }
        }
    }

    # The issue template is a public governance surface. A bare package directory,
    # such as an extracted UPM tarball, has no .github tree to validate.
    if ($PublicRepositoryLayout) {
        $issueTemplate = Get-Content -LiteralPath (Join-Path $RepositoryRoot ".github/ISSUE_TEMPLATE/bug_report.yml") -Raw
        if ($issueTemplate -notmatch "(?m)^\s*placeholder:\s*$([regex]::Escape($PackageVersion))\s*$") {
            throw "Current issue-template package version does not match package.json $PackageVersion."
        }
    }
}

function Test-OrpheusSuspiciousSecretAssignment {
    param(
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][string] $Pattern
    )

    foreach ($match in [regex]::Matches($Text, $Pattern)) {
        $value = $match.Groups["value"].Value.Trim()
        if (
            $value -notmatch "^\$\{\{\s*secrets\." -and
            $value -notmatch '^\$(?:env:)?[A-Za-z_][A-Za-z0-9_]*$'
        ) {
            return $true
        }
    }
    return $false
}

function Assert-OrpheusBinarySensitiveContent {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $PrivateRepositoryPattern,
        [Parameter(Mandatory)][string] $AbsolutePathPattern,
        [Parameter(Mandatory)][string] $CredentialPattern,
        [Parameter(Mandatory)][string] $SigningPattern,
        [Parameter(Mandatory)][string] $SecretAssignmentPattern
    )

    $chunkSize = 65536
    $overlapSize = 8192
    [byte[]] $buffer = [byte[]]::new($chunkSize)
    [byte[]] $carry = [byte[]]::new(0)
    [System.Text.Encoding[]] $encodings = @(
        [System.Text.Encoding]::Latin1
        [System.Text.Encoding]::Unicode
        [System.Text.Encoding]::BigEndianUnicode
    )
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            [byte[]] $window = [byte[]]::new($carry.Length + $read)
            if ($carry.Length -gt 0) {
                [System.Array]::Copy($carry, 0, $window, 0, $carry.Length)
            }
            [System.Array]::Copy($buffer, 0, $window, $carry.Length, $read)
            foreach ($encoding in $encodings) {
                [int[]] $offsets = if ($encoding.CodePage -eq 28591) { @(0) } else { @(0, 1) }
                foreach ($offset in $offsets) {
                    if ($window.Length -le $offset) {
                        continue
                    }
                    $binaryText = $encoding.GetString($window, $offset, $window.Length - $offset)
                    if (
                        $binaryText -match $PrivateRepositoryPattern -or
                        $binaryText -match $AbsolutePathPattern -or
                        $binaryText -match $CredentialPattern -or
                        $binaryText -match $SigningPattern -or
                        (Test-OrpheusSuspiciousSecretAssignment `
                            -Text $binaryText `
                            -Pattern $SecretAssignmentPattern)
                    ) {
                        throw "Public file contains sensitive content: $Path"
                    }
                }
            }
            $carryLength = [Math]::Min($overlapSize, $window.Length)
            $carry = [byte[]]::new($carryLength)
            [System.Array]::Copy(
                $window,
                $window.Length - $carryLength,
                $carry,
                0,
                $carryLength
            )
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-OrpheusSensitiveContent {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $credentialPattern = "(?:gh[opusr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16})"
    $privateRepositoryPattern = "(?i)github\.com[/\\:]sputnicyoji/Orpheus(?:\.git)?(?=$|[/\\?\s`"'#])"
    $absolutePathPattern = "(?im)(?:^|[\s`"'=:(])(?:[A-Za-z]:[\\/]|\\\\[^\\\s]+\\[^\\\s]+|/{1,}(?!/)[^\s`"'<>|)\]}]+)"
    $binaryAbsolutePathPattern = "(?i)(?:[A-Z]:[\\/][\x20-\x7E]{4,}|\\\\[A-Za-z0-9._-]+\\[A-Za-z0-9._$ -]{3,}|/(?:data|home|nix|opt|tmp|Users|var)/[A-Za-z0-9._~+%/@: -]{3,})"
    $signingPattern = "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
    $secretAssignmentPattern = "(?im)^\s*[`"']?(?:password|secret|token|private[_-]?key|client[_-]?secret|unity_(?:email|password|serial))[`"']?\s*[:=]\s*(?<value>[^\r\n#]+)"
    $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot

    foreach ($file in Get-OrpheusRepositoryFiles -RepositoryRoot $root) {
        $extension = $file.Extension
        if ([string]::IsNullOrEmpty($extension)) {
            $extension = $file.Name
        }
        if (-not $script:OrpheusTextExtensions.Contains($extension)) {
            if ($file.Extension -match "^\.(?:jks|key|keystore|p12|pem|pfx)$") {
                throw "Public file contains sensitive signing material: $($file.Name)"
            }
            if ($script:OrpheusBinaryExtensions.Contains($extension)) {
                Assert-OrpheusBinarySensitiveContent `
                    -Path $file.FullName `
                    -PrivateRepositoryPattern $privateRepositoryPattern `
                    -AbsolutePathPattern $binaryAbsolutePathPattern `
                    -CredentialPattern $credentialPattern `
                    -SigningPattern $signingPattern `
                    -SecretAssignmentPattern $secretAssignmentPattern
            }
            continue
        }

        [byte[]] $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        try {
            $text = $strictUtf8.GetString($bytes)
        }
        catch {
            throw "Public text file must use valid UTF-8: $($file.FullName)"
        }
        if ($text.IndexOf([char] 0xFEFF) -ge 0) {
            throw "Public text file must not contain byte-order marks: $($file.FullName)"
        }
        if ($text.IndexOf([char] 0) -ge 0) {
            throw "Public text file must not contain NUL bytes: $($file.FullName)"
        }
        $relativePath = Get-OrpheusRelativePath -Root $root -Path $file.FullName
        $checkPosixPaths = (
            -not [string]::Equals($relativePath, ".gitignore", [System.StringComparison]::Ordinal) -and
            -not [string]::Equals($relativePath, ".github/CODEOWNERS", [System.StringComparison]::Ordinal)
        )
        $slash = [string] [char] 47
        $urlPattern = '(?i)\b(?:https?|git):' + $slash + $slash + '[^\s`"''<>]+'
        $pathText = [regex]::Replace($text, $urlPattern, "")
        $pathText = [regex]::Replace($pathText, '<workspace>/[^\s`"''<>]+', "")
        if ($relativePath.EndsWith(".yml", [System.StringComparison]::OrdinalIgnoreCase)) {
            $pathText = [regex]::Replace(
                $pathText,
                "--exclude='[/]\.(?:ci[/]|git[/]|github[/]|orpheus-export\.json|gitattributes|gitignore)'",
                ""
            )
            $pathText = [regex]::Replace(
                $pathText,
                '(?m)^\s*directory\s*:\s*["'']?/\s*["'']?\s*$',
                ""
            )
        }
        elseif ($relativePath.EndsWith(".cs", [System.StringComparison]::OrdinalIgnoreCase)) {
            # These are explicit project-relative suffix expressions, not host paths.
            $pathText = [regex]::Replace(
                $pathText,
                '\b(?:FixtureRoot|ImportedRoot|ImportedSampleRoot|OutputDirectory|PackageRoot|SampleParent|SampleRoot|TemporaryRoot)\s*\+\s*"[/][^"]+"',
                ""
            )
            $pathText = [regex]::Replace(
                $pathText,
                '\bSceneSuffix\s*=\s*"[/](?:Minimal Setup[/]Scenes[/]MinimalSetup\.unity|OrpheusIssue19[/]FixtureHost\.unity|Scenes[/]PlayableOneShot\.unity)"',
                ""
            )
            $pathText = [regex]::Replace(
                $pathText,
                '\bDoes\.EndWith\(\s*"[/]Runtime[/]OrpheusAudioRuntimeHost\.cs"\s*\)',
                ""
            )
        }
        $pathSensitive = $pathText -match $absolutePathPattern
        $sensitive = (
            $text -match $privateRepositoryPattern -or
            ($checkPosixPaths -and $pathSensitive) -or
            $text -match $credentialPattern -or
            $text -match $signingPattern
        )
        if (
            -not $sensitive -and
            (Test-OrpheusSuspiciousSecretAssignment -Text $text -Pattern $secretAssignmentPattern)
        ) {
            $sensitive = $true
        }

        if ($sensitive) {
            throw "Public file contains sensitive content: $($file.FullName)"
        }
    }
}

function Remove-OrpheusYamlComment {
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Line)

    $singleQuoted = $false
    $doubleQuoted = $false
    for ($index = 0; $index -lt $Line.Length; $index++) {
        $character = $Line[$index]
        if ($character -eq "'" -and -not $doubleQuoted) {
            $singleQuoted = -not $singleQuoted
            continue
        }
        if ($character -eq '"' -and -not $singleQuoted -and ($index -eq 0 -or $Line[$index - 1] -ne "\")) {
            $doubleQuoted = -not $doubleQuoted
            continue
        }
        if (
            $character -eq "#" -and
            -not $singleQuoted -and
            -not $doubleQuoted -and
            ($index -eq 0 -or [char]::IsWhiteSpace($Line[$index - 1]))
        ) {
            return $Line.Substring(0, $index).TrimEnd()
        }
    }

    return $Line.TrimEnd()
}

function Test-OrpheusUnsupportedYamlShape {
    param([Parameter(Mandatory)][string] $Line)

    $candidate = [regex]::Replace($Line, "\$\{\{.*?\}\}", "EXPRESSION")
    $singleQuoted = $false
    $doubleQuoted = $false
    foreach ($character in $candidate.ToCharArray()) {
        if ($character -eq "'" -and -not $doubleQuoted) {
            $singleQuoted = -not $singleQuoted
            continue
        }
        if ($character -eq '"' -and -not $singleQuoted) {
            $doubleQuoted = -not $doubleQuoted
            continue
        }
        if (-not $singleQuoted -and -not $doubleQuoted -and $character -in @("{", "}", "[", "]")) {
            return $true
        }
    }

    return $false
}

function ConvertFrom-OrpheusYamlScalar {
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Value)

    $trimmed = $Value.Trim()
    if (
        $trimmed.Length -ge 2 -and
        (
            ($trimmed[0] -eq "'" -and $trimmed[$trimmed.Length - 1] -eq "'") -or
            ($trimmed[0] -eq '"' -and $trimmed[$trimmed.Length - 1] -eq '"')
        )
    ) {
        return $trimmed.Substring(1, $trimmed.Length - 2)
    }

    return $trimmed
}

function Read-OrpheusYamlBlock {
    param(
        [Parameter(Mandatory)][object[]] $Lines,
        [Parameter(Mandatory)][ref] $Index,
        [Parameter(Mandatory)][int] $Indent
    )

    $isSequence = $Lines[$Index.Value].Content.StartsWith("- ", [System.StringComparison]::Ordinal)
    if ($isSequence) {
        $values = [System.Collections.Generic.List[object]]::new()
        while (
            $Index.Value -lt $Lines.Count -and
            $Lines[$Index.Value].Indent -eq $Indent -and
            $Lines[$Index.Value].Content.StartsWith("- ", [System.StringComparison]::Ordinal)
        ) {
            $itemText = $Lines[$Index.Value].Content.Substring(2).Trim()
            if ($itemText -match "^([A-Za-z0-9_.-]+)\s*:\s*(.*)$") {
                $item = [ordered]@{}
                $key = $matches[1]
                $valueText = $matches[2]
                $Index.Value++
                if ([string]::IsNullOrWhiteSpace($valueText)) {
                    if ($Index.Value -lt $Lines.Count -and $Lines[$Index.Value].Indent -gt $Indent) {
                        $item[$key] = Read-OrpheusYamlBlock -Lines $Lines -Index $Index -Indent $Lines[$Index.Value].Indent
                    }
                    else {
                        $item[$key] = $null
                    }
                }
                else {
                    $item[$key] = ConvertFrom-OrpheusYamlScalar -Value $valueText
                }

                if ($Index.Value -lt $Lines.Count -and $Lines[$Index.Value].Indent -gt $Indent) {
                    $continuation = Read-OrpheusYamlBlock -Lines $Lines -Index $Index -Indent $Lines[$Index.Value].Indent
                    if ($continuation -isnot [System.Collections.IDictionary]) {
                        throw "Workflow policy rejects an invalid YAML sequence mapping."
                    }
                    foreach ($continuationKey in $continuation.Keys) {
                        if ($item.Contains($continuationKey)) {
                            throw "Workflow policy rejects duplicate YAML key: $continuationKey"
                        }
                        $item[$continuationKey] = $continuation[$continuationKey]
                    }
                }
                $values.Add($item)
            }
            else {
                $values.Add((ConvertFrom-OrpheusYamlScalar -Value $itemText))
                $Index.Value++
            }
        }

        return ,$values.ToArray()
    }

    $mapping = [ordered]@{}
    while (
        $Index.Value -lt $Lines.Count -and
        $Lines[$Index.Value].Indent -eq $Indent -and
        -not $Lines[$Index.Value].Content.StartsWith("- ", [System.StringComparison]::Ordinal)
    ) {
        $content = $Lines[$Index.Value].Content
        if ($content -notmatch "^([A-Za-z0-9_.-]+)\s*:\s*(.*)$") {
            throw "Workflow policy rejects unsupported YAML at line $($Lines[$Index.Value].LineNumber)."
        }
        $key = $matches[1]
        $valueText = $matches[2]
        if ($mapping.Contains($key)) {
            throw "Workflow policy rejects duplicate YAML key: $key"
        }
        $Index.Value++
        if ([string]::IsNullOrWhiteSpace($valueText)) {
            if ($Index.Value -lt $Lines.Count -and $Lines[$Index.Value].Indent -gt $Indent) {
                $mapping[$key] = Read-OrpheusYamlBlock -Lines $Lines -Index $Index -Indent $Lines[$Index.Value].Indent
            }
            else {
                $mapping[$key] = $null
            }
        }
        else {
            if ($valueText -in @("|", ">")) {
                throw "Workflow policy rejects unsupported multiline YAML scalars."
            }
            $mapping[$key] = ConvertFrom-OrpheusYamlScalar -Value $valueText
        }
    }

    return ,$mapping
}

function ConvertFrom-OrpheusWorkflowYaml {
    param([Parameter(Mandatory)][string] $Path)

    $parsedLines = [System.Collections.Generic.List[object]]::new()
    $lineNumber = 0
    foreach ($rawLine in [System.IO.File]::ReadAllLines($Path)) {
        $lineNumber++
        if ($rawLine.Contains("`t")) {
            throw "Workflow policy rejects tabs in YAML."
        }
        $line = Remove-OrpheusYamlComment -Line $rawLine
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("---", [System.StringComparison]::Ordinal)) {
            continue
        }
        if (Test-OrpheusUnsupportedYamlShape -Line $line) {
            throw "Workflow policy rejects unsupported inline YAML collections."
        }
        $indent = $line.Length - $line.TrimStart(" ").Length
        $parsedLines.Add([pscustomobject]@{
            Indent = $indent
            Content = $line.Substring($indent)
            LineNumber = $lineNumber
        })
    }

    if ($parsedLines.Count -eq 0 -or $parsedLines[0].Indent -ne 0) {
        throw "Workflow policy rejects an empty or indented YAML document."
    }
    $index = 0
    $document = Read-OrpheusYamlBlock -Lines $parsedLines.ToArray() -Index ([ref] $index) -Indent 0
    if ($index -ne $parsedLines.Count -or $document -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy rejects unsupported trailing YAML."
    }
    return $document
}

function Assert-OrpheusExactYamlKeys {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary] $Mapping,
        [Parameter(Mandatory)][string[]] $Expected,
        [Parameter(Mandatory)][string] $Label
    )

    [string[]] $actual = @($Mapping.Keys)
    [string[]] $expectedSorted = @($Expected)
    [System.Array]::Sort($actual, [System.StringComparer]::Ordinal)
    [System.Array]::Sort($expectedSorted, [System.StringComparer]::Ordinal)
    if ([string]::Join("`n", $actual) -cne [string]::Join("`n", $expectedSorted)) {
        throw "Workflow policy $Label keys are invalid."
    }
}

function Get-OrpheusYamlScalars {
    param([AllowNull()][object] $Value)

    if ($null -eq $Value) {
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Get-OrpheusYamlScalars -Value $Value[$key]
        }
        return
    }
    if ($Value -is [object[]]) {
        foreach ($item in $Value) {
            Get-OrpheusYamlScalars -Value $item
        }
        return
    }
    Write-Output ([string] $Value)
}

function Test-OrpheusYamlContainsKey {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory)][string] $Key
    )

    if ($null -eq $Value) {
        return $false
    }
    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Contains($Key)) {
            return $true
        }
        foreach ($childKey in $Value.Keys) {
            if (Test-OrpheusYamlContainsKey -Value $Value[$childKey] -Key $Key) {
                return $true
            }
        }
    }
    elseif ($Value -is [object[]]) {
        foreach ($item in $Value) {
            if (Test-OrpheusYamlContainsKey -Value $item -Key $Key) {
                return $true
            }
        }
    }
    return $false
}

function Assert-OrpheusCommonWorkflowContract {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary] $Document,
        [Parameter(Mandatory)][string] $ExpectedName,
        [Parameter(Mandatory)][string[]] $ExpectedTopLevelKeys,
        [Parameter(Mandatory)][string] $ExpectedConcurrencyGroup
    )

    Assert-OrpheusExactYamlKeys -Mapping $Document -Expected $ExpectedTopLevelKeys -Label "top-level"
    if (-not [string]::Equals([string] $Document.name, $ExpectedName, [System.StringComparison]::Ordinal)) {
        throw "Workflow policy requires workflow name $ExpectedName."
    }
    if ($Document.permissions -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy permissions must be a block mapping."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.permissions -Expected @("contents") -Label "permissions"
    if (-not [string]::Equals([string] $Document.permissions.contents, "read", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy requires contents read."
    }
    if ($Document.concurrency -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy concurrency must be a block mapping."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.concurrency -Expected @("group", "cancel-in-progress") -Label "concurrency"
    if (
        -not [string]::Equals(
            [string] $Document.concurrency.group,
            $ExpectedConcurrencyGroup,
            [System.StringComparison]::Ordinal
        ) -or
        -not [string]::Equals([string] $Document.concurrency["cancel-in-progress"], "true", [System.StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "Workflow policy concurrency group must be $ExpectedConcurrencyGroup with stale-run cancellation."
    }
    if ($Document.jobs -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy jobs must be a block mapping."
    }
}

function Assert-OrpheusActionPins {
    param([Parameter(Mandatory)][object[]] $Steps)

    foreach ($step in $Steps) {
        if ($step -isnot [System.Collections.IDictionary]) {
            throw "Workflow policy steps must be mappings."
        }
        if ($step.Contains("uses")) {
            $reference = [string] $step.uses
            if (
                -not $reference.StartsWith("./", [System.StringComparison]::Ordinal) -and
                $reference -notmatch "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$"
            ) {
                throw "Workflow policy requires immutable uses references: $reference"
            }
        }
    }
}

function Assert-OrpheusVerifyWorkflow {
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Document)

    Assert-OrpheusCommonWorkflowContract `
        -Document $Document `
        -ExpectedName "Public Package" `
        -ExpectedTopLevelKeys @("name", "on", "permissions", "concurrency", "jobs") `
        -ExpectedConcurrencyGroup 'public-package-${{ github.ref }}'

    if ($Document.on -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy verify triggers must be a block mapping."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.on -Expected @("pull_request", "push", "workflow_dispatch") -Label "verify triggers"
    if ($null -ne $Document.on.pull_request -or $null -ne $Document.on.workflow_dispatch) {
        throw "Workflow policy verify pull_request and workflow_dispatch triggers must be unfiltered."
    }
    if ($Document.on.push -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy verify push trigger must be a block mapping."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.on.push -Expected @("branches", "tags") -Label "verify push"
    if (
        @($Document.on.push.branches).Count -ne 1 -or
        -not [string]::Equals([string] @($Document.on.push.branches)[0], "main", [System.StringComparison]::Ordinal) -or
        @($Document.on.push.tags).Count -ne 1 -or
        -not [string]::Equals([string] @($Document.on.push.tags)[0], "v*", [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy verify push triggers must be main and v*."
    }

    Assert-OrpheusExactYamlKeys -Mapping $Document.jobs -Expected @("verify") -Label "verify jobs"
    $job = $Document.jobs.verify
    if ($job -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy verify job must be a mapping."
    }
    $allowedJobKeys = @("runs-on", "timeout-minutes", "steps")
    if ($job.Contains("name")) {
        if (-not [string]::Equals([string] $job.name, "verify", [System.StringComparison]::Ordinal)) {
            throw "Workflow policy verify job name is invalid."
        }
        $allowedJobKeys += "name"
    }
    Assert-OrpheusExactYamlKeys -Mapping $job -Expected $allowedJobKeys -Label "verify job"
    if (-not [string]::Equals([string] $job["runs-on"], "ubuntu-latest", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy verify must use ubuntu-latest."
    }
    if (-not [string]::Equals([string] $job["timeout-minutes"], "10", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy verify timeout-minutes must be 10."
    }
    $steps = @($job.steps)
    Assert-OrpheusActionPins -Steps $steps
    if ($steps.Count -ne 3) {
        throw "Workflow policy verify requires pinned checkout, the public validator, and tarball validation."
    }
    $checkout = $steps[0]
    $validation = $steps[1]
    $tarballValidation = $steps[2]
    Assert-OrpheusExactYamlKeys -Mapping $checkout -Expected @("uses", "with") -Label "verify checkout step"
    Assert-OrpheusExactYamlKeys -Mapping $checkout.with -Expected @("persist-credentials") -Label "verify checkout inputs"
    Assert-OrpheusExactYamlKeys -Mapping $validation -Expected @("name", "shell", "run") -Label "verify validation step"
    Assert-OrpheusExactYamlKeys -Mapping $tarballValidation -Expected @("name", "shell", "run") -Label "verify tarball validation step"
    if (
        -not [string]::Equals([string] $checkout.uses, "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $checkout.with["persist-credentials"], "false", [System.StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "Workflow policy verify checkout must disable persisted credentials."
    }
    if (
        -not [string]::Equals([string] $validation.name, "Validate", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $validation.shell, "pwsh", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals(
            ([string] $validation.run).Trim(),
            "pwsh -NoProfile -File ./.github/scripts/test-public-package.ps1",
            [System.StringComparison]::Ordinal
        )
    ) {
        throw "Workflow policy verify validation command must be exact and mandatory."
    }
    # The tarball step is a required third step in a fixed position. Both validation
    # commands stay exact so a rename, reorder, or shell swap fails the policy.
    if (
        -not [string]::Equals([string] $tarballValidation.name, "Validate UPM tarball", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $tarballValidation.shell, "pwsh", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals(
            ([string] $tarballValidation.run).Trim(),
            "pwsh -NoProfile -File ./.github/scripts/test-public-tarball.ps1",
            [System.StringComparison]::Ordinal
        )
    ) {
        throw "Workflow policy verify tarball validation command must be exact and mandatory."
    }
    if (
        [string]::Join("`n", @(Get-OrpheusYamlScalars -Value $Document)) -match "(?i)\bsecrets\b" -or
        (Test-OrpheusYamlContainsKey -Value $Document -Key "environment") -or
        (Test-OrpheusYamlContainsKey -Value $Document -Key "id-token")
    ) {
        throw "Workflow policy forbids PR secrets, environments, or OIDC."
    }
}

function Assert-OrpheusUnityWorkflow {
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Document)

    Assert-OrpheusCommonWorkflowContract `
        -Document $Document `
        -ExpectedName "Unity Compatibility" `
        -ExpectedTopLevelKeys @("name", "on", "permissions", "concurrency", "jobs") `
        -ExpectedConcurrencyGroup 'unity-compatibility-${{ github.ref }}'

    Assert-OrpheusExactYamlKeys -Mapping $Document.on -Expected @("push", "workflow_dispatch") -Label "Unity triggers"
    if ($null -ne $Document.on.workflow_dispatch) {
        throw "Workflow policy Unity workflow_dispatch must be unfiltered."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.on.push -Expected @("branches") -Label "Unity push"
    if (
        @($Document.on.push.branches).Count -ne 1 -or
        -not [string]::Equals([string] @($Document.on.push.branches)[0], "main", [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy Unity push trigger must be main only."
    }
    Assert-OrpheusExactYamlKeys -Mapping $Document.jobs -Expected @("compatibility") -Label "Unity jobs"
    $job = $Document.jobs.compatibility
    if ($job -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy compatibility job must be a mapping."
    }
    $allowedJobKeys = @("if", "runs-on", "timeout-minutes", "strategy", "steps")
    if ($job.Contains("name")) {
        if (-not [string]::Equals([string] $job.name, "compatibility", [System.StringComparison]::Ordinal)) {
            throw "Workflow policy compatibility job name is invalid."
        }
        $allowedJobKeys += "name"
    }
    Assert-OrpheusExactYamlKeys -Mapping $job -Expected $allowedJobKeys -Label "compatibility job"
    $normalizedGuard = ([string] $job.if -replace "\s+", " ").Trim()
    if (-not [string]::Equals($normalizedGuard, "github.ref == 'refs/heads/main' && vars.ORPHEUS_UNITY_CI_ENABLED == 'true'", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy compatibility job requires the exact main-ref and enable guards."
    }
    if (-not [string]::Equals([string] $job["runs-on"], "ubuntu-latest", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy compatibility must use ubuntu-latest."
    }
    if (-not [string]::Equals([string] $job["timeout-minutes"], "30", [System.StringComparison]::Ordinal)) {
        throw "Workflow policy compatibility timeout-minutes must be 30."
    }
    Assert-OrpheusExactYamlKeys -Mapping $job.strategy -Expected @("fail-fast", "matrix") -Label "Unity strategy"
    Assert-OrpheusExactYamlKeys -Mapping $job.strategy.matrix -Expected @("testMode") -Label "Unity matrix"
    [string[]] $testModes = @($job.strategy.matrix.testMode)
    if (
        -not [string]::Equals([string] $job.strategy["fail-fast"], "false", [System.StringComparison]::OrdinalIgnoreCase) -or
        $testModes.Count -ne 2 -or
        -not [string]::Equals($testModes[0], "editmode", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals($testModes[1], "playmode", [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy Unity matrix must be exactly editmode and playmode."
    }

    $steps = @($job.steps)
    Assert-OrpheusActionPins -Steps $steps
    if ($steps.Count -ne 4) {
        throw "Workflow policy Unity actions are incomplete or unpinned."
    }
    $checkout = $steps[0]
    $stage = $steps[1]
    $runner = $steps[2]
    $upload = $steps[3]
    Assert-OrpheusExactYamlKeys -Mapping $checkout -Expected @("uses", "with") -Label "Unity checkout step"
    Assert-OrpheusExactYamlKeys -Mapping $checkout.with -Expected @("persist-credentials") -Label "Unity checkout inputs"
    Assert-OrpheusExactYamlKeys -Mapping $stage -Expected @("name", "shell", "run") -Label "Unity package stage"
    Assert-OrpheusExactYamlKeys -Mapping $runner -Expected @("name", "uses", "env", "with") -Label "Unity runner step"
    Assert-OrpheusExactYamlKeys -Mapping $upload -Expected @("name", "if", "uses", "with") -Label "Unity upload step"
    if (
        -not [string]::Equals([string] $checkout.uses, "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $checkout.with["persist-credentials"], "false", [System.StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "Workflow policy Unity checkout must disable persisted credentials."
    }
    $rootSlash = [string] [char] 47
    $expectedStageCommand = "mkdir -p .ci/package && rsync -a --delete --exclude='${rootSlash}.ci${rootSlash}' --exclude='${rootSlash}.git${rootSlash}' --exclude='${rootSlash}.github${rootSlash}' --exclude='${rootSlash}.orpheus-export.json' --exclude='${rootSlash}.gitattributes' --exclude='${rootSlash}.gitignore' ./ .ci/package/"
    if (
        -not [string]::Equals([string] $stage.name, "Stage package", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $stage.shell, "bash", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals(
            ([string] $stage.run).Trim(),
            $expectedStageCommand,
            [System.StringComparison]::Ordinal
        )
    ) {
        throw "Workflow policy Unity package staging must be exact and non-root."
    }
    if (
        -not [string]::Equals([string] $runner.name, "Test package", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $runner.uses, "game-ci/unity-test-runner@0ff419b913a3630032cbe0de48a0099b5a9f0ed9", [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy Unity test-runner identity is invalid."
    }
    $inputs = $runner.with
    if (
        $inputs -isnot [System.Collections.IDictionary] -or
        (Test-OrpheusYamlContainsKey -Value $Document -Key "githubToken") -or
        $runner.env -isnot [System.Collections.IDictionary]
    ) {
        throw "Workflow policy Unity test-runner inputs are invalid."
    }
    Assert-OrpheusExactYamlKeys -Mapping $inputs -Expected @("customImage", "packageMode", "projectPath", "testMode", "unityVersion") -Label "Unity runner inputs"
    Assert-OrpheusExactYamlKeys -Mapping $runner.env -Expected @("UNITY_EMAIL", "UNITY_PASSWORD", "UNITY_SERIAL") -Label "Unity runner environment"
    if (
        -not [string]::Equals([string] $inputs.packageMode, "true", [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string] $inputs.projectPath, ".ci/package", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $inputs.unityVersion, "2022.3.63f1", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $inputs.testMode, '${{ matrix.testMode }}', [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $inputs.customImage, "unityci/editor@sha256:6eaf40ea9a5f1daba68fdfbd50fddaac7855a21a0eb13539ba212fd9b892211f", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $runner.env.UNITY_EMAIL, '${{ secrets.UNITY_EMAIL }}', [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $runner.env.UNITY_PASSWORD, '${{ secrets.UNITY_PASSWORD }}', [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $runner.env.UNITY_SERIAL, '${{ secrets.UNITY_SERIAL }}', [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy Unity test-runner inputs are invalid."
    }

    $secretScalars = [string]::Join("`n", @(Get-OrpheusYamlScalars -Value $Document))
    foreach ($expectedSecret in @(
        '${{ secrets.UNITY_EMAIL }}',
        '${{ secrets.UNITY_PASSWORD }}',
        '${{ secrets.UNITY_SERIAL }}'
    )) {
        if ([regex]::Matches($secretScalars, [regex]::Escape($expectedSecret)).Count -ne 1) {
            throw "Workflow policy Unity secrets must be exactly the three Pro secret references."
        }
        $secretScalars = $secretScalars.Replace($expectedSecret, "")
    }
    if ($secretScalars -match "(?i)\bsecrets\b") {
        throw "Workflow policy Unity secrets must be exactly the three Pro secret references."
    }

    if ($upload.with -isnot [System.Collections.IDictionary]) {
        throw "Workflow policy Unity artifact upload inputs are invalid."
    }
    Assert-OrpheusExactYamlKeys `
        -Mapping $upload.with `
        -Expected @("if-no-files-found", "name", "path", "retention-days") `
        -Label "Unity artifact upload inputs"
    if (
        -not [string]::Equals([string] $upload.name, "Upload results", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.if, "always()", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.uses, "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.with.name, 'unity-${{ matrix.testMode }}', [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.with.path, "artifacts", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.with["retention-days"], "7", [System.StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $upload.with["if-no-files-found"], "error", [System.StringComparison]::Ordinal)
    ) {
        throw "Workflow policy Unity artifact upload inputs are invalid."
    }
    if (
        (Test-OrpheusYamlContainsKey -Value $Document -Key "environment") -or
        (Test-OrpheusYamlContainsKey -Value $Document -Key "id-token")
    ) {
        throw "Workflow policy Unity forbids environments and OIDC."
    }
}

function Assert-OrpheusWorkflowPolicy {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $workflowRoot = Join-Path $RepositoryRoot ".github/workflows"
    $workflows = @(
        Get-ChildItem -LiteralPath $workflowRoot -File |
            Where-Object Extension -In @(".yml", ".yaml")
    )
    if ($workflows.Count -eq 0) {
        throw "Workflow policy requires at least one workflow."
    }

    $byName = [System.Collections.Generic.Dictionary[string, System.IO.FileInfo]]::new([System.StringComparer]::Ordinal)
    foreach ($workflow in $workflows) {
        $byName.Add($workflow.Name, $workflow)
    }
    if (
        $byName.Count -ne 2 -or
        -not $byName.ContainsKey("verify.yml") -or
        -not $byName.ContainsKey("unity-compatibility.yml")
    ) {
        throw "Workflow policy requires exactly verify.yml and unity-compatibility.yml."
    }

    try {
        $verify = ConvertFrom-OrpheusWorkflowYaml -Path $byName["verify.yml"].FullName
        Assert-OrpheusVerifyWorkflow -Document $verify
        $unity = ConvertFrom-OrpheusWorkflowYaml -Path $byName["unity-compatibility.yml"].FullName
        Assert-OrpheusUnityWorkflow -Document $unity
    }
    catch {
        if ($_.Exception.Message.StartsWith("Workflow policy", [System.StringComparison]::Ordinal)) {
            throw
        }
        throw "Workflow policy validation failed: $($_.Exception.Message)"
    }
}

function Assert-OrpheusExportManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [Parameter(Mandatory)]
        [string] $PackageTreeSha256
    )

    $manifestPath = Join-Path $RepositoryRoot ".orpheus-export.json"
    $manifest = Read-OrpheusJson -Path $manifestPath -Label ".orpheus-export.json"
    [string[]] $propertyNames = @($manifest.PSObject.Properties.Name)
    [System.Array]::Sort($propertyNames, [System.StringComparer]::Ordinal)
    $expectedNames = [string[]]@(
        "packageTreeSha256",
        "packageVersion",
        "privateSourceCommit",
        "schemaVersion"
    )
    if ([string]::Join("`n", $propertyNames) -cne [string]::Join("`n", $expectedNames)) {
        throw ".orpheus-export.json has an unexpected schema."
    }
    if ($manifest.schemaVersion -isnot [long] -or $manifest.schemaVersion -ne 1) {
        throw ".orpheus-export.json schemaVersion must be 1."
    }
    if (-not [string]::Equals([string] $manifest.packageVersion, $PackageVersion, [System.StringComparison]::Ordinal)) {
        throw ".orpheus-export.json packageVersion does not match package.json."
    }
    if ([string] $manifest.privateSourceCommit -notmatch "^[0-9a-f]{40}$") {
        throw ".orpheus-export.json privateSourceCommit is invalid."
    }
    if (
        [string] $manifest.packageTreeSha256 -notmatch "^[0-9a-f]{64}$" -or
        -not [string]::Equals([string] $manifest.packageTreeSha256, $PackageTreeSha256, [System.StringComparison]::Ordinal)
    ) {
        throw ".orpheus-export.json packageTreeSha256 does not match the exported package tree."
    }

    return $manifest
}

function Invoke-OrpheusPackageContentValidation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $PackageRoot,

        [switch] $PublicRepositoryLayout
    )

    $root = Get-OrpheusNormalizedRoot -Path $PackageRoot
    Assert-OrpheusNoLinks -Root $root
    $package = Assert-OrpheusPackageManifest -RepositoryRoot $root
    Assert-OrpheusJsonAndAssemblyDefinitions `
        -RepositoryRoot $root `
        -PublicRepositoryLayout:$PublicRepositoryLayout
    Assert-OrpheusUnityMetadata `
        -RepositoryRoot $root `
        -PublicRepositoryLayout:$PublicRepositoryLayout
    Assert-OrpheusCoreIsolation -RepositoryRoot $root
    Assert-OrpheusMarkdownLinks -RepositoryRoot $root
    Assert-OrpheusVersionSurfaces `
        -RepositoryRoot $root `
        -PackageVersion ([string] $package.version) `
        -PublicRepositoryLayout:$PublicRepositoryLayout
    Assert-OrpheusSensitiveContent -RepositoryRoot $root

    $packageFiles = @(
        Get-OrpheusPackageFiles `
            -PackageRoot $root `
            -PublicRepositoryLayout:$PublicRepositoryLayout
    )
    $fingerprint = Get-OrpheusPackageTreeSha256 `
        -PackageRoot $root `
        -PublicRepositoryLayout:$PublicRepositoryLayout

    return [pscustomobject][ordered]@{
        packageName = [string] $package.name
        packageVersion = [string] $package.version
        packageTreeSha256 = $fingerprint
        fileCount = $packageFiles.Count
    }
}

function Invoke-OrpheusPublicPackageValidation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $root = Get-OrpheusNormalizedRoot -Path $RepositoryRoot
    Assert-OrpheusNoLinks -Root $root
    # Workflow policy precedes package content scanning. A policy-violating workflow
    # can carry text that also trips the sensitive-content absolute-path rule, and the
    # policy diagnosis is the specific one. Reordering these swaps the reported cause.
    Assert-OrpheusWorkflowPolicy -RepositoryRoot $root
    $content = Invoke-OrpheusPackageContentValidation `
        -PackageRoot $root `
        -PublicRepositoryLayout
    Assert-OrpheusPublicLayout -RepositoryRoot $root
    $manifest = Assert-OrpheusExportManifest `
        -RepositoryRoot $root `
        -PackageVersion $content.packageVersion `
        -PackageTreeSha256 $content.packageTreeSha256

    return [pscustomobject][ordered]@{
        packageVersion = $content.packageVersion
        packageTreeSha256 = $content.packageTreeSha256
        privateSourceCommit = [string] $manifest.privateSourceCommit
        githubSha = if ($env:GITHUB_SHA) { [string] $env:GITHUB_SHA } else { $null }
    }
}
