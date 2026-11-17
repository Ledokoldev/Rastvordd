@echo off
chcp 65001 >nul
rem Зайти в игру к другу (D73). Свой сейв не трогается.
set /p ADDR=Адрес сервера (например 81.7.13.136:7777): 
set /p NICK=Твоё имя: 
start "" "%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" -- "--connect=%ADDR%" "--name=%NICK%"
