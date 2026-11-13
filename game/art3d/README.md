# Модели для 3D-клиента

Модели делает `python scripts/gen-3d-assets.py` (109 .glb). Положи сюда свой .glb с тем же именем — встанет он.
Имена и правила — `docs/design/27-3d-client.md`.

Коротко: 1 единица = 1 м, Y вверх, опорная точка на земле в центре, «перед» модели вдоль +X.
Персонажи — из частей hips/torso/head/arm_l/arm_r/leg_l/leg_r (их двигает анимация).

Примеры: `char_player.glb`, `held_weapon_akm.glb`, `node_tree_forest_0.glb`, `node_stone_0.glb`, `deploy_workbench.glb`,
`loot_crate_army.glb`, `decor/car_wreck.glb` (модель декора РТ, подгоняется под след).

## hq/ — готовые модели (Poly Haven, CC0)
`hq/<имя>.glb` важнее сгенерированной `<имя>.glb` (`Models3D` ищет сначала в `hq/`). Удалить файл — вернётся сгенерированная.
Источник — [Poly Haven](https://polyhaven.com/models), лицензия **CC0** (можно всё, указывать автора не обязательно).
Конвейер: список `scripts/polyhaven-models.json` (наше имя → ассет Poly Haven, `maxTris`, `tint`, `metal`, `flip`) →
`scripts/polyhaven-fetch.ps1` (качает gltf 1k в `tools/polyhaven/`, кэш не в git) →
`…_console.exe --headless --path game --script <полный путь>/scripts/polyhaven_convert.gd` (упрощает меш, красит руду,
кладёт в габариты старой модели, пишет glb с JPEG-текстурами). Превью «старая | новая»: тот же запуск без `--headless` и с `-- --preview=путь.png`.
Деревья пока свои: у Poly Haven они от 0,3 до 17 млн треугольников, листва карточками при упрощении разваливается.
