---
name: save-safety
description: "Debug/headless runs must never read or write the player's world save; close the running game before launching another"
metadata:
  type: feedback
---

Отладочные прогоны (`--simtest`, `--shot`, `--do` …) не должны трогать `%APPDATA%\Godot\app_userdata\Rastvordd\world_save.json`; сохранение включено только при запуске без аргументов. Перед новым запуском игры закрывать уже открытую.

**Why:** 2026-10-21 фоновый прогон чуть не перезаписал мир пользователя (файл уцелел).
**How to apply:** новые флаги отладки — только через аргументы; не ослаблять `_saveEnabled`. См. [[rastvordd-game-concept]].
