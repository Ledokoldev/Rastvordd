# Библиотека по дизайну локаций (собрано 2026-11-13)

Откуда взяты приёмы для скиллов `.claude/skills/rt-*` (Claude подхватывает их в этом проекте сам).

## Скиллы проекта
| Скилл | О чём |
|---|---|
| `rt-design` | маршрутизатор: роль РТ, топология, сценарий входа, дистанции, чек-лист приёмки, метрики |
| `rt-vertical` | этажи, лестницы, люки, шахты, читаемость в 3/4, «окна в вертикали» |
| `rt-layout-check` | граф комнат с ключами, входы/отходы горячей зоны, темп; скрипт `scripts/rt-check.py` |
| `rt-procgen` | сид, скелет → модули → проверка, прогон по сидам; WFC — только для обстановки |
| `rt-pve-loot` | охрана как темп риска, подкрепления, лут как язык маршрута, ключевые комнаты |
| `rt-extraction` | виды выхода, давление времени, анти-кемп, матрица вход × выход |

## Источники на GitHub
| Репозиторий | ★ | Лицензия | Что взяли |
|---|---|---|---|
| [gamedev-skills/awesome-gamedev-agent-skills](https://github.com/gamedev-skills/awesome-gamedev-agent-skills) | 8,7k | Apache-2.0 | скиллы `level-design` (граф с ключами, темп-пила, порядок блокаут → проверки → одевание), `procedural-gen` (сид, проверка, прогон сидов) |
| [momowangOUO/extraction-shooter-map-level-design-whitepaper](https://github.com/momowangOUO/extraction-shooter-map-level-design-whitepaper) | — | — | белая книга по картам экстракшн-шутеров: 3 класса маршрутов, 8 вопросов сценария входа, ≥ 3 входа / ≥ 2 отхода, дистанции боя, охрана как темп, анти-кемп, чек-лист и метрики (пересказ, без копий текста) |
| [bytecauldron/awesome-level-design](https://github.com/bytecauldron/awesome-level-design) | 186 | CC0 | список: Level Design Book, «An Architectural Approach to Level Design» (Totten), «Push and Pull», доклады GDC |
| [Roobyx/awesome-game-design](https://github.com/Roobyx/awesome-game-design) | 715 | — | общий список материалов по геймдизайну |
| [mxgmn/WaveFunctionCollapse](https://github.com/mxgmn/WaveFunctionCollapse) | 25k | MIT | WFC — для обстановки и «мусорных» зон |
| [BorisTheBrave/DeBroglie](https://github.com/BorisTheBrave/DeBroglie) | 532 | MIT | WFC на C# с ограничениями (путь, связность) — если генерировать в игре |
| [AlexeyBond/godot-constraint-solving](https://github.com/AlexeyBond/godot-constraint-solving) | 466 | MIT | WFC для Godot |
| [thebracket/roguelike-celebration-2020](https://github.com/thebracket/roguelike-celebration-2020) | 105 | — | доклад о генерации карт (BSP, клеточные автоматы, дрейф, Voronoi) |
| [audinowho/RogueElements](https://github.com/audinowho/RogueElements) | 63 | MIT | генерация карт C#: графы комнат, шаги генерации |

## Читать по порядку
1. Level Design Book — https://book.leveldesignbook.com/ (блокаут, метрики, поток).
2. Белая книга экстракшн-карт (ссылка выше) — разделы 2–11 и чек-лист 14.
3. «Push and Pull» — ведение игрока светом и ориентирами.
4. Totten, «An Architectural Approach to Level Design» — пространство, укрытие/обзор («prospect & refuge»).
