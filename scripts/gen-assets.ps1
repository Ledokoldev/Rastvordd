# Пакетная генерация ассетов через Codex (gpt-image) по assets/manifest.json.
# Уже существующие файлы пропускаются. Пример:
#   powershell -File scripts/gen-assets.ps1 -Parallel 3
#   powershell -File scripts/gen-assets.ps1 -Only tree_pine_1,node_metal_1 -Force
param(
    [int]$Parallel = 3,
    [string[]]$Only = @(),
    [switch]$Force,
    [Alias("Manifest")][string]$ExtraManifest = ""   # не $Manifest: переменные PowerShell без учёта регистра, совпало бы с $manifest ниже
)

$root = Split-Path -Parent $PSScriptRoot
$codex = Get-ChildItem "C:\Program Files\WindowsApps" -Directory -Filter "OpenAI.Codex_*" -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "app\resources\codex.exe" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $codex) { throw "codex.exe не найден" }

$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $root "assets\manifest.json") | ConvertFrom-Json
if ($extraManifest) {
    # отдельный список ассетов (параллельная работа): ассеты — из него, оформление кадра — из общего манифеста
    $extra = Get-Content -Raw -Encoding UTF8 (Join-Path $root $extraManifest) | ConvertFrom-Json
    $manifest.assets = $extra.assets
}
$todo = @()
foreach ($a in $manifest.assets) {
    if ($Only.Count -gt 0 -and $Only -notcontains $a.id) { continue }
    $rel = "assets/gen/$($a.cat)/$($a.id).png"
    if (-not $Force -and (Test-Path (Join-Path $root $rel))) { continue }
    New-Item -ItemType Directory -Force (Join-Path $root "assets\gen\$($a.cat)") | Out-Null
    $prompt = @"
Generate ONE image with your image generation tool and save it as a PNG file at $rel (relative to the current directory), overwriting if it exists. Do not write any code or edit any other files.

Image: $(if ($a.ui) { $manifest.ui_framing } elseif ($a.rig) { $manifest.rig_framing } elseif ($a.sheet) { $manifest.sheet_framing } elseif ($a.tile) { $manifest.tile_framing } elseif ($a.weapon) { $manifest.weapon_framing } elseif ($a.view -eq 'side') { $manifest.side_framing } elseif ($a.view -eq 'front') { $manifest.front_framing } elseif ($a.view -eq 'angled') { $manifest.angled_framing } elseif ($a.icon) { $manifest.icon_framing } else { $manifest.framing }) Subject: $($a.desc). $($manifest.style)

When done, reply with just the saved file path.
"@
    $todo += [pscustomobject]@{ Id = $a.id; Rel = $rel; Prompt = $prompt; Ref = $(if ($a.ref) { Join-Path $root $a.ref } else { $null }) }
}

Write-Host "К генерации: $($todo.Count)"
$logDir = Join-Path $root "assets\gen\_logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$running = @()
foreach ($t in $todo) {
    while (($running | Where-Object { -not $_.HasExited }).Count -ge $Parallel) { Start-Sleep -Milliseconds 500 }
    $pf = Join-Path $logDir "$($t.Id).prompt.txt"
    Set-Content -Path $pf -Value $t.Prompt -Encoding UTF8
    $log = Join-Path $logDir "$($t.Id).log"
    $args2 = @("exec", "-C", "`"$root`"", "-s", "workspace-write", "-c", "model_reasoning_effort=`"medium`"")
    $args2 += "-"   # промпт из stdin; -i ставим после, т.к. он принимает несколько файлов
    if ($t.Ref) { $args2 += @("-i", "`"$($t.Ref)`"") }   # образец (например, персонаж для частей тела)
    $p = Start-Process -FilePath $codex -ArgumentList $args2 `
        -RedirectStandardInput $pf -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    $running += $p
    Write-Host "→ $($t.Id)"
}
$running | ForEach-Object { $_.WaitForExit() }
foreach ($t in $todo) {
    $ok = Test-Path (Join-Path $root $t.Rel)
    Write-Host ("{0,-20} {1}" -f $t.Id, $(if ($ok) { "готово" } else { "НЕ ПОЛУЧИЛОСЬ (см. assets/gen/_logs)" }))
}
