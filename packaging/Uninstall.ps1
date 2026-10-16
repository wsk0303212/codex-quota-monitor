param([switch]$NoShortcuts,[switch]$NoRegistration)
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$marker=Join-Path $target 'CodexQuotaMonitor.install.json'
if (-not (Test-Path -LiteralPath $marker)) {throw 'This is not an installed copy. For portable use, delete the extracted files manually.'}
$owner=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if ($owner.Product -ne 'CodexQuotaMonitor' -or $owner.Directory -ne $target) {throw 'Installation ownership does not match; nothing was removed.'}
$targetExe=Join-Path $target 'CodexQuotaMonitor.exe'
if (@(Get-CimInstance Win32_Process -Filter "Name='CodexQuotaMonitor.exe'" | Where-Object ExecutablePath -eq $targetExe).Count) {throw 'Exit this monitor from its tray menu before uninstalling.'}
if (-not $NoShortcuts) {
    $shell=New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))) {
        $path=Join-Path $folder 'Codex Quota Monitor.lnk'
        if ((Test-Path -LiteralPath $path) -and $shell.CreateShortcut($path).TargetPath -eq $targetExe) {Remove-Item -LiteralPath $path}
    }
}
if (-not $NoRegistration) {
    $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor'
    if ((Test-Path -LiteralPath $key) -and (Get-ItemProperty -LiteralPath $key).InstallLocation -eq $target) {Remove-Item -LiteralPath $key}
    $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path -LiteralPath $runKey) {
        $startup=(Get-ItemProperty -LiteralPath $runKey -Name CodexQuotaMonitor -ErrorAction SilentlyContinue).CodexQuotaMonitor
        if ($startup -eq ('"'+$targetExe+'" --tray')) {Remove-ItemProperty -LiteralPath $runKey -Name CodexQuotaMonitor}
    }
}
# Delete only this installer's known files. Preserve unrelated files and all user settings.
foreach ($name in @('CodexQuotaMonitor.exe','CodexQuotaMonitor.exe.previous','Install.cmd','Install.ps1','Uninstall.cmd','README.txt','LICENSE.txt','SHA256SUMS.txt','CodexQuotaMonitor.install.json','Uninstall.ps1')) {
    $path=Join-Path $target $name
    if (Test-Path -LiteralPath $path) {Remove-Item -LiteralPath $path}
}
if (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) {Remove-Item -LiteralPath $target}
Write-Output 'Uninstalled. User settings and Codex login have been preserved.'
