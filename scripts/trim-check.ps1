param(
    [ValidateSet("53", "54")]
    [string]$Version = "54"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/trim-check"

dotnet publish (Join-Path $root "samples/LuaLoom.Sample/LuaLoom.Sample.csproj") `
    -c Release `
    -p:LuaVersion=$Version `
    -p:PublishTrimmed=true `
    -p:TrimMode=partial `
    -o $output `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "trimmed publish failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $output "LuaLoom.Sample.dll"
dotnet $dll
if ($LASTEXITCODE -ne 0) {
    throw "trimmed sample failed with exit code $LASTEXITCODE"
}

Write-Host "Trimmed publish check passed (Lua $Version)."
