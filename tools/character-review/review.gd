extends Node3D

const BASE := "D:/2026/101.DeskCiv/assets-candidates/hunyuan3d/woodcutter-v2/"
const RIGGED := BASE + "rigged/woodcutter.glb"
const OUT := BASE + "review/"
var players: Array[AnimationPlayer] = []
var report: Dictionary
var total := 30
var frame_index := 0
var wait := 0

func _ready() -> void:
	DirAccess.make_dir_recursive_absolute(OUT + "frames")
	report = JSON.parse_string(FileAccess.get_file_as_string(BASE + "rigged/rig-report.json"))
	total = int(report["axe"]["frames"])
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.36, 0.53, 0.25)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.72, 0.76, 0.78)
	env.ambient_light_energy = 0.75
	var world := WorldEnvironment.new()
	world.environment = env
	add_child(world)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, -20, 0)
	sun.light_energy = 1.1
	add_child(sun)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(30, 30)
	var grass := StandardMaterial3D.new()
	grass.albedo_color = Color(0.36, 0.53, 0.25)
	plane.material = grass
	ground.mesh = plane
	add_child(ground)
	_place()
	_trunk()
	var camera := Camera3D.new()
	camera.fov = 30
	add_child(camera)
	camera.current = true
	camera.look_at_from_position(Vector3(-2.4, 3.3, 7.2), Vector3(0.1, 1.05, 0.5), Vector3.UP)
	_seek(0)

func _place() -> void:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(RIGGED, state) != OK:
		print("LOAD_FAILED")
		return
	var node: Node3D = doc.generate_scene(state)
	add_child(node)
	var player := _player(node)
	if player == null or not player.has_animation("chop"):
		print("NO_CLIP")
		return
	player.play("chop")
	player.pause()
	players.append(player)

func _seek(index: int) -> void:
	for player in players:
		player.seek(player.get_animation("chop").length * index / float(total), true)

func _trunk() -> void:
	var tip: Array = report["axe"]["strike_edge_gltf"]
	var way: Array = report["axe"]["strike_direction_gltf"]
	var trunk := MeshInstance3D.new()
	var cylinder := CylinderMesh.new()
	cylinder.top_radius = 0.2
	cylinder.bottom_radius = 0.27
	cylinder.height = 3.0
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.36, 0.25, 0.15)
	cylinder.material = material
	trunk.mesh = cylinder
	trunk.position = Vector3(tip[0] + way[0] * 0.2, 1.5, tip[2] + way[2] * 0.2)
	add_child(trunk)

func _player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := _player(child)
		if found:
			return found
	return null

func _process(_delta: float) -> void:
	wait += 1
	if wait < 4:
		return
	wait = 0
	get_viewport().get_texture().get_image().save_png(OUT + "frames/%02d.png" % frame_index)
	frame_index += 1
	if frame_index >= total:
		get_tree().quit()
		return
	_seek(frame_index)
