param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$downloads = Join-Path $artifacts 'downloads'
$dependencies = Join-Path $artifacts 'dependencies'
$runtimeTarget = Join-Path $artifacts "native/$Configuration/win-x64"
$runtime = Join-Path $artifacts ("native/$Configuration/staging-" + [Guid]::NewGuid().ToString('N'))
$build = Join-Path $artifacts 'native-build'
New-Item -ItemType Directory -Force -Path $downloads, $dependencies, $runtime | Out-Null
if (-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Platform -ne 'Win32NT') {
    throw 'The runtime package requires Windows x64.'
}
foreach ($dependency in (Get-Content (Join-Path $root 'Native/dependencies.json') -Raw | ConvertFrom-Json)) {
    $archive = Join-Path $downloads ($dependency.name + '.zip')
    if (-not (Test-Path -LiteralPath $archive)) {
        & curl.exe -fL --retry 3 --output $archive $dependency.url
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $($dependency.name)" }
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) {
        throw "Checksum mismatch: $archive. Remove this archive and retry."
    }
    $expanded = Join-Path $dependencies $dependency.sha256
    if (-not (Test-Path (Join-Path $expanded '.complete'))) {
        Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
        Set-Content -LiteralPath (Join-Path $expanded '.complete') -Value $dependency.sha256
    }
    $noticeRoot = Join-Path $runtime ('licenses/' + $dependency.name)
    New-Item -ItemType Directory -Force -Path $noticeRoot | Out-Null
    Get-ChildItem -LiteralPath $expanded -Recurse -File | Where-Object {
        $_.Name -match '(?i)license|copying|authors|notice' -or $_.Extension -eq '.txt'
    } | ForEach-Object {
        $relative = $_.FullName.Substring($expanded.Length + 1)
        $destination = Join-Path $noticeRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
    Get-ChildItem -LiteralPath $expanded -Filter '*.dll' -Recurse -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $runtime -Force
    }
    if ($dependency.name -eq 'FFmpeg') {
        $ffmpeg = (Get-ChildItem -LiteralPath $expanded -Directory | Select-Object -First 1).FullName
    }
}
Copy-Item -LiteralPath (Join-Path $root 'Native/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $runtime 'licenses/THIRD-PARTY-NOTICES.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'Native/dependencies.json') -Destination (Join-Path $runtime 'dependencies.json') -Force
& cmake -S (Join-Path $root 'Native') -B $build -G 'Visual Studio 17 2022' -A x64 "-DFFMPEG_DIR=$ffmpeg"
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
& cmake --build $build --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
& cmake --install $build --config $Configuration --prefix $runtime
if ($LASTEXITCODE -ne 0) { throw 'Native installation failed.' }
Set-Content -LiteralPath (Join-Path $runtime 'runtime.complete') -Value 'win-x64'
$artifactPrefix = [IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar
foreach ($candidate in @($runtime, $runtimeTarget)) {
    if (-not [IO.Path]::GetFullPath($candidate).StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Runtime installation must remain inside project artifacts.'
    }
    if ((Test-Path -LiteralPath $candidate) -and ((Get-Item -LiteralPath $candidate).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Runtime installation does not support directory links.'
    }
}
if (Test-Path -LiteralPath $runtimeTarget) { Remove-Item -LiteralPath $runtimeTarget -Recurse -Force }
Move-Item -LiteralPath $runtime -Destination $runtimeTarget
Write-Output "Native runtime: $runtimeTarget"
