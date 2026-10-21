# История работы над «Раствордд»

Подробная хронология: что просил пользователь (его слова — дословно в `user-messages.md`, вся переписка — в `conversation.md`), что сделано, какие проблемы всплыли и как решены, какие решения приняты. В конце — полный список коммитов.

Сессии Claude Code (журналы в `~/.claude/projects/C--Users-Xbaza-Desktop-Enter-the-Gungeon-v2-1-9/`): `39e9e17d` и её ветка `e6ae46fe` (20.10, концепт → прототип → ночная работа), `7a4e3ffa` (21.10 ночью, поиск звуков), `e2d320fe` (20.10 23:17 → 21.10, основная). Время — московское.

---

## 20.10, вечер — концепт

**Пользователь (дословно — №1–8):** игра «внебрачный сын Rust и RimWorld»: процедурная карта с биомами и фиксированными РТ, фарм, стройка базы как в Расте, вид сверху как в RimWorld; боты, имитирующие игроков Раста (вайп с нуля, рашат РТ, строят базы, роумят, убивают и лутают друг друга, разный интеллект, дуо до двух), плюс тупые боты-охрана РТ; механики по блокам (голод, ХП, ресурсы, одежда, броня, оружие, боты); свои ассеты через локальный генератор (ComfyUI), остальное — бесплатные паки; «экстракшн»-азарт: всё или ничего.

Ответы на вопросы: WASD + мышь (как геймпад против клавиатуры: мышью-кликами драться невозможно); сначала локально, мультиплеер потом; длина вайпа и перенос — настраиваемые; число ботов — от размера карты, считать на своём компе; модели людей с простыми анимациями, без «болванчиков» RimWorld; сеттинг — трушный постапокалипсис как в Расте (без кевларовой упашности); ранения по частям тела — максимум упрощённо, скорее нет; движок — любой, лишь бы работал; делают вдвоём с Claude. Любит: рейды, закрытые ящики, кемпить людей с лутом, «казино-рояль» на РТ с выносом ценного, крейты как на карго, high risk — high reward.

Дальше: стабильность «вширь, а не ввысь» (нельзя 30 000 дверей), голод и вода — нескучные, без задалбывающих животных; архетипы ботов по реальным игрокам Раста; бот с фулл-лутом не дерётся с каждым встречным; торговцы скучные — убрать или заменить; вид 3/4 с четырьмя диагональными позами. Попросил: папку на рабочем столе и записывать ВСЕ идеи, периодически перечитывать; использовать GPT («Астра») пока есть подписка.

**Сделано:** документы дизайна (`docs/00-vision.md`, `docs/design/01…10`, `docs/decisions.md`, `docs/ideas-log.md`), движок Godot 4.7.2 .NET, прототип этапа 0: сервер-авторитарная симуляция, стрельба, охрана РТ, лут; вид 2D 3/4 с зеркальными позами.

## 20.10 ночь → 21.10 утро — прототип и ночная работа

**Пользователь (№9–17 и далее):** охрана убивает мгновенно — «shoot on sight»; сдвиг камеры к краю экрана, но только в одну сторону ценой обзора; спринт только вперёд (оглядываешься — переходишь на шаг); поле зрения как в Project Zomboid (ровные края, не сильное затемнение), сдвиг камеры только при прицеливании (ПКМ). Этап 1 — фарм и крафт.

**Сделано (подробно — `docs/night-report.md`, `docs/roadmap.md`):** голод/вода, ресурсные узлы, пояс, крафт с очередью, печь, верстак; мир 400 → 1000 м с биомами пятнами, 5 малых РТ по карточкам Астры, плавание; день и ночь, свет; звуки (сэмплы пользователя, акустика, Steam Audio HRTF); риг персонажа из частей (IK, удары, присед, смерть, вздрагивание); строительство (этап 2): план, молоток, 5 уровней, межевой столб, взрывчатка, замки, огонь; сохранение мира и досчёт офлайна; первые боты-игроки (фермеры Сёма и Жека, бродяга Толян).

## 21.10, утро — меню и названия

**Пользователь:** «нужно сделать сильно проще. С менюшками… у печки должна быть своя менюшка по нажатию… У верстака… я не очень понимаю, как базу тоже строить». Названия утверждены (D27): Шкаф, каркас, Самодельная бомба, Взрывчатка, План, Киянка, Коктейль Молотова.

**Сделано:** окна по E (печь с ячейками и «Зажечь», ящик, шкаф с содержанием, верстак → крафт), чек-лист «Как построить дом», меню элементов по ПКМ.

## 21.10 — инвентарь как в Arc Raiders

**Пользователь:** инвентарь блоками-квадратиками, единый вид; при открытии ящика ячейки раскрываются по одной («гемблинг»), лутание затягивается; меню стройки — тоже сеткой («супертопорно»); персонаж с текущим оружием; менеджмент как в Arc Raiders / Таркове. Выбор: «как в Arc Raiders» (ячейки, стаки, рюкзак 24), «таскание мышкой + ховерлут».

**Сделано:** рюкзак 24 ячейки со стаками, пояс 6, перетаскивание, Shift+клик/ПКМ — перенос, Shift+проводка — ховерлут; обыск ячеек по одной (время от редкости, звон по редкости); тематический UI (ржавые рамки, пиксельный шрифт), превью персонажа с предметом в руках; сетка стройки с иконками.

**Пользователь:** «постапок прям… нужно попроще, поминималистичнее» → **переделано** в плоский минималистичный стиль (тёмные панели, квадраты, Rubik) — правило на будущее.

## 21.10 — стройка понятнее, миникарта

**Пользователь:** «поставил фундаменты, прокачал, а дальше что? Как стены выбирать»; «сделаю мини-карту — не понимаю, что где находится»; «чтобы выбирал колесиком, как в Расте».

**Сделано:** умный План (курсор на фундаменте — стена на ближайшем краю, на проёме — дверь), полоска элементов над поясом, колесо мыши; миникарта (местность, РТ, свои постройки, дом со стрелкой и расстоянием); радиальное колесо по ПКМ как в Расте.

**Пользователь:** непонятно, сколько ХП у построек; дубль оружия (второе торчит из торса); что дальше по роадмапу.
**Сделано:** подсказка прочности у курсора; дубль — старый спрайт оружия включался после смерти/респауна у моделей со скелетом → исправлено.

## 21.10 — подборы, обыск, крафт, баланс, дерево изучения

**Пользователь:** «не рисуется, сколько что ты подобрал, что это вообще такое»; «быстрее прогружались контейнеры»; «интерфейс крафта нормальный, тоже квадратиками — текстом в углу невозможно»; «баланса нет: с дерева 150, а стена 300 — пять карт вырубить; опусти ценники постройки»; «дерево изучения на верстаке, чтобы понятно, что изучено, что дальше, что делает»; «выкидывать предметы; раскрываться постепенно только крейты и люди, и не повторно — сачель падает как рюкзак и ждать, пока откроется — такого не должно».
Выбор по дереву: платить металлоломом, верстаки 1–3, с начала — примитив.

**Сделано:** лента подборов справа внизу, описания предметов, обыск в 2,5 раза быстрее; окно крафта (вкладки, сетка, карточка «есть/нужно», количество, очередь); цены построек в 4–6 раз ниже, первые инструменты вдвое дешевле (D29); дерево изучения (D28) с окном, верстаки 2/3 (арт через Codex); выброс за окно инвентаря, обыск только ящиков РТ и трупов и один раз (D30).

**Пользователь спросил:** «боты всё это время умели собирать и крафтить? Тот, что пришёл ко мне с огнестрелом за полторы минуты…» → ответ: огнестрел у охраны РТ, боты-игроки крафтили только примитив.

## 21.10, день — автономный час: РТ, лом, боты, античит

**Пользователь:** «интересные РТ, разные варианты добычи скрапа, погоняй ботов — я уеду на час»; затем «открываются штуки сквозь стены и двери; чужие базы должны быть серым — читы»; «песочница: только боты, я наблюдатель; цели, кто быстрее, как отбиваются от рейдов».

**Сделано:** бочки с хламом и кучи хлама (D31), лом у охраны; детали, переработчик, сейф под карту доступа (D33); античит (D32): E только «рукой», туман войны для построек и установленного с памятью «как запомнил»; прогрессия ботов (лом → верстаки → изучение), вехи гонки; режим наблюдателя `--spectate` (D34); исправлены зависания ботов (стена, бочка с водой), голод/лечение, петля «сложить — забрать всё»; вооружение фермеров (самопал, порох из серы и угля), рейдер (бомбы на дверь, вынос ящика).

**Важное:** найдено, что прогон `--simtest` без скриншота загружал и мог перезаписать сохранение пользователя → свои процессы остановлены, файл проверен (цел), правило: сохранение — только при запуске без аргументов.

**Пользователь:** «на РТ с обычных болванов не должен падать фулл-лут — выносится 4–5 калашей»; «никогда калаш не должен выпадать» → охрана без оружия в луте (D35).

**Пользователь:** запуск на виртуалке → переносная сборка `dist/Rastvordd-VM.zip` (движок + проект, .NET 8 Runtime, OpenGL-ярлыки).

## 21.10, день — боты тупят на стройке, треугольники

**Пользователь:** «эти ребята адски тупят на строительстве, нужно качественнее»; «давай треугольные фундаменты» → выбор «как в Расте».

**Сделано:** бот вставал на место своей стены («мешает человек» 7 минут) → отходит; тупики у углов → поиск пути A* (D36); треугольные фундаменты (D37): рёбра как пары вершин, стены/окна/проёмы/двери на наклонных рёбрах, призрак с сервера, 6 элементов в колесе.

## 21.10, вечер — мозг ботов v2 и чертежи баз

**Пользователь (вставкой):** переизобрести мозг ботов как ИИ-агентов с наборами инструментов (стройка/ПвП/ПвЕ/фарм), система решений (стеш, гир, выстрелы — идти или нет, антирейд), свои варианты баз в 3–4 этапа из квадратов и треугольников, выбор плана в начале вайпа, знание лута, учиться на ошибках; «тулы — это все возможные действия: как собираться на РТ, драться, кайт, пикать, строиться, когда пушить и отступать, что лутать, сейчить, приоритет — своя безопасность». Порядок: «каркас + слух». Вопрос: «как в RimWorld работают боты?» → «впиши в план и делай чертежи баз».

**Сделано:** `docs/design/bot-brain.md` (архитектура, идеи RimWorld: гибрид лестницы и очков, работа-цепочка с отменой, резервации, тильт, лорды для групп, рассказчик); каркас мозга: доска знаний, директор целей, слух (решения «иду/не иду: причина»), антирейд, журнал мыслей; чертежи баз: 3 плана по характеру, стадии кибитка → мейн → соты → финал, исполнитель стадий, ходьба через свои двери.

**Найдено и исправлено по дороге:** с треугольниками рамки стен стали расширяться во все стороны — косяки сузили проём до 0,7 м (не проходили ни бот, ни игрок); бот «кормил шкаф» через стену и жал E на соседний ящик → подход «рукой» и прямое использование цели; сетка поиска пути не попадала в дверной проём → выровнена по миру, шаг 0,25 м.

**Итог прогона 60 мин:** кибитка к 5-й минуте, мейн к 18-й, соты к 36–50-й, верстак 2 к 36–47-й, без зависаний.

## 21.10, вечер — перенос в отдельный проект Claude Code

**Пользователь:** «создал проект в клод коде, можно всё туда перенести?» (папка `Desktop\Rastvordd`); «супер подробно, со всеми нюансами и особенно тем, что я сам писал текстом — захардкодить в файл, и историей работы».

**Сделано:** `AGENTS.md` (сборка, запуск, тесты, правило сохранения, коммиты, стиль UI), `docs/handoff.md`, журнал идей за 21.10, `scripts/bot-stalls.py`, `scripts/export-history.py`, эта папка `docs/history/` (все сообщения дословно, вся переписка, история работы), память Claude для нового проекта.

---

## Все коммиты

| Время | Коммит | Что |
|---|---|---|
| 2026-10-20 20:46 | `a023ddf` | Add Rastvordd design docs and idea log |
| 2026-10-20 20:59 | `2ceee59` | Add stage 0 prototype: server-authoritative sim, top-down shooting, NPC guards, loot |
| 2026-10-20 21:05 | `81c38fe` | Switch prototype client to 2D 3/4 view with mirrored diagonal poses |
| 2026-10-21 00:43 | `228f7c5` | Add edge peek and forward-only sprint, give players the first shot against RT guards |
| 2026-10-21 00:51 | `4ca297d` | Shift camera only at screen edge, crisp lighter vision cone |
| 2026-10-21 00:53 | `68ce18b` | Move look-ahead to right-mouse aiming with narrower cone and shorter reach |
| 2026-10-21 01:07 | `4ec80ed` | Add stage 1: resource nodes, tools, hunger and water, crafting queue, furnace, workbench, 6-slot belt |
| 2026-10-21 01:13 | `85747fc` | Add Codex asset pipeline, art bible and first generated environment assets |
| 2026-10-21 01:26 | `75be32f` | Pixelize generated art and use it in game: trees, ore, ground, walls, props, animated furnace fire, icons |
| 2026-10-21 01:26 | `fe74ea6` | Add first weapon sprites and icons, Codex design notes on monuments and items |
| 2026-10-21 01:42 | `862ae19` | Add 400 m island with biomes, terrain shader with relief and water, decals, day/night lighting, shared palette |
| 2026-10-21 01:44 | `d798854` | Add synthesized placeholder sounds: shots, hits, swings, footsteps, loot, crafting, furnace loop |
| 2026-10-21 01:45 | `715cbe4` | Add roadmap, open questions and new decisions |
| 2026-10-21 01:49 | `040cb90` | Add generated walking character sprites (front/back, 4 frames) and sprite sheet slicing |
| 2026-10-21 01:51 | `5fa2bcf` | Add guard sprites, finer terrain mask, perf logging, regenerated snow |
| 2026-10-21 01:51 | `b289def` | Add ambient sound mixer: wind, surf, birds, crickets |
| 2026-10-21 02:11 | `43b9839` | Add day/night toggle (N), replace placeholder ruins with sprite props, drop blob decals, fix wind ambience |
| 2026-10-21 02:18 | `048a826` | Pick trees, grass clumps, rocks, props and pickups per biome |
| 2026-10-21 02:27 | `2148027` | Record quiz decisions and new roadmap order |
| 2026-10-21 02:29 | `3ea8d68` | Process biome-styled assets; grass and plants get their own palette |
| 2026-10-21 02:51 | `aeac6d9` | Zomboid-style vision: cone only, occlusion by walls and trees, shade world not characters |
| 2026-10-21 03:00 | `6e50e46` | Add crouch on Alt: slower, quieter, more accurate, hidden behind low cover |
| 2026-10-21 03:17 | `a7d5059` | Use real shot, reload and footstep samples; continuous time-stretched steps; F1 admin tuning |
| 2026-10-21 03:21 | `56e9019` | Admin: live list of playing sounds, ambient volume, stretch toggle; debug auto-walk |
| 2026-10-21 03:21 | `bba7b8f` | Remove synthesized ambience; ambient plays only user recordings |
| 2026-10-21 03:29 | `76b046e` | Add acoustics: speed-of-sound delay, distance layers, occlusion, room reverb, binaural directions |
| 2026-10-21 03:33 | `60ecaea` | Bring the world to life: biome clutter and flowers, wind sway, weather particles, fireflies |
| 2026-10-21 03:35 | `669deb0` | Gungeon-style weapon animation: recoil, ejected casings, phased reload with mag drop, swing smear, two-hand grip |
| 2026-10-21 03:35 | `40710d6` | Add naming draft for approval; update roadmap |
| 2026-10-21 03:55 | `3def04c` | Generate walk cycles from Wan 2.2 video via ComfyUI and use them in game |
| 2026-10-21 03:58 | `4a3a0c4` | Real recoil: per-weapon kick patterns, stronger bloom, gun follows recoil, camera shake |
| 2026-10-21 04:00 | `fb77f4f` | Regenerate front walk cycle with fixed orientation and frame filtering |
| 2026-10-21 04:10 | `9f378b2` | Cutout rig for the player: generated body parts, IK arms on weapon, procedural legs, aim/run/crouch/reload/swing poses, pixel-snapped rendering |
| 2026-10-21 04:14 | `612e0db` | Delay melee impact, per-tool swing arcs, hit chips; fast Lightning walk regen |
| 2026-10-21 04:20 | `960899c` | Rig for guards, pose gallery, weighted tool swings, half-kneel crouch |
| 2026-10-21 04:22 | `e4751d0` | Steam Audio HRTF for positional sounds with fallback to own binaural |
| 2026-10-21 04:26 | `20b79ad` | Spatial hash for walls, nodes and deployables (collisions, LOS, projectiles, snapshots) |
| 2026-10-21 04:28 | `cba032a` | Hit-stop on tool impact, canopy fade by body overlap, --near and --burst debug args |
| 2026-10-21 04:29 | `8fe0e8d` | Tool swing plane projected into 3/4 view for front/back angles |
| 2026-10-21 04:31 | `6d881c0` | Torch: craftable, raised one-handed with pixel flame and flickering light, reduces night view penalty |
| 2026-10-21 04:40 | `cf238f0` | Building stage 2 slice A: 2 m grid, plan and hammer, foundations/walls/doorways/windows/doors, 5 tiers, structure damage rules, cutaway walls |
| 2026-10-21 04:42 | `619c748` | Hammer and plan sprites, door textures; debug script walk/aim actions |
| 2026-10-21 04:45 | `85d2c5e` | Land claim post: zone radius 8 with 21-cell gap, construction budget, door/foundation caps, upkeep with grace and decay |
| 2026-10-21 04:45 | `1bab983` | Claim post sprite; freeze world palette and add --only to asset processing |
| 2026-10-21 04:50 | `c67575e` | Raid explosives: satchel (dud chance) and timed charge, arc throw, sticking, tiered blast damage, explosion fx and synthesized sounds |
| 2026-10-21 04:52 | `85fbb22` | Code lock on doors: random code for installer, code entry prompt, shock on wrong code |
| 2026-10-21 04:52 | `aa29b4e` | Docs: building stage progress |
| 2026-10-21 04:54 | `aa63e14` | Storage box (E take all, Alt+E deposit), boxes and claims break from explosives and spill loot |
| 2026-10-21 04:54 | `b0aee3c` | Controls help: plan, box, raid kit |
| 2026-10-21 04:56 | `9183570` | World save/load: nodes, structures, claims with stock, boxes, bags, players; autosave each minute, F5, on quit |
| 2026-10-21 04:57 | `0d0f3b8` | Offline catch-up on load: claim upkeep, grace and decay, respawns, away report |
| 2026-10-21 05:06 | `798cca6` | First player bots (farmer): naked start, gather, craft tools, pick a site, claim, build 2x2 house with door, upgrade to wood, box, upkeep, eat/drink, flee threats; fast-forward sim test |
| 2026-10-21 05:06 | `66cee3f` | Docs: save, offline catch-up, first bots; bot base screenshot; --simtest home option |
| 2026-10-21 05:07 | `226ef22` | Admin: new wipe button (deletes save on player's request and restarts) |
| 2026-10-21 05:09 | `7b850f6` | 1 km island with patchy biomes; hammer targets floor unless aiming near an edge |
| 2026-10-21 05:11 | `6e711d5` | Five small monuments from Astra's cards: fence with gates, building, cover, crates, guards; concrete yards, names on entry, no building nearby |
| 2026-10-21 05:12 | `858491f` | Save version 2 for the 1 km map; roadmap |
| 2026-10-21 05:13 | `5676272` | Swimming: water is passable at reduced speed, no weapons, waist-deep rig with ripples |
| 2026-10-21 05:15 | `d5d272b` | Key lock: key item per lock (lootable), keys stay in pocket on deposit; bots lock their doors |
| 2026-10-21 05:17 | `c652bf0` | Fire: molotov bottle creates a burning zone that destroys twig/wood and hurts people; glass and whoosh sounds; --buildspot debug |
| 2026-10-21 05:18 | `bf1ea91` | Island map on M: biomes, monuments with names, player arrow, home claim; monuments spaced 130 m+ |
| 2026-10-21 05:21 | `8bbde01` | Roamer bot archetype: gears up (spear, bow, arrows), hunts weaker players and bots, loots corpses, roams monuments and beaches; death log in fast-forward |
| 2026-10-21 05:22 | `590116d` | Bots steer around guarded monuments; docs |
| 2026-10-21 05:22 | `5d58091` | Night report |
| 2026-10-21 05:24 | `ee14d3e` | Sleeping bag: respawn at your bag (B) with 5 min cooldown; bots lay bags at home; bots blacklist unreachable nodes |
| 2026-10-21 05:25 | `eec7022` | Sleeping bag sprite |
| 2026-10-21 05:25 | `ba061e4` | Report: sleeping bag |
| 2026-10-21 05:26 | `08d9f37` | Report tidy-up |
| 2026-10-21 05:48 | `ce48b9f` | Rig death pose: lying spread-eagle, weapon dropped, centered on death point |
| 2026-10-21 06:10 | `b39d688` | Rig flinch on hit: torso and head jerk away from the attacker, brief red tint |
| 2026-10-21 06:37 | `8c9878e` | Farmer bots upgrade the house to stone (stone from box or gathered); hammer targeting, compact corner box, bag ignores E, roamer hoarding fix |
| 2026-10-21 06:58 | `69de7e1` | HUD: claim limits line while holding the building plan |
| 2026-10-21 07:20 | `58f5938` | Swim sounds: splash on entering water, strokes while swimming; --sfxlog counter |
| 2026-10-21 07:41 | `34610e1` | Crate loot tiers by monument guard level; crate summary in --simtest log |
| 2026-10-21 08:04 | `5512647` | Farmer bots build a furnace, mine metal ore and smelt it in batches |
| 2026-10-21 08:26 | `20494c0` | Hide ground decor on building floors, restore when floors are destroyed |
| 2026-10-21 08:48 | `9d839d5` | Bots react to raids: wait out stronger raiders, then repair damaged pieces and the door; --raidtest debug |
| 2026-10-21 09:11 | `32df909` | WIP: container windows by E (box, bag, furnace with on/off, claim upkeep, workbench opens craft) |
| 2026-10-21 09:18 | `a4cb9d1` | Approved names (D27): Шкаф, каркас, Самодельная бомба, Взрывчатка, План, Киянка, Коктейль Молотова |
| 2026-10-21 09:21 | `de09993` | Windows by E work (furnace on/off, put/take); RMB build menu with costs and hints; step-by-step build guide with checkmarks |
| 2026-10-21 09:32 | `a510407` | Arc Raiders-style inventory: 24-slot pack with stack limits, drag and drop, Shift+click/RMB quick move, Shift hover-loot, progressive search reveal with rarity chimes; themed UI (rusty metal frames, slots, pixel fonts), character preview with held item |
| 2026-10-21 09:37 | `2d5ec40` | Minimal flat UI: dark translucent panels, flat square cells, thin headers, Rubik only; build menu as a grid of icon tiles with costs (red when unaffordable), keys 1-5 pick |
| 2026-10-21 09:46 | `228f89a` | Smart build plan: cursor on a foundation places a wall on the nearest edge, on an empty doorway a door; build bar above the belt, mouse wheel cycles pieces; clearer house guide |
| 2026-10-21 09:50 | `83ed47d` | Minimap (top right, north up, 40 m radius): terrain, monuments with names, own structures and deployables, home marker with edge arrow and distance; info debug action |
| 2026-10-21 09:58 | `da2a3af` | Rust-style radial build wheel: hold RMB with the plan, point at a sector, release to pick; scroll wheel still cycles |
| 2026-10-21 10:37 | `a28e9c6` | Fix duplicate weapon on respawned rigged actors (legacy held sprite re-shown after death); structure tooltip under cursor with tier and HP bar |
| 2026-10-21 10:37 | `ae51c07` | Roadmap: interface work, names and building leftovers done |
| 2026-10-21 10:41 | `445430e` | Pickup feed (+N item, bottom right, merges repeats); item descriptions in inventory tooltips; container search ~2.5x faster |
| 2026-10-21 11:04 | `994adb3` | Craft window: tabs, square recipe grid, recipe card with have/need, amount and max, queue with progress; icons mapped for deployables, locks and explosives; building costs 4-6x lower and early tools/stations ~2x lower, bot thresholds adjusted |
| 2026-10-21 11:17 | `1596f4d` | Tech tree at the workbench (scrap, workbench levels 1-3, starter primitives known), research window with stats and requirements; craft window shows locks and bench levels; drop items by dragging out of the inventory; search only crates and corpses, once per player |
| 2026-10-21 11:57 | `23b1aeb` | Scrap sources: breakable loot barrels and junk piles at monuments and in the wild, guards carry scrap; interact only within reach (no looting through walls/doors); fog of war for structures and deployables with per-player memory, remembered pieces drawn gray |
| 2026-10-21 12:06 | `ad39a6c` | Bots progress through scrap, workbench 1-3 and research (milestones log); components, recycler and keycard safes at monuments; spectator sandbox mode (--spectate) with bot panel and time scale; wander/roam unstuck, bandaging, gentler regen |
| 2026-10-21 12:07 | `fda66da` | Spectator: hide own HUD, panel at top; launcher for bot sandbox |
| 2026-10-21 12:17 | `7b60543` | Recycler starts off; debug actions peace/rec |
| 2026-10-21 12:18 | `7326d38` | Never load or write the player's world save in runs with any debug/command-line flags (simtest, spectate, scripted tests) |
| 2026-10-21 12:20 | `29ca063` | Bots: needs (heal, drink, eat higher when hurt) before roaming; farmers craft pipe gun and pistol ammo (sulfur, charcoal, gunpowder) after the tree and defend when not weaker; bots shoot guns with reloads; weapon craft milestones; sandbox journal |
| 2026-10-21 12:22 | `169c824` | Safe: own title and hint, central safe moved away from the water barrel |
| 2026-10-21 12:22 | `b62f82f` | Sandbox journal: runs 2 and 3 |
| 2026-10-21 12:24 | `42b6377` | Raider pattern: pick the cheapest enemy door, build satchels (gunpowder pipeline), throw from outside and wait, loot the enemy box; raid milestones; --raidkit debug |
| 2026-10-21 12:25 | `8c3c416` | Bots take only the needed item from their box (no deposit/take-all loop) |
| 2026-10-21 12:27 | `53ccaac` | Bots see items in the pack and move them to the belt to hold (no endless re-crafting when the belt is full) |
| 2026-10-21 12:30 | `51db636` | Box take diagnostics; journal: raid runs |
| 2026-10-21 12:37 | `791378c` | Monument guards are not full loot: never drop weapons, only some ammo, scrap, keycard, sometimes bandage/food |
| 2026-10-21 12:44 | `da868cd` | Ignore dist/ (portable VM build) |
| 2026-10-21 13:20 | `5c2cf7e` | Bot navigation: A* on a 0.5 m grid around obstacles (walls, trees, rocks, deployables) when the straight line is blocked or the bot is stuck, string-pulled paths; bots step off a wall's footprint before placing it and skip pieces blocked by others; interact target in bot log |
| 2026-10-21 13:33 | `f7f83cd` | Triangle foundations (Rust-like): attach to any free floor edge (square or triangle); edges keyed by quantized vertex pairs so squares and triangles share edges; walls, windows, doorways and doors on slanted edges (collision as a chain of small boxes, drawn as smooth prisms); server-side ghost geometry; fog, tooltip, minimap, explosions, saves; 6-piece wheel and build bar |
| 2026-10-21 13:34 | `38f8132` | Decisions: bot pathfinding, triangle foundations |
| 2026-10-21 14:55 | `21ce919` | Bot brain v2 framework: blackboard (load, stash value, heard fights, threat), utility director with commitment, goals (flee, fight, anti-raid, hunt, needs, investigate gunfire, loot corpse, gear, base, roam) weighted by archetype; hearing shots/explosions with go/no-go reasoning; thought journal in sim log and spectator panel; item value table; design doc |
| 2026-10-21 15:01 | `066e1ed` | Bot brain plan: RimWorld ideas (priority+utility hybrid, cancellable jobs, reservations, tilt, lords for group raids, storyteller) |
| 2026-10-21 15:16 | `a9783da` | Bot base blueprints: staged plans (hut, main, honeycomb, final metal) with squares and triangles, chosen by character; stage executor builds/rebuilds, places boxes/bag, locks doors, upgrades, repairs; door-aware pathfinding (open ahead, close behind); reach-based standing and direct use of targets; fix wall boxes widening into doorways; finer 0.25 m path grid |
| 2026-10-21 15:26 | `625b178` | Handoff to the dedicated Claude Code project: AGENTS.md workflow rules (build, run, tests, save safety, commits), docs/handoff.md session summary, ideas log for 2026-10-21, bot stall analyzer script |
