$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$target=Join-Path $root 'dist\CodexQuotaMonitor.exe'
if(-not (Test-Path -LiteralPath $target)){throw 'Build the executable first.'}
$desktop=[Environment]::GetFolderPath('DesktopDirectory')
$path=Join-Path $desktop 'Codex 额度监控.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($path)
if((Test-Path -LiteralPath $path) -and $shortcut.TargetPath -ne $target){throw 'A different shortcut exists at this path; it has been preserved.'}
$shortcut.TargetPath=$target
$shortcut.WorkingDirectory=Split-Path $target -Parent
$shortcut.IconLocation=(Join-Path $root 'assets\app-icon.ico')+',0'
$shortcut.Description='查看 Codex 订阅额度、Credits 和恢复时间'
$shortcut.Save()
Write-Output $path
