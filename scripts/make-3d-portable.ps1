# Переносная 3D-сборка: dist/Rastvordd-3D + dist/Rastvordd-3D.zip.
# Внутри: движок Godot .NET, проект (уже собранная DLL), встроенный .NET 8 Runtime — на другой машине ничего ставить не надо.
# Запуск: powershell -ExecutionPolicy Bypass -File scripts\make-3d-portable.ps1 [-NoZip]
param([switch]$NoZip)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$engineSrc = Join-Path $root 'tools\godot\Godot_v4.7.2-stable_mono_win64'
$out = Join-Path $root 'dist\Rastvordd-3D'

# .NET: сначала из DOTNET_ROOT, потом рядом с dotnet.exe из PATH
$dotnetSrc = $env:DOTNET_ROOT
if (-not $dotnetSrc -or -not (Test-Path (Join-Path $dotnetSrc 'dotnet.exe'))) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { throw 'dotnet не найден: поставь .NET 8 SDK' }
    $dotnetSrc = Split-Path $cmd.Source -Parent
}
$rt = Get-ChildItem (Join-Path $dotnetSrc 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '8.*' | Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $rt) { throw ".NET 8 Runtime не найден в $dotnetSrc" }
if (-not (Test-Path $engineSrc)) { throw "Нет движка: $engineSrc" }

Write-Host '1/5 Сборка...'
& (Join-Path $dotnetSrc 'dotnet.exe') build (Join-Path $root 'game') -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла' }

Write-Host "2/5 Папка $out (зеркало: копируется только изменённое)"
New-Item -ItemType Directory -Force $out | Out-Null
$rc = '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/MT:16'

Write-Host '3/5 Движок и .NET Runtime' $rt.Name
robocopy $engineSrc (Join-Path $out 'engine') @rc | Out-Null
$dn = Join-Path $out 'dotnet'
New-Item -ItemType Directory -Force (Join-Path $dn 'shared\Microsoft.NETCore.App') | Out-Null
Copy-Item (Join-Path $dotnetSrc 'dotnet.exe') $dn
Copy-Item (Join-Path $dotnetSrc 'LICENSE.txt'), (Join-Path $dotnetSrc 'ThirdPartyNotices.txt') $dn -ErrorAction SilentlyContinue
robocopy (Join-Path $dotnetSrc 'host') (Join-Path $dn 'host') @rc | Out-Null
robocopy $rt.FullName (Join-Path $dn "shared\Microsoft.NETCore.App\$($rt.Name)") @rc | Out-Null

Write-Host '4/5 Проект (без screenshots, .godot/editor, .godot/shader_cache, obj)'
$g = Join-Path $root 'game'
robocopy $g (Join-Path $out 'game') @rc /XD (Join-Path $g 'screenshots') (Join-Path $g '.godot\editor') (Join-Path $g '.godot\shader_cache') (Join-Path $g 'obj') | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy: ошибка копирования ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $out 'game\.godot\mono\temp\bin\Debug\Rastvordd.dll'))) { throw 'В сборке нет Rastvordd.dll' }

# батники: только ASCII внутри (cmd читает их в кодировке консоли)
$exe = 'engine\Godot_v4.7.2-stable_mono_win64.exe'
$head = "@echo off`r`ncd /d `"%~dp0`"`r`nset `"DOTNET_ROOT=%~dp0dotnet`"`r`nset `"DOTNET_ROOT_X64=%~dp0dotnet`"`r`nset DOTNET_MULTILEVEL_LOOKUP=0`r`n"
$gl = '--rendering-driver opengl3 --rendering-method gl_compatibility'
$bats = [ordered]@{
    'Играть 3D.cmd'                       = "rem Rastvordd 3D: you + 20 bots. Save: %APPDATA%\Godot\app_userdata\Rastvordd\world_save.json`r`nstart `"`" `"%~dp0$exe`" --path `"%~dp0game`" res://Main3D.tscn -- --bots=20`r`n"
    'Играть 3D (OpenGL, если не стартует).cmd' = "rem For VMs / old GPUs without Vulkan`r`nstart `"`" `"%~dp0$exe`" $gl --path `"%~dp0game`" res://Main3D.tscn -- --bots=20`r`n"
    'Наблюдать за ботами 3D.cmd'          = "rem Bot sandbox, fresh world, nothing is saved. Tab - next bot, F - free camera, 1-6 - speed`r`nstart `"`" `"%~dp0$exe`" --path `"%~dp0game`" res://Main3D.tscn -- --spectate`r`n"
    'Если не запускается - лог.cmd'       = "rem Runs with console window and writes log.txt next to this file`r`n`"%~dp0engine\Godot_v4.7.2-stable_mono_win64_console.exe`" --path `"%~dp0game`" res://Main3D.tscn --log-file `"%~dp0log.txt`" -- --bots=20`r`npause`r`n"
}
foreach ($k in $bats.Keys) { [IO.File]::WriteAllText((Join-Path $out $k), $head + $bats[$k], [Text.Encoding]::ASCII) }

$readme = @"
Раствордд 3D — переносная сборка для теста
==========================================
Ничего ставить не нужно: движок и .NET лежат внутри.

1. Распакуй архив целиком в любую папку (не запускай из zip).
2. «Играть 3D.cmd» — своя игра (ты + 20 ботов).
   Если окно не открылось или чёрный экран (виртуалка, старая видеокарта) —
   «Играть 3D (OpenGL, если не стартует).cmd».
3. «Наблюдать за ботами 3D.cmd» — песочница ботов, ничего не сохраняет.
4. Если не запускается совсем — «Если не запускается - лог.cmd», пришли log.txt.

Сохранение: %APPDATA%\Godot\app_userdata\Rastvordd\world_save.json
(на этой машине своё; не запускай две копии игры одновременно).

Управление: WASD относительно камеры, Z / X — поворот камеры, средняя кнопка — свободный поворот,
колёсико — зум. ЛКМ, ПКМ, R, E, H, F, B, Enter, 1–6, Tab, Q, G — карта, Esc — настройки,
F1 — админка, F5 — сохранить, F10 — FPS.
"@
[IO.File]::WriteAllText((Join-Path $out 'ПРОЧТИ.txt'), $readme, (New-Object Text.UTF8Encoding $true))

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Папка готова: {0:N0} МБ" -f $size)
if (-not $NoZip) {
    Write-Host '5/5 Архив...'
    $zip = "$out.zip"
    $tmp = "$out.zip.part"   # пишем во временный: оборванная запись не оставит битый архив под настоящим именем
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($out, $tmp, [IO.Compression.CompressionLevel]::Optimal, $true)
    Move-Item $tmp $zip -Force
    Write-Host ("Архив: {0} ({1:N0} МБ)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
