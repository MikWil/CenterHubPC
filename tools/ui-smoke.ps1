# CenterHub UI smoke test.
# Launches the app, clicks through every sidebar page via Windows UI Automation,
# screenshots each page, and fails if the process dies along the way.
#
#   pwsh -File tools\ui-smoke.ps1                       # Debug build, default out dir
#   pwsh -File tools\ui-smoke.ps1 -Exe <path> -OutDir <dir>
#
# Exit code 0 = every page opened and the app stayed alive.

param(
    [string]$Exe = "$PSScriptRoot\..\bin\x64\Debug\net10.0-windows10.0.22621.0\CenterHubNew.exe",
    [string]$OutDir = "$env:TEMP\centerhub-smoke",
    [int]$SettleMs = 1500
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SmokeWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  public static void Click(int x, int y) {
    // Move like a person would: approach, settle, then press. Teleport-and-click
    // races the hover/tooltip state from the previous item and drops the click.
    SetCursorPos(x - 6, y);
    System.Threading.Thread.Sleep(80);
    SetCursorPos(x, y);
    mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero); // MOVE
    System.Threading.Thread.Sleep(150);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
    System.Threading.Thread.Sleep(40);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // LEFTUP
  }
}
"@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-ChildItem $OutDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

Get-Process CenterHubNew -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
$proc = Start-Process -FilePath (Resolve-Path $Exe) -PassThru

# Wait for the main window to appear in the automation tree.
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $null
for ($i = 0; $i -lt 40 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 250
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}
if (-not $win) { Write-Error "Main window never appeared"; exit 1 }

function Save-Shot([string]$name) {
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    [SmokeWin]::ShowWindow($hwnd, 9) | Out-Null      # SW_RESTORE
    [SmokeWin]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 250
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $file = Join-Path $OutDir ("{0}.png" -f ($name -replace '[^A-Za-z0-9]', '_'))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return $file
}

# Every sidebar entry is a RadioButton.
$rbCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
$tabs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rbCond)
$names = @($tabs | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
Write-Output "Found $($names.Count) pages: $($names -join ', ')"

$failed = @()
foreach ($name in $names) {
    $nameCond = New-Object System.Windows.Automation.AndCondition($rbCond,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    $tab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    if (-not $tab) { $failed += "$name (not found)"; continue }
    # Real mouse click (UIA Select() only flips IsChecked and skips the Command).
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    [SmokeWin]::SetForegroundWindow($hwnd) | Out-Null
    $r = $tab.Current.BoundingRectangle
    [SmokeWin]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds $SettleMs
    if ($proc.HasExited) { $failed += "$name (APP CRASHED, exit $($proc.ExitCode))"; break }
    $file = Save-Shot $name
    Write-Output "OK   $name -> $file"
}

$alive = -not $proc.HasExited
Write-Output ""
Write-Output ("Process alive at end: {0}" -f $alive)
if ($failed.Count -gt 0) { Write-Output "FAILED: $($failed -join '; ')"; exit 1 }
if (-not $alive) { exit 1 }
exit 0
