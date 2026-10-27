# Первая установка: скачать Godot 4.7.2 .NET в tools/godot и собрать проект.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root "tools\godot"
$exe = Join-Path $dir "Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Write-Host "Нет .NET SDK 8 — поставь с dotnet.microsoft.com и запусти снова." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $exe)) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $env:TEMP "godot472mono.zip"
    Write-Host "Качаю Godot 4.7.2 .NET (~80 МБ)..."
    Invoke-WebRequest "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_win64.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath $dir -Force
    Remove-Item $zip
}
Write-Host "Сборка..."
dotnet build (Join-Path $root "game") -v q -nologo
Write-Host "Импорт ассетов (первый раз долго)..."
& ($exe -replace '\.exe$', '_console.exe') --headless --path (Join-Path $root "game") --import | Out-Null
Write-Host "Готово. Запускай Играть.cmd" -ForegroundColor Green
