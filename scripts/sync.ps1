# Две кнопки для git: Получить.cmd (pull) и Отправить.cmd (commit + pull + push). Конфликт — стоп и подсказка.
param([ValidateSet("get", "send")] [string]$Mode = "get")
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
function Conflict { Write-Host "`nКОНФЛИКТ: ты и напарник правили одно и то же. Ничего не трогай — открой Claude Code в этой папке и напиши: «разреши конфликт git»." -ForegroundColor Red; exit 1 }

if ($Mode -eq "send") {
    git add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        $msg = Read-Host "Коротко: что сделал"
        if (-not $msg) { $msg = "Правки" }
        git commit -q -m $msg
    } else { Write-Host "Своих изменений нет." }
}
git pull --rebase --autostash -q
if ($LASTEXITCODE -ne 0) { Conflict }
if ($Mode -eq "send") {
    git push -q
    if ($LASTEXITCODE -ne 0) { Write-Host "Не отправилось — проверь интернет и запусти ещё раз." -ForegroundColor Red; exit 1 }
    Write-Host "Отправлено." -ForegroundColor Green
} else {
    Write-Host "Обновлено. Собираю..." ; dotnet build game -v q -nologo
    Write-Host "Готово." -ForegroundColor Green
}
