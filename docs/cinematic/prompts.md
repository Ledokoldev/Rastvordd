# Заставка спуска в «Объект 42» — промты для видео

Схема: 4 клипа по 5–8 с → склейка (~25–30 с). Каждый клип — **image-to-video**: стартовый кадр из этой папки + промт ниже.
Опорные картинки (`ref_sobr.png`, `ref_ozk.png`, `ref_lift_booth.png`) — прикладывать, если сервис принимает референсы персонажей
(Kling «Elements», Runway «References», Veo «Ingredients»).

Сеттинг: постсоветский остров, 1980-е, секретный запасной командный пункт «Объект 42» под землёй; лифт с поверхности —
бетонная будка с красной звездой, лебёдкой, рупорами и янтарными лампами; внизу — сырой бетон, ржавчина, кабели,
красный аварийный свет, пыль. Боец А — СОБР в тяжёлой броне (шлем «Алтын», плитник, АК-74М с глушителем и фонарём).
Боец Б — в химзащите ОЗК и противогазе ГП-5 (АКМ с фонарём, дозиметр на груди).

## Общий блок (вставлять в каждый промт)
```
Two soldiers in an abandoned Soviet top-secret underground bunker "Object 42", 1980s. Soldier A: Russian SOBR special forces operator in full heavy armor — dark grey-black uniform, black plate carrier, black balaclava, titanium Altyn helmet with the visor raised, AK-74M with suppressor and underbarrel flashlight. Soldier B: olive-green rubber OZK hazmat suit with hood, GP-5 gas mask with round glass eyepieces and side filter, dosimeter on the chest, AKM with underbarrel flashlight. Raw concrete, rust, cable trays, hazard stripes, dim red emergency lights, dust in the air. Photorealistic, cinematic, 35mm anamorphic, desaturated cold grade, film grain. No music, no text.
```

## Клип 1 — спуск в лифте (старт: `shot1_lift_descent.png`, 8 с)
```
[общий блок] Inside a heavy enclosed Soviet freight elevator cabin descending: riveted steel walls, yellow-black hazard stripes, caged red lamp on the ceiling swaying slightly. Both soldiers stand side by side and silently check their rifles — Soldier A racks the charging handle and glances into the chamber, Soldier B pulls the magazine, looks at it, slaps it back in. Shadows of the shaft slide upward through the door gap. Static medium shot from the front with subtle vibration. At the end the cabin jolts to a hard stop, dust falls from the ceiling, both look up.
Sound: elevator cable hum, metal rattling, rifle clicks, breathing through the gas mask, heavy clunk on stop.
```

## Клип 2 — двери, вид сзади (старт: `shot2_doors_back.png`, 6 с)
```
[общий блок] Over-the-shoulder shot from behind both soldiers inside the elevator. The double steel doors grind open in front of them revealing a pitch-black concrete corridor. They raise their rifles to the shoulder and slowly step forward out of the cabin, aiming into the darkness. Camera follows slowly behind at shoulder height.
Sound: grinding doors, distant dripping water, low ventilation drone.
```

## Клип 3 — сбоку: фонари и жест (старт: `shot3_side_signal.png`, 8 с)
```
[общий блок] Strict side profile shot, static camera perpendicular to them. The two soldiers step out of the elevator into the dark corridor and click on their underbarrel flashlights — two white beams cut through the dust. They exchange a glance. Soldier A raises his left arm, elbow bent at 90 degrees, hand shaped like a finger-gun pointing forward, and pumps the forearm forward twice — the military "move forward" hand signal. Soldier B nods.
Sound: flashlight clicks, muffled breathing, distant metallic creak.
```
Если жест ломается (пальцы, рука) — сгенерировать 3–5 вариантов или разделить на два клипа: «фонари и взгляд» + крупно «жест».

## Клип 4 — уходят в темноту (старт: `shot4_into_dark.png`, 6 с)
```
[общий блок] Wide shot from behind down a long dark concrete corridor with faint red emergency lamps far away. The two soldiers advance in tactical formation, flashlight beams sweeping across the walls and pipes, getting smaller and smaller until they dissolve into the darkness and only two faint beams remain, then the beams turn the corner and everything goes black.
Sound: footsteps fading, ventilation drone, a far-away metallic bang at the very end.
```

## Склейка
1 → (резкий стык на остановке лифта) → 2 → 3 → 4 → затемнение. Между 1 и 2 можно 0,3 с чёрного кадра. Звук: если сервис
не делает звук (не Veo) — наложить отдельно: гул троса, лязг, дыхание в противогаз, щелчки фонарей.

## Где генерировать
- Veo 3.1 (Gemini Pro/Ultra, Google Flow) — лучший реализм и сразу со звуком.
- Kling — дёшево, хорошо с движением, есть ежедневные кредиты.
- Runway — больше контроля камерой.
- Локально — Wan 2.2 (image-to-video).
