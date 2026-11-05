# Draws the STEP Export logo (a blue isometric cube, in the blue of the other add-ins, #0078D7) as a 512 px PNG,
# to src\Assets\step.png (ribbon icon) and installer\logo.png (installer pictures; run make-images.ps1 after it).
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$s = 512
$bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)

$cx = $s / 2; $r = $s * 0.47; $h = $r * [Math]::Sin([Math]::PI / 6)
function P($x, $y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }
$top    = P $cx ($cx - $r)
$tr     = P ($cx + $r * 0.866) ($cx - $h)
$br     = P ($cx + $r * 0.866) ($cx + $h)
$bottom = P $cx ($cx + $r)
$bl     = P ($cx - $r * 0.866) ($cx + $h)
$tl     = P ($cx - $r * 0.866) ($cx - $h)
$mid    = P $cx $cx

function Face($pts, $hex) {
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$pts); $brush.Dispose()
}
Face @($top, $tr, $mid, $tl) '#5FB0F0'
Face @($tl, $mid, $bottom, $bl) '#0078D7'
Face @($mid, $tr, $br, $bottom) '#00508F'

$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]($s * 0.035))
$pen.LineJoin = 'Round'
$g.DrawLine($pen, $mid, $tl); $g.DrawLine($pen, $mid, $tr); $g.DrawLine($pen, $mid, $bottom)
$pen.Dispose(); $g.Dispose()

$bmp.Save((Join-Path $here '..\src\Assets\step.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Save((Join-Path $here 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Made src\Assets\step.png and installer\logo.png"
