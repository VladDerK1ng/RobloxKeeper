# Generates app.ico - violet rounded tile with a tilted white square and a green "alive" dot.
Add-Type -AssemblyName System.Drawing

function RoundPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$blobs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Background: violet gradient rounded tile
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 106, 91, 226),
        [System.Drawing.Color]::FromArgb(255, 154, 92, 246), [single]45)
    $path = RoundPath 0 0 $s $s ($s * 0.22)
    $g.FillPath($bg, $path)

    # Tilted white square outline with a filled center dot (keeper mark)
    $g.TranslateTransform($s / 2, $s / 2)
    $g.RotateTransform(-14)
    $side = $s * 0.46
    $half = $side / 2
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.5, $s * 0.085))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawRectangle($pen, [single](-$half), [single](-$half), [single]$side, [single]$side)
    $inner = $s * 0.15
    $g.FillRectangle([System.Drawing.Brushes]::White, [single](-$inner / 2), [single](-$inner / 2), [single]$inner, [single]$inner)
    $g.ResetTransform()

    # Green status dot, bottom-right, with a dark ring so it pops
    $dotSize = $s * 0.30
    $dotX = $s * 0.66
    $dotY = $s * 0.66
    $ring = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 30, 46))
    $g.FillEllipse($ring, [single]($dotX - $s * 0.03), [single]($dotY - $s * 0.03), [single]($dotSize + $s * 0.06), [single]($dotSize + $s * 0.06))
    $dot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 129, 216, 143))
    $g.FillEllipse($dot, [single]$dotX, [single]$dotY, [single]$dotSize, [single]$dotSize)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $blobs += , ($ms.ToArray())
    $ms.Dispose()
}

# Assemble the .ico container (PNG-compressed entries)
$out = Join-Path $PSScriptRoot 'app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                # reserved
$bw.Write([uint16]1)                # type: icon
$bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $d = $blobs[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b)             # width
    $bw.Write([byte]$b)             # height
    $bw.Write([byte]0)              # palette
    $bw.Write([byte]0)              # reserved
    $bw.Write([uint16]1)            # planes
    $bw.Write([uint16]32)           # bpp
    $bw.Write([uint32]$d.Length)
    $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $blobs) { $bw.Write($d) }
$bw.Close()
Write-Output "Wrote $out ($((Get-Item $out).Length) bytes)"
