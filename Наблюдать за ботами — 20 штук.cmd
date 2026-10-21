@echo off
rem Песочница: 20 ботов, без своего персонажа. Tab — следить за ботом, F — свободная камера, WASD/Shift — лететь,
rem 1-6 — скорость x1/x2/x4/x8/x10/x20. Свой мир не трогается.
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --spectate --bots=20
