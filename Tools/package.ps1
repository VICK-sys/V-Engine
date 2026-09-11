param([string]$Version = '0.1.0', [switch]$SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Use a semantic package version.' }
if (-not $SkipNativeBuild) { & (Join-Path $PSScriptRoot 'build-native.ps1') }
$packages = Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$previousRequireNative = $env:VENGINE_REQUIRE_NATIVE
try {
    $env:VENGINE_REQUIRE_NATIVE = '1'
    & dotnet test (Join-Path $root 'VEngine.sln') -c Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Native integration tests failed.' }
}
finally { $env:VENGINE_REQUIRE_NATIVE = $previousRequireNative }
& dotnet pack (Join-Path $root 'VEngine.Engine/VEngine.Engine.csproj') -c Release --no-build --output $packages "-p:PackageVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
$runtime = Join-Path $root 'artifacts/native/Release/win-x64'
$archive = Join-Path $packages "VEngine.Native.win-x64.$Version.zip"
Compress-Archive -Path (Join-Path $runtime '*') -DestinationPath $archive -Force
Get-FileHash -LiteralPath (Join-Path $packages "VEngine.Engine.$Version.nupkg"), $archive -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) } |
    Set-Content -LiteralPath (Join-Path $packages "SHA256SUMS.$Version.txt")
Write-Output "Packages: $packages"
