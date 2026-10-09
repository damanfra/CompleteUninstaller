# Gera o icone do Complete Uninstaller: lixeira branca sobre um quadrado azul arredondado.
# Saidas: assets/icon.ico (16-256 px, para o .exe e as janelas do Windows) e assets/icon.png (256 px, Avalonia/Linux).
# Uso: powershell -ExecutionPolicy Bypass -File assets/make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    # Fundo: quadrado arredondado com gradiente azul.
    $bg = New-RoundedRect 8 8 240 240 56
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 256, 256), ([System.Drawing.Color]::FromArgb(255, 59, 130, 246)), ([System.Drawing.Color]::FromArgb(255, 30, 58, 138)))
    $g.FillPath($grad, $bg)

    $white = [System.Drawing.Brushes]::White
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 37, 78, 190))

    # Alca da tampa, tampa e corpo da lixeira.
    $g.FillPath($white, (New-RoundedRect 106 44 44 22 8))
    $g.FillPath($white, (New-RoundedRect 62 62 132 24 10))
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddPolygon(@(
        (New-Object System.Drawing.PointF 78, 96), (New-Object System.Drawing.PointF 178, 96),
        (New-Object System.Drawing.PointF 168, 208), (New-Object System.Drawing.PointF 88, 208)))
    $g.FillPath($white, $body)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 10), $body)

    # Ranhuras da lixeira.
    $pen = New-Object System.Drawing.Pen ($blue), 9
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($x in 108, 128, 148) {
        $g.DrawLine($pen, $x, 114, $x - ($x - 128) * 0.0, 188)
    }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 16, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    [pscustomobject]@{ Size = $size; Png = [byte[]](Get-PngBytes $bmp) }
    if ($size -eq 256) { $bmp.Save((Join-Path $here 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO com imagens PNG (suportado desde o Windows Vista).
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $bw.Write([byte]$dimension); $bw.Write([byte]$dimension); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$image.Png.Length); $bw.Write([uint32]$offset)
    $offset += $image.Png.Length
}
foreach ($image in $images) { $bw.Write([byte[]]$image.Png, 0, $image.Png.Length) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $here 'icon.ico'), $ms.ToArray())
Write-Host "Gerados: assets/icon.ico e assets/icon.png"
