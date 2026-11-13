extends SceneTree
## Poly Haven (CC0) → game/art3d/hq/<key>.glb по списку scripts/polyhaven-models.json.
## Для каждой модели: упростить меш (maxTris), подкрасить (tint/metal), повернуть и масштабировать так,
## чтобы она легла в габариты старой модели с тем же именем (game/art3d/<key>.glb): самая длинная ось — на самую
## длинную, размер по самой длинной оси, центр по X/Z и низ по Y — как у старой. Так геймплейный вид не меняется.
## Текстуры встраиваются в .glb (JPEG).
##
## Запуск (из корня репозитория):
##   …_console.exe --headless --path game --script ../scripts/polyhaven_convert.gd -- [ключ…]
##   …_console.exe --path game --script ../scripts/polyhaven_convert.gd -- --preview=путь.png [ключ…]   (лист превью; нужно окно, не --headless)

const ART := "res://art3d/"
var _root_dir := ""

func _initialize() -> void:
	_root_dir = ProjectSettings.globalize_path("res://").get_base_dir().get_base_dir()   # …/Rastvordd
	var args := OS.get_cmdline_user_args()
	var only: Array[String] = []
	var preview := ""
	for a in args:
		if a.begins_with("--preview="): preview = a.substr(10)
		else: only.append(a)
	var list = JSON.parse_string(FileAccess.get_file_as_string(_root_dir.path_join("scripts/polyhaven-models.json")))
	if list == null:
		push_error("polyhaven-models.json не прочитан"); quit(1); return
	if preview != "":
		await _preview(list, only, preview)
		quit(); return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(ART + "hq"))
	var ok := 0
	for e in list:
		if only.size() > 0 and not only.has(e.key): continue
		if _convert(e): ok += 1
	print("готово: %d" % ok)
	quit()

func _convert(e: Dictionary) -> bool:
	var src: String = _root_dir.path_join("tools/polyhaven/%s/%s.gltf" % [e.src, e.src])
	if not FileAccess.file_exists(src):
		push_error("нет %s (сначала scripts/polyhaven-fetch.ps1)" % src); return false
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(src, state) != OK:
		push_error("не прочитан %s" % src); return false
	var tris_before := 0
	var tris_after := 0
	for gm in state.get_meshes():
		var im: ImporterMesh = gm.mesh
		var t := _tris(im)
		tris_before += t
		if e.has("maxTris") and t > int(e.maxTris):
			im = _decimate(im, int(e.maxTris))
			gm.mesh = im
		tris_after += _tris(im)
	if e.has("tint"):
		var c := Color(e.tint[0], e.tint[1], e.tint[2])
		for m in state.get_materials():
			if m is BaseMaterial3D:
				m.albedo_color = m.albedo_color * c
				if e.has("metal"): m.metallic = float(e.metal)
	var scene := doc.generate_scene(state) as Node3D
	var root := Node3D.new()
	root.name = e.key
	root.add_child(scene)
	scene.owner = root
	_own(scene, root)

	var old := _load_old(e.key)
	if old != null:
		var ob := _bounds(old)
		var nb := _bounds(root)
		# поворот: длинная ось новой → длинная ось старой, средняя → средняя
		# в руках — любая ось может стать длиной (+X); остальное стоит на земле: вертикаль не трогаем, меняем местами только X/Z
		var basis := _axis_map(nb.size, ob.size) if String(e.key).begins_with("held_") else _flat_map(nb.size, ob.size)
		if e.get("flip", false): basis = Basis(Vector3.UP, PI) * basis
		scene.transform = Transform3D(basis, Vector3.ZERO) * scene.transform
		nb = _bounds(root)
		var k: float = _max3(ob.size) / max(0.001, _max3(nb.size))
		scene.transform = Transform3D(Basis().scaled(Vector3.ONE * k), Vector3.ZERO) * scene.transform
		nb = _bounds(root)
		var oc := ob.get_center()
		var nc := nb.get_center()
		scene.transform = Transform3D(Basis(), Vector3(oc.x - nc.x, ob.position.y - nb.position.y, oc.z - nc.z)) * scene.transform
		old.free()

	var out := ProjectSettings.globalize_path(ART + "hq/%s.glb" % e.key)
	var wdoc := GLTFDocument.new()
	wdoc.image_format = "JPEG"
	wdoc.lossy_quality = 0.85
	var wstate := GLTFState.new()
	if wdoc.append_from_scene(root, wstate) != OK or wdoc.write_to_filesystem(wstate, out) != OK:
		push_error("не записан %s" % out); root.free(); return false
	var kb := FileAccess.get_file_as_bytes(out).size() / 1024
	print("%-22s ← %-24s %7d → %6d тр.  %5d КБ" % [e.key, e.src, tris_before, tris_after, kb])
	root.free()
	return true

func _own(n: Node, owner_node: Node) -> void:
	for c in n.get_children():
		c.owner = owner_node
		_own(c, owner_node)

func _tris(im: ImporterMesh) -> int:
	var t := 0
	for s in im.get_surface_count():
		var arr := im.get_surface_arrays(s)
		var idx = arr[Mesh.ARRAY_INDEX]
		t += (idx.size() if idx != null else arr[Mesh.ARRAY_VERTEX].size()) / 3
	return t

## Упрощение встроенным генератором LOD: берём первый уровень, где треугольников не больше лимита (или самый грубый).
func _decimate(im: ImporterMesh, max_tris: int) -> ImporterMesh:
	im.generate_lods(25.0, 60.0, [])
	var out := ImporterMesh.new()
	var total := _tris(im)
	for s in im.get_surface_count():
		var arr := im.get_surface_arrays(s)
		var share: float = float((arr[Mesh.ARRAY_INDEX] as PackedInt32Array).size() / 3) / max(1.0, float(total))
		var limit := int(max_tris * share)
		var best: PackedInt32Array = arr[Mesh.ARRAY_INDEX]
		for l in im.get_surface_lod_count(s):
			var li := im.get_surface_lod_indices(s, l)
			best = li
			if li.size() / 3 <= limit: break
		arr[Mesh.ARRAY_INDEX] = best
		out.add_surface(im.get_surface_primitive_type(s), arr, [], {}, im.get_surface_material(s), im.get_surface_name(s))
	return out

func _load_old(key: String) -> Node3D:
	var p := ProjectSettings.globalize_path(ART + key + ".glb")
	if not FileAccess.file_exists(p): return null
	var d := GLTFDocument.new()
	var s := GLTFState.new()
	if d.append_from_file(p, s) != OK: return null
	return d.generate_scene(s) as Node3D

func _bounds(root: Node3D) -> AABB:
	var acc := [false, AABB()]
	_walk(root, Transform3D.IDENTITY, acc)
	return acc[1] if acc[0] else AABB(Vector3.ZERO, Vector3.ONE)

func _walk(n: Node, t: Transform3D, acc: Array) -> void:
	for c in n.get_children():
		if not (c is Node3D): continue
		var ct: Transform3D = t * (c as Node3D).transform
		if c is MeshInstance3D and c.mesh != null:
			var b: AABB = ct * c.mesh.get_aabb()
			acc[1] = b if not acc[0] else (acc[1] as AABB).merge(b)
			acc[0] = true
		_walk(c, ct, acc)

func _max3(v: Vector3) -> float:
	return max(v.x, max(v.y, v.z))

## Поворот на кратные 90°: оси новой модели по убыванию длины → оси старой по убыванию длины.
func _axis_map(n: Vector3, o: Vector3) -> Basis:
	var ni := _order(n)
	var oi := _order(o)
	var cols := [Vector3.ZERO, Vector3.ZERO, Vector3.ZERO]
	for r in 3:
		var v := Vector3.ZERO
		v[oi[r]] = 1.0
		cols[ni[r]] = v
	var b := Basis(cols[0], cols[1], cols[2])
	if b.determinant() < 0: b = Basis(-cols[0], cols[1], cols[2]) if ni[2] == 0 else Basis(cols[0], -cols[1], cols[2]) if ni[2] == 1 else Basis(cols[0], cols[1], -cols[2])
	return b

## Поворот вокруг вертикали на 0 или 90°: длинная сторона в плане новой — вдоль длинной стороны старой.
func _flat_map(n: Vector3, o: Vector3) -> Basis:
	if (n.x >= n.z) == (o.x >= o.z): return Basis()
	return Basis(Vector3.UP, PI * 0.5)

func _order(v: Vector3) -> Array:
	var a := [0, 1, 2]
	a.sort_custom(func(i, j): return v[i] > v[j])
	return a

## Лист превью: старая модель (слева) и новая (справа) по парам, под углом камеры игры.
func _preview(list: Array, only: Array[String], out: String) -> void:
	await process_frame   # из _initialize корень ещё не в дереве
	var world := Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.32, 0.34, 0.36)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.7, 0.72, 0.75)
	e.ambient_light_energy = 0.6
	env.environment = e
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 35, 0)
	sun.shadow_enabled = true
	world.add_child(sun)
	var keys: Array = []
	for it in list:
		if only.size() == 0 or only.has(it.key): keys.append(it.key)
	var cols := 6
	var cell := 3.0
	for i in keys.size():
		var key: String = keys[i]
		var pos := Vector3((i % cols) * cell * 2.2, 0, (i / cols) * cell * 1.6)
		for side in 2:
			var p := ProjectSettings.globalize_path(ART + ("hq/" if side == 1 else "") + key + ".glb")
			if not FileAccess.file_exists(p): continue
			var d := GLTFDocument.new()
			var s := GLTFState.new()
			if d.append_from_file(p, s) != OK: continue
			var m := d.generate_scene(s) as Node3D
			var holder := Node3D.new()
			holder.add_child(m)
			var b := _bounds(holder)
			var k: float = 2.2 / max(0.05, _max3(b.size))   # мелочь крупнее — чтобы было видно
			holder.scale = Vector3.ONE * k
			holder.position = pos + Vector3(side * cell, 0, 0)
			world.add_child(holder)
		var lbl := Label3D.new()
		lbl.text = key
		lbl.pixel_size = 0.012
		lbl.rotation_degrees = Vector3(-60, 0, 0)
		lbl.position = pos + Vector3(cell * 0.5, 0.05, 1.3)
		world.add_child(lbl)
	var rows := int(ceil(keys.size() / float(cols)))
	var cam := Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.size = max(rows * cell * 1.6 + 2.0, (cols * cell * 2.2) * 9.0 / 16.0 + 1.0)
	var center := Vector3((cols - 1) * cell * 2.2 * 0.5 + cell * 0.5, 0, (rows - 1) * cell * 1.6 * 0.5)
	world.add_child(cam)
	cam.look_at_from_position(center + Vector3(0, 30, 18), center, Vector3.UP)
	cam.current = true
	root.size = Vector2i(1920, 1080)
	for f in 8: await process_frame
	root.get_texture().get_image().save_png(out)
	print("превью: ", out)
