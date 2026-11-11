@echo off
rem Своя игра в 3D: ты + 20 ботов, камера как в Zomboid. Сейв общий с 2D (world_save.json) — не запускай обе версии сразу.
rem Новый вайп — админка F1 или Esc - Настройки.
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" res://Main3D.tscn -- --bots=20
