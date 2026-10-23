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

## Выбранные кадры (2026-10-23)
| Клип | Стартовый кадр |
|---|---|
| 0 · Пролог | `ref_lift_booth_n4.png` |
| 1 · Спуск | `shot1_lift_descent_n4.png` |
| 2 · Двери | `shot2_doors_back_n3.png` |
| 3 · Сбоку: фонари и жест | `shot3_side_signal_n4.png` |
| 4 · Во тьму | `shot4_into_dark_n4.png` |
Референсы бойцов: `ref_sobr_n4.png`, `ref_ozk_n4.png`.

## Клип 0 — пролог, будка лифта (`ref_lift_booth_n4.png`, 3 с)
```
Cinematic shot. The surface elevator head-house of a secret Soviet bunker at overcast dusk: a massive concrete blockhouse with a faded red star, a steel headframe with a cable winch, loudspeaker horns and amber warning lamps on the corners, wet ground, birch forest behind. The amber lamps start flashing, a siren wails, the big winch wheel begins to turn and the steel cable jerks downward. Slow push-in toward the building, light drizzle. Photorealistic, 35mm anamorphic, desaturated cold color grade, film grain. No music, no text.
Sound: rising siren wail, wind, heavy clank of the winch starting.
```

## Клип 1 — спуск (`shot1_lift_descent_n4.png`, 5 с)
Полный промт — в чате (проверка оружия и экипировки, остановка с ударом). Коротко:
```
[общий блок] Inside a huge Soviet freight elevator cage descending, concrete shaft sliding upward past the mesh walls, red lamps swaying. Both soldiers run silent pre-combat checks: Soldier A press-checks his rifle chamber and tugs his plate carrier strap tight, Soldier B presses his palm over the gas mask filter to check the seal and taps the dosimeter. The cage jolts to a hard stop, dust falls, both look up. Static medium-wide shot from the front, subtle vibration.
Sound: cable hum, metal rattling, rifle clicks, gas-mask breathing, heavy clunk on stop.
```

## Клип 2 — двери (`shot2_doors_back_n3.png`, 3 с)
```
[общий блок] Over-the-shoulder shot from behind the two soldiers inside the elevator cabin, red light from the ceiling lamps. The closed double steel doors in front of them grind open, revealing a pitch-black concrete corridor. Both soldiers slightly raise their rifles toward the darkness. Static camera, very slight push-in.
Sound: grinding steel doors, a hiss of air, low ventilation drone.
```

## Клип 3 — сбоку: фонари и жест (`shot3_side_signal_n4.png`, 4 с)
```
[общий блок] Strict side profile, static camera perpendicular to the soldiers standing on the threshold of the huge freight elevator cage, a dark corridor ahead. They click on their underbarrel flashlights — two white beams cut through the dust — and exchange a quick glance. Soldier A raises his left hand with the elbow bent at 90 degrees, fingers forming a finger-gun pointing forward, and pumps the forearm forward once — the military "move forward" signal — then both raise their rifles and step forward out of the cage, aiming.
Sound: two flashlight clicks, rubber suit squeak, boots stepping onto wet concrete.
```
Если жест ломается — убрать фразу про жест: «включили фонари → переглянулись → вскинули → шагнули».

## Клип 4 — во тьму (`shot4_into_dark_n4.png`, 4 с)
```
[общий блок] Almost pitch-black long concrete corridor, a few dim red emergency lamps far away. The two soldiers, seen from behind as dark silhouettes, walk away from the camera in tactical formation, flashlight beams sweeping the walls and pipes, quickly getting smaller and dissolving into the darkness until only two faint beams remain, then black.
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
