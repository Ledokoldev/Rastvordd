# Дерево прогрессии «Раствордд»: данные (по docs/design/13, 14, 15) + иконки → progression.html
import base64, json, os
ART = 'C:/Users/Xbaza/Desktop/Rastvordd/game/art/'
OUT = os.path.dirname(os.path.abspath(__file__))

N = []
def n(id, name, cat, col, icon=None, how=(), rec=(), st=None, res=0, note=''):
    N.append(dict(id=id, name=name, cat=cat, col=col, icon=icon, how=list(how), rec=[list(x) for x in rec], st=st, res=res, note=note))

# --- добыча и ресурсы (столбец 0 — с первой минуты) ---
n('wood','Дерево','res',0,'icons/icon_wood',['деревья — топор или булыжник'])
n('stone','Камень','res',0,'icons/icon_stone',['каменные узлы — кирка или булыжник'])
n('metal_ore','Металлическая руда','res',0,'icons/icon_metal_ore',['рудные узлы — кирка'])
n('sulfur_ore','Серная руда','res',0,'icons/icon_sulfur_ore',['серные узлы — кирка'])
n('cloth','Ткань','res',0,'icons/icon_cloth',['конопля (собрать E)','разбор одежды'])
n('berries','Ягоды','res',0,'icons/icon_berries',['кусты (собрать E)'])
n('scrap','Металлолом','res',0,'icons/icon_scrap',['бочки (1–3)','крейты РТ (понемногу)','ГЛАВНОЕ: переработчик на РТ — компоненты и ценный лом'])
n('crude','Сырая нефть','res',0,None,['бочки с мазутом и ржавые канистры в мире и на РТ (3–8)'])
n('rubber','Резина','res',0,'icons/icon_rubber',['покрышки (2–4), штабеля и кучи шин (6–10) — бить инструментом'])
n('leather','Кожа','res',0,'icons/icon_leather',['бочки и ящики'])
# --- переработанное ---
n('metal_fragments','Металл','proc',0,'icons/icon_metal_fragments',['переработчик (из компонентов)'],[('metal_ore',1)],'furnace',note='печь: руда → металл; электропечь вдвое быстрее')
n('sulfur','Сера','proc',0,'icons/icon_sulfur',[],[('sulfur_ore',1)],'furnace')
n('charcoal','Уголь','proc',0,'icons/icon_charcoal',[],[('wood',1)],'furnace',note='побочный продукт печи')
n('fuel','Топляк','proc',1,None,['Заправка: насос подземной цистерны (20–40, шумно)','канистры в крейтах РТ 1–2, аирдроп, УАЗ'],[('crude',2)],'still',note='расходник: ток, патроны, хилки, молотов, ПВО')
n('medspirt','Медспирт','proc',1,None,[],[('berries',10)],'still',note='или конопля; для аптечек и шприцев')
n('gunpowder','Порох ×5','proc',1,'icons/icon_gunpowder',[],[('charcoal',15),('sulfur',10)],'bench1',20,'на химстоле быстрее и дешевле')

# --- металл высокого качества: легированная сталь ---
n('alloy','Легированная сталь','proc',4,'icons/icon_hqm',['аирдропы и ивенты','переработчик: лут высоких тиров (детали тира 2–3, пластины, стволы, дорогой ценный лом)','крейты РТ тира 1–2'],[],None,0,'только найти или переработать; не плавится. Аналог МВК: бронедвери и стены, стволы тира 3, верстак 3, армейская экипировка')

# --- станки и верстаки ---
n('furnace','Печь','stn',0,'icons/icon_furnace',[],[('stone',100),('wood',50)],None)
n('bench1','Верстак 1','stn',1,'icons/icon_workbench',[],[('wood',150),('metal_fragments',50),('scrap',50)],None,note='изучить всё на нём ≈ 250 лома')
n('still','Самогонный аппарат','stn',1,None,[],[('metal_fragments',50),('comp_pipe',2)],'bench1',30,note='нефть → топляк, ягоды → медспирт; дымит — видно базу')
n('sewing','Швейный стол','stn',1,None,[],[('wood',100),('cloth',20)],'bench1',20,note='одежда тир 0–1 быстрее, ремонт одежды')
n('bench2','Верстак 2','stn',2,'icons/icon_workbench2',[],[('metal_fragments',300),('scrap',150)],'bench1',note='изучить всё ≈ 650 лома')
n('lathe','Токарный станок','stn',2,None,[],[('metal_fragments',150),('comp_gears',2)],'bench2',note='детали из деталей; под током быстрее')
n('generator','Дизель-генератор','stn',2,None,[],[('metal_fragments',200),('comp_gears',3),('comp_pipe',2)],'bench2',60,note='топляк → ток: свет, электропечь, ускоренный крафт')
n('chem','Химстол','stn',2,None,[],[('metal_fragments',150),('comp_pipe',1)],'bench2',60,note='порох, патроны, антирадин, сатчель')
n('lamp','Лампы, прожектор','stn',2,None,[],[('metal_fragments',20),('val_copper_wire',1)],'bench2',20,note='свет от генератора')
n('bench3','Верстак 3','stn',3,'icons/icon_workbench3',[],[('metal_fragments',600),('scrap',400),('alloy',25),('comp_circuit',3),('comp_gears',5)],'bench2',note='детали с РТ обязательны; изучить всё ≈ 1300 лома')
n('efurnace','Электропечь','stn',3,None,[],[('metal_fragments',300),('val_motor',1),('val_copper_wire',2)],'bench3',100,note='плавит ×2, без дерева, на токе')
n('guntable','Оружейный стол','stn',3,None,[],[('metal_fragments',300),('alloy',20),('comp_gears',4),('comp_spring',4)],'bench3',150,note='стволы тира 3 и армейские обвесы — только здесь; ремонт')
n('medtable','Медстол','stn',3,None,[],[('metal_fragments',150),('cloth',50)],'bench3',80,note='аптечки, шприцы')
n('conveyor','Конвейер, сортировщик','stn',3,None,[],[('metal_fragments',100),('val_motor',1),('comp_circuit',1)],'bench3',120,note='автоматизация между ящиками, печами, станками')
n('recycler','Переработчик','stn',4,None,['только на РТ тира 0'],[],None,note='компоненты и ценный лом → лом и металл')
# --- компоненты ---
C=[('comp_spring','Пружина',0,['бочки','крейты РТ 0','переработка'],[],None,'только найти'),
   ('comp_pipe','Труба',0,['бочки','РТ 0'],[('metal_fragments',40)],'lathe',''),
   ('comp_tape','Изолента',0,['бочки','РТ 0'],[('cloth',15)],'lathe',''),
   ('comp_gears','Шестерни',0,['РТ 0–1'],[('metal_fragments',60),('scrap',10)],'lathe',''),
   ('comp_oil','Оружейное масло',0,['бочки'],[('charcoal',20)],'lathe','ремонт стволов'),
   ('comp_circuit','Плата',2,['крейты РТ 1 (АТС, Подстанция чаще)'],[],None,'только найти'),
   ('comp_lens','Линза',2,['крейты РТ 1','редко бочки'],[],None,'только найти'),
   ('comp_trigger','УСМ',2,['крейты РТ 1'],[('comp_spring',2),('comp_gears',1),('metal_fragments',50)],'lathe',''),
   ('comp_bolt_carrier','Затворная группа',2,['крейты РТ 1'],[('comp_spring',3),('comp_gears',2),('metal_fragments',80)],'lathe',''),
   ('comp_barrel_rifled','Нарезной ствол',2,['крейты РТ 1'],[('comp_pipe',2),('comp_gears',1),('metal_fragments',100)],'lathe',''),
   ('comp_frame_pistol','Корпус пистолета',2,['крейты РТ 1'],[],None,'только найти'),
   ('comp_body_smg','Корпус ПП',2,['крейты РТ 1'],[],None,'только найти'),
   ('comp_body_rifle','Корпус винтовки',2,['крейты РТ 1'],[],None,'только найти'),
   ('comp_receiver_ak','Ствольная коробка АК',4,['РТ 2','аирдроп','вертолёт'],[],None,'только найти, с середины вайпа'),
   ('comp_barrel_chrome','Хромированный ствол',3,['РТ 2','аирдроп'],[('comp_barrel_rifled',2),('comp_spring',2),('metal_fragments',200)],'lathe','станок рядом с верстаком 3'),
   ('comp_gas_block','Газоотводный узел',3,['РТ 2','аирдроп'],[('comp_trigger',1),('comp_spring',3),('comp_gears',2),('metal_fragments',150)],'lathe','станок рядом с верстаком 3')]
for id,nm,col,how,rec,st,note in C: n(id,nm,'comp',col,'icons/icon_'+{'comp_spring':'spring','comp_pipe':'metal_pipe','comp_gears':'gears'}.get(id,id),how,rec,st,0,note)
# --- ценный лом ---
for id,nm,where,y in [('val_copper_wire','Медный провод','бочки, РТ 0','4 лома, 5 металла'),('val_radio_parts','Радиодетали','РТ 0–1, АТС','8 лома'),('val_battery','Аккумулятор 6СТ','машины на РТ, РТ 1','12 лома, 10 металла'),('val_motor','Электромотор','Подстанция, Депо, РТ 1','20 лома, 30 металла'),('val_radio_tubes','Радиолампы','РТ 2, Радиоцентр, ПВО','40 лома'),('val_gold_contacts','Золотые контакты','РТ 2, «Объект 42»','60 лома'),('val_computer_block','Блок ЭВМ','«Объект 42», вертолёт','120 лома')]:
    n(id,nm,'val',4 if 'РТ 2' in where or 'Объект' in where else 2 if 'РТ 1' in where else 0,'icons/icon_'+id,[where],[],'recycler',0,'переработка → '+y)
# --- оружие ---
W=[('weapon_spear','Копьё',0,[('wood',150)],None,0,'weapons/weapon_spear'),('weapon_bow','Лук',0,[('wood',100),('cloth',30)],None,0,'weapons/weapon_bow'),
   ('weapon_crossbow','Арбалет',1,[('wood',150),('cloth',20),('comp_spring',1)],'bench1',30,None),
   ('weapon_nagant','Наган',1,[('metal_fragments',100),('comp_spring',2),('comp_pipe',1)],'bench1',50,None),
   ('weapon_homesmg','Самоделка (ПП)',1,[('metal_fragments',125),('comp_spring',2),('comp_pipe',2),('comp_tape',2)],'bench1',90,None),
   ('weapon_grach','Грач',2,[('metal_fragments',150),('comp_frame_pistol',1),('comp_trigger',1),('comp_spring',2)],'bench2',120,None),
   ('weapon_pps43','ППС-43',2,[('metal_fragments',175),('comp_body_smg',1),('comp_bolt_carrier',1),('comp_spring',3)],'bench2',150,None),
   ('weapon_svt40','СВТ-40',2,[('metal_fragments',200),('wood',100),('comp_body_rifle',1),('comp_barrel_rifled',1),('comp_trigger',1)],'bench2',150,None),
   ('weapon_akm','АКМ',3,[('metal_fragments',250),('alloy',25),('wood',100),('comp_receiver_ak',1),('comp_bolt_carrier',1),('comp_gas_block',1),('comp_spring',4)],'guntable',300,None),
   ('weapon_an94','АН-94 «Абакан»',3,[('metal_fragments',250),('alloy',30),('comp_receiver_ak',1),('comp_barrel_chrome',1),('comp_bolt_carrier',1),('comp_gas_block',1),('comp_gears',3)],'guntable',300,None),
   ('weapon_ppsh','ППШ',3,[('metal_fragments',200),('alloy',20),('wood',150),('comp_body_smg',1),('comp_barrel_chrome',1),('comp_bolt_carrier',1),('comp_spring',4)],'guntable',250,None),
   ('weapon_svd','СВД',3,[('metal_fragments',250),('alloy',25),('wood',100),('comp_body_rifle',1),('comp_barrel_chrome',1),('comp_gas_block',1),('comp_trigger',1)],'guntable',300,None)]
for id,nm,col,rec,st,res,ic in W: n(id,nm,'gun',col,ic or 'icons/icon_'+id,[],rec,st,res)
for id,nm,where in [('weapon_pipeshotgun','Самопал','бочки, РТ 0'),('weapon_sawnoff','Обрез','крейты РТ 0–1'),('weapon_mp133','МР-133','крейты РТ 1, аирдроп'),('weapon_saiga12','Сайга-12','вертолёт, «Объект 42», редко аирдроп'),('weapon_mosin','Мосинка с ПУ','«Объект 42», редко аирдроп'),('weapon_aps','АПС','вертолёт, «Объект 42», редко аирдроп')]:
    n(id,nm,'gun',4,'icons/icon_'+id,[where],[],None,0,'не крафтится')
# --- патроны ---
n('arrow','Стрелы ×2','ammo',0,'icons/icon_arrow',[],[('wood',25),('stone',10)])
n('ammo_9mm','9 мм ×5','ammo',1,'icons/icon_ammo_9mm',[],[('metal_fragments',10),('gunpowder',5)],'bench1',30)
n('ammo_12g','12 калибр ×5','ammo',1,'icons/icon_ammo_12g',[],[('metal_fragments',5),('gunpowder',5),('fuel',1)],'bench1',30)
n('ammo_762','7,62 ×5','ammo',2,'icons/icon_ammo_762',[],[('metal_fragments',10),('gunpowder',10),('fuel',1)],'chem',100)
n('ammo_545','5,45 ×5','ammo',3,'icons/icon_ammo_545',[],[('metal_fragments',8),('gunpowder',6),('fuel',1)],'chem',120)
# --- обвесы ---
A=[('att_suppressor_can','Глушитель «банка»',1,[('metal_fragments',50),('comp_tape',1)],'bench1',30,[]),
   ('att_grip_wood','Рукоять кустарная',1,[('wood',30),('comp_tape',1)],'bench1',30,[]),
   ('att_torch','Фонарь на изоленте',1,[('cloth',10),('comp_tape',1),('fuel',2)],'bench1',30,[]),
   ('att_mag_twin','Спарка',1,[('comp_tape',2)],'bench1',40,[]),
   ('att_reddot','Коллиматор',2,[('metal_fragments',75),('comp_lens',1),('comp_circuit',1)],'bench2',80,[]),
   ('att_scope2x','Прицел 2×',2,[('metal_fragments',100),('comp_lens',2),('comp_gears',1)],'bench2',100,['лут']),
   ('att_laser','ЛЦУ',2,[('comp_circuit',1),('comp_lens',1)],'bench2',80,[]),
   ('att_suppressor','Глушитель заводской',2,[],None,0,['крейты РТ 1']),
   ('att_compensator','Компенсатор',2,[],None,0,['крейты РТ 1']),
   ('att_grip','Рукоять заводская',2,[],None,0,['крейты РТ 1']),
   ('att_flashlight','Тактический фонарь',2,[],None,0,['крейты РТ 1']),
   ('att_mag_ext','Увеличенный магазин',2,[],None,0,['крейты РТ 1']),
   ('att_stock_tac','Тактический приклад',2,[],None,0,['крейты РТ 1']),
   ('att_scope4x','ПСО 4×',4,[],None,0,['РТ 2','аирдроп']),
   ('att_suppressor_pbs','ПБС',4,[],None,0,['РТ 2','аирдроп','вертолёт'])]
for id,nm,col,rec,st,res,how in A: n(id,nm,'att',col,'icons/icon_'+id,how,rec,st,res)
# --- одежда (сеты) ---
S=[('set_rags','Тряпьё',0,'wear_rags_shirt',[('cloth',20)],None,0,[],'тир 0'),
   ('set_vatnik','Ватник',1,'wear_vatnik_telogreika',[('cloth',40),('leather',3)],'sewing',0,[],'холод до −30°'),
   ('set_leather','Кожа и дерево',1,'wear_leather_chestplate',[('leather',10),('wood',60)],'bench1',0,[],'от людей'),
   ('set_spec','Спецовка',2,'wear_spec_robe',[('cloth',40),('rubber',15)],'bench2',40,[],'радиация до 40'),
   ('set_scrap','Кустарная броня',2,'wear_scrap_signvest',[('metal_fragments',120),('comp_tape',3),('rubber',6)],'bench2',60,[],'от людей'),
   ('plate_homemade','Пластина кустарная',2,'plate_homemade',[('metal_fragments',100),('comp_tape',1)],'bench2',0,[],'+20%, 150 урона'),
   ('set_ozk','ОЗК',3,'wear_ozk_gp5',[('rubber',30),('cloth',30)],'bench3',100,['лут РТ 1–2'],'радиация до 67'),
   ('set_army','Армейская',3,'wear_army_6b2',[('metal_fragments',150),('alloy',20),('cloth',60),('plate_steel',0)],'bench3',120,[],'пули, холод до −13°'),
   ('plate_steel','Пластина армейская',4,'plate_steel',[],None,0,['крейты РТ 2','сейф'],'+30%, 400 урона'),
   ('plate_ceramic','Пластина керамическая',4,'plate_ceramic',[],None,0,['вертолёт','«Объект 42»'],'+40%, 300 урона'),
   ('set_likv','Ликвидатор',4,'wear_likvidator_pmk',[],None,0,['аирдроп','закрытый ящик','«Объект 42»'],'радиация до 96, холод до −33°'),
   ('set_sobr','СОБР',4,'wear_sobr_kora',[],None,0,['вертолёт','аирдроп','«Объект 42»'],'корпус до 75%'),
   ('wear_watch','Часы «Электроника»',0,'wear_watch',[],None,0,['бочки, РТ 0–1'],'время, t°, радиация')]
for id,nm,col,ic,rec,st,res,how,note in S: n(id,nm,'wear',col,'icons/icon_'+ic,how,[r for r in rec if r[1]>0],st,res,note)
# --- медицина и прочее ---
n('bandage','Бинт','med',0,'icons/icon_bandage',[],[('cloth',4)])
n('antirad','Антирадин','med',2,'icons/icon_antirad',['ящики'],[('medspirt',2),('charcoal',10)],'chem',40,'−120 дозы')
n('medkit','Аптечка','med',3,'icons/icon_medkit',[],[('medspirt',4),('cloth',10),('fuel',2)],'medtable',80)
n('syringe','Шприц','med',3,'icons/icon_syringe',['лут РТ 2'],[('medspirt',2),('fuel',1)],'medtable',80)
n('punchcard_1','Перфокарта I','med',0,'icons/icon_punchcard_1',['РТ 0: бардачок, касса, магазин','аирдроп'],[],None,0,'открывает РТ тира 1')
n('punchcard_2','Перфокарта II','med',2,'icons/icon_punchcard_2',['крейты РТ 1','аирдроп'],[],None,0,'открывает РТ тира 2')
n('punchcard_3','Перфокарта III','med',4,None,['РТ 2 (Радиоцентр — вызов)','аирдроп редко'],[],None,0,'лифты «Объекта 42»')
# --- взрывчатка и стройка ---
n('lock_key','Ключевой замок','boom',0,'icons/icon_key_lock',[],[('wood',75)])
n('lock_code','Кодовый замок','boom',1,'icons/icon_code_lock',[],[('metal_fragments',100)],'bench1',30)
n('molotov','Молотов','boom',1,'icons/icon_molotov',[],[('cloth',10),('fuel',5)],'bench1',30)
n('satchel','Сатчель','boom',2,'icons/icon_satchel',[],[('cloth',10),('gunpowder',30),('metal_fragments',20)],'chem',75)
n('c4','C4','boom',3,'icons/icon_c4',[],[('gunpowder',200),('alloy',5),('metal_fragments',100),('scrap',20),('cloth',5)],'chem',250,'нужен верстак 3')


# --- инструменты ---
n('tool_rock','Булыжник','tool',0,'weapons/weapon_rock',['есть с начала, подобрать камень'])
n('tool_hatchet','Каменный топор','tool',0,'weapons/weapon_hatchet',[],[('wood',100),('stone',50)])
n('tool_pickaxe','Каменная кирка','tool',0,'weapons/weapon_pickaxe',[],[('wood',100),('stone',50)])
n('tool_hammer','Киянка','tool',0,'icons/icon_hammer',[],[('wood',50)],None,0,'стройка, апгрейд и ремонт')
n('tool_metal_hatchet','Металлический топор','tool',1,'weapons/weapon_metal_hatchet',[],[('metal_fragments',75),('wood',50)],'bench1',30,'дерево ×2')
n('tool_metal_pickaxe','Металлическая кирка','tool',1,'weapons/weapon_metal_pickaxe',[],[('metal_fragments',100),('wood',50)],'bench1',30,'руда ×2')
n('tool_chainsaw','Бензопила «Дружба»','tool',2,None,[],[('metal_fragments',150),('comp_gears',2),('comp_spring',1),('val_motor',1)],'bench2',100,'дерево ×4, жрёт топляк, громко')
n('tool_jackhammer','Отбойный молоток','tool',3,None,['лут РТ 2'],[('metal_fragments',200),('comp_gears',3),('val_motor',1)],'bench3',150,'камень и руда ×4, жрёт топляк, громко')
n('drill_rig','Буровая установка','tool',3,None,[],[('metal_fragments',400),('comp_gears',6),('val_motor',2),('comp_pipe',4)],'bench3',200,'ставится на рудное место, сама добывает руду/серу, пока есть топляк; видна и слышна издалека')
# --- база ---
n('build_plan','План стройки','base',0,'icons/icon_build_plan',[],[('wood',20)])
n('deploy_claim','Шкаф','base',0,'icons/icon_tool_cupboard',[],[('wood',50)],None,0,'зона строительства, содержание')
n('deploy_sleepbag','Спальник','base',0,'icons/icon_sleeping_bag',[],[('cloth',30)])
n('deploy_box','Ящик','base',0,'icons/icon_wood_box',[],[('wood',75)])
n('door_wood','Деревянная дверь','base',0,'icons/icon_wood_door',[],[('wood',150)])
n('box_large','Большой ящик','base',1,None,[],[('wood',250),('metal_fragments',50)],'bench1',20)
n('door_metal','Металлическая дверь','base',1,'icons/icon_metal_door',[],[('metal_fragments',150)],'bench1',40)
n('bars','Решётки и ставни','base',1,None,[],[('metal_fragments',50)],'bench1',20)
n('door_armored','Бронедверь','base',3,None,[],[('metal_fragments',200),('alloy',40),('comp_gears',4)],'bench3',150)
n('walls_armored','Бронированные стены','base',3,None,['апгрейд киянкой поверх металла'],[('alloy',25)],'bench3',120,'на каждый сегмент; рейдить в разы дороже')
n('walls','Стены: дерево → камень → металл','base',0,'icons/icon_build_wall',['киянкой: дерево, потом камень, потом металл'],[('wood',50)],None,0,'апгрейд материалом, без верстака')
# --- снаряжение (идея из Arc Raiders, не утверждено) ---
n('bag_sidor','Сидор (12 ячеек)','equip',0,'icons/icon_backpack',[],[('cloth',40)],None,0,'идея: рюкзак как снаряжение — не утверждено')
n('bag_tourist','Туристический рюкзак (24)','equip',1,'icons/icon_backpack',[],[('cloth',80),('leather',6)],'sewing',30,'идея — не утверждено')
n('bag_army','Армейский рюкзак (36)','equip',4,'icons/icon_backpack',['крейты РТ 2','аирдроп'],[],None,0,'идея — не утверждено')
n('rig_vest','Разгрузка (+2 пояса)','equip',2,None,[],[('cloth',40),('leather',6),('comp_tape',2)],'sewing',60,'идея — не утверждено')
# --- еда и вода ---
n('water','Вода','med',0,'icons/icon_water_bottle',['колонки, бочки с водой, водоёмы'])
n('canned','Консервы','med',0,'icons/icon_canned',['ящики еды, крейты РТ 0'])
n('ammo_bolt','Болты ×2','ammo',0,'icons/icon_ammo_bolt',[],[('wood',30),('metal_fragments',5)],'bench1',0,'для арбалета')

icons = {}
for x in N:
    if x['icon']:
        p = ART + x['icon'] + '.png'
        if os.path.exists(p):
            icons[x['id']] = 'data:image/png;base64,' + base64.b64encode(open(p, 'rb').read()).decode()
    x.pop('icon')
tpl = open(os.path.join(OUT, 'page.html'), encoding='utf-8').read()
html = tpl.replace('/*NODES*/[]', json.dumps(N, ensure_ascii=False)).replace('/*ICONS*/{}', json.dumps(icons))
open(os.path.join(OUT, 'progression.html'), 'w', encoding='utf-8').write(html)
print(len(N), 'nodes', len(icons), 'icons', len(html) // 1024, 'KB')
