param(
    [int]$IntervalMs = 500,
    [string]$OutputPath = (Join-Path $PWD ("steam-window-monitor-{0:yyyyMMdd-HHmmss}.jsonl" -f (Get-Date)))
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class SteamWindowNative {
    public IntPtr Hwnd; public int Pid; public string Title; public string ClassName;
    public bool Visible; public IntPtr Owner; public IntPtr Parent; public long Style; public long ExStyle;
    public int Left; public int Top; public int Right; public int Bottom;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    public static SteamWindowNative[] Snapshot(HashSet<int> pids) {
        var result = new List<SteamWindowNative>();
        EnumWindows((h, _) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (!pids.Contains((int)pid)) return true;
            var title = new StringBuilder(1024); GetWindowText(h, title, title.Capacity);
            var cls = new StringBuilder(512); GetClassName(h, cls, cls.Capacity);
            RECT r; GetWindowRect(h, out r);
            result.Add(new SteamWindowNative { Hwnd=h, Pid=(int)pid, Title=title.ToString(), ClassName=cls.ToString(),
                Visible=IsWindowVisible(h), Owner=GetWindow(h, 4), Parent=GetParent(h),
                Style=GetWindowLongPtr(h, -16).ToInt64(), ExStyle=GetWindowLongPtr(h, -20).ToInt64(),
                Left=r.Left, Top=r.Top, Right=r.Right, Bottom=r.Bottom });
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
}
'@

function Get-SteamProcessInfo {
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='steam.exe' OR Name='steamwebhelper.exe'" -ErrorAction SilentlyContinue)
    $map = @{}
    foreach ($p in $rows) {
        $map[[int]$p.ProcessId] = [ordered]@{ Pid=[int]$p.ProcessId; ProcessName=[string]$p.Name; FullPath=[string]$p.ExecutablePath; CommandLine=[string]$p.CommandLine }
    }
    return $map
}

function Convert-Window($w, $p) {
    [ordered]@{ Hwnd=('0x{0:X}' -f $w.Hwnd.ToInt64()); HwndValue=$w.Hwnd.ToInt64(); Pid=$w.Pid;
        ProcessName=$p.ProcessName; FullPath=$p.FullPath; CommandLine=$p.CommandLine; WindowTitle=$w.Title; ClassName=$w.ClassName;
        IsWindowVisible=$w.Visible; OwnerHwnd=('0x{0:X}' -f $w.Owner.ToInt64()); ParentHwnd=('0x{0:X}' -f $w.Parent.ToInt64());
        WindowStyle=$w.Style; ExtendedWindowStyle=$w.ExStyle;
        Rect=[ordered]@{ Left=$w.Left; Top=$w.Top; Right=$w.Right; Bottom=$w.Bottom } }
}

"# Steam window monitor started at $(Get-Date -Format o)" | Tee-Object -FilePath $OutputPath
"# Output: $OutputPath" | Write-Host
$previous = @{}
try {
    while ($true) {
        $now = Get-Date
        $info = Get-SteamProcessInfo
        $pids = [System.Collections.Generic.HashSet[int]]::new()
        foreach ($processId in $info.Keys) { [void]$pids.Add([int]$processId) }
        $current = @{}
        foreach ($w in [SteamWindowNative]::Snapshot($pids)) {
            $p = $info[$w.Pid]; if ($null -eq $p) { continue }
            $row = Convert-Window $w $p
            $key = [string]$row.HwndValue; $current[$key] = $row
            $state = ($row | ConvertTo-Json -Compress -Depth 5)
            if (-not $previous.ContainsKey($key)) {
                $event = [ordered]@{ Event='ADDED'; Timestamp=$now.ToString('o'); Window=$row }
                ($event | ConvertTo-Json -Compress -Depth 6) | Tee-Object -FilePath $OutputPath -Append
            } elseif ($previous[$key] -ne $state) {
                $event = [ordered]@{ Event='CHANGED'; Timestamp=$now.ToString('o'); Window=$row }
                ($event | ConvertTo-Json -Compress -Depth 6) | Tee-Object -FilePath $OutputPath -Append
            }
        }
        foreach ($key in @($previous.Keys)) {
            if (-not $current.ContainsKey($key)) {
                $event = [ordered]@{ Event='REMOVED'; Timestamp=$now.ToString('o'); Hwnd=$previous[$key].Hwnd; HwndValue=[int64]$key; LastWindow=$previous[$key] }
                ($event | ConvertTo-Json -Compress -Depth 6) | Tee-Object -FilePath $OutputPath -Append
            }
        }
        $previous = @{}; foreach ($key in $current.Keys) { $previous[$key] = ($current[$key] | ConvertTo-Json -Compress -Depth 5) }
        Start-Sleep -Milliseconds $IntervalMs
    }
} finally { "# Steam window monitor stopped at $(Get-Date -Format o)" | Tee-Object -FilePath $OutputPath -Append }
