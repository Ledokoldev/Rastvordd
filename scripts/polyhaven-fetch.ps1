# Скачать модели Poly Haven (CC0) в кэш tools/polyhaven/<id>/ (gltf + bin + текстуры). Кэш не в git.
# Список — scripts/polyhaven-models.json (поле "src"). Уже скачанное не качается повторно.
# Запуск: powershell -ExecutionPolicy Bypass -File scripts\polyhaven-fetch.ps1 [-Res 1k]
param([string]$Res = '1k')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'tools\polyhaven'
$list = Get-Content (Join-Path $PSScriptRoot 'polyhaven-models.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ids = $list | ForEach-Object { $_.src } | Sort-Object -Unique
$h = @{ 'User-Agent' = 'rastvordd-asset-fetch' }
foreach ($id in $ids) {
    $dir = Join-Path $cache $id
    $gltfPath = Join-Path $dir "$id.gltf"
    if (Test-Path $gltfPath) { Write-Host "есть   $id"; continue }
    $files = Invoke-RestMethod "https://api.polyhaven.com/files/$id" -Headers $h
    $g = $files.gltf.$Res.gltf
    if (-not $g) { Write-Warning "${id}: нет gltf ${Res}"; continue }
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($p in $g.include.PSObject.Properties) {
        $dst = Join-Path $dir ($p.Name -replace '/', '\')
        New-Item -ItemType Directory -Force (Split-Path $dst -Parent) | Out-Null
        Invoke-WebRequest $p.Value.url -OutFile $dst -Headers $h
    }
    # сам gltf качаем последним: его наличие = скачано целиком
    Invoke-WebRequest $g.url -OutFile "$gltfPath.part" -Headers $h
    Move-Item "$gltfPath.part" $gltfPath -Force
    Write-Host "скачан $id"
}
