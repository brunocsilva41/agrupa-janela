<#
.SYNOPSIS
  Gera os assets de release do SplitDeck em artifacts/.

.DESCRIPTION
  1. dotnet publish do app (framework-dependent, win-x64, ReadyToRun) -> artifacts/publish/
  2. zip portátil (entradas em ordem e data fixa, para ser reproduzível) -> SplitDeck-{versão}-win-x64.zip
  3. o mesmo zip vira o payload embutido no instalador (build Release do AgrupaJanela.Setup)
  4. SplitDeck-Setup-{versão}.exe
  5. SHA256SUMS.txt ("<sha256 minúsculo>  <arquivo>", fim de linha LF)

  Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File build/package.ps1 -Version 1.0.0
        pwsh build/package.ps1 -Version 1.2.3-beta.1
  Data das entradas do zip: SOURCE_DATE_EPOCH (se definido) ou 2026-01-01 00:00:00.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Versão inválida: '$Version'. Use X.Y.Z ou X.Y.Z-sufixo (ex.: 1.0.0, 0.0.0-ci)."
}

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish'
$appProject = Join-Path $root 'src\AgrupaJanela\AgrupaJanela.csproj'
$setupProject = Join-Path $root 'src\AgrupaJanela.Setup\AgrupaJanela.Setup.csproj'
$zipName = "SplitDeck-$Version-win-x64.zip"
$setupName = "SplitDeck-Setup-$Version.exe"
$zipPath = Join-Path $artifacts $zipName
$setupPath = Join-Path $artifacts $setupName
$sumsPath = Join-Path $artifacts 'SHA256SUMS.txt'

function Invoke-Checked([string]$what, [scriptblock]$command) {
    Write-Host "==> $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what falhou (código $LASTEXITCODE)." }
}

Write-Host "SplitDeck $Version -> $artifacts"
& dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'dotnet não encontrado.' }

# Recomeça do zero só dentro de artifacts/ (saída deste script).
if (Test-Path $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

# 1. Publish do app
Invoke-Checked 'Publicando o app' {
    dotnet publish $appProject -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true "-p:Version=$Version" -o $publishDir --nologo
}
if (-not (Test-Path (Join-Path $publishDir 'SplitDeck.exe'))) { throw 'SplitDeck.exe não saiu no publish.' }

# 2. Zip portátil (reproduzível: ordem alfabética, data fixa, separador "/")
Write-Host '==> Gerando o zip portátil'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$epoch = [Environment]::GetEnvironmentVariable('SOURCE_DATE_EPOCH')
$stamp = if ($epoch) { [DateTimeOffset]::FromUnixTimeSeconds([long]$epoch) } else { New-Object DateTimeOffset(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero) }
$names = [string[]]@(Get-ChildItem -LiteralPath $publishDir -Recurse -File | ForEach-Object { $_.FullName.Substring($publishDir.Length + 1).Replace('\', '/') })
[Array]::Sort($names, [StringComparer]::Ordinal)
$fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
try {
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($name in $names) {
            $source = Join-Path $publishDir ($name.Replace('/', '\'))
            $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $stamp
            $out = $entry.Open()
            try {
                $in = [System.IO.File]::OpenRead($source)
                try { $in.CopyTo($out) } finally { $in.Dispose() }
            } finally { $out.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $fs.Dispose() }
Write-Host "    $($names.Count) arquivos -> $zipName"

# 3/4. Instalador com o zip embutido (falha se o payload não existir)
Invoke-Checked 'Compilando o instalador' {
    dotnet build $setupProject -c Release --no-incremental "-p:Version=$Version" "-p:SetupPayload=$zipPath" -p:RequirePayload=true --nologo
}
$setupBuilt = Join-Path $root 'src\AgrupaJanela.Setup\bin\Release\net48\SplitDeck.Setup.exe'
Copy-Item -LiteralPath $setupBuilt -Destination $setupPath

# Confere se a versão do instalador bate com a pedida.
$built = (Get-Item -LiteralPath $setupPath).VersionInfo.ProductVersion
if ($built -ne $Version) { throw "Versão do instalador ($built) diferente da pedida ($Version)." }

# 5. SHA256SUMS.txt
Write-Host '==> Calculando hashes'
function Get-Sha256Hex([string]$path) {
    # .NET direto (não depende do módulo que traz Get-FileHash estar carregado).
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$lines = foreach ($name in @($setupName, $zipName)) {
    "$(Get-Sha256Hex (Join-Path $artifacts $name))  $name"
}
[System.IO.File]::WriteAllText($sumsPath, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "Assets em ${artifacts}:"
Get-Content -LiteralPath $sumsPath | ForEach-Object { Write-Host "  $_" }
Write-Host "  (SHA256SUMS.txt)"
