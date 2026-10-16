param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\releases'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$build=Join-Path $output 'build'
& (Join-Path $root 'build.ps1') -OutputDirectory $build | Out-Null
$exe=Join-Path $build 'CodexQuotaMonitor.exe'
$process=Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
$process.WaitForExit()
$tests=Get-Content -LiteralPath (Join-Path $build 'test-results.json') -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or $tests.failed -ne 0) {throw 'Data validation failed; no release package was created.'}
$version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion -replace '\.0$',''
$name='CodexQuotaMonitor-'+$version+'-win-x64'
$stage=Join-Path $output $name
$zipPath=Join-Path $output ($name+'.zip')
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zipPath)) {throw 'The versioned package already exists. Keep the previous result or choose another output directory.'}
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'CodexQuotaMonitor.exe')
foreach ($file in @('Install.cmd','Install.ps1','Uninstall.cmd','Uninstall.ps1')) {Copy-Item -LiteralPath (Join-Path $root ('packaging\'+$file)) -Destination (Join-Path $stage $file)}
$readme=@'
Codex Quota Monitor
Windows x64 / .NET Framework 4.8 or later

PORTABLE: extract this entire ZIP, then run CodexQuotaMonitor.exe.
INSTALL: double-click Install.cmd. No administrator privileges are needed.
The default directory is %LOCALAPPDATA%\Programs\CodexQuotaMonitor.
Desktop and Start menu shortcuts and a per-user uninstall entry are created.
UNINSTALL: exit this monitor from its tray menu, then run Uninstall.cmd in the installed directory.
Settings in %LOCALAPPDATA%\CodexQuotaMonitor and Codex login are preserved.
To update an installed copy, exit that copy first and run Install.cmd from the newly extracted package.

Requires a separately installed Codex CLI signed into the intended ChatGPT account.
If automatic CLI detection fails, select codex.exe in Settings.
Do not supply an API key to this application.

The ZIP and executable are not code-signed. SHA256SUMS.txt verifies payload integrity;
the separate release SHA256SUMS.txt verifies the downloaded ZIP.
This is a private community utility, not an official OpenAI application.

Chinese installation guide, source and validation:
https://github.com/wsk0303212/codex-quota-monitor
'@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'),$readme,[Text.UTF8Encoding]::new($true))
$lines=@(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())+'  '+$_.Name})
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zipPath,[IO.Compression.CompressionLevel]::Optimal,$false)
$sums=@(((Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($zipPath))
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$sums,[Text.UTF8Encoding]::new($false))
@{Version=$version;Platform='win-x64';DataPassed=$tests.passed;DataFailed=$tests.failed;Package=[IO.Path]::GetFileName($zipPath);PackageSHA256=(Get-FileHash -LiteralPath $zipPath).Hash;ExecutableSHA256=(Get-FileHash -LiteralPath $exe).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding UTF8
Write-Output $zipPath
