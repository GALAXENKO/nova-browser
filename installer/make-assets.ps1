$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $PSScriptRoot 'assets'
$logoPath = Join-Path $root 'NovaBrowser\Assets\newtab-logo.png'
$licensePath = Join-Path $root 'NovaBrowser\LICENSE.txt'

function New-BackgroundBitmap {
    param([int]$Width, [int]$Height, [string]$Path, [string]$Title, [string]$Subtitle, [int]$LogoSize, [int]$LogoTop, [single]$TitleSize)

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(13, 19, 32),
        [System.Drawing.Color]::FromArgb(31, 45, 72),
        90.0)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(124, 149, 255))
    $g.FillRectangle($accent, 0, 0, $Width, 3)
    $accent.Dispose()

    $logo = [System.Drawing.Image]::FromFile($logoPath)
    $logoX = [int](($Width - $LogoSize) / 2)
    $g.DrawImage($logo, $logoX, $LogoTop, $LogoSize, $LogoSize)
    $logo.Dispose()

    $titleFont = New-Object System.Drawing.Font('Segoe UI Semibold', $TitleSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
    $subFont = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(242, 245, 251))
    $muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 192, 214))
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $titleRect = New-Object System.Drawing.RectangleF(0, ($LogoTop + $LogoSize + 12), $Width, 52)
    $g.DrawString($Title, $titleFont, $white, $titleRect, $format)

    if ($Subtitle) {
        $subRect = New-Object System.Drawing.RectangleF(0, ($LogoTop + $LogoSize + 56), $Width, 40)
        $g.DrawString($Subtitle, $subFont, $muted, $subRect, $format)
    }

    $titleFont.Dispose()
    $subFont.Dispose()
    $white.Dispose()
    $muted.Dispose()
    $format.Dispose()
    $g.Dispose()

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

New-BackgroundBitmap -Width 164 -Height 314 -Path (Join-Path $assets 'welcome.bmp') `
    -Title 'Nova Browser' -Subtitle 'Fast. Private. Yours.' -LogoSize 92 -LogoTop 42 -TitleSize 15
New-BackgroundBitmap -Width 150 -Height 57 -Path (Join-Path $assets 'header.bmp') `
    -Title 'Nova Browser' -Subtitle '' -LogoSize 34 -LogoTop 11 -TitleSize 10

# NSIS header image needs the title drawn to the right of the logo, so redraw it.
$header = New-Object System.Drawing.Bitmap(150, 57, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$hg = [System.Drawing.Graphics]::FromImage($header)
$hg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$hg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$hg.Clear([System.Drawing.Color]::FromArgb(13, 19, 32))
$logo = [System.Drawing.Image]::FromFile($logoPath)
$hg.DrawImage($logo, 10, 11, 34, 34)
$logo.Dispose()
$font = New-Object System.Drawing.Font('Segoe UI Semibold', 10.5, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(242, 245, 251))
$hg.DrawString('Nova Browser', $font, $brush, 50, 18)
$font.Dispose()
$brush.Dispose()
$hg.Dispose()
$header.Save((Join-Path $assets 'header.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$header.Dispose()

# Build an RTF license page from the plain-text EULA (Cyrillic-safe).
$text = [System.IO.File]::ReadAllText($licensePath, [System.Text.Encoding]::UTF8)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0\fnil\fcharset204 Segoe UI;}{\f1\b\fnil\fcharset204 Segoe UI;}}')
[void]$sb.Append('\viewkind4\uc1\pard\f0\fs18 ')
$lines = $text -split "`r?`n"
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($i -eq 0) { [void]$sb.Append('\f1\fs22 ') }
    foreach ($ch in $line.ToCharArray()) {
        $code = [int][char]$ch
        switch ($ch) {
            '\' { [void]$sb.Append('\\'); continue }
            '{' { [void]$sb.Append('\{'); continue }
            '}' { [void]$sb.Append('\}'); continue }
            "`t" { [void]$sb.Append('\tab '); continue }
            default {
                if ($code -ge 32 -and $code -le 126) {
                    [void]$sb.Append($ch)
                } else {
                    [void]$sb.Append('\u' + $code + '?')
                }
            }
        }
    }
    if ($i -eq 0) { [void]$sb.Append('\f0\fs18\par\par ') } else { [void]$sb.Append('\par ') }
}
[void]$sb.Append('}')
[System.IO.File]::WriteAllText((Join-Path $assets 'license.rtf'), $sb.ToString(), (New-Object System.Text.ASCIIEncoding))

Get-ChildItem $assets | Select-Object Name, Length
