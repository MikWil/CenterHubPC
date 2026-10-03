# CenterHub quiet smoke test: starts the given build BESIDE the installed app, opens every page
# through UI Automation patterns (no mouse, no keyboard, no foreground needed) and screenshots the
# window with PrintWindow, which works even when it is covered. Safe while the PC is in use: the
# only visible effect is a second CenterHub window for about half a minute.
#
#   powershell -ExecutionPolicy Bypass -File tools\smoke-quiet.ps1 -Exe <path-to-CenterHubNew.exe> -OutDir <dir>
#
# Nothing is clicked, so no preset is applied and Voicemeeter is not touched. Exit code = failures.

param(
    [string]$Exe = "$PSScriptRoot\..\bin\x64\Debug\net10.0-windows10.0.22621.0\CenterHubNew.exe",
    [string]$OutDir = "$env:TEMP\centerhub-smoke-quiet",
    [string[]]$Screenshot = @('Metronome', 'Sound')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class QuietWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static void Capture(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    using (var bmp = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2);   // PW_RENDERFULLCONTENT: works for a covered window
      g.ReleaseHdc(dc);
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
  }
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$exePath = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$failures = 0
function Report([string]$name, [bool]$ok) {
    if (-not $ok) { $script:failures++ }
    "{0}  {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name
}

$pages = [ordered]@{
    'Monitoring' = 'Monitoring'; 'Standing' = 'Standing Timer'; 'Notes' = 'Notes'; 'Layouts' = 'Window Layouts'
    'Sound' = 'Sound'; 'Soundboard' = 'Soundboard'; 'Utilities' = 'Utilities'; 'Auto Clicker' = 'Auto Clicker'
    'Clipboard' = 'Clipboard History'; 'Randomizer' = 'Randomizer'; 'Metronome' = 'Metronome'
    'Network' = 'Network'; 'Hotkeys' = 'Global Hotkeys'
}

function Has-Heading($win, [string]$heading) {
    $tc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    $wr = $win.Current.BoundingRectangle
    foreach ($t in @($win.FindAll('Descendants', $tc))) {
        $r = $t.Current.BoundingRectangle
        if ($t.Current.Name -eq $heading -and $r.X -gt $wr.X + 230 -and $r.Y -lt $wr.Y + 160) { return $true }
    }
    return $false
}

$env:CENTERHUB_DEV_INSTANCE = '1'
$proc = Start-Process $exePath -PassThru
Remove-Item Env:\CENTERHUB_DEV_INSTANCE

try {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $null
    for ($i = 0; $i -lt 60 -and -not $win; $i++) { $win = $AE::RootElement.FindFirst('Children', $cond); Start-Sleep -Milliseconds 250 }
    Report 'App starts beside the installed one and shows its window' ($null -ne $win)
    if (-not $win) { throw 'no window' }
    Start-Sleep -Seconds 3

    # UI Automation can answer E_FAIL while the window is still building its tree; retry briefly.
    $version = $null
    for ($try = 0; $try -lt 10 -and -not $version; $try++) {
        try {
            $version = @($win.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text))) |
                Where-Object { $_.Current.Name -match '^v\d+\.\d+\.\d+$' } | Select-Object -First 1)
        } catch { Start-Sleep -Milliseconds 500 }
    }
    "      version shown: $($version.Current.Name)"

    foreach ($nav in $pages.Keys) {
        $item = $win.FindFirst('Descendants', (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::RadioButton)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $nav)))))
        $opened = $false
        if ($item) {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $sw = [Diagnostics.Stopwatch]::StartNew()
            while ($sw.ElapsedMilliseconds -lt 6000 -and -not $opened) { $opened = Has-Heading $win $pages[$nav]; if (-not $opened) { Start-Sleep -Milliseconds 200 } }
        }
        $proc.Refresh()
        Report "Page opens: $nav" ($opened -and -not $proc.HasExited)
        if ($proc.HasExited) { throw "the app exited while opening $nav" }
        if ($Screenshot -contains $nav) {
            Start-Sleep -Milliseconds 1500
            $file = Join-Path $OutDir ("page-" + ($nav -replace ' ', '') + ".png")
            [QuietWin]::Capture([IntPtr]$win.Current.NativeWindowHandle, $file)
            "      screenshot: $file"
        }
    }
}
catch { "ERROR: $($_.Exception.Message)"; $failures++ }
finally {
    # Close like a user would (WM_CLOSE) so view-models dispose and save; force only if it lingers.
    $proc.Refresh()
    if (-not $proc.HasExited) {
        if ($win) { [QuietWin]::PostMessage([IntPtr]$win.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        if (-not $proc.WaitForExit(8000)) { $proc.Kill(); "      (had to force-close the test instance)" }
    }
}

if ($failures -eq 0) { 'ALL PASS' } else { "$failures FAILED" }
exit $failures
