param(
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repoRoot "packaging\windows"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$icoPath = Join-Path $OutDir "AutoEQ.ico"

Add-Type -AssemblyName System.Drawing

function Draw-Icon {
    param([int]$size)

    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Scale factor: SVG is 120x120
    $s = $size / 120.0

    # Background rounded rect with gradient
    $bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 0),
        [System.Drawing.PointF]::new($size, $size),
        [System.Drawing.Color]::FromArgb(255, 15, 23, 42),
        [System.Drawing.Color]::FromArgb(255, 30, 58, 95)
    )
    $radius = [int](20 * $s)
    $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($rect.X, $rect.Y, $radius*2, $radius*2, 180, 90)
    $path.AddArc($rect.Right - $radius*2, $rect.Y, $radius*2, $radius*2, 270, 90)
    $path.AddArc($rect.Right - $radius*2, $rect.Bottom - $radius*2, $radius*2, $radius*2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $radius*2, $radius*2, $radius*2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bgBrush, $path)
    $bgBrush.Dispose()
    $path.Dispose()

    # Subtle grid lines
    if ($size -ge 32) {
        $gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(40, 30, 64, 175), [float]($s * 0.5))
        $g.DrawLine($gridPen, [float](10*$s), [float](88*$s), [float](110*$s), [float](88*$s))
        $g.DrawLine($gridPen, [float](10*$s), [float](70*$s), [float](110*$s), [float](70*$s))
        $g.DrawLine($gridPen, [float](10*$s), [float](52*$s), [float](110*$s), [float](52*$s))
        $gridPen.Dispose()
    }

    # EQ bars: x, y, w, h in SVG coords (bottom=88)
    $bars = @(
        @(13, 62, 10, 26),
        @(27, 52, 10, 36),
        @(41, 40, 10, 48),
        @(55, 43, 10, 45),
        @(69, 36, 10, 52),
        @(83, 55, 10, 33),
        @(97, 70, 10, 18)
    )

    $barColors = @(
        @([System.Drawing.Color]::FromArgb(255, 96, 165, 250),  [System.Drawing.Color]::FromArgb(127, 59, 130, 246)),
        @([System.Drawing.Color]::FromArgb(255, 96, 165, 250),  [System.Drawing.Color]::FromArgb(127, 59, 130, 246)),
        @([System.Drawing.Color]::FromArgb(255, 147, 197, 253), [System.Drawing.Color]::FromArgb(127, 96, 165, 250)),
        @([System.Drawing.Color]::FromArgb(255, 191, 219, 254), [System.Drawing.Color]::FromArgb(127, 147, 197, 253)),
        @([System.Drawing.Color]::FromArgb(255, 191, 219, 254), [System.Drawing.Color]::FromArgb(127, 147, 197, 253)),
        @([System.Drawing.Color]::FromArgb(255, 147, 197, 253), [System.Drawing.Color]::FromArgb(127, 96, 165, 250)),
        @([System.Drawing.Color]::FromArgb(255, 96, 165, 250),  [System.Drawing.Color]::FromArgb(127, 59, 130, 246))
    )

    for ($i = 0; $i -lt $bars.Count; $i++) {
        $b = $bars[$i]
        $bx = [int]($b[0] * $s); $by = [int]($b[1] * $s)
        $bw = [int]($b[2] * $s); $bh = [int]($b[3] * $s)
        if ($bw -lt 1) { $bw = 1 }
        if ($bh -lt 1) { $bh = 1 }

        $barBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new($bx, $by),
            [System.Drawing.Point]::new($bx, $by + $bh),
            $barColors[$i][0],
            $barColors[$i][1]
        )
        $barRadius = [int](3 * $s)
        $brect = [System.Drawing.Rectangle]::new($bx, $by, $bw, $bh)
        if ($barRadius -gt 0 -and $bh -gt $barRadius * 2 -and $size -ge 32) {
            $bp = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $bp.AddArc($brect.X, $brect.Y, $barRadius*2, $barRadius*2, 180, 90)
            $bp.AddArc($brect.Right - $barRadius*2, $brect.Y, $barRadius*2, $barRadius*2, 270, 90)
            $bp.AddArc($brect.Right - $barRadius*2, $brect.Bottom - $barRadius*2, $barRadius*2, $barRadius*2, 0, 90)
            $bp.AddArc($brect.X, $brect.Bottom - $barRadius*2, $barRadius*2, $barRadius*2, 90, 90)
            $bp.CloseFigure()
            $g.FillPath($barBrush, $bp)
            $bp.Dispose()
        } else {
            $g.FillRectangle($barBrush, $brect)
        }
        $barBrush.Dispose()
    }

    # Curve line over bars
    if ($size -ge 24) {
        $curvePoints = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float](18*$s), [float](60*$s)),
            [System.Drawing.PointF]::new([float](32*$s), [float](50*$s)),
            [System.Drawing.PointF]::new([float](46*$s), [float](38*$s)),
            [System.Drawing.PointF]::new([float](60*$s), [float](41*$s)),
            [System.Drawing.PointF]::new([float](74*$s), [float](34*$s)),
            [System.Drawing.PointF]::new([float](88*$s), [float](53*$s)),
            [System.Drawing.PointF]::new([float](102*$s), [float](68*$s))
        )
        $penW = [float]([Math]::Max(1.0, 1.5 * $s))
        $curvePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(178, 147, 197, 253), $penW)
        $curvePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $curvePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $curvePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLines($curvePen, $curvePoints)
        $curvePen.Dispose()

        # Peak dot
        $dotR = [float]([Math]::Max(1.5, 2.5 * $s))
        $dotBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 191, 219, 254))
        $g.FillEllipse($dotBrush,
            [float](74*$s - $dotR), [float](34*$s - $dotR),
            $dotR*2, $dotR*2)
        $dotBrush.Dispose()
    }

    # "AutoEQ" text label at bottom (only for larger sizes)
    if ($size -ge 48) {
        $fontSize = [float]([Math]::Max(5.0, 8.0 * $s))
        $font = [System.Drawing.Font]::new("Consolas", $fontSize, [System.Drawing.FontStyle]::Bold)
        $txtBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(230, 147, 197, 253))
        $sf = [System.Drawing.StringFormat]::new()
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Far
        $textRect = [System.Drawing.RectangleF]::new(0, 0, $size, [float]($size * 0.97))
        $g.DrawString("AutoEQ", $font, $txtBrush, $textRect, $sf)
        $font.Dispose()
        $txtBrush.Dispose()
        $sf.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function Save-Ico {
    param([string]$path, [System.Drawing.Bitmap[]]$bitmaps)
    $ms = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($ms)

    $count = $bitmaps.Count
    $w.Write([uint16]0)
    $w.Write([uint16]1)
    $w.Write([uint16]$count)

    $pngStreams = @()
    foreach ($bmp in $bitmaps) {
        $ps = [System.IO.MemoryStream]::new()
        $bmp.Save($ps, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngStreams += $ps
    }

    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $bmp = $bitmaps[$i]
        $sz = $pngStreams[$i].Length
        $dim = if ($bmp.Width -ge 256) { [byte]0 } else { [byte]$bmp.Width }
        $w.Write([byte]$dim)
        $w.Write([byte]$dim)
        $w.Write([byte]0)
        $w.Write([byte]0)
        $w.Write([uint16]1)
        $w.Write([uint16]32)
        $w.Write([uint32]$sz)
        $w.Write([uint32]$offset)
        $offset += [uint32]$sz
    }

    foreach ($ps in $pngStreams) {
        $ps.Position = 0
        $ps.CopyTo($ms)
        $ps.Dispose()
    }

    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $ms.Dispose()
    $w.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$bitmaps = @()
foreach ($sz in $sizes) {
    $bitmaps += Draw-Icon -size $sz
}

Save-Ico -path $icoPath -bitmaps $bitmaps
foreach ($b in $bitmaps) { $b.Dispose() }
Write-Host "Icon written: $icoPath"
