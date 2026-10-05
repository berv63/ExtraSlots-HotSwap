# Builds ExtraSlotsHotSwap.dll inside a .NET SDK container, nothing is installed on the machine.
# Usage: .\build.ps1            build to .\dist
#        .\build.ps1 -Install   build and copy the dll into the game's BepInEx\plugins folder
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$project = $PSScriptRoot

if (-not (Test-Path "$GameDir\BepInEx\plugins\ExtraSlots\ExtraSlots.dll")) {
    throw "ExtraSlots.dll was not found in $GameDir\BepInEx\plugins\ExtraSlots"
}

docker run --rm `
    -v "${GameDir}:/game:ro" `
    -v "${project}:/src" `
    -v "extraslots-hotswap-nuget:/root/.nuget/packages" `
    -w /src `
    mcr.microsoft.com/dotnet/sdk:8.0 `
    dotnet build ExtraSlotsHotSwap.csproj -c Release -o /src/dist --nologo -v minimal

if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if ($Install) {
    $target = "$GameDir\BepInEx\plugins\ExtraSlotsHotSwap"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item "$project\dist\ExtraSlotsHotSwap.dll" $target -Force
    Write-Host "Installed to $target"
}
