[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
}

. (Join-Path $PSScriptRoot "OrpheusUpmTarball.ps1")

# This entry point proves the tarball can be constructed and structurally
# validated. It never retains an artifact: publishing is a private release step.
$temporaryRoot = ""
try {
    Write-Output "Validating Orpheus UPM tarball..."
    $temporaryRoot = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("orpheus-upm-tarball-ci-" + [Guid]::NewGuid().ToString("N"))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    $summary = New-OrpheusUpmTarball `
        -PublicRepositoryRoot $RepositoryRoot `
        -OutputDirectory $temporaryRoot
    Write-Output "Orpheus UPM tarball validation passed."
    Write-Output ($summary | ConvertTo-Json -Compress)
}
catch {
    $message = [regex]::Replace($_.Exception.Message, "[^\x20-\x7E]", "?")
    [Console]::Error.WriteLine("Orpheus UPM tarball validation failed: {0}", $message)
    exit 1
}
finally {
    if (
        -not [string]::IsNullOrWhiteSpace($temporaryRoot) -and
        [System.IO.Directory]::Exists($temporaryRoot)
    ) {
        [System.IO.Directory]::Delete($temporaryRoot, $true)
    }
}
