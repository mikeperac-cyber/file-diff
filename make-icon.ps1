Add-Type -AssemblyName System.Drawing

$assetDir = $args[0]
$icoPath = Join-Path $assetDir 'FileDiff.ico'
$tempDir = Join-Path $env:TEMP ("fdicon_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

function New-IconFrame([int]$size, [string]$outPath) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [math]::Max(1, [int][math]::Round($size * 0.07))
    $side = $size - (2 * $pad)
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, $side, $side

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $radius = [float]($side * 0.22)
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.ColorTranslator]::FromHtml('#5A97FF'),
        [System.Drawing.ColorTranslator]::FromHtml('#2563EB'),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $path)

    $fontSize = [float]($side * 0.66)
    $bold = [System.Drawing.FontStyle]::Bold
    $pixel = [System.Drawing.GraphicsUnit]::Pixel
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, $bold, $pixel
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $bounds = New-Object System.Drawing.RectangleF 0, ($size * 0.015), $size, $size
    $g.DrawString([char]0x0394, $font, $white, $bounds, $fmt)

    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()

foreach ($size in $sizes) {
    $png = Join-Path $tempDir ("frame$size.png")
    New-IconFrame $size $png
    $frames += ,@($size, $png)
}

$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)

foreach ($frame in $frames) {
    $size = $frame[0]
    $bytes = [System.IO.File]::ReadAllBytes($frame[1])
    $dim = if ($size -ge 256) { 0 } else { $size }

    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)

    $offset += $bytes.Length
}

foreach ($frame in $frames) {
    $writer.Write([System.IO.File]::ReadAllBytes($frame[1]))
}

$writer.Flush()
$stream.Close()

if ($args.Count -ge 2) {
    Copy-Item (Join-Path $tempDir 'frame256.png') $args[1] -Force
    "preview written to $($args[1])"
}

Remove-Item $tempDir -Recurse -Force
"created $icoPath ($((Get-Item $icoPath).Length) bytes, $($sizes.Count) sizes)"
