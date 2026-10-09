# Dev helper: launches winPaint, runs canvas automation scripts through UI Automation, saves a snapshot, prints state.
# Example: .\tools\drive.ps1 -Script "drag 10 10 300 200; snapshot C:\temp\a.png"
param(
    [string[]]$Script = @(),
    [string]$Snapshot = "",
    [string]$Exe = "$PSScriptRoot\..\src\WinPaint.App\bin\Debug\net10.0-windows\winPaint.exe",
    [string]$Theme = "",
    [switch]$KeepOpen
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$settings = "$env:TEMP\winpaint-dev-settings.json"
if ($Theme) { "{ ""Theme"": ""$Theme"" }" | Set-Content -Encoding utf8 $settings } elseif (Test-Path $settings) { Remove-Item $settings }
$env:WINPAINT_SETTINGS = $settings
$p = Start-Process -FilePath $Exe -PassThru
$A = [System.Windows.Automation.AutomationElement]
$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 200
    $win = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))
}
Start-Sleep -Milliseconds 800
$canvas = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "Canvas")))
$vp = $canvas.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
function Find($id) { $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id))) }
foreach ($line in ($Script -join ";").Split(";")) {
    $s = $line.Trim(); if (-not $s) { continue }
    if ($s.StartsWith("invoke:")) { (Find $s.Substring(7)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    elseif ($s.StartsWith("type:")) { (Find "TextEditor").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($s.Substring(5).Replace("\n", "`n")) }
    elseif ($s.StartsWith("sleep:")) { Start-Sleep -Milliseconds ([int]$s.Substring(6)) }
    else {
        $seq = [int](($vp.Current.Value -split "\|")[0] -replace "seq=","")
        $vp.SetValue($s)
        for ($t = 0; $t -lt 100 -and [int](($vp.Current.Value -split "\|")[0] -replace "seq=","") -le $seq; $t++) { Start-Sleep -Milliseconds 50 }
    }
    Start-Sleep -Milliseconds 60
}
if ($Snapshot) { Start-Sleep -Milliseconds 300; $vp.SetValue("snapshot $Snapshot"); Start-Sleep -Milliseconds 800 }
Write-Output $vp.Current.Value
if (-not $KeepOpen) { Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force }
