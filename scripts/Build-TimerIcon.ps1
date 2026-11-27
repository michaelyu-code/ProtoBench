<# Regenerates the running-timer icon from the existing app artwork. Requires Windows. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/ProtoBench.Desktop/Assets'))
$source = [Drawing.Bitmap]::new((Join-Path $assets 'ProtoBench.png'))
$canvas = [Drawing.Bitmap]::new(256, 256)
$graphics = [Drawing.Graphics]::FromImage($canvas)
$outline = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0C1526'))
$face = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F5FAFF'))
$hands = [Drawing.Pen]::new($outline, 7)
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.DrawImage($source, 0, 0, 256, 256)
    # Smaller top-right badge, with simple hands readable at taskbar sizes.
    $graphics.FillEllipse($outline, 172, 2, 82, 82)
    $graphics.FillEllipse($face, 177, 7, 72, 72)
    $hands.StartCap = $hands.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($hands, 213, 22, 213, 43)
    $graphics.DrawLine($hands, 213, 43, 230, 43)

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = foreach ($size in $sizes) {
        $frame = [Drawing.Bitmap]::new($size, $size)
        $drawing = [Drawing.Graphics]::FromImage($frame)
        $stream = [IO.MemoryStream]::new()
        try {
            $drawing.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $drawing.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $drawing.DrawImage($canvas, 0, 0, $size, $size)
            $frame.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally { $stream.Dispose(); $drawing.Dispose(); $frame.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $assets 'ProtoBench.TimerRunning.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    }
    finally { $writer.Dispose(); $file.Dispose() }
}
finally {
    $hands.Dispose(); $face.Dispose(); $outline.Dispose()
    $graphics.Dispose(); $canvas.Dispose(); $source.Dispose()
}
