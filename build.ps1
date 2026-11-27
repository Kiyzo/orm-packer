param(
    # Desktop single-file exe is always built; these switches only skip the extras.
    [switch]$SkipWeb,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$out = Join-Path $root 'publish'

# Everything runnable lands in publish/. Remove both the output tree and the stale
# per-project bin/obj copies, so there is no "wrong exe" to launch by accident.
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$stale = @(
    'OrmPacker\bin', 'OrmPacker\obj',
    'tests\OrmPacker.Tests\bin', 'tests\OrmPacker.Tests\obj',
    'web\OrmPacker.Web\bin', 'web\OrmPacker.Web\obj',
    # Leftovers from ad-hoc -p:BaseOutputPath runs.
    'OrmPacker\OrmPacker\bin', 'OrmPacker\tests', 'OrmPacker\publish',
    'tests\OrmPacker.Tests\tests', 'tests\OrmPacker.Tests\publish',
    'web\OrmPacker.Web\publish'
)
function Remove-StaleBuilds {
    foreach ($relative in $stale) {
        $path = Join-Path $root $relative
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
}
Remove-StaleBuilds

# 1. Desktop: one exe, versioned zip and checksums -> publish/
Write-Host '== Desktop single-file exe =='
& (Join-Path $root 'publish.ps1')

# 2. Test runner -> publish/tests/. Absolute BaseOutputPath keeps the tree flat.
if (-not $SkipTests) {
    Write-Host '== Tests =='
    $testProject = Join-Path $root 'tests/OrmPacker.Tests/OrmPacker.Tests.csproj'
    $testOut = Join-Path $out 'tests/'
    dotnet build $testProject -c Release "-p:BaseOutputPath=$testOut"
    # The runner locates golden/ by walking up from its output directory.
    Copy-Item (Join-Path $root 'tests/OrmPacker.Tests/golden') -Destination (Join-Path $out 'tests/golden') -Recurse -Force
    & dotnet (Join-Path $testOut 'Release/net8.0-windows/OrmPacker.Tests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Test run failed.' }
}

# 3. Web bundle and Blazor publish -> publish/web/.
if (-not $SkipWeb) {
    Write-Host '== Web =='
    $webRoot = Join-Path $root 'web'
    Push-Location $webRoot
    try {
        npm ci
        npm run build
    }
    finally { Pop-Location }
    dotnet publish (Join-Path $webRoot 'OrmPacker.Web/OrmPacker.Web.csproj') -c Release -o (Join-Path $out 'web')
}

# dotnet publish/test recreate small apphost copies under bin/. Drop them again so
# the only runnable desktop exe in the tree is publish/OrmPacker.exe.
Remove-StaleBuilds

Write-Host ''
Write-Host 'All build outputs are under publish/:'
Write-Host '  publish/OrmPacker.exe                       <- launch this desktop exe'
Write-Host '  publish/orm-packer-<version>-win-x64-fd.zip'
Write-Host '  publish/SHA256SUMS.txt'
if (-not $SkipTests) { Write-Host '  publish/tests/Release/net8.0-windows/        <- test runner' }
if (-not $SkipWeb) { Write-Host '  publish/web/wwwroot/index.html               <- serve over HTTP, not file://' }
