extends Node
## Renders every clip of a rigged character into one sheet, off-screen, so no large window opens.
## Usage: godot --path tools/character-review --resolution 480x270 res://clips.tscn -- <role>-vN

const ROOT := "D:/2026/101.DeskCiv/assets-candidates/hunyuan3d/"
const PHASES := 5
var viewport: SubViewport
var out := ""
var frames := 0

func _ready() -> void:
	var version: String = OS.get_cmdline_user_args()[0]
	var role: String = version.split("-")[0]
	var path := ROOT + version + "/rigged/" + role + ".glb"
	out = ROOT + version + "/review/clips.png"
	viewport = SubViewport.new()
	viewport.size = Vector2i(1500, 1500)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	var world := Node3D.new()
	viewport.add_child(world)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.36, 0.53, 0.25)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.72, 0.76, 0.78)
	env.ambient_light_energy = 0.75
	var environment_node := WorldEnvironment.new()
	environment_node.environment = env
	world.add_child(environment_node)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, -20, 0)
	sun.light_energy = 1.1
	world.add_child(sun)
	var clips: Array = JSON.parse_string(FileAccess.get_file_as_string(ROOT + version + "/rigged/rig-report.json"))["clips"]
	if OS.get_cmdline_user_args().size() > 1:
		clips = Array(OS.get_cmdline_user_args()[1].split(","))
	for row in range(clips.size()):
		for column in range(PHASES):
			_place(world, path, clips[row], column / float(PHASES), Vector3((column - 2) * 2.1, 0, (row - (clips.size() - 1) / 2.0) * 3.0))
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = max(10.6, clips.size() * 2.75)
	world.add_child(camera)
	camera.current = true
	camera.look_at_from_position(Vector3(-2, 8, 12), Vector3(0, 0.9, 0), Vector3.UP)

func _place(world: Node3D, path: String, clip: String, phase: float, origin: Vector3) -> void:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		print("LOAD_FAILED ", path)
		return
	var node: Node3D = doc.generate_scene(state)
	node.position = origin
	world.add_child(node)
	var player := _player(node)
	if player == null or not player.has_animation(clip):
		print("NO_CLIP ", clip)
		return
	player.play(clip)
	player.seek(player.get_animation(clip).length * phase, true)
	player.pause()

func _player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := _player(child)
		if found:
			return found
	return null

func _process(_delta: float) -> void:
	frames += 1
	if frames < 16:
		return
	viewport.get_texture().get_image().save_png(out)
	print("CLIPS_SAVED ", out)
	get_tree().quit()
