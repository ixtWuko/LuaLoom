#Requires -Version 7.0
<#
.SYNOPSIS
    编译 Lua 5.4.9 为 lua54.dll，产物放到 artifacts/native/win-x64。
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$luaSrc = Join-Path $root 'build/lua-5.4.9/src'
$buildDir = Join-Path $root 'build/native'
$nativeDir = Join-Path $root 'native'
$artifactDir = Join-Path $root 'artifacts/native/win-x64'

if (-not (Test-Path (Join-Path $luaSrc 'lua.h'))) {
    & (Join-Path $PSScriptRoot 'fetch-lua.ps1')
}

$luaSrcForCMake = $luaSrc -replace '\\', '/'

Write-Host "Configuring native build"
cmake -S $nativeDir -B $buildDir -G "Visual Studio 17 2022" -A x64 "-DLUA_SRC=$luaSrcForCMake"
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed with exit code $LASTEXITCODE" }

Write-Host "Building lua54.dll"
cmake --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item -Path (Join-Path $buildDir 'Release/lua54.dll') -Destination (Join-Path $artifactDir 'lua54.dll') -Force

Write-Host "Native library ready: $(Join-Path $artifactDir 'lua54.dll')"
