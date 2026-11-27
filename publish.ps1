param(
    # By default, the most compact option: a single ~28 MB exe.
    # The target machine needs the .NET 8 Desktop Runtime (usually already installed on Windows 11).
    [switch]$SelfContained,
    # Only with -SelfContained: compression inside the single file (~83 MB instead of ~189 MB).
    [switch]$Compressed,
    # Output directory (relative to the repository root by default: publish).
    [string]$OutputDir,
    # Authenticode-sign the exe with a local self-signed certificate.
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'OrmPacker/OrmPacker.csproj'
$output = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $PSScriptRoot 'publish'
} elseif ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path $PSScriptRoot $OutputDir
}
$props = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$version = ([regex]::Match($props, '<Version>([^<]+)</Version>')).Groups[1].Value
if (-not $version) { throw 'Version not found in Directory.Build.props' }

$arguments = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $output,
    '-p:PublishSingleFile=true',
    '-p:DebugType=None',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)

if ($SelfContained) {
    $arguments += '--self-contained', 'true'
    if ($Compressed) { $arguments += '-p:EnableCompressionInSingleFile=true' }
} else {
    $arguments += '--self-contained', 'false'
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet @arguments

$exe = Join-Path $output 'OrmPacker.exe'
if (-not (Test-Path $exe)) { throw "Published executable not found: $exe" }

if ($Sign) {
    & (Join-Path $PSScriptRoot 'sign.ps1') -Exe $exe
}

$variant = if ($SelfContained) { if ($Compressed) { 'sc-compressed' } else { 'sc' } } else { 'fd' }
$archive = Join-Path $output "orm-packer-$version-win-x64-$variant.zip"
$docs = @('LICENSE', 'NOTICE.md', 'THIRD_PARTY_LICENSES.md') |
    ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ }
if ($Sign) {
    $cer = Join-Path $output 'orm-packer-local.cer'
    if (Test-Path $cer) { $docs += $cer }
}

Compress-Archive -Path $exe -DestinationPath $archive
if ($docs.Count -gt 0) { Compress-Archive -Path $docs -DestinationPath $archive -Update }

$sums = Join-Path $output 'SHA256SUMS.txt'
$lines = foreach ($file in @($archive, $exe)) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, [System.IO.Path]::GetFileName($file)
}
$lines | Set-Content $sums -Encoding ascii

Write-Host ""
Write-Host ("Done: {0}" -f $exe)
Write-Host ("Archive: {0}" -f $archive)
Write-Host ("Checksums: {0}" -f $sums)
Get-ChildItem $output | Select-Object Name, Length
