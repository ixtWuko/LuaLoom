#Requires -Version 7.0
<#
.SYNOPSIS
    本地全量校验：原生库、构建、测试、生成物一致性、示例。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    Write-Host '== native =='
    & (Join-Path $PSScriptRoot 'fetch-lua.ps1')
    & (Join-Path $PSScriptRoot 'build-native.ps1')

    Write-Host '== build =='
    dotnet build LuaLoom.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

    Write-Host '== test =='
    dotnet test LuaLoom.sln -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed: $LASTEXITCODE" }

    Write-Host '== generated code check =='
    dotnet run --project src/LuaLoom.Cli -c Release --no-build -- check --config lua-loom.json
    if ($LASTEXITCODE -ne 0) { throw "generated code is out of date: $LASTEXITCODE" }

    Write-Host '== sample =='
    dotnet run --project samples/LuaLoom.Sample -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "sample failed: $LASTEXITCODE" }

    Write-Host 'ALL CHECKS PASSED'
}
finally {
    Pop-Location
}
