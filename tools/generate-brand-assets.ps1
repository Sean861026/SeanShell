[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\src\SeanShell.App\Assets'
$assetDirectory = [System.IO.Path]::GetFullPath($assetDirectory)

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc(
        $Rectangle.Right - $diameter,
        $Rectangle.Y,
        $diameter,
        $diameter,
        270,
        90)
    $path.AddArc(
        $Rectangle.Right - $diameter,
        $Rectangle.Bottom - $diameter,
        $diameter,
        $diameter,
        0,
        90)
    $path.AddArc(
        $Rectangle.X,
        $Rectangle.Bottom - $diameter,
        $diameter,
        $diameter,
        90,
        90)
    $path.CloseFigure()
    return $path
}

function New-SeanShellLogoBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [double]$LogoRatio = 0.82
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality =
            [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode =
            [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode =
            [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode =
            [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        $logoSize = [single]([Math]::Min($Width, $Height) * $LogoRatio)
        $logoX = [single](($Width - $logoSize) / 2)
        $logoY = [single](($Height - $logoSize) / 2)
        $logoRectangle = [System.Drawing.RectangleF]::new(
            $logoX,
            $logoY,
            $logoSize,
            $logoSize)
        $cornerRadius = [single]($logoSize * 0.22)
        $logoPath = New-RoundedRectanglePath $logoRectangle $cornerRadius

        try {
            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $logoRectangle,
                [System.Drawing.Color]::FromArgb(255, 96, 205, 255),
                [System.Drawing.Color]::FromArgb(255, 79, 107, 237),
                135)
            try {
                $graphics.FillPath($gradient, $logoPath)
            }
            finally {
                $gradient.Dispose()
            }

            $highlightRectangle = [System.Drawing.RectangleF]::new(
                $logoX + ($logoSize * 0.08),
                $logoY + ($logoSize * 0.08),
                $logoSize * 0.84,
                $logoSize * 0.38)
            $highlightPath = New-RoundedRectanglePath(
                $highlightRectangle)(
                [single]($logoSize * 0.12))
            try {
                $highlight = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                    $highlightRectangle,
                    [System.Drawing.Color]::FromArgb(54, 255, 255, 255),
                    [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
                    90)
                try {
                    $graphics.FillPath($highlight, $highlightPath)
                }
                finally {
                    $highlight.Dispose()
                }
            }
            finally {
                $highlightPath.Dispose()
            }

            $strokeWidth = [single][Math]::Max(1.5, $logoSize * 0.085)
            $promptPen = [System.Drawing.Pen]::new(
                [System.Drawing.Color]::FromArgb(245, 255, 255, 255),
                $strokeWidth)
            try {
                $promptPen.StartCap =
                    [System.Drawing.Drawing2D.LineCap]::Round
                $promptPen.EndCap =
                    [System.Drawing.Drawing2D.LineCap]::Round
                $promptPen.LineJoin =
                    [System.Drawing.Drawing2D.LineJoin]::Round

                $graphics.DrawLines(
                    $promptPen,
                    [System.Drawing.PointF[]]@(
                        [System.Drawing.PointF]::new(
                            $logoX + ($logoSize * 0.27),
                            $logoY + ($logoSize * 0.29)),
                        [System.Drawing.PointF]::new(
                            $logoX + ($logoSize * 0.47),
                            $logoY + ($logoSize * 0.50)),
                        [System.Drawing.PointF]::new(
                            $logoX + ($logoSize * 0.27),
                            $logoY + ($logoSize * 0.71))
                    ))
                $graphics.DrawLine(
                    $promptPen,
                    $logoX + ($logoSize * 0.52),
                    $logoY + ($logoSize * 0.70),
                    $logoX + ($logoSize * 0.75),
                    $logoY + ($logoSize * 0.70))
            }
            finally {
                $promptPen.Dispose()
            }
        }
        finally {
            $logoPath.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Save-SeanShellPng {
    param(
        [string]$Name,
        [int]$Width,
        [int]$Height,
        [double]$LogoRatio
    )

    $bitmap = New-SeanShellLogoBitmap $Width $Height $LogoRatio
    try {
        $path = Join-Path $assetDirectory $Name
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function New-SeanShellIcon {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $images = foreach ($size in $Sizes) {
        $bitmap = New-SeanShellLogoBitmap $size $size 0.90
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)

        $offset = 6 + (16 * $images.Count)
        for ($index = 0; $index -lt $images.Count; $index++) {
            $size = $Sizes[$index]
            $image = $images[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Length
        }

        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

Save-SeanShellPng 'LockScreenLogo.scale-200.png' 48 48 0.72
Save-SeanShellPng 'SplashScreen.scale-200.png' 1240 600 0.34
Save-SeanShellPng 'Square150x150Logo.scale-200.png' 300 300 0.82
Save-SeanShellPng 'Square44x44Logo.scale-200.png' 88 88 0.84
Save-SeanShellPng 'Square44x44Logo.targetsize-24_altform-unplated.png' 24 24 0.90
Save-SeanShellPng 'Square44x44Logo.targetsize-24_altform-lightunplated.png' 24 24 0.90
Save-SeanShellPng 'Square44x44Logo.targetsize-48_altform-unplated.png' 48 48 0.90
Save-SeanShellPng 'Square44x44Logo.targetsize-48_altform-lightunplated.png' 48 48 0.90
Save-SeanShellPng 'StoreLogo.png' 50 50 0.82
Save-SeanShellPng 'Wide310x150Logo.scale-200.png' 620 300 0.56

New-SeanShellIcon(
    (Join-Path $assetDirectory 'AppIcon.ico'))(
    @(16, 20, 24, 32, 40, 48, 64, 128, 256))

Write-Host "Generated SeanShell brand assets in $assetDirectory"
