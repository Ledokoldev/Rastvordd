@echo off
rem Полигон: звук (в наушниках). Выстрелы по типам с дистанции и направления, за стеной, шаги призрака, звуки по id,
rem викторина «Угадай направление» (ЛКМ или пробел - ответ). F2 - панель. Свой мир не трогается.
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- --sandbox=sound
