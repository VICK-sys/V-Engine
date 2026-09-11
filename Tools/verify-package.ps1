param([string]$Version = '0.1.0', [switch]$Graphics)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('vengine-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'RuntimeProbe/*') -Destination $scratch
$project = Join-Path $scratch 'RuntimeProbe.csproj'
$dotnet = (Get-Command dotnet).Source
$cache = Join-Path $scratch 'packages'
$source = [Security.SecurityElement]::Escape((Join-Path $root 'artifacts/packages'))
$config = Join-Path $scratch 'NuGet.Config'
Set-Content -LiteralPath $config -Value "<configuration><packageSources><clear/><add key=`"local`" value=`"$source`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>"
& $dotnet restore $project --configfile $config "-p:EngineVersion=$Version" "-p:RestorePackagesPath=$cache" -p:SelfContained=true -p:PublishSingleFile=true -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed.' }
foreach ($singleFile in @($false, $true)) {
    $output = Join-Path $scratch $(if ($singleFile) { 'single' } else { 'folder' })
    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore --output $output "-p:EngineVersion=$Version" "-p:RestorePackagesPath=$cache" "-p:PublishSingleFile=$($singleFile.ToString().ToLowerInvariant())" -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer publish failed.' }
    if (-not (Test-Path (Join-Path $output 'licenses/THIRD-PARTY-NOTICES.txt'))) { throw 'Published dependency notices are missing.' }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $output 'RuntimeProbe.exe'
    $start.WorkingDirectory = $output
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory
    $start.EnvironmentVariables['SDL_AUDIODRIVER'] = 'dummy'
    if ($Graphics) { $start.Arguments = '--graphics' }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Published runtime probe timed out.' }
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Published runtime probe failed: $($process.ExitCode)" }
}
Write-Output "Package verification passed: $scratch"
