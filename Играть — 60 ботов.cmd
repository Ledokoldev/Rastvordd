@echo off
rem Своя игра: ты + 60 ботов. Отдельный мир (сейв world_save_60.json) — основной мир из «Играть.cmd» не трогается.
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --bots=60 --save=60
