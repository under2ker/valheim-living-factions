# Keeps the test game out of the way: puts its window behind every other window without activating it,
# and gives the keyboard back to the window that had it before the game started.
# Started in the background by tools/lftest.sh and tools/lfmp.sh before the game is launched; it ends when the
# game has been seen and is gone again, or after -Seconds.
#   -Offscreen  also moves the window far off the screen (nothing of it shows on the desktop; the game goes on drawing)
# Switched off with LF_QUIET=0 in the environment of the test scripts.
param(
    [string]$Process = "valheim",
    [int]$Seconds = 1200,
    [switch]$Offscreen
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LfWin
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@

$bottom = [IntPtr]1
$noSize = 0x1; $noMove = 0x2; $noActivate = 0x10
$previous = [LfWin]::GetForegroundWindow()
$deadline = (Get-Date).AddSeconds($Seconds)
$seen = $false
$tidied = @{}

while ((Get-Date) -lt $deadline) {
    $games = @(Get-Process -Name $Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero })
    if ($games.Count -gt 0) {
        $seen = $true
        foreach ($g in $games) {
            $h = $g.MainWindowHandle
            if ($Offscreen -and -not $tidied.ContainsKey([string]$h)) {
                [void][LfWin]::SetWindowPos($h, $bottom, -30000, -30000, 0, 0, ($noSize -bor $noActivate))
                $tidied[[string]$h] = $true
            }
            [void][LfWin]::SetWindowPos($h, $bottom, 0, 0, 0, 0, ($noSize -bor $noMove -bor $noActivate))
            if ([LfWin]::GetForegroundWindow() -eq $h -and $previous -ne [IntPtr]::Zero -and [LfWin]::IsWindow($previous) -and $previous -ne $h) {
                [void][LfWin]::SetForegroundWindow($previous)
            }
        }
        Start-Sleep -Milliseconds 250
    }
    else {
        if ($seen -and -not (Get-Process -Name $Process -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }
}
