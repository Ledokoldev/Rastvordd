@echo off
rem Своя игра: ты + 20 ботов (сейв — world_save.json; новый вайп — админка F1).
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --bots=20
