# Сборка игры под Windows (D74): Release-экспорт Godot + два комплекта.
#   dist\Rastvordd-host\        — для хоста: игра + frp\ (проброс через VPS, с токеном) + server.txt
#   dist\Rastvordd-friends.zip  — для друзей: игра + server.txt (адрес VPS подставлен в меню), без frp и токена
# Нужны: шаблоны экспорта Godot 4.7.2 mono (%APPDATA%\Godot\export_templates\4.7.2.stable.mono),
#        server.txt в корне репозитория (адрес:порт VPS; в .gitignore), tools\frp\frpc.exe + frpc.toml (у хоста).
# Запуск: powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 [-Godot путь\к\Godot_console.exe]
param([string]$Godot = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Godot -eq "") {
    foreach ($p in @("$root\tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
                     "$root\..\Rastvordd\tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe")) {
        if (Test-Path $p) { $Godot = (Resolve-Path $p).Path; break }
    }
}
if (-not (Test-Path $Godot)) { throw "Не найден Godot (console.exe) — передай -Godot путь" }

# C# в Release Godot собирает сам при экспорте

Write-Host "1/3 экспорт…"
$out = "$root\build\Rastvordd"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
$ErrorActionPreference = "Continue"   # Godot пишет предупреждения в stderr — PowerShell 5.1 считает их ошибкой
& $Godot --headless --path "$root\game" --export-release "Windows Desktop" "$out\Rastvordd.exe" 2>&1 | Out-Null
$ErrorActionPreference = "Stop"
if (-not (Test-Path "$out\Rastvordd.pck")) { throw "экспорт не удался" }
Remove-Item -Force "$out\*.lib" -ErrorAction SilentlyContinue      # phonon.lib — для линковки, игре не нужен
if (Test-Path "$root\server.txt") { Copy-Item "$root\server.txt" "$out\server.txt" }
else { Write-Warning "нет server.txt в корне — у друзей поле адреса будет пустым" }

Write-Host "2/3 комплект хоста…"
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$hostDir = "$dist\Rastvordd-host"
if (Test-Path $hostDir) { Remove-Item -Recurse -Force $hostDir }
Copy-Item -Recurse $out $hostDir
if (Test-Path "$root\tools\frp\frpc.toml") {
    New-Item -ItemType Directory -Force "$hostDir\frp" | Out-Null
    Copy-Item "$root\tools\frp\frpc.exe", "$root\tools\frp\frpc.toml" "$hostDir\frp\"
} else { Write-Warning "нет tools\frp\frpc.toml — хост без проброса через VPS" }

Write-Host "3/3 zip для друзей…"
$zip = "$dist\Rastvordd-friends.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
"{0}  {1:N0} МБ" -f $zip, ((Get-Item $zip).Length / 1MB)
"{0}  (запуск: Rastvordd.exe → «Сервер для друзей»)" -f $hostDir
