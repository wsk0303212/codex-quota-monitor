param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {throw 'Windows .NET Framework compiler is missing.'}
$wpf = Join-Path $framework 'WPF'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','Microsoft.CSharp.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object {$_.FullName}
$icon = Join-Path $root 'assets\app-icon.ico'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/resource:'+(Join-Path $root 'src\MainWindow.xaml')+',MainWindow.xaml') ('/resource:'+(Join-Path $root 'src\SettingsWindow.xaml')+',SettingsWindow.xaml') ('/resource:'+$icon+',AppIcon.ico') ('/win32icon:'+$icon) ('/win32manifest:'+(Join-Path $root 'src\app.manifest')) ('/out:'+(Join-Path $output 'CodexQuotaMonitor.exe')) $references $sources
if ($LASTEXITCODE -ne 0) {throw 'Build failed'}
Write-Output (Join-Path $output 'CodexQuotaMonitor.exe')
