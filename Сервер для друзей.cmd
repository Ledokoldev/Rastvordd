@echo off
chcp 65001 >nul
rem Своя игра, открытая друзьям (D73): ты + 20 ботов, сервер на UDP 7777, сейв как обычно.
rem Если рядом лежит tools\frp\frpc.exe и tools\frp\frpc.toml — заодно поднимает проброс через VPS (docs/multiplayer.md).
if exist "%~dp0tools\frp\frpc.exe" start "frpc" /min "%~dp0tools\frp\frpc.exe" -c "%~dp0tools\frp\frpc.toml"
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --bots=20 --host
