# Asks every running DeskCiv instance to save and quit through its tray window.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DeskCivStop {
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
  [DllImport("user32")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static int CloseAll() {
    int count = 0;
    for (IntPtr h = FindWindowExW(IntPtr.Zero, IntPtr.Zero, "DeskCivTray", "DeskCiv"); h != IntPtr.Zero; h = FindWindowExW(IntPtr.Zero, h, "DeskCivTray", "DeskCiv"))
      if (PostMessageW(h, 0x10, IntPtr.Zero, IntPtr.Zero)) count++;
    return count;
  }
}
"@
$processes = @(Get-Process -Name "Godot_v4.7.2-stable_mono_win64*" -ErrorAction SilentlyContinue)
$asked = [DeskCivStop]::CloseAll()
foreach ($process in $processes) { [void]$process.WaitForExit(10000) }
$left = @($processes | Where-Object { -not $_.HasExited }).Count
"DeskCiv: asked=$asked still_running=$left"
if ($left -gt 0) { exit 1 }
