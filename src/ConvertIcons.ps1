Add-Type -AssemblyName System.Drawing
foreach($asset in @(@{Input='app-source.png';Name='lumi'},@{Input='tray-source.png';Name='tray'})) {
 $source=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot $asset.Input))
 $frames=@()
 foreach($size in @(16,20,24,32,48,64,128,256)) {
  $bmp=New-Object Drawing.Bitmap $size,$size
  $g=[Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Transparent)
  $g.InterpolationMode='HighQualityBicubic';$g.PixelOffsetMode='HighQuality';$g.CompositingQuality='HighQuality'
  $g.DrawImage($source,(New-Object Drawing.Rectangle 0,0,$size,$size))
  $ms=New-Object IO.MemoryStream
  $bmp.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
  $frames+=,@{Size=$size;Bytes=$ms.ToArray()}
  if($size -eq 32 -and $asset.Name -eq 'tray'){$bmp.Save((Join-Path $PSScriptRoot 'tray.png'),[Drawing.Imaging.ImageFormat]::Png)}
  $ms.Dispose();$g.Dispose();$bmp.Dispose()
 }
 $source.Dispose()
 $file=[IO.File]::Create((Join-Path $PSScriptRoot ($asset.Name+'.ico')))
 $w=New-Object IO.BinaryWriter $file
 $w.Write([uint16]0);$w.Write([uint16]1);$w.Write([uint16]$frames.Count);$offset=6+16*$frames.Count
 foreach($frame in $frames){$s=$frame.Size;if($s -eq 256){$s=0};$w.Write([byte]$s);$w.Write([byte]$s);$w.Write([byte]0);$w.Write([byte]0);$w.Write([uint16]1);$w.Write([uint16]32);$w.Write([uint32]$frame.Bytes.Length);$w.Write([uint32]$offset);$offset+=$frame.Bytes.Length}
 foreach($frame in $frames){$w.Write([byte[]]$frame.Bytes)}
 $w.Dispose();$file.Dispose()
}
