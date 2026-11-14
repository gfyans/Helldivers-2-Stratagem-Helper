# Saves a full-screen screenshot each time Ctrl is held for ~0.8s.
# Used to collect samples of the Helldivers 2 stratagem menu. Stops after $Max captures or $Minutes minutes.
param([int]$Max = 6, [int]$Minutes = 30)

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K {
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int v);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[K]::SetProcessDPIAware() | Out-Null

$out = Join-Path $PSScriptRoot "..\captures"
New-Item -ItemType Directory -Force $out | Out-Null
$deadline = (Get-Date).AddMinutes($Minutes)
$count = 0; $heldSince = $null; $taken = $false

Write-Output "Waiting for Ctrl holds (max $Max captures)..."
while ($count -lt $Max -and (Get-Date) -lt $deadline) {
  $down = ([K]::GetAsyncKeyState(0x11) -band 0x8000) -ne 0
  if ($down) {
    if (-not $heldSince) { $heldSince = Get-Date }
    if (-not $taken -and ((Get-Date) - $heldSince).TotalMilliseconds -gt 800) {
      $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
      $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
      $file = Join-Path $out ("menu_{0:yyyyMMdd_HHmmss}.png" -f (Get-Date))
      $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
      $g.Dispose(); $bmp.Dispose()
      $count++; $taken = $true
      Write-Output "Saved $file ($($b.Width)x$($b.Height))"
    }
  } else { $heldSince = $null; $taken = $false }
  Start-Sleep -Milliseconds 50
}
Write-Output "Done. $count capture(s)."
