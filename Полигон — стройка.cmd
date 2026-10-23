@echo off
rem Полигон: стройка. Ровная площадка, свой шкаф, бесконечные ресурсы, мгновенный крафт, без гниения, всегда день.
rem F2 - панель (снести всё, верстаки, ресурсы). Свой мир не трогается.
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --fullscreen --path "%~dp0game" -- --sandbox=build
