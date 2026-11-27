param([string]$Certificate = "publish\orm-packer-local.cer")

$ErrorActionPreference = 'Stop'
$path = if ([System.IO.Path]::IsPathRooted($Certificate)) {
    $Certificate
} else {
    Join-Path $PSScriptRoot $Certificate
}
if (-not (Test-Path $path)) { throw "Certificate not found: $path" }

# Opens the Windows certificate wizard. Choose "Current User", then import into
# Trusted Root Certification Authorities and Trusted Publishers when asked.
Start-Process $path
Write-Host "Opened certificate import wizard for: $path"
Write-Host "Import into Current User > Trusted Root Certification Authorities"
Write-Host "and Current User > Trusted Publishers."
