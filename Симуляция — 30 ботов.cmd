@echo off
rem Bot simulation: 30 bots, no player, fresh world, nothing is saved. Tab - follow a bot, F - free camera, WASD/Shift - fly, 1-4 - speed
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --spectate --bots=30
