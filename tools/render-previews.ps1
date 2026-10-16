param([string]$Executable=(Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\CodexQuotaMonitor.exe'))
$ErrorActionPreference='Stop'
# Render packaged XAML and the real taskbar painter with synthetic data only.
# Never start Program.Main, Monitor, the CLI, a sampler, or screen capture.
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Drawing,System.Windows.Forms
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Executable))
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'docs\images'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Load-View($name){$stream=$assembly.GetManifestResourceStream($name);try{return [Windows.Markup.XamlReader]::Load($stream)}finally{$stream.Dispose()}}
function Save-View($view,$width,$height,$name){
    $view.Width=$width;$view.Height=$height
    if($view.Content -is [Windows.Controls.Border] -and $null -eq $view.Content.Background){$view.Content.Background=$view.Background}
    $view.Content.Measure([Windows.Size]::new($width,$height))
    $view.Content.Arrange([Windows.Rect]::new(0,0,$width,$height));$view.Content.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($width*1.5),[int]($height*1.5),144,144,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($view.Content);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file=[IO.File]::Create((Join-Path $output $name));try{$encoder.Save($file)}finally{$file.Dispose()}
}
function Text($value,$size){$t=[Windows.Controls.TextBlock]::new();$t.Text=$value;$t.FontSize=$size;return $t}
$main=Load-View 'MainWindow.xaml'
$main.FindName('PlanTitle').Text='ChatGPT Pro'
$main.FindName('StatusText').Visibility='Collapsed'
$panel=$main.FindName('QuotaRows')
$label=Text '周额度剩余' 16;$label.FontWeight='SemiBold';$panel.Children.Add($label)|Out-Null
$percent=Text '82%' 44;$percent.FontWeight='SemiBold';$panel.Children.Add($percent)|Out-Null
$hint=Text 'Credits 仍有余额。' 12;$hint.Foreground=$main.Resources['Muted'];$panel.Children.Add($hint)|Out-Null
$track=[Windows.Controls.Grid]::new();$track.Height=5;$track.Margin='0,8,0,8';$track.Background=$main.Resources['Track']
$track.ColumnDefinitions.Add([Windows.Controls.ColumnDefinition]::new());$track.ColumnDefinitions[0].Width=[Windows.GridLength]::new(82,[Windows.GridUnitType]::Star)
$track.ColumnDefinitions.Add([Windows.Controls.ColumnDefinition]::new());$track.ColumnDefinitions[1].Width=[Windows.GridLength]::new(18,[Windows.GridUnitType]::Star)
$fill=[Windows.Controls.Border]::new();$fill.Background=$main.Resources['Accent'];$fill.CornerRadius=3;$track.Children.Add($fill)|Out-Null;$panel.Children.Add($track)|Out-Null
$reset=Text '下次恢复：10月23日 12:00' 12;$reset.Foreground=$main.Resources['Muted'];$panel.Children.Add($reset)|Out-Null
foreach($pair in @{CreditsText='1,000.00';ResetText='2 次';ExpiryText='11月30日 12:00';YesterdayTokenText='≈0.12亿';TotalTokenText='≈8.76亿';TokenNoteText='演示数据 · 非真实账户';UpdatedText='预览 · 不读取本机账户'}.GetEnumerator()){$main.FindName($pair.Key).Text=$pair.Value}
Save-View $main 390 548 'panel-demo.png'
$settings=Load-View 'SettingsWindow.xaml'
$settings.FindName('GeneralTab').Background=$settings.Resources['Accent'];$settings.FindName('GeneralTab').Foreground=$settings.Resources['AccentInk']
$settings.FindName('IntervalChoice').ItemsSource=@('1 分钟','5 分钟','15 分钟');$settings.FindName('IntervalChoice').SelectedIndex=1
$settings.FindName('ThemeChoice').ItemsSource=@('跟随系统','浅色','深色');$settings.FindName('ThemeChoice').SelectedIndex=1
$settings.FindName('ScaleChoice').Value=1;$settings.FindName('ScaleLabel').Text='100%'
$settings.FindName('WarningChoice').Text='20';$settings.FindName('CriticalChoice').Text='10';$settings.FindName('CliStatus').Text='演示预览 · 不读取本机路径'
foreach($name in @('Taskbar','TaskbarLeft','Dock','Notify')){$settings.FindName($name+'Choice').IsChecked=$true}
Save-View $settings 460 700 'settings-demo.png'
$settings.FindName('SettingsScroll').Visibility='Collapsed';$settings.FindName('TaskbarSettingsScroll').Visibility='Visible'
$settings.FindName('TaskbarTab').Background=$settings.Resources['Accent'];$settings.FindName('TaskbarTab').Foreground=$settings.Resources['AccentInk']
$settings.FindName('GeneralTab').Background=$settings.Resources['Card'];$settings.FindName('GeneralTab').Foreground=$settings.Resources['Ink']
foreach($name in @('Quota','Credits','Resets','YesterdayTokens','TotalTokens','Upload','Download','Cpu','Memory','Gpu','Disk')){$settings.FindName('Show'+$name+'Choice').IsChecked=$true}
$settings.FindName('TaskbarPreview').Text='已选择 11 项 · 预计宽度 598 px'
Save-View $settings 460 700 'taskbar-settings-demo.png'
$snapshot=[CodexQuotaMonitor.Snapshot]::Parse([CodexQuotaMonitor.Json]::Parse('{"rateLimits":{"primary":{"usedPercent":18,"windowDurationMins":10080},"credits":{"balance":"1000"}},"rateLimitResetCredits":{"availableCount":2,"credits":[]}}'),'pro')
$snapshot.ResetExpirations.Add([DateTimeOffset]::Parse('2026-11-30T04:00:00Z').ToUnixTimeSeconds())
$data=[CodexQuotaMonitor.TaskbarContent]::Create($snapshot,$false,20);$data.YesterdayTokens='昨日 ≈0.12亿';$data.TotalTokens='累计 ≈8.76亿'
$usage=[CodexQuotaMonitor.SystemUsage]::new();$usage.Upload=13107;$usage.Download=3491758;$usage.Cpu=28;$usage.Memory=42;$usage.Gpu=18;$usage.Disk=3
$selection=[CodexQuotaMonitor.TaskbarSelection]::From([CodexQuotaMonitor.Settings]::new())
$columns=$assembly.GetType('CodexQuotaMonitor.TaskbarColumns').GetMethod('Create').Invoke($null,@($data,$usage,$selection))
$stripType=$assembly.GetType('CodexQuotaMonitor.TaskbarHost+Strip')
$menu=[Windows.Forms.ContextMenuStrip]::new()
$strip=[Activator]::CreateInstance($stripType,[Reflection.BindingFlags]'Instance,Public,NonPublic',$null,[object[]]@(192,[Action]{},$menu),$null)
try{
    $strip.ClientSize=[Drawing.Size]::new(1196,80)
    $stripType.GetMethod('UpdateText').Invoke($strip,@($columns,'Synthetic demonstration data'))|Out-Null
    $bitmap=[Drawing.Bitmap]::new(1196,80)
    try{$strip.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,1196,80));$bitmap.Save((Join-Path $output 'taskbar-demo.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
}finally{$strip.Dispose();$menu.Dispose();$main.Close();$settings.Close()}
Write-Output 'Four synthetic previews rendered; no account, system sampler or screen capture was used.'
