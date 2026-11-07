Add-Type -AssemblyName System.Drawing

$srcPath = Join-Path $PSScriptRoot "wtopmbot-icon.jpg"
$icoPath = Join-Path $PSScriptRoot "app.ico"
$src = [System.Drawing.Image]::FromFile($srcPath)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = New-Object System.Collections.Generic.List[object]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()

    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
    $entries.Add([pscustomobject]@{ Size = $s; Bytes = $bytes })
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([int16]1)
$bw.Write([int16]$entries.Count)

$offset = 6 + (16 * $entries.Count)
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { [byte]0 } else { [byte]$e.Size }
    $bw.Write($dim)
    $bw.Write($dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int32]$e.Bytes.Length)
    $bw.Write([int32]$offset)
    $offset += $e.Bytes.Length
}

foreach ($e in $entries) {
    $bw.Write($e.Bytes)
}

[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$src.Dispose()
Write-Output "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
