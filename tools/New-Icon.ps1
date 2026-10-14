$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../src/CursorJail/Assets'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(16, 20, 29))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(145, 242, 195), 12)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(86, 35), [System.Drawing.Point]::new(35, 35), [System.Drawing.Point]::new(35, 86)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(170, 35), [System.Drawing.Point]::new(221, 35), [System.Drawing.Point]::new(221, 86)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(221, 170), [System.Drawing.Point]::new(221, 221), [System.Drawing.Point]::new(170, 221)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(86, 221), [System.Drawing.Point]::new(35, 221), [System.Drawing.Point]::new(35, 170)))
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(241, 245, 250))
$graphics.FillPolygon($brush, [System.Drawing.Point[]]@([System.Drawing.Point]::new(94, 73), [System.Drawing.Point]::new(94, 181), [System.Drawing.Point]::new(125, 150), [System.Drawing.Point]::new(155, 199), [System.Drawing.Point]::new(176, 186), [System.Drawing.Point]::new(145, 138), [System.Drawing.Point]::new(189, 138)))
$memory = [System.IO.MemoryStream]::new()
$bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $memory.ToArray()
$stream = [System.IO.File]::Create((Join-Path $assetDirectory 'CursorJail.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
$writer.Write($png)
$writer.Dispose(); $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $brush.Dispose()
