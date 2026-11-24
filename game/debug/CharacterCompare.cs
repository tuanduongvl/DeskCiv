using System;
using Godot;

namespace DeskCiv;

/// <summary>Review scene comparing the current woodcutter with the Hunyuan test character. User arg: --out=DIRECTORY.</summary>
public partial class CharacterCompare : Node3D
{
    private const string Shader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D bones : filter_nearest, repeat_disable;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap;
uniform bool textured = false;
void vertex() {
    int start = int(INSTANCE_CUSTOM.x + 0.5);
    int count = max(1, int(INSTANCE_CUSTOM.y + 0.5));
    float cursor = mod(INSTANCE_CUSTOM.z * float(count), float(count));
    int a = start + int(cursor);
    int b = start + (int(cursor) + 1) % count;
    float blend = fract(cursor);
    vec4 p = vec4(VERTEX, 1.0);
    vec3 skinned = vec3(0.0), normal = vec3(0.0);
    for (int i = 0; i < 4; i++) {
        float w = CUSTOM1[i];
        if (w <= 0.0) continue;
        int x = int(CUSTOM0[i] + 0.5) * 3;
        vec4 r0 = mix(texelFetch(bones, ivec2(x, a), 0), texelFetch(bones, ivec2(x, b), 0), blend);
        vec4 r1 = mix(texelFetch(bones, ivec2(x + 1, a), 0), texelFetch(bones, ivec2(x + 1, b), 0), blend);
        vec4 r2 = mix(texelFetch(bones, ivec2(x + 2, a), 0), texelFetch(bones, ivec2(x + 2, b), 0), blend);
        skinned += w * vec3(dot(r0, p), dot(r1, p), dot(r2, p));
        normal += w * vec3(dot(r0.xyz, NORMAL), dot(r1.xyz, NORMAL), dot(r2.xyz, NORMAL));
    }
    VERTEX = skinned;
    NORMAL = normalize(normal);
}
void fragment() { ALBEDO = textured ? texture(albedo_tex, UV).rgb : COLOR.rgb; }";

    private static readonly (Clip Clip, float Phase)[] Poses =
    {
        (Clip.Idle, 0.2f), (Clip.Walk, 0.25f), (Clip.Chop, 0.05f), (Clip.Chop, 0.35f), (Clip.Chop, 0.58f), (Clip.Chop, 0.72f),
    };

    private readonly (float Distance, float Height, string Name)[] _shots =
    {
        (13.2f, 0.34f, "compare-close.png"), (30f, 0.36f, "compare-game-30.png"), (58f, 0.46f, "compare-game-58.png"),
    };

    private Camera3D _camera;
    private string _out;
    private int _shot, _frames;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) _out = arg[6..];
        BuildStage();

        var current = new CharacterKit();
        ShaderMaterial oldMaterial = NewMaterial((Texture2D)current.Material.GetShaderParameter("bones"), null);
        Mesh oldBody = CharacterKit.LoadMesh("woodcutter"), oldAxe = CharacterKit.LoadMesh("tool_axe");

        string dir = ProjectSettings.GlobalizePath("res://").TrimEnd('/') + "/../assets-candidates/hunyuan3d/woodcutter-v1/game/";
        (ImageTexture newBones, (int Start, int Length)[] newClips) = LoadRig(dir + "rig.bin");
        var albedo = Image.LoadFromFile(dir + "woodcutter_albedo.png");
        albedo.GenerateMipmaps();
        ShaderMaterial bodyMaterial = NewMaterial(newBones, ImageTexture.CreateFromImage(albedo));
        ShaderMaterial axeMaterial = NewMaterial(newBones, null);
        Mesh newBody = LoadHq(dir + "woodcutter.dchq"), newAxe = LoadHq(dir + "axe.dchq");

        for (int i = 0; i < Poses.Length; i++)
        {
            float x = (i - (Poses.Length - 1) / 2f) * 2.15f;
            (Clip clip, float phase) = Poses[i];
            float yaw = i == 0 ? 0 : 0.72f;
            var (oldStart, oldLength) = current.Clips[(int)clip];
            AddFigure(oldBody, oldMaterial, new Vector3(x, 0, -2.35f), yaw, 1.12f, oldStart, oldLength, phase);
            if (CharacterKit.ToolFor(clip) == 0) AddFigure(oldAxe, oldMaterial, new Vector3(x, 0, -2.35f), yaw, 1.12f, oldStart, oldLength, phase);
            var (newStart, newLength) = newClips[(int)clip];
            AddFigure(newBody, bodyMaterial, new Vector3(x, 0, 1.9f), yaw, 1.12f, newStart, newLength, phase);
            if (clip == Clip.Chop) AddFigure(newAxe, axeMaterial, new Vector3(x, 0, 1.9f), yaw, 1.12f, newStart, newLength, phase);
        }
        SetShot();
    }

    private void BuildStage()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.58f, 0.74f, 0.88f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.72f, 0.76f, 0.78f),
                AmbientLightEnergy = 0.74f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-54, 24, 0), LightEnergy = 1.14f, LightColor = new Color(1f, 0.95f, 0.84f) });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(180, 120) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.53f, 0.25f), Roughness = 1 },
        });
        _camera = new Camera3D { Fov = 34, Current = true, Near = 0.2f, Far = 400 };
        AddChild(_camera);
    }

    private static ShaderMaterial NewMaterial(Texture2D bones, Texture2D albedo)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = Shader } };
        material.SetShaderParameter("bones", bones);
        material.SetShaderParameter("textured", albedo != null);
        if (albedo != null) material.SetShaderParameter("albedo_tex", albedo);
        return material;
    }

    private void AddFigure(Mesh mesh, Material material, Vector3 position, float yaw, float scale, int start, int length, float phase)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
        multi.SetInstanceTransform(0, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), position));
        multi.SetInstanceColor(0, Colors.White);
        multi.SetInstanceCustomData(0, new Color(start, length, phase, 0));
        AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = material, CustomAabb = new Aabb(position - Vector3.One * 5, Vector3.One * 10) });
    }

    private static (ImageTexture, (int, int)[]) LoadRig(string path)
    {
        byte[] rig = System.IO.File.ReadAllBytes(path);
        int joints = BitConverter.ToInt32(rig, 0), frames = BitConverter.ToInt32(rig, 4), clips = BitConverter.ToInt32(rig, 8), offset = 16;
        var table = new (int, int)[clips];
        for (int i = 0; i < clips; i++, offset += 8) table[i] = (BitConverter.ToInt32(rig, offset), BitConverter.ToInt32(rig, offset + 4));
        byte[] pixels = new byte[rig.Length - offset];
        Buffer.BlockCopy(rig, offset, pixels, 0, pixels.Length);
        return (ImageTexture.CreateFromImage(Image.CreateFromData(joints * 3, frames, false, Image.Format.Rgbaf, pixels)), table);
    }

    private static ArrayMesh LoadHq(string path)
    {
        byte[] d = System.IO.File.ReadAllBytes(path);
        int v = BitConverter.ToInt32(d, 0), n = BitConverter.ToInt32(d, 4), at = 8;
        var positions = new Vector3[v];
        var normals = new Vector3[v];
        var uvs = new Vector2[v];
        var colors = new Color[v];
        var joints = new float[v * 4];
        var weights = new float[v * 4];
        var indices = new int[n];
        for (int i = 0; i < v; i++, at += 12) positions[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < v; i++, at += 12) normals[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < v; i++, at += 8) uvs[i] = new Vector2(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4));
        for (int i = 0; i < v; i++, at += 12) colors[i] = new Color(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        Buffer.BlockCopy(d, at, joints, 0, joints.Length * 4); at += joints.Length * 4;
        Buffer.BlockCopy(d, at, weights, 0, weights.Length * 4); at += weights.Length * 4;
        Buffer.BlockCopy(d, at, indices, 0, n * 4);
        for (int i = 0; i + 2 < n; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Custom0] = joints;
        arrays[(int)Mesh.ArrayType.Custom1] = weights;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        long format = ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, (Mesh.ArrayFormat)format);
        return mesh;
    }

    private void SetShot()
    {
        (float distance, float height, _) = _shots[_shot];
        _camera.LookAtFromPosition(new Vector3(0, distance * height + 0.8f, distance * 0.92f), new Vector3(0, 0.85f, 0), Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (++_frames < 12) return;
        _frames = 0;
        if (_out != null) GetViewport().GetTexture().GetImage().SavePng(_out + "/" + _shots[_shot].Name);
        if (++_shot >= _shots.Length) GetTree().Quit();
        else SetShot();
    }
}
