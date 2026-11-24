"""Rigs the Hunyuan woodcutter to the CC0 Quaternius skeleton in Blender and exports a preview GLB.

Run: blender -b --factory-startup --python tools/rig_ual_character.py
"""
import json
import struct
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(bpy.path.abspath(__file__)).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
import bake_villagers as bv  # noqa: E402

VERSION = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "woodcutter-v1"
BASE = ROOT / "assets-candidates" / "hunyuan3d" / VERSION
GAME = BASE / "game"
OUTPUT = BASE / "rigged"
UAL = ROOT / "assets-source" / "quaternius-ual"
UAL1 = UAL / "ual1" / "Universal Animation Library[Standard]" / "Unreal-Godot" / "UAL1_Standard.glb"
UAL2 = UAL / "ual2" / "Universal Animation Library 2[Standard]" / "Unreal-Godot" / "UAL2_Standard.glb"
ROLE = VERSION.split("-")[0]
COMMON_CLIPS = {"Idle_Loop": "idle", "Walk_Loop": "walk", "Walk_Carry_Loop": "carry"}
ROLE_CLIPS = {
    "woodcutter": {"TreeChopping_Loop": "chop"},
    "farmer": {"Farm_PlantSeed": "sow", "Farm_Harvest": "reap"},
    "woman": {"Farm_PlantSeed": "sow", "Farm_Harvest": "reap", "Interact": "knead"},
    "laborer": {"Farm_Harvest": "reap", "Interact": "knead"},
    "builder": {"Fixing_Kneeling": "build"},
    "baker": {"Interact": "knead"},
    "fisher": {"Farm_Watering": "fish"},
    "miner": {"TreeChopping_Loop": "mine"},
    "elder": {"Idle_FoldArms_Loop": "rest"},
}
TOOL_ROLES = {"woodcutter", "miner"}
PROP_ROLES = {"fisher", "farmer", "builder"}  # One-handed tools held in the right hand.
CLIPS = {**COMMON_CLIPS, **ROLE_CLIPS[ROLE]}
RIGID_HAT_ROLES = {"farmer"}
HAT_BOTTOM = 1.56
LANDMARKS = {
    "pelvis": "hips", "neck_01": "neck", "Head": "head",
    "upperarm_l": "armL", "lowerarm_l": "foreL", "hand_l": "handL", "upperarm_r": "armR", "lowerarm_r": "foreR", "hand_r": "handR",
    "thigh_l": "legL", "calf_l": "shinL", "foot_l": "footL", "thigh_r": "legR", "calf_r": "shinR", "foot_r": "footR",
}
AIM = {
    "clavicle_l": "upperarm_l", "upperarm_l": "lowerarm_l", "lowerarm_l": "hand_l",
    "clavicle_r": "upperarm_r", "upperarm_r": "lowerarm_r", "lowerarm_r": "hand_r",
    "thigh_l": "calf_l", "calf_l": "foot_l", "thigh_r": "calf_r", "calf_r": "foot_r",
}
WORLD_ORIENTED = {"neck_01", "Head", "foot_l", "ball_l", "ball_leaf_l", "foot_r", "ball_r", "ball_leaf_r"}


def to_blender(p):
    return Vector((p[0], -p[2], p[1]))


def activate(obj, mode="OBJECT"):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if mode != "OBJECT":
        bpy.ops.object.mode_set(mode=mode)


def import_glb(path):
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=str(path))
    return list(set(bpy.data.objects) - before_objects), list(set(bpy.data.actions) - before_actions)


def find_action(actions, clip):
    for action in actions:
        if action.name == clip or action.name.startswith(clip + "_") or action.name.startswith(clip + "."):
            return action
    raise RuntimeError(f"Missing clip {clip}: {[a.name for a in actions][:8]}")


def object_slot(action):
    for slot in action.slots:
        if slot.target_id_type == "OBJECT":
            return slot
    return action.slots[0]


def assign(arm, action):
    data = arm.animation_data or arm.animation_data_create()
    data.action = action
    data.action_slot = object_slot(action)


def load_body():
    raw = (GAME / "woodcutter.dchq").read_bytes()
    vertices, index_count = struct.unpack("<ii", raw[:8])
    at = 8
    p = np.frombuffer(raw, "<f4", vertices * 3, at).reshape(-1, 3); at += vertices * 12
    at += vertices * 12
    uv = np.frombuffer(raw, "<f4", vertices * 2, at).reshape(-1, 2); at += vertices * 8
    at += vertices * 12 + vertices * 16 + vertices * 16
    indices = np.frombuffer(raw, "<i4", index_count, at).reshape(-1, 3)
    mesh = bpy.data.meshes.new("Woodcutter")
    mesh.from_pydata([tuple(to_blender(v)) for v in p], [], indices.tolist())
    layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        u, v = uv[loop.vertex_index]
        layer.data[loop.index].uv = (float(u), 1 - float(v))
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    material = bpy.data.materials.new("WoodcutterMaterial")
    material.use_nodes = True
    shader = material.node_tree.nodes["Principled BSDF"]
    shader.inputs["Roughness"].default_value = 1.0
    image = material.node_tree.nodes.new("ShaderNodeTexImage")
    image.image = bpy.data.images.load(str(GAME / "woodcutter_albedo.png"))
    material.node_tree.links.new(image.outputs["Color"], shader.inputs["Base Color"])
    mesh.materials.append(material)
    obj = bpy.data.objects.new("Woodcutter", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def fit_rest(arm, landmarks):
    bones = arm.data.bones
    source_rot = {b.name: b.matrix_local.to_3x3() for b in bones}
    source_head = {b.name: b.head_local.copy() for b in bones}
    order = [b.name for b in bones if b.parent is None]
    for name in order:
        order.extend(child.name for child in bones[name].children)
    targets = {bone: to_blender(landmarks[key]) for bone, key in LANDMARKS.items()}
    span = source_head["neck_01"].z - source_head["pelvis"].z
    for bone in ("spine_01", "spine_02", "spine_03"):
        t = (source_head[bone].z - source_head["pelvis"].z) / span
        targets[bone] = targets["pelvis"].lerp(targets["neck_01"], t)

    activate(arm, "EDIT")
    edit = arm.data.edit_bones
    total, head = {}, {}
    for name in order:
        bone = edit[name]
        bone.use_connect = False
        parent = bone.parent.name if bone.parent else None
        parent_rotation = total[parent] if parent else Matrix.Identity(3)
        if name in targets:
            position = targets[name].copy()
        elif parent:
            position = head[parent] + parent_rotation @ (source_head[name] - source_head[parent])
        else:
            position = source_head[name].copy()
        rotation = parent_rotation.copy()
        if name in AIM:
            child = AIM[name]
            current = parent_rotation @ (source_head[child] - source_head[name])
            desired = targets[child] - position
            rotation = current.rotation_difference(desired).to_matrix() @ parent_rotation
        if name in WORLD_ORIENTED:
            rotation = Matrix.Identity(3)
        length = bone.length
        bone.matrix = Matrix.Translation(position) @ (rotation @ source_rot[name]).to_4x4()
        bone.length = length
        total[name], head[name] = rotation, position
    bpy.ops.object.mode_set(mode="OBJECT")
    return order, source_rot


def bind(body, arm):
    for bone in arm.data.bones:
        bone.use_deform = not any(key in bone.name for key in ("index", "middle", "ring", "pinky", "thumb", "leaf")) and bone.name != "root"
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    if ROLE in RIGID_HAT_ROLES:
        # Automatic weights bend a wide brim with the neck and shoulders, so a hat follows the head only.
        head = body.vertex_groups["Head"]
        for vertex in body.data.vertices:
            if vertex.co.z > HAT_BOTTOM:
                for group in list(vertex.groups):
                    body.vertex_groups[group.group].remove([vertex.index])
                head.add([vertex.index], 1.0, "REPLACE")
    assigned = sum(1 for v in body.data.vertices if any(g.weight > 0.001 for g in v.groups))
    if assigned != len(body.data.vertices):
        raise RuntimeError(f"Automatic weights left {len(body.data.vertices) - assigned} vertices unassigned")


def restore_source_pose(body, arm, order, source_rot):
    activate(arm, "POSE")
    for name in order:
        bone = arm.pose.bones[name]
        translation = bone.matrix.to_translation()
        bone.matrix = Matrix.Translation(translation) @ source_rot[name].to_4x4()
        bpy.context.view_layer.update()
    activate(body)
    modifier = next(m for m in body.modifiers if m.type == "ARMATURE")
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    activate(arm, "POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm


def analyse_axe(arm, action):
    assign(arm, action)
    scene = bpy.context.scene
    start, end = [int(round(v)) for v in action.frame_range]
    right, left, chest = arm.pose.bones["hand_r"], arm.pose.bones["hand_l"], arm.pose.bones["spine_03"]
    samples = []
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        samples.append((right.matrix.copy(), left.matrix.to_translation(), chest.matrix.to_translation()))
    count = len(samples)
    hand = [m.to_translation() for m, _, _ in samples]
    hand_velocity = [hand[(i + 1) % count] - hand[i - 1] for i in range(count)]
    fastest = max(range(count), key=lambda i: hand_velocity[i].length)
    top_speed = hand_velocity[fastest].length
    # Contact is where the swing stops; later frames are the recoil and move the opposite way.
    flat = lambda value: Vector((value.x, value.y, 0))
    heading = flat(hand_velocity[fastest])
    peak = fastest
    for offset in range(1, count // 2):
        index = (fastest + offset) % count
        if flat(hand_velocity[index]).dot(heading) <= top_speed * top_speed * 0.01:
            break
        peak = index
    swing = hand_velocity[(peak - 1) % count].copy()
    swing.z = 0
    swing.normalize()
    radial = hand[peak] - samples[peak][2]
    radial.z = 0
    radial -= swing * radial.dot(swing)
    radial.normalize()
    to_hand = samples[peak][0].to_3x3().inverted()
    direction = (to_hand @ radial).normalized()
    blade = to_hand @ swing
    blade = (blade - direction * blade.dot(direction)).normalized()
    side = direction.cross(blade).normalized()
    grip = Vector((0, 0.085, 0))
    reach = 0.6
    tips = [m @ (grip + direction * reach) for m, _, _ in samples]
    speeds = [tips[(i + 1) % count] - tips[i - 1] for i in range(count)]
    rotation = Matrix((side, direction, blade)).transposed()
    transform = Matrix.Translation(grip) @ rotation.to_4x4()
    velocity = speeds[(peak - 1) % count].normalized()
    cutting_edge = samples[peak][0] @ transform @ Vector((0, 0.56, 0.265))
    report = {
        "frames": len(samples),
        "grip": "two-handed, left arm solved with Blender IK",
        "strike_frame": start + peak,
        "contact_index": peak,
        "fastest_index": fastest,
        "strike_velocity_horizontal": round(Vector((velocity.x, velocity.y)).length, 3),
        "strike_velocity_vertical": round(abs(velocity.z), 3),
        "strike_tip_gltf": [round(tips[peak].x, 3), round(tips[peak].z, 3), round(-tips[peak].y, 3)],
        "strike_edge_gltf": [round(cutting_edge.x, 3), round(cutting_edge.z, 3), round(-cutting_edge.y, 3)],
        "strike_direction_gltf": [round(swing.x, 3), round(swing.z, 3), round(-swing.y, 3)],
    }
    data = arm.animation_data
    data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    return transform, report


def build_axe(arm, transform):
    m = bv.Mesh()
    wood, dark, flint, light, edge = (0.56, 0.37, 0.19), (0.27, 0.17, 0.09), (0.40, 0.41, 0.39), (0.56, 0.56, 0.53), (0.78, 0.78, 0.74)
    if ROLE == "fisher":
        m.tube(0, (0, -0.2, 0), (0, 0.8, 0), 0.02, 0.012, wood, 6)
        m.tube(0, (0, 0.8, 0), (0, 1.9, 0.08), 0.012, 0.004, wood, 5)
        m.tube(0, (0, 1.9, 0.08), (0, 1.86, 0.1), 0.006, 0.006, light, 4)
    elif ROLE == "farmer":
        m.tube(0, (0, -0.1, 0), (0, 0.2, 0), 0.022, 0.02, wood, 6)
        for a, b, r0, r1 in (((0, 0.2, 0), (0, 0.32, 0.12), 0.024, 0.02), ((0, 0.32, 0.12), (0, 0.33, 0.27), 0.02, 0.014), ((0, 0.33, 0.27), (0, 0.26, 0.37), 0.014, 0.002)):
            m.tube(0, a, b, r0, r1, edge, 5)
    elif ROLE == "builder":
        m.tube(0, (0, -0.12, 0), (0, 0.3, 0), 0.02, 0.018, wood, 6)
        m.tube(0, (0, 0.3, -0.08), (0, 0.3, 0.08), 0.042, 0.042, flint, 6)
    else:
        m.tube(0, (0, -0.34, 0), (0, 0.66, 0), 0.031, 0.026, wood, 8)
    for y in (0.47, 0.53, 0.59, 0.65) if ROLE in TOOL_ROLES else ():
        m.tube(0, (0, y - 0.013, 0), (0, y + 0.013, 0), 0.04, 0.04, dark, 8)
    if ROLE in PROP_ROLES:
        pass
    elif ROLE == "miner":
        # Pickaxe head: a curved iron bar through the handle, tapering to a point on each side.
        iron, tip = (0.36, 0.37, 0.39), (0.62, 0.63, 0.62)
        m.tube(0, (0, 0.585, -0.05), (0, 0.615, 0.05), 0.052, 0.052, iron, 8)
        for sign in (-1, 1):
            m.tube(0, (0, 0.61, 0), (0, 0.595, sign * 0.17), 0.04, 0.03, iron, 6)
            m.tube(0, (0, 0.595, sign * 0.17), (0, 0.55, sign * 0.29), 0.03, 0.017, iron, 6)
            m.tube(0, (0, 0.55, sign * 0.29), (0, 0.49, sign * 0.37), 0.017, 0.002, tip, 6)
    else:
        inside = np.array((0, 0.56, 0.12))
        t = 0.048
        back_top, back_bottom = (0.66, 0.02), (0.46, 0.02)
        mid_top, mid_bottom = (0.69, 0.17), (0.43, 0.17)
        edge_top, edge_bottom = (0.72, 0.265), (0.40, 0.265)
        point = lambda x, p: np.array((x, p[0], p[1]))
        for sign in (-1, 1):
            m.quad(0, inside, point(sign * t, back_top), point(sign * t, back_bottom), point(sign * 0.026, mid_bottom), point(sign * 0.026, mid_top), flint)
            m.quad(0, inside, point(sign * 0.026, mid_top), point(sign * 0.026, mid_bottom), point(0, edge_bottom), point(0, edge_top), edge)
            m.quad(0, inside, point(sign * t, back_top), point(sign * 0.026, mid_top), point(0, edge_top), point(0, back_top), light)
            m.quad(0, inside, point(sign * t, back_bottom), point(sign * 0.026, mid_bottom), point(0, edge_bottom), point(0, back_bottom), light)
        m.quad(0, inside, point(t, back_top), point(t, back_bottom), point(-t, back_bottom), point(-t, back_top), flint)
    positions = np.array(m.p)
    colors = np.round(np.array(m.c), 3)
    faces = [(i, i + 1, i + 2) for i in range(0, len(positions), 3)]
    rest = arm.data.bones["hand_r"].matrix_local @ transform
    mesh = bpy.data.meshes.new("Axe")
    mesh.from_pydata([tuple(rest @ Vector(p)) for p in positions], [], faces)
    palette = [tuple(c) for c in np.unique(colors, axis=0)]
    for i, color in enumerate(palette):
        material = bpy.data.materials.new(f"Axe{i}")
        material.use_nodes = True
        shader = material.node_tree.nodes["Principled BSDF"]
        shader.inputs["Base Color"].default_value = (*(float(c) ** 2.2 for c in color), 1)
        shader.inputs["Roughness"].default_value = 0.9
        mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.material_index = palette.index(tuple(colors[polygon.vertices[0]]))
    mesh.update()
    obj = bpy.data.objects.new("Axe", mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = arm
    group = obj.vertex_groups.new(name="hand_r")
    group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    return obj


def add_left_grip(arm, action, transform):
    """Bakes the left hand onto the axe handle using Blender's IK and Damped Track constraints."""
    assign(arm, action)
    scene = bpy.context.scene
    start, end = [int(round(v)) for v in action.frame_range]
    wrist_target = bpy.data.objects.new("LeftWristTarget", None)
    grip_target = bpy.data.objects.new("LeftGripTarget", None)
    scene.collection.objects.link(wrist_target)
    scene.collection.objects.link(grip_target)
    right, chest = arm.pose.bones["hand_r"], arm.pose.bones["spine_03"]
    handle_point = transform @ Vector((0, -0.13, 0))
    wrists = {}
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        handle = right.matrix @ handle_point
        wrist = handle + (chest.matrix.to_translation() - handle).normalized() * 0.085
        wrists[frame] = wrist.copy()
        grip_target.location = handle
        wrist_target.location = wrist
        grip_target.keyframe_insert("location", frame=frame)
        wrist_target.keyframe_insert("location", frame=frame)

    activate(arm, "POSE")
    ik = arm.pose.bones["lowerarm_l"].constraints.new("IK")
    ik.target = wrist_target
    ik.chain_count = 3
    track = arm.pose.bones["hand_l"].constraints.new("DAMPED_TRACK")
    track.target = grip_target
    track.track_axis = "TRACK_Y"
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.nla.bake(frame_start=start, frame_end=end, step=1, only_selected=True, visual_keying=True,
                     clear_constraints=True, use_current_action=False, bake_types={"POSE"})
    baked = arm.animation_data.action
    baked.name = "TreeChopping_TwoHand"
    baked.use_fake_user = True
    errors = []
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        errors.append((arm.pose.bones["hand_l"].matrix.to_translation() - wrists[frame]).length)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.data.objects.remove(wrist_target, do_unlink=True)
    bpy.data.objects.remove(grip_target, do_unlink=True)
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    return baked, {"left_wrist_error_max": round(max(errors), 3), "left_wrist_error_mean": round(sum(errors) / len(errors), 3)}


def to_game_matrix():
    return Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))


def game_mesh(obj, joint, with_texture):
    """Splits vertices at UV and normal seams and returns arrays in the game's mesh layout."""
    convert = to_game_matrix()
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active.data if with_texture else None
    normals = mesh.corner_normals
    palette = []
    for material in mesh.materials:
        base = material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
        palette.append((1.0, 1.0, 1.0) if with_texture else tuple(float(c) ** (1 / 2.2) for c in base[:3]))
    keys, positions, out_normals, uvs, colors, joints, weights, indices = {}, [], [], [], [], [], [], []
    rotation = (convert @ obj.matrix_world).to_3x3()
    for triangle in mesh.loop_triangles:
        corner = []
        for loop in triangle.loops:
            vertex = mesh.loops[loop].vertex_index
            normal = normals[loop].vector
            uv = uv_layer[loop].uv if with_texture else (0.0, 0.0)
            key = (vertex, round(uv[0], 5), round(uv[1], 5), round(normal.x, 3), round(normal.y, 3), round(normal.z, 3), triangle.material_index)
            if key not in keys:
                keys[key] = len(positions)
                positions.append(tuple(convert @ obj.matrix_world @ mesh.vertices[vertex].co))
                out_normals.append(tuple((rotation @ normal).normalized()))
                uvs.append((uv[0], 1 - uv[1]))
                colors.append(palette[triangle.material_index])
                pairs = sorted(((g.weight, joint[obj.vertex_groups[g.group].name]) for g in mesh.vertices[vertex].groups
                                if obj.vertex_groups[g.group].name in joint and g.weight > 0.001), reverse=True)[:4]
                total = sum(w for w, _ in pairs)
                joints.append(tuple(j for _, j in pairs) + (0,) * (4 - len(pairs)))
                weights.append(tuple(w / total for w, _ in pairs) + (0.0,) * (4 - len(pairs)))
            corner.append(keys[key])
        indices.extend((corner[0], corner[2], corner[1]))  # Godot treats clockwise triangles as front faces.
    return positions, out_normals, uvs, colors, joints, weights, indices


def write_game_mesh(path, arrays):
    positions, normals, uvs, colors, joints, weights, indices = arrays
    with path.open("wb") as out:
        out.write(struct.pack("<ii", len(positions), len(indices)))
        for values in (positions, normals, uvs, colors, joints, weights):
            out.write(np.asarray(values, dtype="<f4").tobytes())
        out.write(np.asarray(indices, dtype="<i4").tobytes())


def export_game(arm, body, axe, actions, chop):
    """Writes the mesh, tool, texture and sampled bone matrices that the game loads directly."""
    name = "hq_" + VERSION.split("-")[0]
    out = ROOT / "game" / "assets" / "characters"
    used = sorted({g.name for g in body.vertex_groups if g.name in arm.data.bones} | {"hand_r"})
    joint = {bone: i for i, bone in enumerate(used)}
    write_game_mesh(out / f"{name}.dcmesh", game_mesh(body, joint, True))
    if axe:
        write_game_mesh(out / f"{name}_tool.dcmesh", game_mesh(axe, joint, False))
    (out / f"{name}.dctex").write_bytes((GAME / "woodcutter_albedo.png").read_bytes())
    convert, inverse = to_game_matrix(), to_game_matrix().inverted()
    rest = {bone: arm.data.bones[bone].matrix_local.inverted() for bone in used}
    scene = bpy.context.scene
    frames, table = [], []
    for clip in CLIPS:
        action = chop if clip == "TreeChopping_Loop" else find_action(actions, clip)
        assign(arm, action)
        start, end = [int(round(v)) for v in action.frame_range]
        table.append((len(frames), max(1, end - start)))  # The last key repeats the first in these loops.
        for frame in range(start, max(start + 1, end)):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            row = []
            for bone in used:
                skin = convert @ arm.pose.bones[bone].matrix @ rest[bone] @ inverse
                row.append([[skin[r][c] for c in range(4)] for r in range(3)])
            frames.append(row)
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    with (out / f"{name}.rig").open("wb") as rig:
        rig.write(struct.pack("<iiii", len(used), len(frames), len(table), scene.render.fps))
        for start, length in table:
            rig.write(struct.pack("<ii", start, length))
        rig.write(np.asarray(frames, dtype="<f4").tobytes())
    print("GAME_EXPORT", name, "joints", len(used), "frames", len(frames), "clips", table)


def add_tracks(arm, actions, overrides=None):
    data = arm.animation_data or arm.animation_data_create()
    data.action = None
    while data.nla_tracks:
        data.nla_tracks.remove(data.nla_tracks[0])
    for clip, label in CLIPS.items():
        action = (overrides or {}).get(clip) or find_action(actions, clip)
        track = data.nla_tracks.new()
        track.name = label
        strip = track.strips.new(label, int(action.frame_range[0]), action)
        strip.action_slot = object_slot(action)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    objects2, actions2 = import_glb(UAL2)
    arm = next(o for o in objects2 if o.type == "ARMATURE")
    objects1, actions1 = import_glb(UAL1)
    for obj in [o for o in objects1 + objects2 if o != arm]:
        bpy.data.objects.remove(obj, do_unlink=True)
    actions = actions2 + actions1
    if arm.animation_data:
        arm.animation_data.action = None
        while arm.animation_data.nla_tracks:
            arm.animation_data.nla_tracks.remove(arm.animation_data.nla_tracks[0])
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    arm.location, arm.rotation_mode = (0, 0, 0), "XYZ"
    arm.rotation_euler = (0, 0, 0)
    arm.scale = (1, 1, 1)

    b = arm.data.bones
    orientation = {"left_x": round(b["hand_l"].head_local.x, 3), "toe_y_minus_ankle_y": round(b["ball_l"].head_local.y - b["foot_l"].head_local.y, 3), "head_z": round(b["Head"].head_local.z, 3)}
    if orientation["left_x"] <= 0 or orientation["toe_y_minus_ankle_y"] >= 0:
        raise RuntimeError(f"Unexpected rig orientation: {orientation}")

    landmarks = json.loads((GAME / "landmarks.json").read_text(encoding="utf-8"))
    body = load_body()
    order, source_rot = fit_rest(arm, landmarks)
    bind(body, arm)
    restore_source_pose(body, arm, order, source_rot)
    axe, chop, axe_report = None, None, {}
    if ROLE in TOOL_ROLES:
        transform, axe_report = analyse_axe(arm, find_action(actions, "TreeChopping_Loop"))
        chop, grip_report = add_left_grip(arm, find_action(actions, "TreeChopping_Loop"), transform)
        axe_report.update(grip_report)
        axe = build_axe(arm, transform)
    elif ROLE in PROP_ROLES:
        axe = build_axe(arm, Matrix.Translation((0, 0.085, 0)) @ Matrix.Rotation(math.radians(90), 4, "X"))
    if VERSION != "woodcutter-v1":
        export_game(arm, body, axe, actions, chop)
    add_tracks(arm, actions, {"TreeChopping_Loop": chop} if chop else None)
    activate(arm)
    body.select_set(True)
    if axe:
        axe.select_set(True)
    path = OUTPUT / f"{ROLE}.glb"
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_animations=True, export_animation_mode="NLA_TRACKS",
        export_force_sampling=True, export_def_bones=True, export_skins=True, export_image_format="AUTO", export_apply=False,
        export_normals=True,
    )
    report = {"orientation": orientation, "body_triangles": len(body.data.polygons), "body_vertices": len(body.data.vertices),
              "axe_triangles": len(axe.data.polygons) if axe else 0, "clips": list(CLIPS.values()), "axe": axe_report, "glb_kb": path.stat().st_size // 1024}
    (OUTPUT / "rig-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("RIG_OK", json.dumps(report))


try:
    main()
except Exception as error:
    import traceback
    traceback.print_exc()
    print("RIG_FAILED", error)
    sys.exit(1)
