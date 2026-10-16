$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TaskbarInspect {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string name,string title);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out Rect rect);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 public struct Rect {public int Left,Top,Right,Bottom;}
}
'@
$previousDpiContext=[TaskbarInspect]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
$handle=[TaskbarInspect]::FindWindow('Shell_TrayWnd',$null)
$rect=New-Object TaskbarInspect+Rect
[TaskbarInspect]::GetWindowRect($handle,[ref]$rect)|Out-Null
$root=[System.Windows.Automation.AutomationElement]::FromHandle($handle)
$items=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$data=@(foreach($item in $items){$current=$item.Current;$r=$current.BoundingRectangle;if(-not $r.IsEmpty -and $r.Width -gt 0){[pscustomobject]@{Name=$current.Name;Class=$current.ClassName;Type=$current.ControlType.ProgrammaticName;Left=$r.Left;Top=$r.Top;Width=$r.Width;Height=$r.Height}}})
$data|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $PSScriptRoot '..\evidence\taskbar-inventory.json') -Encoding UTF8
$data|Format-Table -AutoSize|Out-String -Width 240|Write-Output
$bmp=New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left,$rect.Top,0,0,$bmp.Size)
$bmp.Save((Join-Path $PSScriptRoot '..\evidence\taskbar-running.png'))
$g.Dispose();$bmp.Dispose()
$slot=$data | Where-Object Name -eq 'CodexQuotaTaskbar' | Select-Object -First 1
if($slot){
    $strip=New-Object System.Drawing.Bitmap ([int]$slot.Width),([int]$slot.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($strip)
    $graphics.CopyFromScreen([int]$slot.Left,[int]$slot.Top,0,0,$strip.Size)
    $strip.Save((Join-Path $PSScriptRoot '..\evidence\taskbar-current.png'))
    $graphics.Dispose();$strip.Dispose()
}
} finally {
    if($previousDpiContext -ne [IntPtr]::Zero){[TaskbarInspect]::SetThreadDpiAwarenessContext($previousDpiContext)|Out-Null}
}
