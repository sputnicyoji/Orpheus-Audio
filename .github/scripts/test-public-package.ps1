[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
}

. (Join-Path $PSScriptRoot "OrpheusPublicPackageValidation.ps1")

try {
    Write-Output "Validating Orpheus public package..."
    $summary = Invoke-OrpheusPublicPackageValidation -RepositoryRoot $RepositoryRoot
    Write-Output "Orpheus public package validation passed."
    Write-Output ($summary | ConvertTo-Json -Compress)
}
catch {
    $message = [regex]::Replace($_.Exception.Message, "[^\x20-\x7E]", "?")
    [Console]::Error.WriteLine("Orpheus public package validation failed: {0}", $message)
    exit 1
}
