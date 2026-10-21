@echo off
rem Песочница ботов: без своего персонажа. Tab — следить за ботом, F — свободная камера, WASD/Shift — лететь, 1-4 — скорость
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --spectate
