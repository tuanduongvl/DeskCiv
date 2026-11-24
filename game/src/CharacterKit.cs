using System;
using Godot;

namespace DeskCiv;

public enum Clip : byte { Idle, Walk, Carry, Chop, Hoe, Mine, Sow, Gather, Fish, Build, Knead, Reap }

/// <summary>Loads baked characters. Skinning runs in the vertex shader from one small bone texture.</summary>
public sealed class CharacterKit
{
    private const string SkinShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D bones : filter_nearest, repeat_disable;
uniform float fps = 24.0;
void vertex() {
    int start = int(INSTANCE_CUSTOM.x + 0.5);
    int count = max(1, int(INSTANCE_CUSTOM.y + 0.5));
    float cursor = mod(TIME * fps + INSTANCE_CUSTOM.z * float(count), float(count));
    int a = start + int(cursor);
    int b = start + (int(cursor) + 1) % count;
    float blend = fract(cursor);
    vec4 p = vec4(VERTEX, 1.0);
    vec3 skinned = vec3(0.0);
    vec3 normal = vec3(0.0);
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
void fragment() { ALBEDO = COLOR.rgb; }";

    public static readonly string[] Characters = { "laborer", "woman", "woodcutter", "farmer", "fisher", "miner", "builder", "baker", "child", "elder" };
    public static readonly string[] Tools = { "axe", "hoe", "pick", "hammer", "rod", "sickle", "basket", "pestle" };

    public readonly (int Start, int Length)[] Clips;
    public readonly ShaderMaterial Material;

    public CharacterKit()
    {
        (Clips, ImageTexture texture, int fps) = LoadRig("res://assets/characters/rig.bin");
        Material = new ShaderMaterial { Shader = new Shader { Code = SkinShader } };
        Material.SetShaderParameter("bones", texture);
        Material.SetShaderParameter("fps", (float)fps);
    }

    private static ((int Start, int Length)[] Clips, ImageTexture Bones, int Fps) LoadRig(string path)
    {
        byte[] rig = FileAccess.GetFileAsBytes(path);
        if (rig.Length < 16) throw new InvalidOperationException($"Character rig {path} is missing");
        int joints = BitConverter.ToInt32(rig, 0), frames = BitConverter.ToInt32(rig, 4), count = BitConverter.ToInt32(rig, 8), fps = BitConverter.ToInt32(rig, 12);
        var clips = new (int, int)[count];
        int offset = 16;
        for (int i = 0; i < count; i++, offset += 8)
            clips[i] = (BitConverter.ToInt32(rig, offset), BitConverter.ToInt32(rig, offset + 4));
        byte[] pixels = new byte[rig.Length - offset];
        Buffer.BlockCopy(rig, offset, pixels, 0, pixels.Length);
        return (clips, ImageTexture.CreateFromImage(Image.CreateFromData(joints * 3, frames, false, Image.Format.Rgbaf, pixels)), fps);
    }

    /// <summary>A textured character made with the pipeline in docs/character-pipeline.md.</summary>
    public sealed class Textured
    {
        /// <summary>Radians between the body's facing and the point its tool strikes.</summary>
        public const float ChopAim = 0.184f;

        public readonly ArrayMesh Body, Tool;
        public readonly ShaderMaterial BodyMaterial, ToolMaterial;
        private readonly (int Start, int Length)[] _clips;
        private readonly Clip[] _order;

        /// <param name="name">Asset name in assets/characters.</param>
        /// <param name="order">The clips in the order they were baked into the rig.</param>
        public Textured(string name, params Clip[] order)
        {
            _order = order;
            (_clips, ImageTexture bones, int fps) = LoadRig($"res://assets/characters/{name}.rig");
            Body = LoadMesh(name, true);
            if (FileAccess.FileExists($"res://assets/characters/{name}_tool.dcmesh")) Tool = LoadMesh(name + "_tool", true);
            var image = new Image();
            Error error = image.LoadPngFromBuffer(FileAccess.GetFileAsBytes($"res://assets/characters/{name}.dctex"));
            if (error != Error.Ok) throw new InvalidOperationException($"Character texture {name} failed: {error}");
            image.GenerateMipmaps();
            var shader = new Shader
            {
                Code = SkinShader
                    .Replace("uniform float fps = 24.0;", "uniform float fps = 24.0;\nuniform sampler2D albedo : source_color, hint_default_white, filter_linear_mipmap;\nuniform vec3 cloth_a = vec3(1.0);\nuniform vec3 cloth_b = vec3(1.0);\nuniform float dye = 0.0;\nuniform float primitive = 0.0;\nvarying float cloth_mask;\nvarying float body_mask;\nvarying float hide_mask;\nvarying vec3 cloth_color;")
                    .Replace("vec4 p = vec4(VERTEX, 1.0);", "vec4 p = vec4(VERTEX, 1.0);\n    cloth_mask = (1.0 - smoothstep(1.3, 1.42, VERTEX.y)) * (1.0 - smoothstep(0.3, 0.38, abs(VERTEX.x))) * smoothstep(0.08, 0.16, VERTEX.y);\n    cloth_color = mix(cloth_a, cloth_b, step(0.5, fract(INSTANCE_CUSTOM.z * 3.0)));\n    body_mask = 1.0 - smoothstep(1.44, 1.5, VERTEX.y);\n    hide_mask = (1.0 - smoothstep(1.42, 1.47, VERTEX.y)) * smoothstep(0.58, 0.64, VERTEX.y) * (1.0 - smoothstep(0.26, 0.31, abs(VERTEX.x)));")
                    .Replace("ALBEDO = COLOR.rgb;", "vec3 base = texture(albedo, UV).rgb;\n    float light = dot(base, vec3(0.3, 0.59, 0.11));\n    vec3 dressed = mix(base, cloth_color * light * 1.9, cloth_mask * dye);\n    vec3 skin = vec3(0.66, 0.45, 0.32) * (0.72 + light * 0.5);\n    vec3 hide = vec3(0.36, 0.25, 0.15) * (0.7 + light * 0.7);\n    vec3 bare = mix(mix(base, skin, body_mask), hide, hide_mask);\n    ALBEDO = COLOR.rgb * mix(dressed, bare, primitive);"),
            };
            BodyMaterial = new ShaderMaterial { Shader = shader };
            ToolMaterial = new ShaderMaterial { Shader = shader };
            foreach (ShaderMaterial material in new[] { BodyMaterial, ToolMaterial })
            {
                material.SetShaderParameter("bones", bones);
                material.SetShaderParameter("fps", (float)fps);
            }
            BodyMaterial.SetShaderParameter("albedo", ImageTexture.CreateFromImage(image));
        }

        /// <summary>Dyes the torso and legs with the clothing colors of an era.</summary>
        public void SetCloth(Color a, Color b, float dye)
        {
            BodyMaterial.SetShaderParameter("cloth_a", new Vector3(a.R, a.G, a.B));
            BodyMaterial.SetShaderParameter("cloth_b", new Vector3(b.R, b.G, b.B));
            BodyMaterial.SetShaderParameter("dye", dye);
        }

        /// <summary>Old Stone Age look: bare arms and legs with a simple animal-hide wrap over the body.</summary>
        public void SetPrimitive(bool on) => BodyMaterial.SetShaderParameter("primitive", on ? 1f : 0f);

        /// <summary>Uses the bent-over harvest motion for field work that has no clip of its own.</summary>
        public (int Start, int Length) ClipFor(Clip clip)
        {
            int index = Array.IndexOf(_order, clip);
            if (index < 0 && clip is Clip.Hoe or Clip.Gather) index = Array.IndexOf(_order, Clip.Reap);
            // Anyone can be assigned any job, so fall back to the role's own work motion.
            if (index < 0 && clip is not (Clip.Idle or Clip.Walk or Clip.Carry) && _order.Length > 3) index = _order.Length - 1;
            return _clips[Math.Max(index, 0)];
        }

        /// <summary>The role's tool shows only during its own work clip.</summary>
        public bool ShowsTool(Clip clip) => Tool != null && clip == _order[^1];
    }

    public static ArrayMesh LoadMesh(string name, bool textured = false)
    {
        byte[] d = FileAccess.GetFileAsBytes($"res://assets/characters/{name}.dcmesh");
        if (d.Length < 8) throw new InvalidOperationException($"Character mesh {name} is missing");
        int v = BitConverter.ToInt32(d, 0), n = BitConverter.ToInt32(d, 4), at = 8;
        var positions = new Vector3[v];
        var normals = new Vector3[v];
        var colors = new Color[v];
        var joints = new float[v * 4];
        var weights = new float[v * 4];
        var indices = new int[n];
        for (int i = 0; i < v; i++, at += 12) positions[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < v; i++, at += 12) normals[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        var uvs = new Vector2[textured ? v : 0];
        for (int i = 0; i < uvs.Length; i++, at += 8) uvs[i] = new Vector2(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4));
        for (int i = 0; i < v; i++, at += 12) colors[i] = new Color(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        Buffer.BlockCopy(d, at, joints, 0, joints.Length * 4); at += joints.Length * 4;
        Buffer.BlockCopy(d, at, weights, 0, weights.Length * 4); at += weights.Length * 4;
        Buffer.BlockCopy(d, at, indices, 0, n * 4);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        if (textured) arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Custom0] = joints;
        arrays[(int)Mesh.ArrayType.Custom1] = weights;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        long format = ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, (Mesh.ArrayFormat)format);
        return mesh;
    }

    public static int ToolFor(Clip clip) => clip switch
    {
        Clip.Chop => 0,
        Clip.Hoe => 1,
        Clip.Mine => 2,
        Clip.Build => 3,
        Clip.Fish => 4,
        Clip.Reap => 5,
        Clip.Gather or Clip.Sow => 6,
        Clip.Knead => 7,
        _ => -1,
    };
}
