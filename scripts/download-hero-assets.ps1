param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\assets\heroes")
)

$ErrorActionPreference = "Stop"
$heroListUri = "https://www.dota2.com/datafeed/herolist?language=english"
$imageBaseUri = "https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/heroes/icons"
$assetRoot = Split-Path $OutputDirectory -Parent
$metadataPath = Join-Path $assetRoot "heroes.json"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$response = Invoke-RestMethod -Uri $heroListUri
$heroes = $response.result.data.heroes
$metadata = [System.Collections.Generic.List[object]]::new()

foreach ($hero in $heroes) {
    $key = $hero.name -replace '^npc_dota_hero_', ''
    $destination = Join-Path $OutputDirectory "$key.png"
    $uri = "$imageBaseUri/$key.png"

    Write-Host "Downloading $key"
    Invoke-WebRequest -Uri $uri -OutFile $destination
    $metadata.Add([ordered]@{
        id = $hero.id
        key = $key
        name = $hero.name_loc
        file = "heroes/$key.png"
        source = $uri
    })
}

$metadata | ConvertTo-Json -Depth 4 | Set-Content -Path $metadataPath -Encoding utf8
Write-Host "Downloaded $($metadata.Count) hero icons to $OutputDirectory"
