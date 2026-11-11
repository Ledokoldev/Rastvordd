@echo off
rem Песочница ботов в 3D: без своего персонажа. Tab - следить за ботом, F - свободная камера (WASD), 1-6 - скорость, Z/X - поворот камеры
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" res://Main3D.tscn -- --spectate
