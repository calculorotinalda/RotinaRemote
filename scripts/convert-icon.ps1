param (
    [string]$PngPath = "icon.png",
    [string]$IcoPath = "icon.ico"
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $PngPath)) {
    Write-Error "Source PNG not found at: $PngPath"
    exit 1
}

$pngBytes = [System.IO.File]::ReadAllBytes($PngPath)
$msIn = [System.IO.MemoryStream]::new($pngBytes)
$srcImg = [System.Drawing.Image]::FromStream($msIn)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytesList = [System.Collections.Generic.List[byte[]]]::new()
$sizesUsed = [System.Collections.Generic.List[int]]::new()

foreach ($size in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($srcImg, 0, 0, $size, $size)
    $g.Dispose()

    $msOut = [System.IO.MemoryStream]::new()
    $bmp.Save($msOut, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    $pngBytesList.Add($msOut.ToArray())
    $sizesUsed.Add($size)
    $msOut.Dispose()
}

$srcImg.Dispose()
$msIn.Dispose()

$fs = [System.IO.File]::Create($IcoPath)
$bw = [System.IO.BinaryWriter]::new($fs)

# ICONDIR header: 6 bytes
$bw.Write([uint16]0)                  # Reserved
$bw.Write([uint16]1)                  # Resource type: 1 = Icon
$bw.Write([uint16]$pngBytesList.Count) # Number of images

$offset = 6 + (16 * $pngBytesList.Count)
for ($i = 0; $i -lt $pngBytesList.Count; $i++) {
    $s = $sizesUsed[$i]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)                    # Color count (0 = >=8bpp)
    $bw.Write([byte]0)                    # Reserved
    $bw.Write([uint16]1)                  # Color planes
    $bw.Write([uint16]32)                 # Bits per pixel
    $bw.Write([uint32]$pngBytesList[$i].Length) # Image bytes count
    $bw.Write([uint32]$offset)            # Image offset
    $offset += $pngBytesList[$i].Length
}

for ($i = 0; $i -lt $pngBytesList.Count; $i++) {
    $bw.Write($pngBytesList[$i])
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Successfully generated ICO file at $IcoPath" -ForegroundColor Green
