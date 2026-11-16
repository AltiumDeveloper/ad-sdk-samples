<#
.SYNOPSIS
  Lists the visible top-level windows of the running Altium Designer (dialogs included) and optionally saves a
  PNG of each (PrintWindow: renders the window itself, never what covers it). For diagnosing a stalled AD.

.EXAMPLE
  .\tools\ad-windows.ps1
  .\tools\ad-windows.ps1 -Out .\diag
#>
param([string] $Out = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Native -Name Win2 -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr hWnd, System.IntPtr lParam);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr lParam);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hWnd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr hWnd, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(System.IntPtr hWnd, System.Text.StringBuilder s, int n);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdc, uint flags);
'@

$ids = @(Get-Process X2 -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
if (-not $ids) { Write-Host 'Altium Designer is not running.'; exit 0 }
$found = New-Object System.Collections.Generic.List[object]
$cb = [Native.Win2+EnumProc] {
    param($h, $l)
    $procId = [uint32]0
    [void][Native.Win2]::GetWindowThreadProcessId($h, [ref]$procId)
    if ($ids -contains $procId -and [Native.Win2]::IsWindowVisible($h)) {
        $t = New-Object System.Text.StringBuilder 512; [void][Native.Win2]::GetWindowText($h, $t, 512)
        $c = New-Object System.Text.StringBuilder 256; [void][Native.Win2]::GetClassName($h, $c, 256)
        $r = New-Object Native.Win2+RECT; [void][Native.Win2]::GetWindowRect($h, [ref]$r)
        if ($r.R - $r.L -gt 20 -and $r.B - $r.T -gt 20) { $found.Add([pscustomobject]@{ Handle = $h; Pid = $procId; Class = "$c"; Title = "$t"; W = $r.R - $r.L; H = $r.B - $r.T }) }
    }
    return $true
}
[void][Native.Win2]::EnumWindows($cb, [IntPtr]::Zero)
$i = 0
foreach ($w in $found) {
    $i++
    "{0}  pid {1}  {2,-28} {3}x{4}  '{5}'" -f $i, $w.Pid, $w.Class, $w.W, $w.H, $w.Title
    if ($Out) {
        New-Item -ItemType Directory -Force -Path $Out | Out-Null
        $bmp = New-Object System.Drawing.Bitmap $w.W, $w.H
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc(); [void][Native.Win2]::PrintWindow($w.Handle, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
        $file = Join-Path $Out ("window{0}.png" -f $i)
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        "   -> $file"
    }
}
