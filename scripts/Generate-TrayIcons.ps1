#requires -Version 7.0
# Regenerate the checked-in tray badges using vector artwork. Windows only.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/EcoMode/Assets'
foreach ($state in @('eco_on', 'restore_pending')) {
    $frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform(($size / 16.0), ($size / 16.0))
            # Keep the app's leaf silhouette, with transparent edges at every DPI.
            $leaf = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $green = [System.Drawing.Color]::FromArgb(20, 125, 84)
            $leafBrush = [System.Drawing.SolidBrush]::new($green)
            $stem = [System.Drawing.Pen]::new($green, 0.6)
            $stem.StartCap = $stem.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            try {
                $leaf.AddBezier(4.3, 8.7, 1.8, 5.0, 5.2, 4.0, 8.7, 3.2)
                $leaf.AddBezier(8.7, 3.2, 8.8, 6.2, 8.3, 8.5, 5.0, 9.1)
                $leaf.AddBezier(5.0, 9.1, 5.2, 7.7, 6.6, 6.2, 7.2, 5.2)
                $leaf.AddBezier(7.2, 5.2, 5.8, 6.0, 4.4, 7.5, 4.3, 8.7)
                $leaf.CloseFigure()
                $graphics.FillPath($leafBrush, $leaf)
                $graphics.DrawBezier($stem, 2.8, 11.2, 2.9, 8.8, 4.7, 7.1, 6.3, 5.9)
            } finally { $leaf.Dispose(); $leafBrush.Dispose(); $stem.Dispose() }
            $ink = [System.Drawing.Color]::FromArgb(23, 35, 29)
            $fill = if ($state -eq 'eco_on') { [System.Drawing.Color]::FromArgb(43, 182, 115) } else { [System.Drawing.Color]::FromArgb(245, 187, 67) }
            $brush = [System.Drawing.SolidBrush]::new($fill)
            $outline = [System.Drawing.Pen]::new($ink, 0.8)
            $mark = [System.Drawing.Pen]::new($(if ($state -eq 'eco_on') { [System.Drawing.Color]::White } else { $ink }), 1.3)
            $mark.StartCap = $mark.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            try {
                $graphics.FillEllipse($brush, 6.8, 6.8, 8.7, 8.7)
                $graphics.DrawEllipse($outline, 6.8, 6.8, 8.7, 8.7)
                if ($state -eq 'eco_on') {
                    $graphics.DrawLines($mark, [System.Drawing.PointF[]]@(
                        [System.Drawing.PointF]::new(8.8, 11.2),
                        [System.Drawing.PointF]::new(10.4, 12.8),
                        [System.Drawing.PointF]::new(13.5, 9.6)))
                } else {
                    $graphics.DrawLine($mark, 11.2, 8.9, 11.2, 11.5)
                    $graphics.DrawLine($mark, 11.2, 13.2, 11.2, 13.25)
                }
            } finally { $brush.Dispose(); $outline.Dispose(); $mark.Dispose() }
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [System.IO.File]::Create((Join-Path $assets "tray_$state.ico"))
    $writer = [System.IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $writer.Write([byte]$frame.Size); $writer.Write([byte]$frame.Size)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $output.Dispose() }
}
