# Makes the installer's icon and wizard pictures from logo.png (the same look as the other Inventor tool installers: white page, the logo in the middle).
# Run it again only when the logo changes:  powershell -ExecutionPolicy Bypass -File make-images.ps1
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$logo = [System.Drawing.Image]::FromFile((Join-Path $here 'logo.png'))

function Draw-Logo($bitmap, $size, $centreX, $centreY) {
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($logo, [int]($centreX - $size / 2), [int]($centreY - $size / 2), $size, $size)
    $g.Dispose()
}

# The wizard pictures (twice the size Inno asks for at 100%, so they stay sharp on a high-dpi screen).
foreach ($spec in @(@('wizard-large.bmp', 328, 628, 300), @('wizard-small.bmp', 110, 110, 100))) {
    $bmp = New-Object System.Drawing.Bitmap $spec[1], $spec[2], ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::White); $g.Dispose()
    Draw-Logo $bmp $spec[3] ($spec[1] / 2) ($spec[2] / 2)
    $bmp.Save((Join-Path $here $spec[0]), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

# The icon: PNG pictures of 16 to 256 pixels in one .ico file.
$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    Draw-Logo $bmp $s ($s / 2) ($s / 2)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256 in an .ico header
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $here 'StepExport.ico'), $out.ToArray())
$logo.Dispose()
"Made wizard-large.bmp, wizard-small.bmp and StepExport.ico in $here"

