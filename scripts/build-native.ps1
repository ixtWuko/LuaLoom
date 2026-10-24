#Requires -Version 7.0
<#
.SYNOPSIS
    编译指定版本的 Lua 为 lualoom.dll，产物放到 artifacts/native/win-x64。
.PARAMETER Version
    lua-versions.json 中的版本号，默认 54。
#>
param(
    [string]$Version = '54'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$versionsFile = Join-Path $PSScriptRoot 'lua-versions.json'
$entry = (Get-Content $versionsFile -Raw | ConvertFrom-Json).PSObject.Properties[$Version]
if ($null -eq $entry) {
    throw "Unsupported Lua version '$Version'."
}

$sourceDir = Join-Path $root ('build/' + $entry.Value.source + '/src')
$buildDir = Join-Path $root ('build/native/' + $Version)
$nativeDir = Join-Path $root 'native'
$artifactDir = Join-Path $root 'artifacts/native/win-x64'

if (-not (Test-Path (Join-Path $sourceDir 'lua.h'))) {
    & (Join-Path $PSScriptRoot 'fetch-lua.ps1') -Version $Version
}

$sourceForCMake = $sourceDir -replace '\\', '/'

Write-Host "Configuring native build for Lua $Version"
cmake -S $nativeDir -B $buildDir -G "Visual Studio 17 2022" -A x64 "-DLUA_VERSION=$Version" "-DLUA_SRC=$sourceForCMake"
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed with exit code $LASTEXITCODE" }

Write-Host "Building lualoom.dll"
cmake --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item -Path (Join-Path $buildDir 'Release/lualoom.dll') -Destination (Join-Path $artifactDir 'lualoom.dll') -Force

Write-Host "Native library ready: $(Join-Path $artifactDir 'lualoom.dll') (Lua $Version)"
