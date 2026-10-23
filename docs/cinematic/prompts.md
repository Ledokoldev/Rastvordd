# Заставка спуска в «Объект 42» — промты для видео

Схема: 5 клипов, ~19 с: пролог 3 с → спуск 5 с → двери 3 с → «пошли» 4 с → во тьму 4 с. Каждый клип — **image-to-video**:
стартовый кадр (это первый кадр клипа) + промт ниже. Генерировать по 5 с (минимум у большинства сервисов) и обрезать при склейке.
Опорные картинки (`ref_sobr.png`, `ref_ozk.png`, `ref_lift_booth*.png`) — прикладывать, если сервис принимает референсы
(Kling «Elements», Runway «References», Veo «Ingredients»).

Сеттинг: постсоветский остров, 1980-е, секретный запасной командный пункт «Объект 42» под землёй; лифт с поверхности —
бетонный блокгауз с красной звездой, копром с лебёдкой, рупорами и янтарными лампами; внизу — сырой бетон, ржавчина, кабели,
красный аварийный свет, пыль. Боец А — СОБР в тяжёлой броне (шлем, плитник, АК-74М с глушителем и фонарём).
Боец Б — в химзащите ОЗК и противогазе ГП-5 (АКМ с фонарём, дозиметр на груди).

## Общий блок (вставлять в каждый промт с бойцами)
```
Two soldiers in an abandoned Soviet top-secret underground bunker "Object 42", 1980s. Soldier A: Russian SOBR special forces operator in full heavy armor — dark grey-black uniform, black plate carrier, black balaclava, ballistic helmet, AK-74M with suppressor and underbarrel flashlight. Soldier B: olive-green rubber OZK hazmat suit with hood, GP-5 gas mask with round glass eyepieces and side filter, dosimeter on the chest, AKM with underbarrel flashlight. Raw concrete, rust, cable trays, hazard stripes, dim red emergency lights, dust in the air. Photorealistic, cinematic, 35mm anamorphic, desaturated cold grade, film grain. No music, no text.
```

## Клип 0 — пролог, будка лифта (старт: `ref_lift_booth_*.png`, 3 с)
```
Surface elevator head-house of a secret Soviet bunker at overcast dusk: a massive concrete blockhouse with a faded red star, a steel headframe with a cable winch, loudspeaker horns and amber lamps on the corners. The amber lamps start flashing, a siren wails, the winch wheel begins to turn and the cable jerks downward. Slow push-in, slight drizzle. Photorealistic, cinematic, 35mm, desaturated cold grade, film grain. No music, no text.
Sound: siren wail, wind, heavy winch clank.
```

## Клип 1 — спуск (старт: `shot1_lift_descent_n*.png`, 5 с)
```
[общий блок] Inside a huge Soviet freight elevator cage descending, the concrete shaft wall sliding upward past the mesh walls, red lamps swaying slightly. Soldier A racks the charging handle of his rifle, Soldier B slaps the magazine into his rifle. Then the cage jolts to a hard stop, dust falls from above, both look up. Static medium shot from the front with subtle vibration.
Sound: elevator cable hum, metal rattling, rifle clicks, gas-mask breathing, heavy clunk on stop.
```

## Клип 2 — двери (старт: `shot2_doors_back_w*.png`, 3 с)
```
[общий блок] Wide shot from the back of a big Soviet freight elevator cabin, both soldiers full body facing the closed double steel doors. The doors grind open, revealing a pitch-black concrete corridor. The soldiers stand still, slightly raising their rifles. Static camera.
Sound: grinding steel doors, low ventilation drone.
```

## Клип 3 — фонари, вскинули, пошли (старт: `shot3_side_signal_w*.png`, 4 с)
```
[общий блок] Wide shot from behind, both soldiers full body in the open doorway of the elevator cabin, a pitch-black corridor ahead. They click on their underbarrel flashlights — two white beams cut through the dust — snap their rifles up to the shoulder and step forward out of the cabin, aiming. Camera follows slowly behind.
Sound: two flashlight clicks, boots on wet concrete, gas-mask breathing.
```

## Клип 4 — во тьму (старт: `shot4_into_dark_n*.png`, 4 с)
```
[общий блок] Almost pitch-black long concrete corridor, a few dim red emergency lamps far away. The two soldiers walk away from the camera in tactical formation, flashlight beams sweeping the walls, quickly getting smaller and dissolving into the darkness until only two faint beams remain, then black.
Sound: footsteps fading, ventilation drone, a far-away metallic bang at the very end.
```

## Склейка (~19 с)
0 (3 с) → резкий стык в темноту → 1 (5 с) → стык на ударе остановки → 2 (3 с) → 3 (4 с) → 4 (4 с) → чёрный экран / название.
Сервисы обычно дают 5 с — обрезать лишнее с конца клипа (у клипа 2 оставить момент, когда двери раскрылись).
Звук: если сервис не делает звук (не Veo) — наложить отдельно.

## Где генерировать
- Veo 3.1 (Gemini Pro/Ultra, Google Flow) — лучший реализм и сразу со звуком.
- Kling — дёшево, хорошо с движением, есть ежедневные кредиты.
- Runway — больше контроля камерой.
- Локально — Wan 2.2 (image-to-video).
