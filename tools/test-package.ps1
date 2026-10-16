param([string]$ReleaseDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\releases'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$release=[IO.Path]::GetFullPath($ReleaseDirectory)
$package=Get-Content -LiteralPath (Join-Path $release 'validation.json') -Raw | ConvertFrom-Json
$zipPath=Join-Path $release $package.Package
$test=Join-Path (Join-Path $root 'artifacts') ('package-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $test | Out-Null
$extract=Join-Path $test 'extracted package'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath,$extract)
$results=New-Object 'System.Collections.Generic.List[string]'
function Check($name,$ok){if(-not $ok){throw "FAILED: $name"};$results.Add($name)}
$config=Join-Path $env:LOCALAPPDATA 'CodexQuotaMonitor\settings.json'
$before=if(Test-Path -LiteralPath $config){(Get-FileHash -LiteralPath $config).Hash}else{$null}
$registry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor'
$regBefore=if(Test-Path -LiteralPath $registry){Get-ItemProperty -LiteralPath $registry|ConvertTo-Json -Compress}else{$null}
$files=@(Get-ChildItem -LiteralPath $extract -File)
Check 'zip-contains-only-eight-distributable-files' ($files.Count -eq 8)
foreach($line in Get-Content -LiteralPath (Join-Path $extract 'SHA256SUMS.txt')){
    Check 'payload-checksum' ((Get-FileHash -LiteralPath (Join-Path $extract $line.Substring(66))).Hash -eq $line.Substring(0,64))
}
$destination=Join-Path $test 'installed copy'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $extract 'Install.ps1') -InstallDirectory $destination -NoShortcuts -NoRegistration
Check 'install-exit-success' ($LASTEXITCODE -eq 0)
Check 'installed-executable-matches-package' ((Get-FileHash -LiteralPath (Join-Path $destination 'CodexQuotaMonitor.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $extract 'CodexQuotaMonitor.exe')).Hash)
Check 'ownership-marker-created' (Test-Path -LiteralPath (Join-Path $destination 'CodexQuotaMonitor.install.json'))
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $extract 'Install.ps1') -InstallDirectory $destination -NoShortcuts -NoRegistration
Check 'upgrade-exit-success' ($LASTEXITCODE -eq 0)
Check 'upgrade-preserves-previous-executable' (Test-Path -LiteralPath (Join-Path $destination 'CodexQuotaMonitor.exe.previous'))
$unrelated=Join-Path $destination 'user-note.txt'
[IO.File]::WriteAllText($unrelated,'Keep this user file')
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $destination 'Uninstall.ps1') -NoShortcuts -NoRegistration
Check 'uninstall-exit-success' ($LASTEXITCODE -eq 0)
Check 'uninstall-removes-only-owned-program' (-not (Test-Path -LiteralPath (Join-Path $destination 'CodexQuotaMonitor.exe')))
Check 'uninstall-preserves-unrelated-file' ((Get-Content -LiteralPath $unrelated -Raw) -eq 'Keep this user file')
Check 'uninstall-removes-its-own-script' (-not (Test-Path -LiteralPath (Join-Path $destination 'Uninstall.ps1')))
$foreign=Join-Path $test 'foreign directory'
New-Item -ItemType Directory -Path $foreign | Out-Null
$rejected=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $extract 'Install.ps1')+'"'),'-InstallDirectory',('"'+$foreign+'"'),'-NoShortcuts','-NoRegistration') -RedirectStandardError (Join-Path $test 'expected-ownership-error.txt') -WindowStyle Hidden -Wait -PassThru
Check 'refuses-unowned-existing-directory' ($rejected.ExitCode -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $foreign 'CodexQuotaMonitor.exe')))
$payload=Join-Path $extract 'CodexQuotaMonitor.exe'
$bytes=[IO.File]::ReadAllBytes($payload);$bytes[$bytes.Length-1]=$bytes[$bytes.Length-1] -bxor 1;[IO.File]::WriteAllBytes($payload,$bytes)
$badTarget=Join-Path $test 'tampered target'
$rejected=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $extract 'Install.ps1')+'"'),'-InstallDirectory',('"'+$badTarget+'"'),'-NoShortcuts','-NoRegistration') -RedirectStandardError (Join-Path $test 'expected-checksum-error.txt') -WindowStyle Hidden -Wait -PassThru
Check 'rejects-tampered-payload-before-writing' ($rejected.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $badTarget))
$after=if(Test-Path -LiteralPath $config){(Get-FileHash -LiteralPath $config).Hash}else{$null}
Check 'real-user-settings-unchanged' ($before -eq $after)
$regAfter=if(Test-Path -LiteralPath $registry){Get-ItemProperty -LiteralPath $registry|ConvertTo-Json -Compress}else{$null}
Check 'real-uninstall-registration-unchanged' ($regBefore -eq $regAfter)
$validation=Get-Content -LiteralPath (Join-Path $release 'validation.json') -Raw | ConvertFrom-Json
$validation|Add-Member -Force -NotePropertyName PackagingDate -NotePropertyValue '2026-10-16'
$validation|Add-Member -Force -NotePropertyName InstallationChecks -NotePropertyValue $results.ToArray()
$validation|Add-Member -Force -NotePropertyName InstallationPassed -NotePropertyValue $results.Count
$validation|Add-Member -Force -NotePropertyName InstallationFailed -NotePropertyValue 0
$validation|Add-Member -Force -NotePropertyName InstallationScope -NotePropertyValue 'Isolated directory, shortcut and HKCU integration disabled; those integrations were reviewed but not exercised against the real user profile'
$validation|Add-Member -Force -NotePropertyName HistoricalUI -NotePropertyValue '57/57 on 2026-10-14; not rerun during distribution-only preparation'
$validation|ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $release 'validation.json') -Encoding UTF8
@{Passed=$results.Count;Failed=0;EvidenceDirectory=$test}|ConvertTo-Json
