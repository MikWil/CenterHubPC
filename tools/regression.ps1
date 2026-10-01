# CenterHub behavioural regression suite (drives the real app via UI Automation).
#
#   powershell -ExecutionPolicy Bypass -File tools\regression.ps1
#   powershell -ExecutionPolicy Bypass -File tools\regression.ps1 -Exe <path-to-CenterHubNew.exe>
#
# Covers: single instance + activation, every page opens (mouse click), keyboard/UIA
# selection navigates, no toast on opening Hotkeys, version format, Sound anti-echo UI,
# Notes autosave + switch persistence, minimize-to-tray, close exits.
# Your quick-notes.json is backed up before and restored after. Exit code = failures.

param(
    [string]$Exe = "$PSScriptRoot\..\bin\x64\Debug\net10.0-windows10.0.22621.0\CenterHubNew.exe"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class RegWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  public static int VisibleTopLevel(uint pid) {
    int n = 0;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0) n++; return true; }, IntPtr.Zero);
    return n;
  }
  public static void Click(int x, int y) {
    SetCursorPos(x - 6, y); System.Threading.Thread.Sleep(80);
    SetCursorPos(x, y); mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(150);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(40);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$exePath = (Resolve-Path $Exe).Path
$results = New-Object System.Collections.Generic.List[object]

function Check([string]$name, [scriptblock]$test) {
    try   { $ok = & $test; $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }) }) }
    catch { $results.Add([pscustomobject]@{ Test = $name; Result = "FAIL ($($_.Exception.Message))" }) }
}
function Procs { @(Get-Process CenterHubNew -ErrorAction SilentlyContinue) }
function Get-Win($processId) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    for ($i = 0; $i -lt 40; $i++) {
        $w = $AE::RootElement.FindFirst('Children', $c); if ($w) { return $w }; Start-Sleep -Milliseconds 250
    }
    return $null
}
function Texts($win) {
    $tc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    @($win.FindAll('Descendants', $tc))
}
function Has-Heading($win, [string]$heading) {
    $wr = $win.Current.BoundingRectangle
    foreach ($t in (Texts $win)) {
        $r = $t.Current.BoundingRectangle
        if ($t.Current.Name -eq $heading -and $r.X -gt $wr.X + 230 -and $r.Y -lt $wr.Y + 160) { return $true }
    }
    return $false
}
function Wait-Heading($win, [string]$heading, [int]$ms = 5000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { if (Has-Heading $win $heading) { return $true }; Start-Sleep -Milliseconds 200 }
    return $false
}
function Nav-Item($win, [string]$name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::RadioButton)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $win.FindFirst('Descendants', $c)
}
function Click-Element($win, $el) {
    [RegWin]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    $r = $el.Current.BoundingRectangle
    [RegWin]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}

$pages = [ordered]@{
    'Monitoring' = 'Monitoring'; 'Standing' = 'Standing Timer'; 'Notes' = 'Notes'; 'Layouts' = 'Window Layouts'
    'Sound' = 'Sound'; 'Soundboard' = 'Soundboard'; 'Utilities' = 'Utilities'; 'Auto Clicker' = 'Auto Clicker'
    'Clipboard' = 'Clipboard History'; 'Randomizer' = 'Randomizer'; 'Metronome' = 'Metronome'
    'Network' = 'Network'; 'Hotkeys' = 'Global Hotkeys'
}

# Protect the user's notes.
$notesFile = Join-Path $env:APPDATA 'CenterHub\quick-notes.json'
$notesBackup = "$notesFile.regression-backup"
if (Test-Path $notesFile) { Copy-Item $notesFile $notesBackup -Force }

try {
    Procs | Stop-Process -Force; Start-Sleep -Milliseconds 800
    $proc = Start-Process $exePath -PassThru
    $win = Get-Win $proc.Id
    Check 'App launches with a main window' { $null -ne $win }
    Start-Sleep -Seconds 2

    Check 'Title shows version as vX.Y.Z (no build segment)' {
        @(Texts $win | Where-Object { $_.Current.Name -match '^v\d+\.\d+\.\d+$' }).Count -ge 1
    }

    Start-Process $exePath | Out-Null; Start-Sleep -Seconds 3
    Check 'Second launch keeps a single instance' { (Procs).Count -eq 1 }

    foreach ($nav in $pages.Keys) {
        $item = Nav-Item $win $nav
        Click-Element $win $item
        $heading = $pages[$nav]
        Check "Page opens by click: $nav" { Wait-Heading $win $heading }
    }

    # Keyboard / accessibility selection used to highlight an item without changing page.
    foreach ($nav in @('Sound', 'Metronome', 'Monitoring')) {
        $item = Nav-Item $win $nav
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Check "Page opens by keyboard/UIA select: $nav" { Wait-Heading $win $pages[$nav] }
    }

    Click-Element $win (Nav-Item $win 'Hotkeys'); Wait-Heading $win 'Global Hotkeys' | Out-Null
    Start-Sleep -Milliseconds 1200
    Check 'Opening Hotkeys does not toast "Global hotkeys enabled"' {
        @(Texts $win | Where-Object { $_.Current.Name -like '*hotkeys enabled*' }).Count -eq 0
    }

    Click-Element $win (Nav-Item $win 'Sound'); Wait-Heading $win 'Sound' | Out-Null
    Start-Sleep -Milliseconds 1500
    Check 'Sound: Desktop & Discord is marked you-only' {
        @(Texts $win | Where-Object { $_.Current.Name -eq 'you only' }).Count -ge 1
    }
    Check 'Sound: "Others hear" never lists Desktop & Discord' {
        $all = Texts $win; $names = @($all | ForEach-Object { $_.Current.Name })
        $i = [Array]::IndexOf($names, 'Others hear')
        if ($i -lt 0) { return $false }
        $summary = ($names[($i + 1)..([Math]::Min($i + 4, $names.Count - 1))] | Sort-Object Length -Descending)[0]
        -not ($summary -like '*Desktop & Discord*')
    }

    # Notes: autosave + persistence across switching notes.
    Click-Element $win (Nav-Item $win 'Notes'); Wait-Heading $win 'Notes' | Out-Null
    $btnCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, '+ New Note')))
    $newBtn = $win.FindFirst('Descendants', $btnCond)
    Click-Element $win $newBtn; Start-Sleep -Milliseconds 800
    $editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Edit)
    $edits = @($win.FindAll('Descendants', $editCond) | Sort-Object { $_.Current.BoundingRectangle.Height })
    $titleBox = $edits | Where-Object { $_.Current.BoundingRectangle.X -gt $win.Current.BoundingRectangle.X + 500 } | Select-Object -First 1
    $bodyBox = $edits[-1]
    $stamp = "regression-body-$(Get-Random)"
    $titleBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Regression note')
    $bodyBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($stamp)
    Start-Sleep -Milliseconds 2000
    Check 'Notes: typing autosaves to disk' { (Test-Path $notesFile) -and ((Get-Content $notesFile -Raw) -like "*$stamp*") }

    Click-Element $win $newBtn; Start-Sleep -Milliseconds 800   # switch away to a fresh note
    $listText = Texts $win | Where-Object { $_.Current.Name -eq 'Regression note' } | Select-Object -First 1
    Click-Element $win $listText; Start-Sleep -Milliseconds 800
    $bodyNow = $bodyBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Check 'Notes: switching away and back keeps the text' { $bodyNow -eq $stamp }

    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    [RegWin]::ShowWindow($hwnd, 6) | Out-Null; Start-Sleep -Seconds 2   # SW_MINIMIZE
    Check 'Minimize hides to tray (no visible windows, still running)' {
        ([RegWin]::VisibleTopLevel([uint32]$proc.Id) -eq 0) -and -not $proc.HasExited
    }

    Start-Process $exePath | Out-Null; Start-Sleep -Seconds 3
    Check 'Launching again restores the hidden window' { [RegWin]::VisibleTopLevel([uint32]$proc.Id) -eq 1 }

    [RegWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_CLOSE (X / taskbar close)
    $proc.WaitForExit(8000) | Out-Null
    Check 'Closing the main window exits the app (no prompt)' { $proc.HasExited -and (Procs).Count -eq 0 }
}
finally {
    Procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    if (Test-Path $notesBackup) { Move-Item $notesBackup $notesFile -Force }
    elseif (Test-Path $notesFile) { Remove-Item $notesFile -Force }
}

$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
$failed = @($results | Where-Object { $_.Result -ne 'PASS' }).Count
Write-Output ("{0} passed, {1} failed" -f ($results.Count - $failed), $failed)
exit $failed
