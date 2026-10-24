#Requires -Version 7.0
<#
.SYNOPSIS
    编译指定版本的 Lua 为 lualoom，产物放到 artifacts/native/<rid>。
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

$rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
if ([string]::IsNullOrWhiteSpace($rid)) {
    $rid = 'win-x64'
}

$onWindows = $rid.StartsWith('win')
$onMac = $rid.StartsWith('osx')
$sourceDir = Join-Path $root ('build/' + $entry.Value.source + '/src')
$buildDir = Join-Path $root ('build/native/' + $Version + '/' + $rid)
$nativeDir = Join-Path $root 'native'
$artifactDir = Join-Path $root ('artifacts/native/' + $rid)

if (-not (Test-Path (Join-Path $sourceDir 'lua.h'))) {
    & (Join-Path $PSScriptRoot 'fetch-lua.ps1') -Version $Version
}

$sourceForCMake = $sourceDir -replace '\\', '/'
$cmakeArgs = @(
    '-S', $nativeDir,
    '-B', $buildDir,
    "-DLUA_VERSION=$Version",
    "-DLUA_SRC=$sourceForCMake"
)

if ($onWindows) {
    $arch = if ($rid -like '*arm64') { 'arm64' } else { 'x64' }
    $cmakeArgs += @('-G', 'Visual Studio 17 2022', '-A', $arch)
}
else {
    $cmakeArgs += @('-G', 'Unix Makefiles', '-DCMAKE_BUILD_TYPE=Release')
}

Write-Host "Configuring native build for Lua $Version ($rid)"
cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed with exit code $LASTEXITCODE" }

Write-Host 'Building lualoom'
cmake --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed with exit code $LASTEXITCODE" }

$binaryName = if ($onWindows) { 'lualoom.dll' } elseif ($onMac) { 'liblualoom.dylib' } else { 'liblualoom.so' }
$builtPath = if ($onWindows) { Join-Path $buildDir ('Release/' + $binaryName) } else { Join-Path $buildDir $binaryName }

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item -Path $builtPath -Destination (Join-Path $artifactDir $binaryName) -Force

Write-Host "Native library ready: $(Join-Path $artifactDir $binaryName) (Lua $Version, $rid)"
