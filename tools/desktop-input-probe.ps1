# Starts DeskCiv on the desktop and samples whether its window and the desktop keep processing messages.
# With -ClickTest it sends one left click to a blank desktop spot, then restores the cursor and foreground window.
# It uses a fresh world and does not write the user's save.
param([int]$Seconds = 25, [string[]]$ExtraArgs = @(), [switch]$ClickTest)

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
Add-Type @"
using System; using System.Text; using System.Diagnostics; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Probe {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct CI { public int cb; public int flags; public IntPtr cursor; public P pt; }
  public delegate bool E(IntPtr h, IntPtr l);
  [DllImport("user32", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr p, IntPtr a, string c, string t);
  [DllImport("user32")] static extern bool EnumWindows(E e, IntPtr l);
  [DllImport("user32")] static extern bool EnumChildWindows(IntPtr h, E e, IntPtr l);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32")] public static extern bool IsHungAppWindow(IntPtr h);
  [DllImport("user32")] static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32")] static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32")] static extern IntPtr SendMessageTimeoutW(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
  [DllImport("user32")] static extern bool GetCursorInfo(ref CI c);
  [DllImport("user32")] static extern IntPtr LoadCursorW(IntPtr i, IntPtr n);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string N(IntPtr h) { if (h==IntPtr.Zero) return "none"; var s=new StringBuilder(64); GetClassNameW(h,s,64); return s.ToString(); }
  public static IntPtr Engine(uint pid) {
    IntPtr f=IntPtr.Zero;
    EnumWindows((top,l)=>{ uint p; GetWindowThreadProcessId(top,out p); if(p==pid&&N(top)=="Engine"){f=top;return false;}
      EnumChildWindows(top,(h,m)=>{uint q;GetWindowThreadProcessId(h,out q); if(q==pid&&N(h)=="Engine"){f=h;return false;} return true;},IntPtr.Zero); return f==IntPtr.Zero; },IntPtr.Zero);
    return f;
  }
  public static IntPtr DefView() {
    IntPtr f=FindWindowExW(FindWindowW("Progman",null),IntPtr.Zero,"SHELLDLL_DefView",null);
    if(f!=IntPtr.Zero) return f;
    EnumWindows((top,l)=>{ var v=FindWindowExW(top,IntPtr.Zero,"SHELLDLL_DefView",null); if(v!=IntPtr.Zero){f=v;return false;} return true; },IntPtr.Zero);
    return f;
  }
  [DllImport("user32")] static extern bool SetCursorPos(int x,int y);
  [DllImport("user32")] static extern bool GetCursorPos(out P p);
  [DllImport("user32")] static extern void mouse_event(uint f,int x,int y,uint d,UIntPtr e);
  [DllImport("user32")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr h);
  public static string Click(int[] xs,int[] ys) {
    int x=-1,y=-1; for(int i=0;i<xs.Length;i++) if(At(xs[i],ys[i])=="SysListView32"){x=xs[i];y=ys[i];break;}
    if(x<0) return "skipped: no uncovered blank spot";
    IntPtr before=GetForegroundWindow(); P old; GetCursorPos(out old);
    string beforeName=N(before);
    SetCursorPos(x,y); System.Threading.Thread.Sleep(120);
    var s=Stopwatch.StartNew(); mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero);
    string now=""; bool ok=false;
    while(s.ElapsedMilliseconds<3000){ now=N(GetForegroundWindow()); if(now=="Progman"||now=="WorkerW"||now=="SHELLDLL_DefView"||now=="SysListView32"){ok=true;break;} System.Threading.Thread.Sleep(10); }
    long ms=s.ElapsedMilliseconds; bool busy=Busy();
    SetCursorPos(old.X,old.Y); SetForegroundWindow(before);
    return "click at "+x+","+y+": desktop_activated="+ok+" ms="+ms+" foreground_after="+now+" was="+beforeName+" busy_cursor="+busy;
  }
  public static string Fg() { return N(GetForegroundWindow()); }
  public static string Parent(IntPtr h) { return N(GetParent(h)); }
  public static long Ping(IntPtr h) { var s=Stopwatch.StartNew(); IntPtr r; var ok=SendMessageTimeoutW(h,0,IntPtr.Zero,IntPtr.Zero,2,1500,out r); return ok==IntPtr.Zero?-1:s.ElapsedMilliseconds; }
  public static string At(int x,int y) { return N(WindowFromPoint(new P{X=x,Y=y})); }
  public static bool Busy() { var c=new CI(); c.cb=Marshal.SizeOf(c); GetCursorInfo(ref c); return c.cursor==LoadCursorW(IntPtr.Zero,(IntPtr)32514); }
}
"@

$a = @("--path", "`"$root\game`"", "--audio-driver", "Dummy", "--", "--silent", "--fresh", "--no-save", "--seed=1000", "--start-year=10", "--duration=$Seconds") + $ExtraArgs
$p = Start-Process -FilePath $godot -ArgumentList $a -PassThru
$start = Get-Date
$rows = [System.Collections.Generic.List[object]]::new()
$clickResult = "not requested"
$clicked = $false
while (-not $p.HasExited -and ((Get-Date) - $start).TotalSeconds -lt ($Seconds + 8)) {
    Start-Sleep -Milliseconds 1000
    if ($ClickTest -and -not $clicked -and ((Get-Date) - $start).TotalSeconds -ge 10) {
        $clicked = $true
        $clickResult = [Probe]::Click([int[]](1700, 1900, 2100, 2300), [int[]](1150, 1200, 1100, 1250))
    }
    $engine = [Probe]::Engine([uint32]$p.Id); $def = [Probe]::DefView()
    if ($engine -eq [IntPtr]::Zero) { continue }
    $list = [Probe]::FindWindowExW($def, [IntPtr]::Zero, "SysListView32", [NullString]::Value)
    $rows.Add([pscustomobject]@{
        t = [int]((Get-Date) - $start).TotalSeconds; engineParent = [Probe]::Parent($engine); defParent = [Probe]::Parent($def)
        engineHung = [Probe]::IsHungAppWindow($engine); enginePingMs = [Probe]::Ping($engine); iconsPingMs = [Probe]::Ping($list)
        hit = [Probe]::At(40, 40); busy = [Probe]::Busy(); fg = [Probe]::Fg()
    })
}
if (-not $p.WaitForExit(15000)) { Stop-Process -Id $p.Id -Force; "forced stop" }
$rows | Format-Table -AutoSize | Out-String -Width 160
$valid = @($rows | Where-Object { $_.t -ge 4 })
"summary: samples=$($valid.Count) hung=$(@($valid | Where-Object engineHung).Count) engine_timeouts=$(@($valid | Where-Object { $_.enginePingMs -lt 0 }).Count) icon_timeouts=$(@($valid | Where-Object { $_.iconsPingMs -lt 0 }).Count) max_engine_ms=$(($valid.enginePingMs | Measure-Object -Maximum).Maximum) max_icons_ms=$(($valid.iconsPingMs | Measure-Object -Maximum).Maximum) busy=$(@($valid | Where-Object busy).Count)"
"click: $clickResult"
"exit=$($p.ExitCode)"
