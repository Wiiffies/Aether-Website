<#
    Builds the cursor assets from a pointer image on a solid background:

      src/AetherLauncher/Assets/cursor.cur   Windows cursor for the launcher (32 & 48 px)
      src/AetherLauncher/Assets/cursor.png   transparent PNG used as the default
                                             in-game cursor (the client scales it)

    Usage:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-cursor.ps1 -Source <image.png>

    The script samples the background from the image corners, removes the background,
    unpremultiplies the soft edge pixels, and writes a .cur container with PNG frames.
#>

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$OutputDirectory = ''
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $Source).Path

# Default target: launcher assets, relative to this script rather than the caller.
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $OutputDirectory = Join-Path $scriptDir '../src/AetherLauncher/Assets'
}
$outDir = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outDir) | Out-Null

Write-Host "Source : $source"
Write-Host "Output : $outDir"

$image = [System.Drawing.Bitmap]::FromFile($source)

# Pick the background color from the four corners (majority wins).
$corners = @(
    $image.GetPixel(0, 0), $image.GetPixel($image.Width - 1, 0),
    $image.GetPixel(0, $image.Height - 1), $image.GetPixel($image.Width - 1, $image.Height - 1)
)
$bg = $corners | Group-Object { "$($_.R),$($_.G),$($_.B),$($_.A)" } |
      Sort-Object Count -Descending | Select-Object -First 1
$bgColor = $corners[0]
foreach ($c in $corners) { if ("$($c.R),$($c.G),$($c.B)" -eq $bg.Name) { $bgColor = $c; break } }
Write-Host "Background detected: R=$($bgColor.R) G=$($bgColor.G) B=$($bgColor.B)"

# --- 1) Key the background at full resolution -------------------------------
# Colors near the background become transparent. Edge pixels are softened and
# the background contribution is removed from partially transparent pixels so no
# gray fringe remains around the pointer.
$near = 18.0     # fully transparent up to this distance
$far  = 90.0     # fully opaque from this distance

$keyed = New-Object System.Drawing.Bitmap $image.Width, $image.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $image.Height; $y++) {
    for ($x = 0; $x -lt $image.Width; $x++) {
        $c = $image.GetPixel($x, $y)
        $d = [Math]::Sqrt([Math]::Pow($c.R - $bgColor.R, 2) + [Math]::Pow($c.G - $bgColor.G, 2) + [Math]::Pow($c.B - $bgColor.B, 2))

        if ($d -le $near) { $keyed.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0)); continue }

        if ($d -ge $far) {
            $keyed.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B))
            continue
        }

        $a = ($d - $near) / ($far - $near)
        $alpha = [int][Math]::Round(255 * $a)
        # Recover the original color: observed = a*real + (1-a)*background
        $r = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round(($c.R - (1 - $a) * $bgColor.R) / $a)))
        $g = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round(($c.G - (1 - $a) * $bgColor.G) / $a)))
        $b = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round(($c.B - (1 - $a) * $bgColor.B) / $a)))
        $keyed.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $r, $g, $b))
    }
}

function Resize-Transparent([System.Drawing.Bitmap]$source, [int]$size) {
    $target = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()
    return $target
}

# --- 2) Find the tip (the cursor hotspot) ------------------------------------
# The tip is the topmost opaque pixel; the hotspot sits exactly there.
$tipX = -1; $tipY = -1
for ($y = 0; $y -lt $keyed.Height -and $tipY -lt 0; $y++) {
    for ($x = 0; $x -lt $keyed.Width; $x++) {
        if ($keyed.GetPixel($x, $y).A -gt 200) { $tipX = $x; $tipY = $y; break }
    }
}
if ($tipY -lt 0) { throw 'No opaque pixel found - is this a cursor image?' }
Write-Host "Tip    : $tipX,$tipY (of $($keyed.Width)x$($keyed.Height))"

# --- 3) PNG for the in-game cursor -------------------------------------------
$big = Resize-Transparent $keyed 256
$pngPath = Join-Path $outDir 'cursor.png'
$big.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote: $pngPath"

# --- 4) .cur with 32 and 48 px -----------------------------------------------
$sizes = @(32, 48)
$frames = @()
foreach ($size in $sizes) {
    $img = Resize-Transparent $keyed $size
    $ms = New-Object System.IO.MemoryStream
    $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{
        Size = $size
        Data = $ms.ToArray()
        HotX = [int][Math]::Round($tipX * $size / $keyed.Width)
        HotY = [int][Math]::Round($tipY * $size / $keyed.Height)
    }
    $ms.Dispose(); $img.Dispose()
}

$curPath = Join-Path $outDir 'cursor.cur'
$fs = [System.IO.File]::Create($curPath)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    # ICONDIR: reserved, type 2 (cursor), frame count
    $bw.Write([uint16]0); $bw.Write([uint16]2); $bw.Write([uint16]$frames.Count)

    # ICONDIRENTRY: cursor entries use hotspot coordinates instead of planes/bit depth
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $bw.Write([byte]($f.Size % 256)); $bw.Write([byte]($f.Size % 256))
        $bw.Write([byte]0); $bw.Write([byte]0)             # color count, reserved
        $bw.Write([uint16]$f.HotX); $bw.Write([uint16]$f.HotY)
        $bw.Write([uint32]$f.Data.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Data.Length
    }

    foreach ($f in $frames) { $bw.Write($f.Data) }
}
finally {
    $bw.Dispose(); $fs.Dispose()
}
Write-Host "Wrote: $curPath  (frames: $($frames | ForEach-Object { "$($_.Size)px@$($_.HotX),$($_.HotY)" }))"

$keyed.Dispose(); $big.Dispose(); $image.Dispose()
Write-Host 'Done.'
