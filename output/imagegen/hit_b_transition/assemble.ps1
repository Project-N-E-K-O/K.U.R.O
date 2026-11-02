Add-Type -AssemblyName System.Drawing
$ErrorActionPreference='Stop'
$work=$PSScriptRoot
$source='C:\Users\STG-PC-TC\Desktop\KURO_Project\素材\特效\攻击特效\Hit\HIT_B'
$target=Join-Path $source '重置'
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach($n in @(0,1,3,5,7,8)) {
  $tag=$n.ToString('0000')
  $im=[System.Drawing.Bitmap]::FromFile((Join-Path $work "raw_$tag.png"))
  $out=New-Object System.Drawing.Bitmap(608,656,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g=[System.Drawing.Graphics]::FromImage($out)
  $g.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $opacity=1.0
  if($n -eq 5){$opacity=0.60}
  if($n -eq 7){$opacity=0.24}
  if($n -eq 8){$opacity=0.08}
  $matrix=New-Object System.Drawing.Imaging.ColorMatrix
  $matrix.Matrix33=[single]$opacity
  $attrs=New-Object System.Drawing.Imaging.ImageAttributes
  $attrs.SetColorMatrix($matrix)
  $g.DrawImage($im,(New-Object System.Drawing.Rectangle(0,0,608,656)),0,0,$im.Width,$im.Height,[System.Drawing.GraphicsUnit]::Pixel,$attrs)
  $attrs.Dispose()
  $g.Dispose(); $im.Dispose()
  $out.Save((Join-Path $target "HIT_B_$tag.png"),[System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose()
}
foreach($pair in @(@(1,2),@(2,4),@(3,6))) {
  $src=Join-Path $source ('HIT_B000'+$pair[0]+'.png')
  $dst=Join-Path $target ('HIT_B_'+$pair[1].ToString('0000')+'.png')
  Copy-Item -LiteralPath $src -Destination $dst
  if((Get-FileHash -LiteralPath $src).Hash -ne (Get-FileHash -LiteralPath $dst).Hash) {throw 'Original mismatch'}
}
$sheet=New-Object System.Drawing.Bitmap(912,1056)
$g=[System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(55,55,55))
$font=New-Object System.Drawing.Font('Arial',12)
for($n=0;$n -lt 9;$n++) {
  $tag=$n.ToString('0000'); $p=Join-Path $target "HIT_B_$tag.png"
  $im=[System.Drawing.Bitmap]::FromFile($p)
  if($im.Width -ne 608 -or $im.Height -ne 656 -or $im.GetPixel(0,0).A -ne 0){throw "Invalid frame $tag"}
  $x=($n%3)*304; $y=[math]::Floor($n/3)*352
  $g.DrawImage($im,[int]$x,[int]$y,304,328)
  $g.DrawString($tag,$font,[System.Drawing.Brushes]::White,[single]($x+8),[single]($y+330))
  $im.Dispose()
}
$g.Dispose();$font.Dispose()
$sheet.Save((Join-Path $work '九帧预览.png'),[System.Drawing.Imaging.ImageFormat]::Png);$sheet.Dispose()
Write-Output 'Verified: nine 608x656 RGBA frames, three original hashes unchanged.'
