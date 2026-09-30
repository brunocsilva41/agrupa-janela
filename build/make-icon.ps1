<#
.SYNOPSIS
  Gera build/assets/AgrupaJanela.ico (16/32/48/256) com o mesmo desenho do AppIcon do app:
  quatro quadrados arredondados, o superior esquerdo azul #4C8DFF e os demais #9A9EA6.

.DESCRIPTION
  O .ico gerado fica versionado no repositório (o build não o regenera, para ser determinístico).
  Rode de novo só se o desenho mudar:  powershell -NoProfile -File build/make-icon.ps1
  Tamanhos 16/32/48 vão como DIB 32 bits (compatível com tudo); 256 vai como PNG (padrão do Windows).
#>
[CmdletBinding()]
param(
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutFile) { $OutFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'assets\AgrupaJanela.ico' }
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([System.Drawing.RectangleF]$r, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Mesmo algoritmo de src/AgrupaJanela/Shell/AppIcon.cs (Create).
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        $gap = $size / 16.0
        $cell = ($size - $gap * 3) / 2
        $dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0x9A, 0x9E, 0xA6))
        $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0x4C, 0x8D, 0xFF))
        for ($r = 0; $r -lt 2; $r++) {
            for ($c = 0; $c -lt 2; $c++) {
                $rect = New-Object System.Drawing.RectangleF(([single]($gap + $c * ($cell + $gap))), ([single]($gap + $r * ($cell + $gap))), ([single]$cell), ([single]$cell))
                $path = New-RoundedPath $rect ([single]($size / 10.0))
                if ($r -eq 0 -and $c -eq 0) { $g.FillPath($accent, $path) } else { $g.FillPath($dim, $path) }
                $path.Dispose()
            }
        }
        $dim.Dispose(); $accent.Dispose()
    } finally { $g.Dispose() }
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (altura dobrada: XOR + máscara AND)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    $w.Write([int]($size * $size * 4 + $maskStride * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {        # de baixo para cima
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    for ($y = $size - 1; $y -ge 0; $y--) {        # máscara AND: 1 = transparente
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($bmp.GetPixel($x, $y).A -eq 0) { $row[[int][math]::Floor($x / 8)] = $row[[int][math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $w.Write($row)
    }
    $w.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 32, 48, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    try {
        if ($s -eq 256) {
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $images += ,@{ Size = $s; Data = $ms.ToArray() }
        } else {
            $images += ,@{ Size = $s; Data = (Get-DibBytes $bmp) }
        }
    } finally { $bmp.Dispose() }
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)   # ICONDIR
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {                                               # ICONDIRENTRY
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Data.Length); $w.Write([int]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Host "Ícone gerado: $OutFile ($($out.Length) bytes; tamanhos: $($sizes -join ', '))"
