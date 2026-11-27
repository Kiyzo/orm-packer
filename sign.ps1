param(
    [string]$Exe = "publish\OrmPacker.exe",
    [string]$Subject = "CN=ORM Packer"
)

$ErrorActionPreference = 'Stop'
$exePath = if ([System.IO.Path]::IsPathRooted($Exe)) {
    [System.IO.Path]::GetFullPath($Exe)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Exe))
}
if (-not (Test-Path $exePath)) { throw "Executable not found: $exePath" }

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "ORM Packer (local build)" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(3)
}

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse |
    Where-Object FullName -Match '\\x64\\' | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe (Windows SDK) not found' }

& $signtool sign /fd sha256 /sha1 $cert.Thumbprint $exePath
if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }

& $signtool verify /pa /sha1 $cert.Thumbprint $exePath
Write-Host ""
Write-Host "Signed: $exePath"
Write-Host "Certificate: $($cert.Subject) ($($cert.Thumbprint))"

# Export the public certificate so it can be trusted locally (Windows shows a
# confirmation dialog for the Root store; this must be approved by the user).
$cer = Join-Path (Split-Path $exePath) 'orm-packer-local.cer'
[System.IO.File]::WriteAllBytes($cer, $cert.RawData)
Write-Host "Local certificate exported: $cer"
Write-Host "To remove the Windows warning on this PC, import it into"
Write-Host "  Current User > Trusted Root Certification Authorities"
Write-Host "and into Current User > Trusted Publishers."
