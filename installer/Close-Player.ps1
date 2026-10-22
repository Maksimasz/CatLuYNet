param([Parameter(Mandatory)][string]$AppDirectory)

$ErrorActionPreference = 'Stop'
try {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class CatLuPlayerWindows {
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool Close(int processId) {
        bool found = false;
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId) return true;
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() == "CatLu YNet")
                found = PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero) || found;
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    $target = [IO.Path]::GetFullPath((Join-Path $AppDirectory 'YouTubeRadio.exe'))
    $players = @(Get-Process -Name YouTubeRadio -ErrorAction SilentlyContinue | Where-Object {
        [string]::Equals($_.Path, $target, [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($player in $players) {
        if ($player.HasExited) { continue }
        # Let FormClosing save the library; never kill a player that cannot save.
        # The main window may be hidden while the mini-player is visible.
        if (![CatLuPlayerWindows]::Close($player.Id)) { throw 'Player did not accept the close request.' }
    }
    foreach ($player in $players) {
        if (!$player.WaitForExit(15000)) { throw 'Player is still running.' }
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
