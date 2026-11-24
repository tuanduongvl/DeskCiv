extends Node3D
## Renders a .dcmodel building from four angles into one review image.
## Usage: Godot --path tools/character-review --resolution 640x480 res://model.tscn -- <abs path to .dcmodel> <out.png> [part.dcmodel px py pz]

func load_model(path: String) -> ArrayMesh:
	var d := FileAccess.get_file_as_bytes(path)
	var v := d.decode_s32(0)
	var n := d.decode_s32(4)
	var at := 8
	var pos := PackedVector3Array(); var nor := PackedVector3Array(); var uv := PackedVector2Array(); var idx := PackedInt32Array()
	pos.resize(v); nor.resize(v); uv.resize(v); idx.resize(n)
	for i in v:
		pos[i] = Vector3(d.decode_float(at), d.decode_float(at + 4), d.decode_float(at + 8)); at += 12
	for i in v:
		nor[i] = Vector3(d.decode_float(at), d.decode_float(at + 4), d.decode_float(at + 8)); at += 12
	for i in v:
		uv[i] = Vector2(d.decode_float(at), d.decode_float(at + 4)); at += 8
	for i in n:
		idx[i] = d.decode_s32(at); at += 4
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = pos; arrays[Mesh.ARRAY_NORMAL] = nor; arrays[Mesh.ARRAY_TEX_UV] = uv; arrays[Mesh.ARRAY_INDEX] = idx
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var img := Image.new()
	var bytes := FileAccess.get_file_as_bytes(path.get_basename() + ".dctex")
	if bytes[0] == 0x89: img.load_png_from_buffer(bytes)
	else: img.load_jpg_from_buffer(bytes)
	img.generate_mipmaps()
	var mat := StandardMaterial3D.new()
	mat.albedo_texture = ImageTexture.create_from_image(img)
	mat.roughness = 1.0
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	if img.detect_alpha() != Image.ALPHA_NONE:
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
	mesh.surface_set_material(0, mat)
	return mesh

func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	var mesh := load_model(args[0])
	var holder := Node3D.new()
	add_child(holder)
	var body := MeshInstance3D.new(); body.mesh = mesh; holder.add_child(body)
	if args.size() > 5:
		var part := MeshInstance3D.new(); part.mesh = load_model(args[2])
		part.position = Vector3(float(args[3]), float(args[4]), float(args[5]))
		holder.add_child(part)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.36, 0.52, 0.3)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(1, 1, 1)
	env.ambient_light_energy = 0.55
	var we := WorldEnvironment.new(); we.environment = env; add_child(we)
	var sun := DirectionalLight3D.new(); sun.rotation_degrees = Vector3(-50, 30, 0); sun.light_energy = 0.9; add_child(sun)
	var aabb := mesh.get_aabb()
	var size := aabb.size.length()
	var cam := Camera3D.new(); add_child(cam)
	cam.fov = 35
	var shots: Array[Image] = []
	for k in 4:
		holder.rotation.y = k * PI / 2 + PI / 4
		var c := aabb.get_center()
		cam.position = c + Vector3(0, size * 0.75, size * 1.35)
		cam.look_at(c, Vector3.UP)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		shots.append(get_viewport().get_texture().get_image())
	var w := shots[0].get_width(); var h := shots[0].get_height()
	var sheet := Image.create(w * 2, h * 2, false, shots[0].get_format())
	for k in 4:
		sheet.blit_rect(shots[k], Rect2i(0, 0, w, h), Vector2i((k % 2) * w, (k / 2) * h))
	sheet.save_png(args[1])
	get_tree().quit()
