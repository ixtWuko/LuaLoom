#Requires -Version 7.0
<#
.SYNOPSIS
    下载并校验指定版本的 Lua 源码到 build/<source>。
.PARAMETER Version
    lua-versions.json 中的版本号，默认 54。
#>
param(
    [string]$Version = '54'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root 'build'
$versionsFile = Join-Path $PSScriptRoot 'lua-versions.json'

$versions = Get-Content $versionsFile -Raw | ConvertFrom-Json
$property = $versions.PSObject.Properties[$Version]
if ($null -eq $property) {
    $supported = ($versions.PSObject.Properties.Name -join ', ')
    throw "Unsupported Lua version '$Version'. Supported: $supported"
}

$entry = $property.Value
$destDir = Join-Path $buildDir $entry.source
$archive = Join-Path $buildDir ($entry.source + '.tar.gz')

if (Test-Path (Join-Path $destDir 'src/lua.h')) {
    Write-Host "Lua source already present: $destDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "Downloading $($entry.url)"
Invoke-WebRequest -Uri $entry.url -OutFile $archive

$actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $entry.sha256) {
    Remove-Item -Force $archive
    throw "SHA-256 mismatch. Expected $($entry.sha256), got $actual"
}

Write-Host "Extracting to $buildDir"
tar -xzf $archive -C $buildDir
Remove-Item -Force $archive

if (-not (Test-Path (Join-Path $destDir 'src/lua.h'))) {
    throw "Extraction failed: $destDir/src/lua.h not found"
}

Write-Host "Lua $Version ready: $destDir"
