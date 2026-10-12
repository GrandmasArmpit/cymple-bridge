#!/usr/bin/env pwsh
# Builds the app, packs it into the installer payload, and emits dist/CympleBridgeSetup.exe.
# Requires the .NET 10 SDK.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$root      = Split-Path -Parent $PSScriptRoot
$appProj   = Join-Path $root 'src/CympleBridge/CympleBridge.csproj'
$setupProj = Join-Path $root 'src/CympleBridgeSetup/CympleBridgeSetup.csproj'
$appOut    = Join-Path $root 'artifacts/app'
$payload   = Join-Path $root 'src/CympleBridgeSetup/payload'
$dist      = Join-Path $root 'dist'

Write-Host '==> Publishing app (win-x64, self-contained)'
dotnet publish $appProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $appOut

New-Item -ItemType Directory -Force -Path $payload, $dist | Out-Null

Write-Host '==> Packing installer payload'
$exe = Join-Path $appOut 'CympleBridge.exe'
Copy-Item $exe (Join-Path $payload 'CympleBridge.exe') -Force

$gz = Join-Path $payload 'CympleBridge.exe.gz'
if (Test-Path $gz) { Remove-Item $gz -Force }
$input = [IO.File]::OpenRead($exe)
try {
    $output = [IO.File]::Create($gz)
    try {
        $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::Optimal)
        try { $input.CopyTo($gzip) } finally { $gzip.Dispose() }
    } finally { $output.Dispose() }
} finally { $input.Dispose() }

Write-Host '==> Building installer (win-x64, self-contained)'
dotnet publish $setupProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dist

$setup = Join-Path $dist 'CympleBridgeSetup.exe'
Write-Host ''
Write-Host "Done: $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
