<#
    Baut aus einem Cursor-Bild (Pfeil auf einfarbigem Hintergrund) die Dateien, die der
    Launcher benutzt:

      src/AetherLauncher/Assets/cursor.cur   Windows-Cursor für die eigene Oberfläche (32 & 48 px)
      src/AetherLauncher/Assets/cursor.png   freigestelltes PNG, dient als Standard für den
                                             Ingame-Cursor (der Client skaliert selbst)

    Aufruf:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-cursor.ps1 -Source <bild.png>

    Das Skript entfernt den Hintergrund (Farbe wird automatisch aus den Bildecken bestimmt),
    rechnet die grauen Ränder an den Kanten heraus (Unpremultiply) und schreibt einen
    .cur-Container mit PNG-Frames. Alles nachvollziehbar, ohne Zusatzprogramme.
#>

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$OutputDirectory = ''
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $Source).Path

# Standardziel: Assets des Launchers, ausgehend vom Skriptordner (nicht vom Aufrufordner).
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $OutputDirectory = Join-Path $scriptDir '../src/AetherLauncher/Assets'
}
$outDir = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outDir) | Out-Null

Write-Host "Quelle : $source"
Write-Host "Ziel   : $outDir"

$image = [System.Drawing.Bitmap]::FromFile($source)

# Hintergrundfarbe aus den vier Ecken bestimmen (Mehrheit gewinnt)
$corners = @(
    $image.GetPixel(0, 0), $image.GetPixel($image.Width - 1, 0),
    $image.GetPixel(0, $image.Height - 1), $image.GetPixel($image.Width - 1, $image.Height - 1)
)
$bg = $corners | Group-Object { "$($_.R),$($_.G),$($_.B),$($_.A)" } |
      Sort-Object Count -Descending | Select-Object -First 1
$bgColor = $corners[0]
foreach ($c in $corners) { if ("$($c.R),$($c.G),$($c.B)" -eq $bg.Name) { $bgColor = $c; break } }
Write-Host "Hintergrund erkannt: R=$($bgColor.R) G=$($bgColor.G) B=$($bgColor.B)"

# --- 1) Freistellen in voller Auflösung -------------------------------------
# Farbe nahe am Hintergrund -> transparent. Der Übergang wird weich, und aus den
# halbtransparenten Kantenpixeln wird der Hintergrundanteil herausgerechnet, damit
# kein grauer Saum am Pfeil bleibt.
$near = 18.0     # bis hierher vollständig transparent
$far  = 90.0     # ab hier vollständig deckend

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
        # Ursprungsfarbe zurückrechnen: beobachtet = a*echt + (1-a)*hintergrund
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

# --- 2) Spitze finden (für den Klickpunkt des Cursors) ----------------------
# Die Spitze ist das oberste deckende Pixel; der Klickpunkt sitzt genau dort.
$tipX = -1; $tipY = -1
for ($y = 0; $y -lt $keyed.Height -and $tipY -lt 0; $y++) {
    for ($x = 0; $x -lt $keyed.Width; $x++) {
        if ($keyed.GetPixel($x, $y).A -gt 200) { $tipX = $x; $tipY = $y; break }
    }
}
if ($tipY -lt 0) { throw 'Kein deckendes Pixel gefunden - ist das ein Cursor-Bild?' }
Write-Host "Spitze : $tipX,$tipY (von $($keyed.Width)x$($keyed.Height))"

# --- 3) PNG für den Ingame-Cursor ------------------------------------------
$big = Resize-Transparent $keyed 256
$pngPath = Join-Path $outDir 'cursor.png'
$big.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "geschrieben: $pngPath"

# --- 4) .cur mit 32 und 48 px ----------------------------------------------
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
    # ICONDIR: reserviert, Typ 2 (Cursor), Anzahl
    $bw.Write([uint16]0); $bw.Write([uint16]2); $bw.Write([uint16]$frames.Count)

    # ICONDIRENTRY: bei Cursorn stehen an Stelle von Ebenen/Bittiefe die Hotspot-Koordinaten
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $bw.Write([byte]($f.Size % 256)); $bw.Write([byte]($f.Size % 256))
        $bw.Write([byte]0); $bw.Write([byte]0)             # Farbanzahl, reserviert
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
Write-Host "geschrieben: $curPath  (Frames: $($frames | ForEach-Object { "$($_.Size)px@$($_.HotX),$($_.HotY)" }))"

$keyed.Dispose(); $big.Dispose(); $image.Dispose()
Write-Host 'Fertig.'
