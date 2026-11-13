# CC0-текстуры Poly Haven для своих моделей (стиль «советский постапок») → tools/textures/<id>/ (кэш, не в git).
# Каждая: diff (цвет), nor_gl (нормали, OpenGL — как в Blender и glTF), rough (шероховатость). 1k — хватает для камеры сверху.
# Запуск: powershell -ExecutionPolicy Bypass -File scripts\blender\fetch-textures.ps1
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cache = Join-Path $root 'tools\textures'
$h = @{ 'User-Agent' = 'rastvordd-asset-fetch' }
# список — в одном месте с материалами: scripts/blender/soviet_kit.py (MATS)
$ids = 'green_metal_rust', 'rusty_metal_02', 'rust_coarse_01', 'metal_plate', 'painted_metal_shutter', 'blue_metal_plate',
       'green_rough_planks', 'weathered_planks', 'plywood', 'concrete', 'worn_corrugated_iron'
$maps = @{ 'Diffuse' = 'diff'; 'nor_gl' = 'nor'; 'Rough' = 'rough' }
foreach ($id in $ids) {
    $dir = Join-Path $cache $id
    if (Test-Path (Join-Path $dir 'done')) { Write-Host "есть   $id"; continue }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $f = Invoke-RestMethod "https://api.polyhaven.com/files/$id" -Headers $h
    foreach ($m in $maps.Keys) {
        $u = $f.$m.'1k'.jpg.url
        if (-not $u) { Write-Warning "${id}: нет $m"; continue }
        Invoke-WebRequest $u -OutFile (Join-Path $dir "$($maps[$m]).jpg") -Headers $h
    }
    Set-Content (Join-Path $dir 'done') 'ok'
    Write-Host "скачан $id"
}
