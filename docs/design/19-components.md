# 19. Прогрессия через компоненты

2026-10-23. Предложение для внедрения; код не меняется.
Запрос: цельные цепочки сырьё → компоненты → вещи, лут, узкие места и манифест без генерации. Запись запроса сохранена здесь: задача разрешает менять только три указанных файла.

## Совместимость и станции

Фактические RecipeOverride D54 приоритетнее старых таблиц D46: В3 стоит 10 стали, оружейный стол 15; АКМ — 250 металла +25 стали. Масло сейчас делается из 20 угля. Сталь не плавится. Пружины, платы, линзы, оружейные корпуса и коробка АК не крафтятся. Дробовики и элита только лут. Новые детали не добавляются в старые цены. JSON — выборка существующих рецептов плюс предложения, не замена полного реестра.

hand — руки; bench1 — deploy_workbench, bench2/3 — deploy_workbench2/3. lathe — deploy_lathe; lathe+bench3 — рядом с В3. still/sewing/chem/guntable/medtable/rectifier/efurnace/campfire — существующие deploy_* установки. Спецстанция заменяет верстак для своих рецептов D54. Новый станок не нужен. Переработчик публичный, на РТ.

Время в таблице — секунды до бонусов; n — выход партии. Жидкости — литры в ёмкостях. Генератор даёт 6 мощности в 15 м за 0,2 л топляка/мин; колонна требует 2, электропечь 3. Аппарат дополнительно сжигает дерево 6/мин. Токарный без электричества работает, под током ×1,5. Обычная печь/уголь остаются по коду; числовой пример плавки в JSON — электрический. Две строки water_boiled обозначают два действующих способа кипячения.

## Компоненты (34)

| ID | Название | Тир | Статус | Источник | Для чего |
|---|---|---:|---|---|---|
| comp_spring | Пружина | 0 | Есть | Бочки, ящики РТ0–3; только лут | comp_trigger, comp_bolt_carrier, weapon_akm |
| comp_pipe | Труба | 0 | Есть | Бочки; токарный: металл 40 | comp_barrel_rifled, deploy_still |
| comp_tape | Изолента | 0 | Есть | Бочки; токарный: ткань 15 | plate_lead, wear_army_6b2 |
| comp_gears | Шестерни | 0 | Есть | Ящики РТ0–3; токарный: металл 60, лом 10 | comp_bolt_carrier, deploy_generator |
| comp_circuit | Плата | 1 | Есть | АТС, Подстанция; только лут | deploy_workbench3, att_reddot, item_dynamo |
| comp_lens | Линза | 1 | Есть | Крейты РТ1–3, редко бочки; только лут | att_scope2x, wear_ozk_gp5, comp_optics |
| comp_trigger | УСМ | 2 | Есть | Крейты РТ1–3; токарный | weapon_grach, comp_gas_block |
| comp_bolt_carrier | Затворная группа | 2 | Есть | Крейты РТ1–3; токарный | weapon_pps43, weapon_akm |
| comp_barrel_rifled | Нарезной ствол | 2 | Есть | Крейты РТ1–3; токарный | weapon_svt40, comp_barrel_chrome |
| comp_frame_pistol | Корпус пистолета | 2 | Есть | Закрытые крейты РТ1–3; только лут | weapon_grach |
| comp_body_smg | Корпус ПП | 2 | Есть | Закрытые крейты РТ1–3; только лут | weapon_pps43, weapon_ppsh |
| comp_body_rifle | Корпус винтовки | 2 | Есть | Закрытые крейты РТ1–3; только лут | weapon_svt40, weapon_svd |
| comp_receiver_ak | Ствольная коробка АК | 3 | Есть | РТ2–3, сброс, Объект 42; только лут, квота D49 | weapon_akm, weapon_an94 |
| comp_barrel_chrome | Хромированный ствол | 3 | Есть | РТ2–3, сброс; токарный рядом с В3 | weapon_an94, weapon_svd |
| comp_gas_block | Газоотводный узел | 3 | Есть | РТ2–3, сброс; токарный рядом с В3 | weapon_akm, weapon_svd |
| comp_oil | Оружейное масло | 0 | Есть | Бочки; токарный: уголь 20, действующий рецепт | Ремонт оружия |
| alloy | Легированная сталь | 3 | Есть | Лут РТ1–3; переработка УСМ 2, коробки АК 4, блока ЭВМ 8 сверх обычного выхода | deploy_workbench3, deploy_guntable, weapon_akm, expl_c4 |
| rubber | Резина | 0 | Есть | Покрышки мира и РТ; бочки и ящики | wear_spec_robe, wear_ozk_cape, comp_seal |
| leather | Кожа | 1 | Есть | Ремни и старые куртки в луте; животных нет | wear_army_ssh68, item_patchkit |
| medspirt | Медспирт | 1 | Есть | Аппарат: ягоды 10; медицинский лут | antirad, medkit, syringe, comp_sterile |
| fuel | Топляк | 1 | Есть | Аппарат: нефть 2 л; насос Заправки 20–40 л | diesel, expl_fire, deploy_generator |
| diesel | Дизель | 3 | Есть | Колонна: топляк 2 л; высокие крейты | tool_jackhammer, deploy_drill |
| val_motor | Электромотор | 2 | Есть | Гаражи, Депо; только лут, разбор 20 лома +2 стали | tool_chainsaw, tool_jackhammer, deploy_efurnace |
| val_copper_wire | Медный провод | 1 | Есть | АТС, Подстанция; только лут, разбор 4 лома | deploy_lamp, comp_winding |
| comp_fasteners | Крепёж | 0 | НОВОЕ | Мусор, бочки; В1: металл 15 даёт 4 крепежа | comp_handtools, item_anchor |
| comp_canvas | Парусина | 1 | НОВОЕ | Хозяйственные ящики; швейный стол | item_boat, item_patchkit |
| comp_seal | Уплотнения | 1 | НОВОЕ | Гаражи, бочки; В1 | comp_outboard, item_patchkit |
| comp_bearing | Подшипник | 2 | НОВОЕ | Депо, Гаражи; только лут | comp_outboard |
| comp_winding | Обмотка | 2 | НОВОЕ | АТС, Подстанция; В2 | item_dynamo |
| comp_sterile | Стерильная укладка | 2 | НОВОЕ | Медшкаф; химстол | item_patchkit |
| comp_optics | Юстированная оптика | 3 | НОВОЕ | Командный ящик Объекта 42; оружейный стол | item_binoculars |
| comp_handtools | Столярный набор | 1 | НОВОЕ | Гаражи, ПТУ; В1 | item_boat, item_anchor |
| comp_resin | Смола | 1 | НОВОЕ | Хозяйственный ящик; аппарат | item_boat, item_patchkit |
| comp_outboard | Лодочный мотор | 2 | НОВОЕ | Прибрежный ящик; В2 с лутовыми подшипниками | item_boat |

## Рецепты

«НОВОЕ» — новый рецепт, прочие скопированы с учётом D54. Таблица и JSON совпадают.

| Выход × n | Станция | Цена партии | Секунды | Статус |
|---|---|---|---:|---|
| comp_pipe ×1 | lathe | metal_fragments ×40 | 10 | Есть |
| comp_tape ×1 | lathe | cloth ×15 | 6 | Есть |
| comp_gears ×1 | lathe | metal_fragments ×60, scrap ×10 | 15 | Есть |
| comp_oil ×1 | lathe | charcoal ×20 | 8 | Есть |
| comp_trigger ×1 | lathe | comp_spring ×2, comp_gears ×1, metal_fragments ×50 | 30 | Есть |
| comp_bolt_carrier ×1 | lathe | comp_spring ×3, comp_gears ×2, metal_fragments ×80 | 40 | Есть |
| comp_barrel_rifled ×1 | lathe | comp_pipe ×2, comp_gears ×1, metal_fragments ×100 | 45 | Есть |
| comp_barrel_chrome ×1 | lathe+bench3 | comp_barrel_rifled ×2, comp_spring ×2, metal_fragments ×200 | 90 | Есть |
| comp_gas_block ×1 | lathe+bench3 | comp_trigger ×1, comp_spring ×3, comp_gears ×2, metal_fragments ×150 | 75 | Есть |
| tool_hatchet ×1 | hand | wood ×100, stone ×50 | 6 | Есть |
| tool_pickaxe ×1 | hand | wood ×100, stone ×50 | 6 | Есть |
| weapon_spear ×1 | hand | wood ×150 | 5 | Есть |
| deploy_sleepbag ×1 | hand | cloth ×30 | 3 | Есть |
| deploy_box ×1 | hand | wood ×75 | 3 | Есть |
| deploy_claim ×1 | hand | wood ×50 | 3 | Есть |
| expl_satchel ×1 | bench2 | cloth ×10, gunpowder ×30, metal_fragments ×20 | 4 | Есть |
| expl_c4 ×1 | bench3 | gunpowder ×200, metal_fragments ×100, scrap ×20, cloth ×5, alloy ×5 | 15 | Есть |
| lock_key ×1 | hand | wood ×75 | 2 | Есть |
| lock_code ×1 | hand | metal_fragments ×100 | 4 | Есть |
| expl_fire ×1 | bench0 | cloth ×10, fuel ×5 | 3 | Есть |
| build_plan ×1 | hand | wood ×20 | 1 | Есть |
| tool_hammer ×1 | hand | wood ×50 | 3 | Есть |
| weapon_torch ×1 | hand | wood ×30, cloth ×5 | 2 | Есть |
| weapon_bow ×1 | hand | wood ×100, cloth ×30 | 8 | Есть |
| arrow ×2 | hand | wood ×25, stone ×10 | 2 | Есть |
| bandage ×1 | hand | cloth ×4 | 2 | Есть |
| deploy_furnace ×1 | hand | stone ×100, wood ×50 | 8 | Есть |
| deploy_workbench ×1 | hand | wood ×150, metal_fragments ×50, scrap ×50 | 10 | Есть |
| gunpowder ×5 | bench1 | charcoal ×15, sulfur ×10 | 3 | Есть |
| deploy_workbench2 ×1 | bench1 | metal_fragments ×300, scrap ×150 | 15 | Есть |
| deploy_workbench3 ×1 | bench2 | metal_fragments ×600, scrap ×400, comp_circuit ×3, comp_gears ×5, alloy ×10 | 25 | Есть |
| ammo_9mm ×5 | bench1 | metal_fragments ×10, gunpowder ×5 | 3 | Есть |
| ammo_12g ×5 | bench1 | metal_fragments ×15, gunpowder ×10 | 3 | Есть |
| ammo_762 ×5 | bench2 | metal_fragments ×15, gunpowder ×15 | 4 | Есть |
| ammo_545 ×5 | bench3 | metal_fragments ×10, gunpowder ×10 | 4 | Есть |
| ammo_bolt ×2 | bench0 | wood ×25, metal_fragments ×5 | 2 | Есть |
| weapon_crossbow ×1 | bench1 | wood ×150, cloth ×20, comp_spring ×1 | 10 | Есть |
| weapon_nagant ×1 | bench1 | metal_fragments ×100, comp_spring ×2, comp_pipe ×1 | 10 | Есть |
| weapon_homesmg ×1 | bench1 | metal_fragments ×125, comp_spring ×2, comp_pipe ×2, comp_tape ×2 | 14 | Есть |
| weapon_grach ×1 | bench2 | metal_fragments ×150, comp_frame_pistol ×1, comp_trigger ×1, comp_spring ×2 | 16 | Есть |
| weapon_pps43 ×1 | bench2 | metal_fragments ×175, comp_body_smg ×1, comp_bolt_carrier ×1, comp_spring ×3 | 18 | Есть |
| weapon_svt40 ×1 | bench2 | metal_fragments ×200, wood ×100, comp_body_rifle ×1, comp_barrel_rifled ×1, comp_trigger ×1 | 18 | Есть |
| weapon_akm ×1 | guntable | metal_fragments ×250, wood ×100, comp_receiver_ak ×1, comp_bolt_carrier ×1, comp_gas_block ×1, comp_spring ×4, alloy ×25 | 25 | Есть |
| weapon_an94 ×1 | guntable | metal_fragments ×300, comp_receiver_ak ×1, comp_barrel_chrome ×1, comp_bolt_carrier ×1, comp_gas_block ×1, comp_gears ×3, alloy ×30 | 25 | Есть |
| weapon_ppsh ×1 | guntable | metal_fragments ×200, wood ×150, comp_body_smg ×1, comp_barrel_chrome ×1, comp_bolt_carrier ×1, comp_spring ×4, alloy ×20 | 25 | Есть |
| weapon_svd ×1 | guntable | metal_fragments ×250, wood ×100, comp_body_rifle ×1, comp_barrel_chrome ×1, comp_gas_block ×1, comp_trigger ×1, alloy ×25 | 25 | Есть |
| deploy_lathe ×1 | bench2 | metal_fragments ×250, scrap ×50, comp_gears ×2 | 20 | Есть |
| att_suppressor_can ×1 | bench1 | metal_fragments ×50, comp_tape ×1 | 5 | Есть |
| att_grip_wood ×1 | bench1 | wood ×50, comp_tape ×1, rubber ×2 | 4 | Есть |
| att_torch ×1 | bench1 | wood ×30, cloth ×5, comp_tape ×1 | 4 | Есть |
| att_mag_twin_crude ×1 | bench1 | comp_tape ×2 | 4 | Есть |
| att_reddot ×1 | bench2 | metal_fragments ×75, comp_lens ×1, comp_circuit ×1 | 10 | Есть |
| att_scope2x ×1 | bench2 | metal_fragments ×100, comp_lens ×2, comp_gears ×1 | 12 | Есть |
| att_suppressor ×1 | bench2 | metal_fragments ×150, comp_barrel_rifled ×1, comp_tape ×1 | 10 | Есть |
| att_compensator ×1 | bench2 | metal_fragments ×100, comp_gears ×2 | 8 | Есть |
| att_grip ×1 | bench2 | metal_fragments ×75, rubber ×4, comp_gears ×1 | 6 | Есть |
| att_flashlight ×1 | bench2 | metal_fragments ×50, comp_circuit ×1 | 6 | Есть |
| att_laser ×1 | bench2 | metal_fragments ×50, comp_circuit ×1, comp_lens ×1 | 8 | Есть |
| att_mag_ext ×1 | bench2 | metal_fragments ×100, comp_spring ×2 | 8 | Есть |
| att_mag_twin ×1 | bench2 | metal_fragments ×75, comp_spring ×1 | 6 | Есть |
| att_stock_tac ×1 | bench2 | metal_fragments ×100, wood ×50, comp_gears ×1 | 8 | Есть |
| medkit ×1 | medtable | medspirt ×4, cloth ×10, fuel ×2 | 8 | Есть |
| syringe ×1 | medtable | medspirt ×2, fuel ×1 | 5 | Есть |
| bars ×1 | bench1 | metal_fragments ×50 | 5 | Есть |
| att_scope4x ×1 | guntable | metal_fragments ×150, comp_lens ×3, comp_gears ×2, alloy ×5 | 15 | Есть |
| att_suppressor_pbs ×1 | guntable | metal_fragments ×150, comp_barrel_rifled ×1, comp_tape ×2, alloy ×8 | 15 | Есть |
| att_compensator_army ×1 | guntable | metal_fragments ×120, comp_gears ×2, alloy ×5 | 10 | Есть |
| att_mag_ext_army ×1 | guntable | metal_fragments ×120, comp_spring ×3, alloy ×5 | 10 | Есть |
| fuel ×1 | still | crude ×2 | 15.0 | Есть |
| medspirt ×1 | still | berries ×10 | 20.0 | Есть |
| water_boiled ×1 | still | water_raw ×1 | 6.0 | Есть |
| water_boiled ×1 | campfire | water_raw ×1 | 10.0 | Есть |
| metal_fragments ×1 | efurnace | metal_ore ×1 | 0.2 | Есть |
| sulfur ×1 | efurnace | sulfur_ore ×1 | 0.2 | Есть |
| diesel ×1 | rectifier | fuel ×2 | 25.0 | Есть |
| antirad ×1 | chem | medspirt ×2, charcoal ×10 | 4 | Есть |
| deploy_still ×1 | bench1 | metal_fragments ×50, comp_pipe ×2 | 12 | Есть |
| deploy_guntable ×1 | bench3 | metal_fragments ×300, alloy ×15, comp_gears ×4, comp_spring ×4 | 25 | Есть |
| deploy_generator ×1 | bench2 | metal_fragments ×200, comp_gears ×3, comp_pipe ×2 | 20 | Есть |
| deploy_rectifier ×1 | bench3 | metal_fragments ×250, comp_pipe ×4, comp_gears ×2, val_motor ×1 | 25 | Есть |
| wear_army_6b2 ×1 | bench3 | metal_fragments ×250, cloth ×30, comp_tape ×3, alloy ×10 | 15 | Есть |
| wear_spec_robe ×1 | bench2 | cloth ×35, rubber ×6 | 8 | Есть |
| plate_lead ×1 | bench2 | metal_fragments ×150, comp_tape ×1 | 10 | Есть |
| tool_chainsaw ×1 | bench2 | metal_fragments ×150, comp_gears ×2, comp_spring ×1, val_motor ×1 | 15 | Есть |
| tool_jackhammer ×1 | bench3 | metal_fragments ×200, comp_gears ×3, val_motor ×1 | 20 | Есть |
| comp_fasteners ×4 | bench1 | metal_fragments ×15 | 4 | НОВОЕ |
| comp_canvas ×1 | sewing | cloth ×20, comp_tape ×1 | 8 | НОВОЕ |
| comp_seal ×2 | bench1 | rubber ×8, comp_tape ×1 | 6 | НОВОЕ |
| comp_winding ×1 | bench2 | val_copper_wire ×6, comp_tape ×2 | 20 | НОВОЕ |
| comp_sterile ×2 | chem | cloth ×12, medspirt ×2 | 10 | НОВОЕ |
| comp_optics ×1 | guntable | comp_lens ×4, comp_gears ×2, alloy ×5 | 30 | НОВОЕ |
| comp_handtools ×1 | bench1 | metal_fragments ×50, wood ×30, comp_fasteners ×2 | 12 | НОВОЕ |
| comp_resin ×2 | still | wood ×60, fuel ×1 | 20 | НОВОЕ |
| comp_outboard ×1 | bench2 | metal_fragments ×120, comp_bearing ×2, comp_gears ×3, comp_seal ×2, comp_pipe ×1 | 40 | НОВОЕ |
| item_boat ×1 | bench1 | wood ×400, comp_canvas ×3, comp_resin ×4, comp_handtools ×1 | 60 | НОВОЕ |
| item_anchor ×1 | bench1 | metal_fragments ×60, cloth ×25, comp_fasteners ×4, comp_handtools ×1 | 15 | НОВОЕ |
| item_patchkit ×1 | bench2 | comp_canvas ×1, leather ×4, comp_resin ×1, comp_seal ×1, comp_sterile ×1 | 12 | НОВОЕ |
| item_dynamo ×1 | bench2 | comp_winding ×2, comp_circuit ×1, metal_fragments ×100 | 30 | НОВОЕ |
| item_binoculars ×1 | guntable | comp_optics ×1, metal_fragments ×50, leather ×3 | 20 | НОВОЕ |

## Девять маршрутов 0 → 3

1. **Оружие:** дерево/камень → кирка → руда → металл; ткань → лук (руки). Бочки → пружины → Наган (В1). Лутовый корпус + трубы → нарезной ствол/УСМ (токарный) → СВТ (В2). РТ2 → коробка АК; переработка/крейты → сталь → В3 → оружейный стол → АКМ. АН-94 требует ещё хромированный ствол. Цены выше.
2. **Броня:** ткань → тряпьё (руки) → ватник (В1); покрышки → резина → спецовка (В2). Резина/уголь/лутовая линза → ГП-5 (В3); металл/ткань/сталь → 6Б2. Полные сеты D53: ватник 20 радиозащиты, спецовка 30, ОЗК 40; одна вещь не заменяет комплект.
3. **Медицина:** ткань 4 → бинт (руки); ягоды 10 → спирт (аппарат) → антирадин (химстол); нефть → топляк + спирт → аптечка/шприц (медстол, этап 3). НОВОЕ: ткань/спирт → стерильная укладка → ремонтный набор, лечения он не добавляет.
4. **Взрывчатка:** серная руда → сера (печь); дерево → уголь → порох (В1) → самодельная бомба (В2) → взрывчатка +5 стали (В3). Нефть → топляк → Молотов (руки). Топливо в патроны не добавляется.
5. **Энергия:** нефть 2 л → топляк 1 л (аппарат, В1) → генератор (В2) → ток → колонна (В3) → дизель. НОВОЕ: лутовый провод → обмотка → ручная динамо-машина (В2), выбор между топливом и работой руками.
6. **Стройка:** дерево → План/Киянка → каркас/дерево; руда → металл → bars (ставни/решётки, В1). Высокие детали → публичный переработчик → сталь → бронеуровень стены: 25 стали +50 металла за сегмент, 3500 HP. Улучшение стены — действие, не переносимый предмет, в JSON предметных рецептов не включено.
7. **Инструменты:** дерево/камень → каменные (руки) → металл → металлические (В1, существующий код) → лутовый мотор/шестерни/пружина → бензопила (В2); дизель + отбойник (В3). Мотор при разборе даёт 20 лома +2 стали: сохранить для инструмента или переработать.
8. **Лодка, НОВОЕ:** ткань → парусина; дерево/топляк → смола; металл → крепёж → столярный набор → гребная лодка (В1). Резина → уплотнения; лутовые подшипники → лодочный мотор (В2). Мотор ставится/снимается удержанием E 10 с, не расходуется. Тир 3 улучшает снабжение и добычу, отдельного топового корпуса не требуется.
9. **Оптика:** линзы РТ1 → прицел 2× (В2); линзы/сталь → юстированная оптика (оружейный стол) → бинокль (этап 3). Существующий рецепт 4× D54 не меняется.

## Новые вещи

| ID | Тир | Действие и ограничения |
|---|---:|---|
| item_boat — гребная лодка | 1 | 2 места, 250 HP; вёсла 3 м/с, мотор 7 м/с, топляк 0,5 л/мин при ходе. Груз — инвентари пассажиров. При 0 HP тонет, груз выпадает. Переносить можно только пустую без мотора, скорость носильщика −30%. |
| item_anchor — якорный комплект | 1 | Поставить/снять с лодки E 3 с; остановленная лодка не дрейфует, повторное использование. |
| item_patchkit — походная ремонтная укладка | 2 | E 8 с вне боя, получение урона прерывает. Расходуется, возвращает 100 HP лодке до максимума или 50% износа инструмента. Стволы/пластины чинятся по старым правилам. |
| item_dynamo — ручная динамо-машина | 2 | Установить E, крутить удержанием E: 2 мощности в 5 м, без накопления, шум 25 м. Питает колонну, электропечь не тянет; отпустил — питание пропало. |
| item_binoculars — бинокль Б-6 | 3 | Прицеливание: зум ×2,2, скорость −50%, оружие убрано. Серверная видимость и туман не расширяются. |

Столярный набор — расходуемая оснастка, списывается по рецепту. Новые компоненты известны по доступу к станции. Новые вещи изучаются: лодка 30, якорь 10 лома (В1), ремонтная укладка 25, динамо 50 (В2), бинокль 60 (В3); старые узлы не блокируют.

## Лут: контракт применения

Все таблицы — целевые предложения, не текущий код. На один контейнер/труп таблица применяется один раз; строки независимы, шанс 0…1, количество равномерно [min,max]. Для перечисленных id заменяет текущую выдачу, не добавляется поверх неё. Пустой результат допустим; 100% — гарантия. Еда/вода остаются отдельным существующим лутом.

РТ0: Заправка/Сельпо; РТ1: Гаражи/АТС/Подстанция/Депо/Карьер; РТ2: Радиоцентр; РТ3 и Объект 42 — будущие площадки. rt1_punch расходует карту I, гарантирует II; rt2_punch расходует II, гарантирует III — предложение для открытого вопроса 12-red-rt. III тратится на лифт 42, внутри не выпадает. Главная награда всегда за шумным действием.

Плотность на цикл: РТ0 2 обычных ящика; РТ1 3 обычных +1 закрытый; РТ2 3+1; РТ3 4 обычных за механикой доступа. Обновление 15 мин только без игроков внутри. Объект 42: 2 lab, 2 diesel, 1 command за сессию; для command предлагается запустить дизель: 5 л, 60 с, шум и маркер. Аирдроп — одна таблица на груз.

Профильные детали смещать без увеличения общей выдачи: провод/платы — АТС/Подстанция, моторы/подшипники — Депо/Гаражи, медицина — медшкаф. coast_boat_crate — НОВЫЙ профильный ящик в прибрежных Гаражах, заменяет один обычный.

Квоты D49: коробка АК/хромированный ствол/газоотвод с 5 ч, общий предел 5 деталей на мир в час по всем источникам. При исчерпании строка пропускается без переброса. Элита только с 3 ч и фазы ≥2, максимум 1 ствол за 3,5 ч на мир; единая квота всех источников. Проценты условны при свободной квоте. Готовый обычный АКМ — редкий обход крафта, патроны автоматически не выдаются.

Охрана: guard1/2/3 — карманы и шанс оружия. guard2_clothes отдельно: два броска по 25% только на реально надетые шлем/жилет; 0–2 вещи, среднее 0,5, никогда фуллсет. Старые броски по всем слотам одновременно отключаются. У тира 3 тот же максимум двух надетых вещей. Прочность дропа одежды и оружия 25–60%.

Жидкости не включены в упрощённый JSON loot: он не хранит содержимое канистры. Сохраняются существующие Prog.Loot: РТ1 канистра 5 л с топляком 3–5 л (15%); РТ2 канистра 20 л с топляком 10–20 л (15%), канистра 5 л с дизелем 3–5 л (8%); сброс канистра 20 л с дизелем 8–15 л (30%). Количество здесь — литры, выдаётся одна ёмкость.

| Место | ID: шанс %, количество |
|---|---|
| trash | scrap: 100%, 1–2; comp_fasteners: 18.00%, 1–3; cloth: 20.0%, 2–6 |
| barrel | scrap: 100%, 1–2; comp_spring: 12.00%, 1–1; comp_pipe: 10.0%, 1–1; comp_tape: 8.00%, 1–1; comp_gears: 5.00%, 1–1; rubber: 8.00%, 1–3; leather: 6.00%, 1–3; comp_lens: 0.50%, 1–1; comp_seal: 5.00%, 1–2 |
| rt0_crate | scrap: 100%, 2–4; cloth: 50.0%, 5–12; comp_spring: 30.0%, 1–2; comp_pipe: 30.0%, 1–2; comp_tape: 30.0%, 1–2; comp_canvas: 20.0%, 1–1; leather: 25.00%, 2–4; punchcard_1: 25.00%, 1–1; weapon_pipeshotgun: 1.50%, 1–1 |
| rt1_crate | alloy: 50.0%, 2–4; comp_spring: 40.0%, 1–3; comp_circuit: 20.0%, 1–1; comp_lens: 15.00%, 1–1; comp_trigger: 12.00%, 1–1; comp_frame_pistol: 12.00%, 1–1; comp_body_smg: 10.0%, 1–1; comp_body_rifle: 10.0%, 1–1; comp_bearing: 18.00%, 1–2; val_copper_wire: 30.0%, 1–2; comp_handtools: 15.00%, 1–1; medspirt: 15.00%, 1–3; weapon_sawnoff: 2.50%, 1–1 |
| rt1_punch | punchcard_2: 100%, 1–1; alloy: 100%, 3–6; comp_frame_pistol: 25.00%, 1–1; comp_body_smg: 25.00%, 1–1; comp_body_rifle: 25.00%, 1–1 |
| rt2_crate | alloy: 50.0%, 4–8; comp_bolt_carrier: 25.00%, 1–1; comp_barrel_rifled: 25.00%, 1–1; comp_circuit: 35.00%, 1–2; comp_receiver_ak: 12.00%, 1–1; comp_barrel_chrome: 8.00%, 1–1; comp_gas_block: 5.00%, 1–1; val_motor: 20.0%, 1–1; comp_bearing: 30.0%, 1–2; comp_sterile: 25.00%, 1–2; medkit: 8.00%, 1–1; tool_jackhammer: 3.00%, 1–1; weapon_mp133: 3.50%, 1–1 |
| rt2_punch | punchcard_3: 100%, 1–1; alloy: 100%, 3–6; comp_receiver_ak: 20.0%, 1–1; comp_gas_block: 15.00%, 1–1 |
| rt3_crate | alloy: 100%, 6–10; comp_receiver_ak: 20.0%, 1–1; comp_barrel_chrome: 15.00%, 1–1; comp_gas_block: 15.00%, 1–1; comp_optics: 12.00%, 1–1; att_scope4x: 8.00%, 1–1; plate_ceramic: 2.50%, 1–1; weapon_akm: 1.50%, 1–1 |
| o42_lab | comp_sterile: 100%, 2–4; medspirt: 100%, 2–5; syringe: 50.0%, 1–2; antirad: 50.0%, 1–2; comp_optics: 25.00%, 1–1 |
| o42_diesel | val_motor: 50.0%, 1–2; comp_bearing: 60.0%, 1–3; comp_winding: 40.0%, 1–2; comp_seal: 70.0%, 2–4 |
| o42_command | alloy: 100%, 10–15; comp_receiver_ak: 30.0%, 1–1; comp_barrel_chrome: 30.0%, 1–1; comp_gas_block: 30.0%, 1–1; comp_optics: 40.0%, 1–1; weapon_saiga12: 3.00%, 1–1; weapon_mosin: 3.00%, 1–1; weapon_aps: 3.00%, 1–1 |
| airdrop | alloy: 100%, 8–15; comp_receiver_ak: 35.00%, 1–1; comp_barrel_chrome: 25.00%, 1–1; comp_gas_block: 25.00%, 1–1; medkit: 30.0%, 1–2; att_scope4x: 20.0%, 1–1; plate_lead: 15.00%, 1–1; weapon_saiga12: 1.00%, 1–1 |
| guard1 | bandage: 40.0%, 1–2; ammo_9mm: 35.00%, 5–10; weapon_nagant: 8.00%, 1–1 |
| guard2 | ammo_762: 60.0%, 5–15; comp_spring: 25.00%, 1–2; syringe: 20.0%, 1–1; weapon_svt40: 4.00%, 1–1 |
| guard2_clothes | wear_army_ssh68: 25.00%, 1–1; wear_army_6b2: 25.00%, 1–1 |
| guard3 | ammo_762: 70.0%, 10–20; medkit: 20.0%, 1–1; comp_bolt_carrier: 20.0%, 1–1; weapon_akm: 3.00%, 1–1 |
| coast_boat_crate | comp_outboard: 12.00%, 1–1; comp_seal: 50.0%, 1–2; comp_resin: 40.0%, 1–2; comp_canvas: 40.0%, 1–2 |

## Шесть узких мест

| Компонент | Решение игрока |
|---|---|
| Пружина | Наган сейчас или запас для УСМ/затвора. Бочки безопаснее закрытых крейтов, но крафта нет. |
| Корпус оружия / коробка АК | Ветка зависит от находки: РТ1 → СВТ/ППС, РТ2 → АКМ. Из руды не получить. |
| Плата | Коллиматор/динамо сейчас или 3 платы на В3. Отдельный маршрут АТС/Подстанция. |
| Линза | 2× сейчас, ГП-5 или 4 линзы на оптику. Сборка дорогая, готовая оптика — опасный лут 42. |
| Сталь | Сохранить корпус для оружия или разобрать на 2 стали. Крейты 42 дают больше за рейд, переработчик позволяет реализовать накопленное. |
| Подшипник (НОВОЕ) | Лодка на вёслах доступна без него; мотор требует похода в Гаражи/Депо. |

Два пути: УСМ/затвор/газоотвод — дорогой токарный или опасный крейт; мотор лодки — сборка с двумя лутовыми подшипниками либо целиком из прибрежного ящика; стерильная укладка — спирт/ткань либо медшкаф; оптика — четыре линзы/сталь либо командный центр. Зависимость от лутовых деталей сохраняется.

Переработка новых изделий: 50% обычных ресурсов входа с округлением вниз; пружины, платы, линзы, сталь, корпуса, подшипники и промежуточные детали не возвращаются. Новый подшипник даёт 4 металла. Старую переработку не менять. Для многовыходных рецептов возврат на единицу, не на партию: циклы не дают прибыль.

## Темп и проверка после внедрения

Цели: В1 30–60 мин, В2 2–4 ч, первый скрафченный ствол В3 6–8 ч. Новые ветки необязательны. На АКМ нужны коробка, 16 пружин (затвор 3, газоотвод с УСМ 5, ствол 4, оружейный стол 4), минимум 50 стали (В3 10 + стол 15 + АКМ 25), изучение ветки ≈685 лома плюс станции. Не перерабатывать этот резерв бездумно.

После внедрения: 12 ботов ×10 ч на 3 зёрнах; сравнить В2/В3, первый крафт АКМ, часовую выдачу деталей. 1000 убийств охраны Т2: ни одного дропа >2 вещей. Проверить лодку без мотора, колонну с ручным током, отсутствие прибыльных циклов разборки. Здесь симуляция не запускалась: меняются только документы.

## Ассеты

Манифест: 15 инвентарных иконок — 10 новых компонентов и 5 вещей; id=icon_<item-id>, cat=icons, icon=128, английские desc. Старые id не дублируются. Для мирового отображения лодки/динамо при внедрении потребуется отдельная постановка. Изображения не генерировались.
