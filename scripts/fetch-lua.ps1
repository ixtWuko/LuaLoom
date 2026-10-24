#Requires -Version 7.0
<#
.SYNOPSIS
    下载并校验 Lua 5.4.9 源码到 build/lua-5.4.9。
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root 'build'
$destDir = Join-Path $buildDir 'lua-5.4.9'
$archive = Join-Path $buildDir 'lua-5.4.9.tar.gz'
$url = 'https://www.lua.org/ftp/lua-5.4.9.tar.gz'
$expectedSha256 = '2335b6c582a52654f94612bf10d2f4672805d05329aa6568b1d8cd9e5c6fb8e6'

if (Test-Path (Join-Path $destDir 'src/lua.h')) {
    Write-Host "Lua source already present: $destDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $archive

$actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expectedSha256) {
    Remove-Item -Force $archive
    throw "SHA-256 mismatch. Expected $expectedSha256, got $actual"
}

Write-Host "Extracting to $buildDir"
tar -xzf $archive -C $buildDir
Remove-Item -Force $archive

if (-not (Test-Path (Join-Path $destDir 'src/lua.h'))) {
    throw "Extraction failed: $destDir/src/lua.h not found"
}

Write-Host "Lua 5.4.9 ready: $destDir"
