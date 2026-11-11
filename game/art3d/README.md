# Модели для 3D-клиента

Модели делает `python scripts/gen-3d-assets.py` (109 .glb). Положи сюда свой .glb с тем же именем — встанет он.
Имена и правила — `docs/design/27-3d-client.md`.

Коротко: 1 единица = 1 м, Y вверх, опорная точка на земле в центре, «перед» модели вдоль +X.
Персонажи — из частей hips/torso/head/arm_l/arm_r/leg_l/leg_r (их двигает анимация).

Примеры: `char_player.glb`, `held_weapon_akm.glb`, `node_tree_forest_0.glb`, `node_stone_0.glb`, `deploy_workbench.glb`,
`loot_crate_army.glb`, `decor/car_wreck.glb` (модель декора РТ, подгоняется под след).
