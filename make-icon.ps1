$ErrorActionPreference = 'Stop'

# Generate icon.ico: a half-moon icon (dark night left / warm sun right) for the theme switcher.
Add-Type -AssemblyName System.Drawing

function Get-PngSize([string]$path) {
    $fs = [System.IO.File]::OpenRead($path)
    $buf = New-Object byte[] 24
    [void]$fs.Read($buf, 0, 24)
    $fs.Close()
    $w = ([int]$buf[16] -shl 24) -bor ([int]$buf[17] -shl 16) -bor ([int]$buf[18] -shl 8) -bor [int]$buf[19]
    $h = ([int]$buf[20] -shl 24) -bor ([int]$buf[21] -shl 16) -bor ([int]$buf[22] -shl 8) -bor [int]$buf[23]
    return ,@($w, $h)
}

function New-MoonPng([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [int][Math]::Ceiling($size * 0.06)
    $d = $size - 2 * $pad
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $d, $d)

    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 42, 40, 78))
    $g.FillPie($dark, $rect, 90, 180)

    $bounds = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $light = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bounds, [System.Drawing.Color]::FromArgb(255, 255, 214, 102), [System.Drawing.Color]::FromArgb(255, 250, 166, 26), 90.0)
    $g.FillPie($light, $rect, 270, 180)

    $penW = [float][Math]::Max(1.0, $size * 0.02)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 30, 28, 56), $penW)
    $g.DrawEllipse($pen, $rect)

    $g.Dispose()
    $bmp.Save($path)
    $bmp.Dispose()
}

function New-Ico([string[]]$pngPaths, [string]$outPath) {
    $count = $pngPaths.Count
    $entries = @()
    $offset = 6 + 16 * $count
    foreach ($p in $pngPaths) {
        $bytes = [System.IO.File]::ReadAllBytes($p)
        $sizes = Get-PngSize $p
        $w = if ($sizes[0] -ge 256) { 0 } else { $sizes[0] }
        $h = if ($sizes[1] -ge 256) { 0 } else { $sizes[1] }
        $entries += ,@{ w = [byte]$w; h = [byte]$h; len = $bytes.Length; off = $offset; data = $bytes }
        $offset += $bytes.Length
    }
    $fs = [System.IO.File]::Create($outPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$count)
    foreach ($e in $entries) {
        $bw.Write($e.w)
        $bw.Write($e.h)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$e.len)
        $bw.Write([UInt32]$e.off)
    }
    foreach ($e in $entries) { $bw.Write($e.data) }
    $bw.Flush()
    $bw.Close()
}

$tmp = Join-Path $env:TEMP 'autocolor_icon_tmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    $pngs = @()
    foreach ($s in 256, 64, 48, 32, 24, 16) {
        $p = Join-Path $tmp "icon_$s.png"
        New-MoonPng $s $p
        $pngs += $p
    }
    New-Ico $pngs (Join-Path $PSScriptRoot 'icon.ico')
    Write-Host 'icon.ico generated'
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
