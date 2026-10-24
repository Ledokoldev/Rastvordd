# Непрерывный цикл Астры (Codex) в ветке astra-lab (рабочая копия C:\Users\Xbaza\Rastvordd-wt\astra).
# Круг: работа по роадмапу → охота на баги; каждый 3-й круг ещё и ассеты. Первый запуск — исследование и роадмап.
# Стоп: файл STOP в каталоге логов, исчерпан лимит (сообщение о лимите в выводе) или прошло -Hours часов.
# Запуск: powershell -File scripts/astra-lab.ps1 [-Hours 24]
param([double]$Hours = 0, [string]$Track = "main", [string]$Model = "gpt-6-astra")   # 0 — вечно (стоп только файлом STOP); дорожки: main (роадмап+баги), bots (ИИ ботов, своя копия), art (ассеты)

$wt = if ($Track -eq "bots") { "C:\Users\Xbaza\Rastvordd-wt\astra-bots" } else { "C:\Users\Xbaza\Rastvordd-wt\astra" }
$pdir = Join-Path $PSScriptRoot "astra-lab"
$logs = "C:\Temp\claude\astra-lab"
New-Item -ItemType Directory -Force $logs | Out-Null
$codex = Get-ChildItem "C:\Program Files\WindowsApps" -Directory -Filter "OpenAI.Codex_*" -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "app\resources\codex.exe" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $codex) { throw "codex.exe не найден" }
$common = Get-Content -Raw -Encoding UTF8 (Join-Path $pdir "common.md")
$deadline = if ($Hours -gt 0) { (Get-Date).AddHours($Hours) } else { [datetime]::MaxValue }

function Step([string]$name, [string]$effort) {
    $stamp = (Get-Date -Format "MMdd-HHmm") + "-$Track"
    $prompt = $common + "`n`n" + (Get-Content -Raw -Encoding UTF8 (Join-Path $pdir "$name.md"))
    $pf = Join-Path $logs "$stamp-$name.prompt.txt"
    Set-Content -Path $pf -Value $prompt -Encoding UTF8
    $log = Join-Path $logs "$stamp-$name.log"
    Add-Content (Join-Path $logs "cycle.log") "$(Get-Date -Format s) [$Track] старт $name"
    $p = Start-Process -FilePath $codex -ArgumentList @("exec", "-m", $Model, "-C", "`"$wt`"", "-s", "workspace-write", "-c", "model_reasoning_effort=`"$effort`"", "--skip-git-repo-check", "-") `
        -RedirectStandardInput $pf -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    $p.WaitForExit()
    $tail = (Get-Content $log -Tail 3 -ErrorAction SilentlyContinue) -join " "
    Add-Content (Join-Path $logs "cycle.log") "$(Get-Date -Format s) [$Track] конец $name (код $($p.ExitCode)): $tail"
    # лимит — только по последним строкам ошибок (в теле лога Астра сама ищет слова вроде Quota в коде — ложные срабатывания)
    $errTail = (Get-Content "$log.err" -Tail 15 -ErrorAction SilentlyContinue) -join " "
    return ($p.ExitCode -ne 0 -and $errTail -match "usage limit|You've hit your|rate limit reached|429 Too Many|insufficient_quota")
}

function Run([string]$name, [string]$effort) {
    # шаг не роняет цикл: лимит — ждём 30 мин и повторяем, ошибка/вылет — пауза 2 мин и дальше
    while (-not (Test-Path (Join-Path $logs "STOP"))) {
        $limit = $false
        try { $limit = Step $name $effort } catch { Add-Content (Join-Path $logs "cycle.log") "$(Get-Date -Format s) ошибка шага ${name}: $_"; Start-Sleep -Seconds 120; return }
        if (-not $limit) { return }
        Add-Content (Join-Path $logs "cycle.log") "$(Get-Date -Format s) лимит ChatGPT — жду 30 мин и повторяю $name"
        Start-Sleep -Seconds 1800
    }
}

if ($Track -eq "main" -and -not (Test-Path (Join-Path $wt "docs\astra\ROADMAP.md"))) { Run "setup" "medium" }
$n = 0
while ((Get-Date) -lt $deadline -and -not (Test-Path (Join-Path $logs "STOP"))) {
    $n++
    switch ($Track) {
        "bots" { Run "bots" "medium"; if ($n % 3 -eq 0) { Run "bugs" "medium" } }
        "art"  { Run "assets" "medium" }
        default { Run "work" "medium"; if ($n % 2 -eq 0) { Run "bugs" "medium" } }
    }
}
Add-Content (Join-Path $logs "cycle.log") "$(Get-Date -Format s) цикл остановлен после $n кругов"
