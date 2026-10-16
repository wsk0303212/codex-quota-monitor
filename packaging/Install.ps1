param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\CodexQuotaMonitor'),
    [switch]$NoShortcuts,
    [switch]$NoRegistration
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$marker = 'CodexQuotaMonitor.install.json'
$payload = @('CodexQuotaMonitor.exe','Install.cmd','Install.ps1','Uninstall.cmd','Uninstall.ps1','README.txt','LICENSE.txt','SHA256SUMS.txt')
if ($target -eq $source) {throw 'Run the executable directly for portable use; choose a separate installation directory.'}
if (-not (Test-Path -LiteralPath (Join-Path $source 'CodexQuotaMonitor.exe'))) {throw 'The application payload is missing. Extract the entire ZIP first.'}
if (Test-Path -LiteralPath $target) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $marker))) {throw 'The destination already exists without an ownership marker. It has been preserved.'}
    $owner = Get-Content -LiteralPath (Join-Path $target $marker) -Raw | ConvertFrom-Json
    if ($owner.Product -ne 'CodexQuotaMonitor' -or $owner.Directory -ne $target) {throw 'The destination ownership marker does not match.'}
}
$targetExe = Join-Path $target 'CodexQuotaMonitor.exe'
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor'
if (-not $NoRegistration -and (Test-Path -LiteralPath $key)) {
    $existing=Get-ItemProperty -LiteralPath $key
    if ($existing.InstallLocation -ne $target) {throw 'A different installation is registered; its files and entry were preserved.'}
}
$running = @(Get-CimInstance Win32_Process -Filter "Name='CodexQuotaMonitor.exe'" | Where-Object ExecutablePath -eq $targetExe)
if ($running.Count) {throw 'Exit the installed monitor from its tray menu before installing or updating.'}
$expected = Get-Content -LiteralPath (Join-Path $source 'SHA256SUMS.txt') | Where-Object {$_ -match '^[0-9A-Fa-f]{64}  CodexQuotaMonitor\.exe$'}
if (@($expected).Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $source 'CodexQuotaMonitor.exe')).Hash -ne $expected.Substring(0,64)) {throw 'Application checksum validation failed.'}
foreach ($name in $payload) {if (-not (Test-Path -LiteralPath (Join-Path $source $name))) {throw "Package file is missing: $name"}}
New-Item -ItemType Directory -Path $target -Force | Out-Null
if (Test-Path -LiteralPath $targetExe) {Copy-Item -LiteralPath $targetExe -Destination ($targetExe+'.previous') -Force}
foreach ($name in $payload) {Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force}
@{Product='CodexQuotaMonitor';Directory=$target;Version=(Get-Item -LiteralPath $targetExe).VersionInfo.FileVersion} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target $marker) -Encoding UTF8
if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))) {
        $shortcutPath = Join-Path $folder 'Codex Quota Monitor.lnk'
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $targetExe) {Write-Warning "An unrelated shortcut was preserved: $shortcutPath";continue}
        $shortcut.TargetPath=$targetExe;$shortcut.WorkingDirectory=$target;$shortcut.IconLocation=$targetExe+',0';$shortcut.Description='Codex quota and system monitor';$shortcut.Save()
    }
}
if (-not $NoRegistration) {
    New-Item -Path $key -Force | Out-Null
    $properties=@{DisplayName='Codex Quota Monitor';DisplayVersion=(Get-Item -LiteralPath $targetExe).VersionInfo.FileVersion;Publisher='Peter';InstallLocation=$target;DisplayIcon=$targetExe;UninstallString=('powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $target 'Uninstall.ps1')+'"')}
    foreach ($name in $properties.Keys) {New-ItemProperty -LiteralPath $key -Name $name -Value $properties[$name] -PropertyType String -Force | Out-Null}
    New-ItemProperty -LiteralPath $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
}
Write-Output "Installed: $targetExe"
Write-Output 'Settings and Codex login were preserved. Open the application from the shortcut when ready.'
