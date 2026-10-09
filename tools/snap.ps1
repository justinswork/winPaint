# Dev helper: launches winPaint (isolated settings), waits, and captures the main window to a PNG.
param(
    [string]$Out = "$env:TEMP\winpaint-snap.png",
    [int]$WaitMs = 2500,
    [string]$Exe = "$PSScriptRoot\..\src\WinPaint.App\bin\Debug\net10.0-windows\winPaint.exe",
    [string[]]$AppArgs = @(),
    [switch]$KeepOpen
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Snap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Win32Snap]::SetProcessDPIAware() | Out-Null
$env:WINPAINT_SETTINGS = "$env:TEMP\winpaint-dev-settings.json"
$p = if ($AppArgs.Count -gt 0) { Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru } else { Start-Process -FilePath $Exe -PassThru }
Start-Sleep -Milliseconds $WaitMs
$p.Refresh()
$h = $p.MainWindowHandle
[Win32Snap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object Win32Snap+RECT
[Win32Snap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hgt))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if (-not $KeepOpen) { Stop-Process -Id $p.Id -Force }
Write-Output "$Out ($w x $hgt) pid=$($p.Id) exited=$($p.HasExited)"
