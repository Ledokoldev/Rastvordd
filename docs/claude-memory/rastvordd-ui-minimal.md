---
name: rastvordd-ui-minimal
description: "Раствордд UI style — user wants minimal flat UI, rejected rusty/rivet post-apocalyptic frames"
metadata:
  node_type: memory
  type: feedback
  originSessionId: e2d320fe-189f-40a7-b907-22613ec90c6f
  modified: 2026-10-21T06:37:19.976Z
---

Интерфейс Раствордд — минималистичный и плоский (тёмные полупрозрачные панели, простые квадратные ячейки, тонкие линии, один акцент, шрифт Rubik). Ржавые клёпаные рамки, полосы и пиксельный шрифт пользователь отверг как «слишком постапок».

**Why:** 2026-10-21 после первой версии инвентаря в стиле ржавого металла: «нужно попроще, поминималистичнее».
**How to apply:** новые окна/меню делать через UiTheme (Flat/DrawSlot/Header), без текстурных рамок. См. [[rastvordd-game-concept]].
