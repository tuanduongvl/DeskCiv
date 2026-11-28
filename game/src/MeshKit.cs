using System;
using System.Collections.Generic;
using DeskCiv.Sim;
using Godot;

namespace DeskCiv;

/// <summary>Small flat-shaded low-poly mesh builder and the Neolithic building models.</summary>
public sealed class MeshKit
{
    private readonly List<Vector3> _vertices = new(), _normals = new();
    private readonly List<Color> _colors = new();

    private void Face(Vector3 inside, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        Vector3 normal = (c - a).Cross(b - a);
        if (normal.LengthSquared() < 1e-8f) return;
        normal = normal.Normalized();
        if (normal.Dot((a + b + c) / 3f - inside) < 0)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        foreach (Vector3 v in new[] { a, b, c })
        {
            _vertices.Add(v);
            _normals.Add(normal);
            _colors.Add(color);
        }
    }

    private void Quad(Vector3 inside, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Face(inside, a, b, c, color);
        Face(inside, a, c, d, color);
    }

    public MeshKit Box(Vector3 center, Vector3 size, Color color, float yaw = 0)
    {
        Vector3 h = size / 2;
        Vector3 P(float x, float y, float z) => center + new Vector3(x * h.X, y * h.Y, z * h.Z).Rotated(Vector3.Up, yaw);
        Quad(center, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), color);
        Quad(center, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color);
        Quad(center, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), color);
        Quad(center, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color);
        Quad(center, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), color);
        Quad(center, P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), color);
        return this;
    }

    /// <summary>Rotates everything added so far, for parts built upright but animated in another frame.</summary>
    public MeshKit Turned(Basis basis)
    {
        for (int i = 0; i < _vertices.Count; i++)
        {
            _vertices[i] = basis * _vertices[i];
            _normals[i] = (basis * _normals[i]).Normalized();
        }
        return this;
    }

    public MeshKit Prism(Vector3 baseCenter, float bottom, float top, float height, int sides, Color color, bool capTop = true)
    {
        Vector3 inside = baseCenter + Vector3.Up * height * 0.45f, up = baseCenter + Vector3.Up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            Vector3 d0 = new(MathF.Cos(a0), 0, MathF.Sin(a0)), d1 = new(MathF.Cos(a1), 0, MathF.Sin(a1));
            Vector3 b0 = baseCenter + d0 * bottom, b1 = baseCenter + d1 * bottom, t0 = up + d0 * top, t1 = up + d1 * top;
            Face(inside, b0, b1, t1, color);
            if (top > 0.001f)
            {
                Face(inside, b0, t1, t0, color);
                if (capTop) Face(inside, up, t0, t1, color);
            }
        }
        return this;
    }

    /// <summary>A round piece from <paramref name="a"/> to <paramref name="b"/> (radius r0 at a, r1 at b), with end caps.</summary>
    public MeshKit Cylinder(Vector3 a, Vector3 b, float r0, float r1, int sides, Color color, Color? caps = null)
    {
        Vector3 axis = (b - a).Normalized();
        Vector3 u = MathF.Abs(axis.Y) > 0.9f ? axis.Cross(Vector3.Right).Normalized() : axis.Cross(Vector3.Up).Normalized();
        Vector3 v = axis.Cross(u).Normalized(), inside = (a + b) / 2;
        Color cap = caps ?? color;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            Vector3 d0 = u * MathF.Cos(a0) + v * MathF.Sin(a0), d1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
            Quad(inside, a + d0 * r0, a + d1 * r0, b + d1 * r1, b + d0 * r1, color);
            if (r0 > 0.001f) Face(inside, a, a + d0 * r0, a + d1 * r0, cap);
            if (r1 > 0.001f) Face(inside, b, b + d0 * r1, b + d1 * r1, cap);
        }
        return this;
    }

    public MeshKit Gable(Vector3 center, float width, float depth, float height, Color color)
    {
        float w = width / 2, d = depth / 2;
        Vector3 inside = center + Vector3.Up * height * 0.3f;
        Vector3 a = center + new Vector3(-w, 0, -d), b = center + new Vector3(w, 0, -d), c = center + new Vector3(w, 0, d), e = center + new Vector3(-w, 0, d);
        Vector3 r0 = center + new Vector3(-w, height, 0), r1 = center + new Vector3(w, height, 0);
        Quad(inside, a, b, r1, r0, color);
        Quad(inside, e, c, r1, r0, color);
        Face(inside, a, e, r0, color.Darkened(0.12f));
        Face(inside, b, c, r1, color.Darkened(0.12f));
        return this;
    }

    public ArrayMesh Build()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static readonly Color Thatch = new(0.74f, 0.6f, 0.32f), Mud = new(0.56f, 0.42f, 0.29f), Timber = new(0.37f, 0.26f, 0.17f), Rock = new(0.55f, 0.54f, 0.5f), Dark = new(0.16f, 0.12f, 0.1f);

    /// <summary>Rectangular timber house. Local +Z is the door side.</summary>
    private MeshKit House(float width, float depth, float wallHeight, float roofHeight, Color wall, Color roof, Vector3 at = default)
    {
        float w = width / 2, d = depth / 2;
        Box(at + new Vector3(0, 0.16f, 0), new Vector3(width + 0.34f, 0.32f, depth + 0.34f), Rock.Darkened(0.1f));
        Box(at + new Vector3(0, 0.3f + wallHeight / 2, 0), new Vector3(width, wallHeight, depth), wall);
        int planks = Math.Max(4, (int)(width / 0.62f));
        for (int i = 1; i < planks; i++)
        {
            float x = -w + i * width / planks;
            Box(at + new Vector3(x, 0.3f + wallHeight / 2, d + 0.012f), new Vector3(0.045f, wallHeight, 0.03f), wall.Darkened(0.28f));
            Box(at + new Vector3(x, 0.3f + wallHeight / 2, -d - 0.012f), new Vector3(0.045f, wallHeight, 0.03f), wall.Darkened(0.28f));
        }
        foreach ((float x, float z) in new[] { (-w, -d), (w, -d), (-w, d), (w, d) })
            Box(at + new Vector3(x, 0.3f + wallHeight / 2, z), new Vector3(0.26f, wallHeight + 0.08f, 0.26f), Timber);
        Box(at + new Vector3(0, 0.3f + wallHeight, d), new Vector3(width + 0.2f, 0.2f, 0.2f), Timber);
        Box(at + new Vector3(0, 0.3f + wallHeight, -d), new Vector3(width + 0.2f, 0.2f, 0.2f), Timber);
        Box(at + new Vector3(0, 1.08f, d + 0.04f), new Vector3(0.98f, 1.56f, 0.1f), Dark);
        Box(at + new Vector3(-0.56f, 1.1f, d + 0.08f), new Vector3(0.14f, 1.66f, 0.14f), Timber);
        Box(at + new Vector3(0.56f, 1.1f, d + 0.08f), new Vector3(0.14f, 1.66f, 0.14f), Timber);
        Box(at + new Vector3(0, 1.98f, d + 0.08f), new Vector3(1.26f, 0.15f, 0.16f), Timber);
        if (width > 3.7f)
            foreach (int s in new[] { -1, 1 })
            {
                Box(at + new Vector3(s * (w * 0.62f), 1.42f, d + 0.04f), new Vector3(0.58f, 0.52f, 0.06f), new Color(0.18f, 0.14f, 0.1f));
                Box(at + new Vector3(s * (w * 0.62f) - 0.38f, 1.42f, d + 0.07f), new Vector3(0.17f, 0.58f, 0.05f), wall.Darkened(0.35f));
                Box(at + new Vector3(s * (w * 0.62f) + 0.38f, 1.42f, d + 0.07f), new Vector3(0.17f, 0.58f, 0.05f), wall.Darkened(0.35f));
            }
        float eave = 0.3f + wallHeight + 0.05f, overW = width + 0.74f, overD = depth + 0.8f;
        Gable(at + new Vector3(0, eave, 0), overW, overD, roofHeight, roof);
        for (int i = 1; i <= 4; i++)
        {
            float t = i / 5f, z = (overD / 2) * (1 - t), y = eave + roofHeight * t + 0.03f;
            foreach (int s in new[] { -1, 1 })
                Box(at + new Vector3(0, y, s * z), new Vector3(overW + 0.02f, 0.07f, 0.17f), i % 2 == 0 ? roof.Darkened(0.14f) : roof.Lightened(0.08f));
        }
        Box(at + new Vector3(0, eave + roofHeight + 0.03f, 0), new Vector3(overW + 0.2f, 0.17f, 0.2f), Timber);
        return this;
    }

    /// <summary>A woven basket with a darker rim, heaped with <paramref name="fill"/> (berries, roots).</summary>
    private MeshKit Basket(Vector3 at, float radius, float height, Color fill)
    {
        Color wicker = new(0.66f, 0.52f, 0.3f);
        Prism(at, radius * 0.8f, radius, height, 10, wicker);
        for (int k = 1; k < 4; k++) Prism(at + Vector3.Up * height * k / 4f, radius * (0.8f + 0.2f * k / 4f) + 0.01f, radius * (0.8f + 0.2f * k / 4f) + 0.01f, 0.025f, 10, wicker.Darkened(0.25f), false);
        Prism(at + Vector3.Up * height, radius + 0.02f, radius + 0.02f, 0.05f, 10, wicker.Darkened(0.35f), false);
        Prism(at + Vector3.Up * height * 0.85f, radius * 0.95f, radius * 0.2f, radius * 0.4f, 9, fill);
        return this;
    }

    private MeshKit Sack(Vector3 at, Color color)
    {
        Prism(at, 0.3f, 0.38f, 0.42f, 7, color);
        Prism(at + Vector3.Up * 0.42f, 0.38f, 0.11f, 0.24f, 7, color.Lightened(0.06f));
        return this;
    }

    private MeshKit Crate(Vector3 at, float size)
    {
        Box(at + Vector3.Up * size / 2, Vector3.One * size, new Color(0.55f, 0.39f, 0.23f));
        Box(at + Vector3.Up * size / 2, new Vector3(size + 0.04f, 0.08f, size + 0.04f), Timber);
        Box(at + Vector3.Up * size / 2, new Vector3(0.08f, size + 0.04f, size + 0.04f), Timber);
        return this;
    }

    private MeshKit LogPile(Vector3 at, int columns, int rows)
    {
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < columns - r; c++)
        {
            Vector3 p = at + new Vector3((c + r * 0.5f) * 0.46f, 0.23f + r * 0.38f, 0);
            Box(p, new Vector3(0.42f, 0.42f, 2.2f), new Color(0.46f, 0.31f, 0.18f));
            Box(p + Vector3.Back * 1.11f, new Vector3(0.3f, 0.3f, 0.03f), new Color(0.82f, 0.66f, 0.42f));
        }
        return this;
    }

    /// <summary>Architecture level for an era: thatch, timber, stone, brick, then modern concrete.</summary>
    public static int EraTier(int era) => era switch { 0 => -1, 1 => 0, <= 3 => 1, <= 5 => 2, 6 => 3, _ => 4 };

    /// <summary>The image-generated realistic model for a building and era, or null. Models are listed in docs/building-pipeline.md.</summary>
    public static ArrayMesh RealisticModel(BType type, int tier)
    {
        switch (type)
        {
            case BType.Windmill: return LoadModel("windmill_body");
            case BType.Watermill: return LoadModel("watermill_body");
        }
        // From the industrial era every craft workshop shares one brick workshop model (one texture in memory).
        // A craft with its own Industrial-era model (for example bakery_t3) uses it; the others share it.
        // A 20th-century model of its own (for example bakery_t4) comes first.
        if (tier == 4 && LoadModel($"{type.ToString().ToLowerInvariant()}_t4") is ArrayMesh modern) return modern;
        if (tier >= 3 && LoadModel($"{type.ToString().ToLowerInvariant()}_t3") is ArrayMesh own) return own;
        if (tier >= 3 && type is BType.Woodcutter or BType.Fisher or BType.Gatherer or BType.Bakery or BType.Mill or BType.Winery or BType.Smithy
            && LoadModel("workshop_t3") is ArrayMesh workshop) return workshop;
        // Models are named <type>_t<tier>. An era without its own model borrows the nearest era's, preferring the older one.
        string name = type.ToString().ToLowerInvariant();
        for (int distance = 0; distance <= 4; distance++)
            foreach (int t in new[] { tier - distance, tier + distance })
                // A quarry never borrows a later era's works: the steam stone works would appear in the Middle Ages.
                if (t is >= 0 and <= 4 && !(type == BType.Quarry && t > tier) && LoadModel($"{name}_t{t}") is ArrayMesh mesh) return mesh;
        return null;
    }

    /// <summary>A second shape for the same building and era (<type>_t<tier>b), used for every other building, or null.</summary>
    public static ArrayMesh RealisticVariant(BType type, int tier) =>
        tier < 0 ? null : LoadModel($"{type.ToString().ToLowerInvariant()}_t{tier}b");

    /// <summary>The model for this building in an era. Types without a later version keep their first model.</summary>
    public static ArrayMesh EraBuilding(BType type, int tier)
    {
        if (tier < 0) return CampBuilding(type);
        if (RealisticModel(type, tier) is ArrayMesh real) return real;
        // Modern concrete blocks and the factory stay procedural until a CC0 modern kit is added.
        bool procedural = (type is BType.Hut or BType.Storehouse && tier == 4) || (type == BType.Smithy && tier >= 3);
        if (!procedural && LoadKit($"{type.ToString().ToLowerInvariant()}_{tier}") is ArrayMesh kit) return kit;
        if (tier == 0) return Building(type);
        Color plaster = new(0.8f, 0.73f, 0.6f), tile = new(0.62f, 0.3f, 0.2f), stone = new(0.62f, 0.6f, 0.55f), slate = new(0.3f, 0.33f, 0.38f);
        Color brick = new(0.58f, 0.27f, 0.2f), concrete = new(0.74f, 0.74f, 0.72f), glass = new(0.34f, 0.52f, 0.64f);
        var m = new MeshKit();
        switch (type)
        {
            case BType.Hut when tier == 1:
                return m.House(5.6f, 5f, 2.3f, 2f, plaster, tile).Build();
            case BType.Hut when tier == 2:
                m.House(5.8f, 5.2f, 4f, 2.1f, stone, slate);
                return m.Box(new Vector3(1.9f, 5.7f, -1f), new Vector3(0.7f, 2.4f, 0.7f), stone.Darkened(0.2f)).Build();
            case BType.Hut when tier == 3:
                m.House(5.8f, 5.4f, 4.4f, 1.7f, brick, slate);
                m.Windows(5.8f, 5.4f, 3.4f, glass);
                m.Box(new Vector3(-2f, 6f, 0), new Vector3(0.8f, 2.4f, 0.8f), brick.Darkened(0.2f));
                return m.Box(new Vector3(2f, 6f, 0), new Vector3(0.8f, 2.4f, 0.8f), brick.Darkened(0.2f)).Build();
            case BType.Hut:
                m.Box(new Vector3(0, 3.3f, 0), new Vector3(6f, 6.6f, 5.6f), concrete);
                m.Box(new Vector3(0, 6.72f, 0), new Vector3(6.3f, 0.24f, 5.9f), concrete.Darkened(0.25f));
                m.Windows(6f, 5.6f, 2.1f, glass).Windows(6f, 5.6f, 4.8f, glass);
                return m.Box(new Vector3(0, 1.05f, 2.84f), new Vector3(1.1f, 2.1f, 0.1f), Dark).Build();
            case BType.Storehouse when tier == 1:
                return m.House(8.8f, 6.8f, 3f, 2.7f, plaster, tile).Build();
            case BType.Storehouse when tier == 2:
                return m.House(9f, 7f, 4f, 2.7f, stone, slate).Build();
            case BType.Storehouse when tier == 3:
                m.House(9.4f, 7.2f, 5f, 2.2f, brick, slate);
                return m.Windows(9.4f, 7.2f, 4f, glass).Build();
            case BType.Storehouse:
                m.Box(new Vector3(0, 3.2f, 0), new Vector3(10f, 6.4f, 7.6f), concrete);
                m.Box(new Vector3(0, 6.5f, 0), new Vector3(10.3f, 0.24f, 7.9f), concrete.Darkened(0.25f));
                m.Box(new Vector3(0, 1.7f, 3.84f), new Vector3(4.4f, 3.4f, 0.1f), new Color(0.42f, 0.46f, 0.5f));
                return m.Windows(10f, 7.6f, 5f, glass).Build();
            case BType.Smithy when tier >= 3:
                Color hall = tier == 3 ? brick : concrete;
                m.House(9f, 7f, 4.6f, 1.6f, hall, slate);
                m.Windows(9f, 7f, 3.6f, glass);
                foreach (float x in new[] { -2.8f, 2.8f })
                    m.Prism(new Vector3(x, 5f, -2.2f), 0.72f, 0.46f, 8.5f, 10, brick.Darkened(0.12f));
                return m.Build();
            case BType.Field or BType.Vineyard or BType.Quarry or BType.Dike or BType.TideGate or BType.Windmill:
                return Building(type);
            default:
                return Retint(Building(type), tier);
        }
    }

    /// <summary>Loads a model baked from the CC0 Kenney kit by tools/build_kit_buildings.py, or null if it is absent.</summary>
    public static ArrayMesh LoadKit(string name)
    {
        if (!ReadKit(name, out Vector3[] vertices, out Vector3[] normals, out Color[] colors)) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static readonly Dictionary<string, ArrayMesh> ModelCache = new();

    /// <summary>Drops cached generated models so the next LoadModel reads the files again (live reload).</summary>
    public static void ForgetModels() => ModelCache.Clear();

    /// <summary>
    /// A realistic textured model from assets/models (made by tools/build_hunyuan_model.py or build_cutout_model.py),
    /// with its material on the surface, or null when the file is missing.
    /// </summary>
    public static ArrayMesh LoadModel(string name)
    {
        if (ModelCache.TryGetValue(name, out ArrayMesh cached)) return cached;
        string path = $"res://assets/models/{name}.dcmodel";
        if (!FileAccess.FileExists(path)) return ModelCache[name] = null;
        byte[] d = FileAccess.GetFileAsBytes(path);
        int v = BitConverter.ToInt32(d, 0), n = BitConverter.ToInt32(d, 4), at = 8;
        var positions = new Vector3[v];
        var normals = new Vector3[v];
        var uvs = new Vector2[v];
        var indices = new int[n];
        for (int i = 0; i < v; i++, at += 12) positions[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < v; i++, at += 12) normals[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < v; i++, at += 8) uvs[i] = new Vector2(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4));
        Buffer.BlockCopy(d, at, indices, 0, n * 4);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        ImageTexture texture = ModelTexture(name, out bool cutout);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoTexture = texture,
            VertexColorUseAsAlbedo = true, // carries the instance tint used for buildings under construction
            Roughness = 1,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Transparency = cutout ? BaseMaterial3D.TransparencyEnum.AlphaScissor : BaseMaterial3D.TransparencyEnum.Disabled,
            CullMode = cutout ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
        });
        return ModelCache[name] = mesh;
    }

    /// <summary>The texture of a model in assets/models. PNG textures with transparency are cut-outs.</summary>
    public static ImageTexture ModelTexture(string name, out bool cutout)
    {
        byte[] bytes = FileAccess.GetFileAsBytes($"res://assets/models/{name}.dctex");
        var image = new Image();
        Error error = bytes.Length > 0 && bytes[0] == 0x89 ? image.LoadPngFromBuffer(bytes) : image.LoadJpgFromBuffer(bytes);
        if (error != Error.Ok) throw new InvalidOperationException($"Model texture {name} failed: {error}");
        cutout = image.DetectAlpha() != Image.AlphaMode.None;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static readonly Dictionary<string, (Vector3[], Vector3[], Color[])> KitCache = new();

    private static bool ReadKit(string name, out Vector3[] vertices, out Vector3[] normals, out Color[] colors)
    {
        if (KitCache.TryGetValue(name, out var hit))
        {
            (vertices, normals, colors) = hit;
            return true;
        }
        vertices = null;
        normals = null;
        colors = null;
        string path = $"res://assets/buildings/{name}.dckit";
        if (!FileAccess.FileExists(path)) return false;
        byte[] d = FileAccess.GetFileAsBytes(path);
        int count = BitConverter.ToInt32(d, 0), at = 4;
        vertices = new Vector3[count];
        normals = new Vector3[count];
        colors = new Color[count];
        for (int i = 0; i < count; i++, at += 12) vertices[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < count; i++, at += 12) normals[i] = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        for (int i = 0; i < count; i++, at += 12) colors[i] = new Color(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
        KitCache[name] = (vertices, normals, colors);
        return true;
    }

    /// <summary>Places a baked CC0 prop inside this model. Returns false when the prop file is absent, so callers can fall back.</summary>
    private bool TryKit(string name, Vector3 at, float yaw = 0, float scale = 1)
    {
        if (!ReadKit(name, out Vector3[] vertices, out Vector3[] normals, out Color[] colors)) return false;
        for (int i = 0; i < vertices.Length; i++)
        {
            _vertices.Add(at + (vertices[i] * scale).Rotated(Vector3.Up, yaw));
            _normals.Add(normals[i].Rotated(Vector3.Up, yaw));
            _colors.Add(colors[i]);
        }
        return true;
    }

    /// <summary>Old Stone Age camp: hide tents and open work spots, with no built houses.</summary>
    private static ArrayMesh CampBuilding(BType type)
    {
        Color hide = new(0.55f, 0.41f, 0.27f), hideDark = new(0.41f, 0.3f, 0.2f), pole = new(0.33f, 0.24f, 0.15f), stone = new(0.5f, 0.49f, 0.45f);
        var m = new MeshKit();
        switch (type)
        {
            case BType.Hut:
                m.Prism(Vector3.Zero, 2.5f, 0.16f, 3.5f, 8, hide);
                for (int i = 0; i < 8; i += 2)
                {
                    float a = i * MathF.Tau / 8;
                    m.Beam(new Vector3(MathF.Cos(a) * 2.55f, 0.02f, MathF.Sin(a) * 2.55f), new Vector3(-MathF.Cos(a) * 0.16f, 4.05f, -MathF.Sin(a) * 0.16f), 0.05f, pole);
                }
                m.Prism(new Vector3(0, 0.02f, 2.02f), 0.58f, 0.04f, 1.45f, 4, new Color(0.13f, 0.1f, 0.08f));
                if (!m.TryKit("prop_campfire", new Vector3(0, 0, 3.5f)))
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * MathF.Tau / 7;
                        m.Box(new Vector3(MathF.Cos(a) * 0.72f, 0.1f, 3.5f + MathF.Sin(a) * 0.72f), new Vector3(0.3f, 0.2f, 0.26f), stone, a);
                    }
                m.Beam(new Vector3(-0.38f, 0.14f, 3.3f), new Vector3(0.38f, 0.14f, 3.7f), 0.06f, pole);
                m.Beam(new Vector3(0.38f, 0.14f, 3.3f), new Vector3(-0.38f, 0.14f, 3.7f), 0.06f, pole);
                break;
            case BType.Storehouse:
                foreach (float x in new[] { -2.4f, 2.4f })
                    foreach (float z in new[] { -0.9f, 0.9f })
                        m.Beam(new Vector3(x, 0, z), new Vector3(x, 2.4f, z * 0.2f), 0.07f, pole);
                m.Beam(new Vector3(-2.6f, 2.35f, 0), new Vector3(2.6f, 2.35f, 0), 0.07f, pole);
                for (int i = 0; i < 4; i++) m.Box(new Vector3(-1.6f + i * 1.05f, 1.55f, 0), new Vector3(0.82f, 1.55f, 0.06f), i % 2 == 0 ? hide : hideDark);
                m.Box(new Vector3(-1.5f, 0.18f, 2.2f), new Vector3(1.8f, 0.36f, 1.3f), hideDark).Box(new Vector3(-1.5f, 0.46f, 2.2f), new Vector3(1.4f, 0.2f, 1f), hide);
                // No pottery yet: food is kept in woven baskets and a tied hide bag.
                m.Basket(new Vector3(1.6f, 0, 2.1f), 0.5f, 0.55f, new Color(0.62f, 0.16f, 0.28f));
                m.Prism(new Vector3(0.7f, 0, 2.5f), 0.32f, 0.38f, 0.42f, 8, hideDark).Prism(new Vector3(0.7f, 0.42f, 2.5f), 0.38f, 0.06f, 0.2f, 8, hide);
                break;
            case BType.Gatherer:
                m.Beam(new Vector3(-1.7f, 0, -1f), new Vector3(0, 2.1f, -0.2f), 0.06f, pole).Beam(new Vector3(1.7f, 0, -1f), new Vector3(0, 2.1f, -0.2f), 0.06f, pole);
                m.Box(new Vector3(0, 1.05f, -0.75f), new Vector3(2.7f, 0.08f, 1.9f), hide);
                m.Basket(new Vector3(-1.1f, 0, 1.2f), 0.48f, 0.5f, new Color(0.62f, 0.16f, 0.28f));
                m.Basket(new Vector3(1.1f, 0, 1.2f), 0.42f, 0.45f, new Color(0.3f, 0.22f, 0.5f));
                break;
            case BType.Fisher:
                foreach (float x in new[] { -1.8f, 1.8f }) m.Beam(new Vector3(x, 0, 0), new Vector3(x, 2.1f, 0), 0.07f, pole);
                m.Beam(new Vector3(-2f, 2.05f, 0), new Vector3(2f, 2.05f, 0), 0.06f, pole);
                // Fish split and hung to dry in the wind and smoke, and a midden of shells beside the rack (no boats yet).
                for (int i = 0; i < 6; i++) m.Prism(new Vector3(-1.35f + i * 0.54f, 1.4f, 0), 0.04f, 0.12f, 0.56f, 5, new Color(0.62f, 0.5f, 0.36f).Lerp(new Color(0.5f, 0.56f, 0.58f), i % 2 * 0.4f));
                for (int i = 0; i < 40; i++)
                {
                    float a = i * 2.399963f, r = 1.1f * MathF.Sqrt((i + 0.5f) / 40f);
                    var at = new Vector3(0.3f + MathF.Cos(a) * r * 1.4f, 0.28f * (1 - r / 1.1f), 2.3f + MathF.Sin(a) * r * 0.8f);
                    m.Prism(at, 0.11f, 0.03f, 0.07f, 5, i % 3 == 0 ? new Color(0.3f, 0.3f, 0.36f) : new Color(0.86f, 0.82f, 0.74f));
                }
                break;
            case BType.Woodcutter:
                if (!m.TryKit("prop_logs", new Vector3(-1.5f, 0, 0))) m.LogPile(new Vector3(-1.5f, 0, 0), 4, 2);
                if (!m.TryKit("prop_stump", new Vector3(1.8f, 0, 1f))) m.Prism(new Vector3(1.8f, 0, 1f), 0.46f, 0.42f, 0.56f, 8, new Color(0.55f, 0.39f, 0.23f));
                break;
            case BType.Quarry:
                if (m.TryKit("prop_rock_large", new Vector3(-0.9f, 0, -0.5f), 0.4f)) m.TryKit("prop_rubble", new Vector3(1.1f, 0, 0.8f), -0.3f);
                else m.Box(new Vector3(-0.9f, 0.5f, -0.5f), new Vector3(1.9f, 1f, 1.5f), stone, 0.4f).Box(new Vector3(0.9f, 0.32f, 0.4f), new Vector3(1.3f, 0.64f, 1.1f), stone.Darkened(0.12f), -0.3f);
                break;
            default:
                return Building(type);
        }
        return m.Build();
    }

    /// <summary>Recolors straw roofs and timber walls to the materials of a later era.</summary>
    private static ArrayMesh Retint(ArrayMesh mesh, int tier)
    {
        Color roof = tier switch { 1 => new Color(0.62f, 0.3f, 0.2f), 2 => new Color(0.32f, 0.35f, 0.4f), 3 => new Color(0.27f, 0.29f, 0.33f), _ => new Color(0.46f, 0.47f, 0.48f) };
        Color wall = tier switch { 1 => new Color(0.78f, 0.7f, 0.57f), 2 => new Color(0.62f, 0.6f, 0.55f), 3 => new Color(0.58f, 0.28f, 0.21f), _ => new Color(0.74f, 0.74f, 0.72f) };
        var arrays = mesh.SurfaceGetArrays(0);
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        for (int i = 0; i < colors.Length; i++)
        {
            Color c = colors[i];
            float light = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
            bool warm = c.R > c.G && c.G > c.B && c.R - c.B > 0.14f;
            if (!warm) continue;
            if (c.R > 0.6f && c.G > 0.46f) colors[i] = roof * (light / 0.6f);
            else if (light > 0.24f) colors[i] = wall * (light / 0.42f);
            colors[i].A = 1;
        }
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var result = new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return result;
    }

    private MeshKit Windows(float width, float depth, float height, Color glass)
    {
        int columns = Math.Max(2, (int)(width / 1.7f));
        foreach (int side in new[] { -1, 1 })
        for (int i = 0; i < columns; i++)
        {
            float x = -width / 2 + (i + 0.5f) * width / columns;
            if (side > 0 && height < 2.6f && MathF.Abs(x) < 0.9f) continue;
            Box(new Vector3(x, height, side * (depth / 2 + 0.03f)), new Vector3(0.82f, 1.05f, 0.05f), glass);
        }
        return this;
    }

    public static ArrayMesh Building(BType type)
    {
        var m = new MeshKit();
        Color log = new(0.5f, 0.36f, 0.23f), plaster = new(0.72f, 0.64f, 0.5f);
        switch (type)
        {
            case BType.Hut:
                m.Prism(Vector3.Zero, 2.95f, 2.85f, 0.42f, 12, Rock.Darkened(0.08f));
                m.Prism(Vector3.Up * 0.4f, 2.72f, 2.6f, 1.5f, 12, Mud);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * MathF.Tau / 12 + 0.26f;
                    m.Box(new Vector3(MathF.Cos(a) * 2.68f, 1.15f, MathF.Sin(a) * 2.68f), new Vector3(0.16f, 1.6f, 0.16f), Timber, -a);
                }
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 5f, bottom = Mathf.Lerp(3.7f, 0.65f, t), top = Mathf.Lerp(3.7f, 0.65f, t + 0.26f);
                    m.Prism(Vector3.Up * (1.82f + i * 0.47f), bottom, top, 0.56f, 12, i % 2 == 0 ? Thatch.Darkened(0.08f) : Thatch.Lightened(0.05f));
                }
                m.Prism(Vector3.Up * 4.15f, 0.56f, 0.18f, 0.36f, 7, Timber);
                m.Box(new Vector3(0, 0.84f, 2.68f), new Vector3(1.05f, 1.64f, 0.14f), Dark);
                m.Box(new Vector3(-0.64f, 0.88f, 2.76f), new Vector3(0.18f, 1.78f, 0.2f), Timber);
                m.Box(new Vector3(0.64f, 0.88f, 2.76f), new Vector3(0.18f, 1.78f, 0.2f), Timber);
                m.Box(new Vector3(0, 1.76f, 2.77f), new Vector3(1.5f, 0.18f, 0.24f), Timber);
                m.Prism(new Vector3(2.5f, 0, 2.1f), 0.42f, 0.54f, 0.62f, 8, new Color(0.56f, 0.34f, 0.2f));
                m.LogPile(new Vector3(-3.05f, 0, 2.0f), 3, 2);
                break;
            case BType.Storehouse:
                m.House(8.8f, 6.8f, 2.8f, 2.7f, log, Thatch);
                m.Crate(new Vector3(-3.5f, 0, 4.25f), 0.9f).Crate(new Vector3(-2.55f, 0, 4.45f), 0.7f).Crate(new Vector3(-3.3f, 0.9f, 4.3f), 0.55f);
                m.Sack(new Vector3(2.5f, 0, 4.25f), new Color(0.78f, 0.7f, 0.52f)).Sack(new Vector3(3.3f, 0, 4.42f), new Color(0.7f, 0.62f, 0.45f)).Sack(new Vector3(2.9f, 0.5f, 4.3f), new Color(0.82f, 0.74f, 0.56f));
                m.Prism(new Vector3(4.9f, 0, 2.2f), 0.42f, 0.5f, 1.05f, 9, new Color(0.47f, 0.31f, 0.18f)).Prism(new Vector3(4.9f, 0, 1.2f), 0.42f, 0.5f, 1.05f, 9, new Color(0.5f, 0.33f, 0.2f));
                break;
            case BType.Gatherer:
                m.House(4.8f, 3.9f, 2.0f, 1.8f, log, new Color(0.54f, 0.6f, 0.31f));
                m.Prism(new Vector3(-1.9f, 0, 2.8f), 0.42f, 0.58f, 0.58f, 9, Thatch).Prism(new Vector3(-1.9f, 0.5f, 2.8f), 0.5f, 0.1f, 0.2f, 8, new Color(0.66f, 0.18f, 0.3f));
                m.Prism(new Vector3(1.9f, 0, 2.8f), 0.38f, 0.52f, 0.5f, 9, Thatch).Prism(new Vector3(1.9f, 0.45f, 2.8f), 0.44f, 0.1f, 0.18f, 8, new Color(0.3f, 0.22f, 0.5f));
                break;
            case BType.Fisher:
                m.House(4.6f, 3.8f, 2.0f, 1.7f, new Color(0.45f, 0.41f, 0.35f), Thatch);
                foreach (float z in new[] { -1.6f, 1.6f }) m.Box(new Vector3(3.35f, 1.15f, z), new Vector3(0.14f, 2.3f, 0.14f), Timber);
                m.Box(new Vector3(3.35f, 2.2f, 0), new Vector3(0.12f, 0.12f, 3.5f), Timber);
                for (int i = 0; i < 5; i++) m.Prism(new Vector3(3.35f, 1.55f, -1.2f + i * 0.6f), 0.05f, 0.14f, 0.58f, 5, new Color(0.56f, 0.68f, 0.74f));
                m.Box(new Vector3(-3.1f, 0.26f, 1.2f), new Vector3(1.1f, 0.34f, 3.1f), new Color(0.38f, 0.26f, 0.16f));
                break;
            case BType.Woodcutter:
                m.House(4.6f, 3.6f, 2.0f, 1.7f, new Color(0.42f, 0.29f, 0.18f), Thatch, new Vector3(0, 0, -0.6f));
                m.LogPile(new Vector3(-3.15f, 0, 2.4f), 4, 3);
                m.Prism(new Vector3(2.15f, 0, 2.45f), 0.48f, 0.44f, 0.62f, 9, new Color(0.55f, 0.39f, 0.23f));
                m.Box(new Vector3(2.15f, 0.84f, 2.45f), new Vector3(0.08f, 0.5f, 0.08f), Timber).Box(new Vector3(2.15f, 1.08f, 2.56f), new Vector3(0.06f, 0.18f, 0.3f), new Color(0.72f, 0.74f, 0.76f));
                break;
            case BType.Quarry:
                if (m.TryKit("prop_rock_tall", new Vector3(-1.6f, 0, -1.4f), 0.3f))
                {
                    m.TryKit("prop_rock_mid", new Vector3(1.6f, 0, -1.7f), -0.5f);
                    m.TryKit("prop_rock_large", new Vector3(0.4f, 0, -0.2f), 1.1f, 0.8f);
                    m.TryKit("prop_rubble", new Vector3(2.4f, 0, 0.6f), 0.7f);
                }
                else m.Box(new Vector3(-1.5f, 0.85f, -0.9f), new Vector3(3f, 1.7f, 2.5f), Rock, 0.3f).Box(new Vector3(1.5f, 0.65f, -1.2f), new Vector3(2.5f, 1.3f, 2.1f), Rock.Darkened(0.12f), -0.25f);
                for (int i = 0; i < 6; i++)
                    m.Box(new Vector3(-1.7f + (i % 3) * 1.15f, 0.26f + (i / 3) * 0.5f, 2f), new Vector3(1.02f, 0.48f, 0.82f), Rock.Lightened(i % 2 * 0.1f));
                m.Box(new Vector3(3f, 1.5f, 1.8f), new Vector3(0.16f, 3f, 0.16f), Timber).Box(new Vector3(2.4f, 2.9f, 1.8f), new Vector3(1.5f, 0.14f, 0.14f), Timber);
                break;
            case BType.Mill:
                m.House(7.6f, 6.2f, 2.5f, 2.5f, plaster, Thatch);
                m.Prism(new Vector3(-2.9f, 0, 4.4f), 1, 1, 0.42f, 12, Rock).Prism(new Vector3(-2.9f, 0.42f, 4.4f), 0.76f, 0.76f, 0.3f, 12, Rock.Lightened(0.12f));
                m.Box(new Vector3(-2.3f, 0.92f, 4.4f), new Vector3(0.12f, 0.5f, 0.12f), Timber);
                m.Sack(new Vector3(2.6f, 0, 4.1f), new Color(0.92f, 0.89f, 0.78f)).Sack(new Vector3(3.4f, 0, 4.3f), new Color(0.88f, 0.84f, 0.72f)).Sack(new Vector3(3f, 0.5f, 4.15f), new Color(0.94f, 0.91f, 0.82f));
                break;
            case BType.Bakery:
                m.House(6.2f, 6.0f, 2.4f, 2.3f, plaster.Darkened(0.05f), Thatch, new Vector3(-1.5f, 0, 0));
                m.Prism(new Vector3(3.5f, 0, 0.8f), 2.0f, 1.55f, 1.05f, 11, new Color(0.68f, 0.44f, 0.3f)).Prism(new Vector3(3.5f, 1.05f, 0.8f), 1.55f, 0.15f, 1.15f, 11, new Color(0.73f, 0.49f, 0.34f));
                m.Box(new Vector3(3.5f, 0.56f, 2.72f), new Vector3(0.86f, 0.76f, 0.2f), Dark).Box(new Vector3(3.5f, 0.96f, 2.78f), new Vector3(1.08f, 0.14f, 0.24f), Rock);
                m.Box(new Vector3(3.5f, 2.6f, 0.8f), new Vector3(0.48f, 1.3f, 0.48f), Rock.Darkened(0.12f));
                m.LogPile(new Vector3(1.5f, 0, 4.2f), 3, 2);
                break;
            case BType.Windmill:
                m.Prism(Vector3.Zero, 4.2f, 4.0f, 0.5f, 10, Rock.Darkened(0.1f));
                m.Prism(Vector3.Up * 0.5f, 3.6f, 2.5f, 7.8f, 10, plaster.Lightened(0.08f));
                for (int i = 1; i < 4; i++) m.Prism(Vector3.Up * (0.5f + i * 1.95f), 3.62f - i * 0.28f, 3.5f - i * 0.28f, 0.14f, 10, Timber);
                m.Prism(Vector3.Up * 8.3f, 2.85f, 2.7f, 0.3f, 10, Timber);
                m.Prism(Vector3.Up * 8.6f, 2.75f, 0.2f, 2.2f, 10, Thatch.Darkened(0.12f));
                m.Box(new Vector3(0, 1.28f, 3.42f), new Vector3(1.1f, 1.6f, 0.2f), Dark).Box(new Vector3(0, 2.2f, 3.42f), new Vector3(1.5f, 0.18f, 0.28f), Timber);
                m.Box(new Vector3(0, 4.2f, 2.86f), new Vector3(0.6f, 0.8f, 0.14f), Dark);
                m.Sack(new Vector3(2.5f, 0, 3.6f), new Color(0.92f, 0.89f, 0.78f)).Sack(new Vector3(-2.5f, 0, 3.6f), new Color(0.86f, 0.8f, 0.66f));
                break;
            case BType.Watermill:
                m.House(7.4f, 6.0f, 2.8f, 2.4f, plaster, Thatch, new Vector3(0, 0, 1.2f));
                m.Box(new Vector3(0, 0.45f, -3.3f), new Vector3(9.4f, 1.3f, 1.2f), Rock);
                foreach (int side in new[] { -1, 1 }) m.Box(new Vector3(side * 1.18f, 1.2f, -4.6f), new Vector3(0.5f, 2.6f, 3.4f), Rock.Darkened(0.08f));
                m.Box(new Vector3(0, 2.05f, -2.6f), new Vector3(0.28f, 0.28f, 3.4f), Timber);
                m.Box(new Vector3(0, 4.5f, -5.4f), new Vector3(1.0f, 0.2f, 4.4f), Timber);
                m.Sack(new Vector3(3.2f, 0, 4.9f), new Color(0.92f, 0.89f, 0.78f));
                break;
            case BType.Vineyard:
                m.Box(new Vector3(0, 0.05f, 0), new Vector3(15.6f, 0.1f, 15.6f), new Color(0.42f, 0.32f, 0.2f));
                for (int row = 0; row < 6; row++)
                {
                    float z = -6.25f + row * 2.5f;
                    m.Box(new Vector3(0, 1.3f, z), new Vector3(14.2f, 0.05f, 0.05f), Timber);
                    for (int post = 0; post < 6; post++)
                    {
                        float x = -6.5f + post * 2.6f;
                        m.Box(new Vector3(x, 0.72f, z), new Vector3(0.12f, 1.44f, 0.12f), Timber);
                        if (post == 5) continue;
                        // Vines as separate leafy clumps along the wire, not one solid hedge box, so rows read as plants.
                        for (int k = 0; k < 4; k++)
                        {
                            int h = row * 31 + post * 7 + k * 13;
                            float cx = x + 0.38f + k * 0.62f, r = 0.34f + (h % 5) * 0.03f, tall = 0.5f + (h % 3) * 0.08f;
                            var green = new Color(0.25f + (h % 4) * 0.025f, 0.42f + (h % 3) * 0.04f, 0.18f + (h % 2) * 0.03f);
                            m.Box(new Vector3(cx, 0.3f, z), new Vector3(0.08f, 0.6f, 0.08f), Timber.Darkened(0.2f));
                            m.Prism(new Vector3(cx, 0.5f, z + ((h % 3) - 1) * 0.05f), r * 0.75f, r, tall * 0.55f, 6, green);
                            m.Prism(new Vector3(cx, 0.5f + tall * 0.55f, z), r, r * 0.5f, tall * 0.4f, 6, green.Lightened(0.06f));
                        }
                        m.Prism(new Vector3(x + 0.7f + post * 0.1f, 0.62f, z + 0.3f), 0.03f, 0.17f, 0.3f, 5, new Color(0.34f, 0.15f, 0.42f));
                        m.Prism(new Vector3(x + 1.8f, 0.62f, z - 0.3f), 0.03f, 0.17f, 0.3f, 5, new Color(0.4f, 0.17f, 0.46f));
                    }
                }
                break;
            case BType.Winery:
                m.House(7.2f, 5.6f, 2.6f, 2.4f, new Color(0.62f, 0.5f, 0.4f), new Color(0.56f, 0.34f, 0.25f), new Vector3(0, 0, -0.9f));
                foreach ((float x, float z) in new[] { (-3.7f, 3.5f), (-2.6f, 3.9f), (-3.2f, 4.9f) })
                {
                    m.Prism(new Vector3(x, 0, z), 0.5f, 0.62f, 0.62f, 10, new Color(0.46f, 0.29f, 0.17f));
                    m.Prism(new Vector3(x, 0.62f, z), 0.62f, 0.5f, 0.62f, 10, new Color(0.5f, 0.32f, 0.19f));
                    m.Prism(new Vector3(x, 0.56f, z), 0.65f, 0.65f, 0.12f, 10, new Color(0.26f, 0.24f, 0.24f));
                }
                m.Prism(new Vector3(3.0f, 0, 4.0f), 1.12f, 1.2f, 0.56f, 12, new Color(0.45f, 0.3f, 0.18f));
                m.Prism(new Vector3(3.0f, 0.54f, 4.0f), 1.0f, 1.0f, 0.04f, 12, new Color(0.32f, 0.08f, 0.2f));
                foreach (int side in new[] { -1, 1 }) m.Box(new Vector3(3.0f + side * 1.3f, 1.4f, 4.0f), new Vector3(0.24f, 2.8f, 0.24f), Timber);
                m.Box(new Vector3(3.0f, 2.8f, 4.0f), new Vector3(3.0f, 0.3f, 0.3f), Timber);
                break;
            case BType.Smithy:
                m.House(6.4f, 5.2f, 2.5f, 2.1f, new Color(0.42f, 0.36f, 0.31f), new Color(0.4f, 0.34f, 0.3f), new Vector3(-0.6f, 0, -1.2f));
                m.Box(new Vector3(3.6f, 2.4f, -2.0f), new Vector3(1.3f, 5.6f, 1.3f), Rock.Darkened(0.14f));
                m.Box(new Vector3(3.3f, 0.55f, 2.3f), new Vector3(2.2f, 1.1f, 1.8f), Rock.Darkened(0.05f));
                m.Box(new Vector3(3.3f, 1.13f, 2.3f), new Vector3(1.5f, 0.06f, 1.1f), new Color(1f, 0.46f, 0.12f));
                foreach ((float x, float z) in new[] { (0.9f, 4.1f), (5.0f, 4.1f), (0.9f, 0.9f), (5.0f, 0.9f) }) m.Box(new Vector3(x, 1.5f, z), new Vector3(0.2f, 3f, 0.2f), Timber);
                m.Gable(new Vector3(2.95f, 3f, 2.5f), 4.8f, 4.2f, 0.9f, new Color(0.38f, 0.33f, 0.29f));
                m.Prism(new Vector3(-1.1f, 0, 3.0f), 0.46f, 0.42f, 0.62f, 9, new Color(0.46f, 0.32f, 0.2f));
                m.Box(new Vector3(-1.1f, 0.78f, 3.0f), new Vector3(0.95f, 0.26f, 0.38f), new Color(0.3f, 0.31f, 0.34f));
                break;
            case BType.Dike:
            {
                // One smooth earth bank with a rounded trapezoid profile (it used to be four stacked boxes that read
                // as green steps): a muddy sea-side toe, grassy slopes and crest. Neighbouring dikes join end to end.
                (float Z, float Y)[] profile = { (-4.9f, 0), (-3.4f, 1.0f), (-2.0f, 1.85f), (-0.8f, 2.4f), (0.5f, 2.4f), (1.8f, 1.9f), (3.2f, 1.0f), (4.6f, 0) };
                Color mud = new(0.44f, 0.4f, 0.3f), grass = new(0.38f, 0.48f, 0.25f);
                Vector3 inside = new(0, 1.0f, -0.2f);
                const float half = 6.1f;
                for (int i = 0; i < profile.Length - 1; i++)
                {
                    (float z0, float y0) = profile[i];
                    (float z1, float y1) = profile[i + 1];
                    Color c = i == 0 ? mud : i == 1 ? mud.Lerp(grass, 0.6f) : grass.Lightened(((i * 7) % 3) * 0.025f);
                    m.Quad(inside, new Vector3(-half, y0, z0), new Vector3(half, y0, z0), new Vector3(half, y1, z1), new Vector3(-half, y1, z1), c);
                    foreach (float x in new[] { -half, half })
                        m.Face(inside, new Vector3(x, 0, -0.2f), new Vector3(x, y0, z0), new Vector3(x, y1, z1), grass.Darkened(0.1f));
                }
            }
                for (int i = 0; i < 5; i++)
                    if (!m.TryKit("prop_revetment", new Vector3(-4.8f + i * 2.4f, 0, -4.4f + (i % 2) * 0.15f), (i % 2) * MathF.PI))
                        m.Box(new Vector3(-4.8f + i * 2.4f, 0.62f, -4.55f), new Vector3(2.3f, 1.2f, 0.82f), Rock.Lightened(i % 3 * 0.06f));
                m.Box(new Vector3(0, 2.46f, -0.2f), new Vector3(12.2f, 0.06f, 1.5f), new Color(0.55f, 0.46f, 0.31f));
                break;
            case BType.TideGate:
                m.Box(new Vector3(0, 0.3f, -0.8f), new Vector3(12.2f, 0.6f, 8.8f), Rock.Darkened(0.14f));
                foreach (int side in new[] { -1, 1 })
                {
                    m.Box(new Vector3(side * 3.1f, 3.2f, -1.4f), new Vector3(1.7f, 6.4f, 3.2f), Rock);
                    m.Box(new Vector3(side * 5.0f, 1.4f, -1.2f), new Vector3(2.6f, 2.8f, 5.4f), Rock.Darkened(0.06f));
                    m.Prism(new Vector3(side * 3.1f, 6.4f, -1.4f), 1.3f, 0.2f, 1.1f, 4, new Color(0.45f, 0.31f, 0.2f));
                }
                m.Box(new Vector3(0, 6.1f, -1.4f), new Vector3(5.2f, 0.6f, 1.2f), Timber);
                m.Box(new Vector3(0, 0.62f, -1.4f), new Vector3(4.5f, 0.2f, 3.4f), new Color(0.24f, 0.33f, 0.36f));
                foreach (int side in new[] { -1, 1 }) m.Box(new Vector3(side * 1.4f, 6.7f, -1.4f), new Vector3(0.22f, 0.7f, 0.22f), Timber);
                break;
            case BType.Field:
                m.Box(new Vector3(0, 0.05f, 0), new Vector3(18.6f, 0.12f, 18.6f), new Color(0.38f, 0.28f, 0.18f));
                for (int i = 0; i < 9; i++) m.Box(new Vector3(-8 + i * 2, 0.15f, 0), new Vector3(0.75f, 0.16f, 17.4f), new Color(0.46f, 0.34f, 0.22f));
                break;
        }
        return m.Build();
    }

    /// <summary>An animated part. Mode 0 spins around local Z, mode 1 slides up and down, mode 2 rocks around local Z.</summary>
    public readonly record struct MovingPart(ArrayMesh Mesh, Vector3 Pivot, Basis Local, float Speed, int Mode, float Amount);

    private static readonly Basis SpinFlat = new(Vector3.Right, -MathF.PI / 2), SpinSide = new(Vector3.Up, MathF.PI / 2);

    /// <summary>The moving part of a building, or null. The windmill's sails are aimed at the sea by the renderer.</summary>
    public static MovingPart? Moving(BType type)
    {
        var m = new MeshKit();
        Color cloth = new(0.86f, 0.8f, 0.66f), wood = new(0.42f, 0.29f, 0.18f), stone = new(0.66f, 0.65f, 0.6f);
        switch (type)
        {
            case BType.Windmill when LoadModel("windmill_sails") is ArrayMesh sails && LoadModel("windmill_body") != null:
                // The realistic body's axle stub ends 2.6 m in front of the tower axis, 9.1 m up.
                return new MovingPart(sails, new Vector3(2.62f, 9.1f, 0), Basis.Identity, 0.9f, 0, 0);
            case BType.Windmill:
                m.Prism(new Vector3(0, 0, -0.5f), 0.1f, 0.1f, 0.1f, 3, wood);
                m.Box(new Vector3(0, 0, -0.5f), new Vector3(0.34f, 0.34f, 1.3f), wood).Box(Vector3.Zero, new Vector3(0.6f, 0.6f, 0.34f), wood.Darkened(0.2f));
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI / 2;
                    Vector3 along = new(MathF.Cos(a), MathF.Sin(a), 0), across = new(-along.Y, along.X, 0);
                    m.Beam(along * 0.2f, along * 5.5f, 0.11f, wood);
                    m.Panel(along * 1.1f, along * 5.3f, across, 0.95f, cloth);
                    foreach (float t in new[] { 1.1f, 2.15f, 3.2f, 4.25f, 5.3f }) m.Beam(along * t, along * t + across * 0.98f, 0.045f, wood);
                }
                return new MovingPart(LoadKit("rotor_windmill") ?? m.Build(), new Vector3(3.4f, 8.95f, 0), Basis.Identity, 1.1f, 0, 0);
            case BType.Watermill when LoadModel("watermill_wheel") is ArrayMesh wheel && LoadModel("watermill_body") != null:
                // The realistic body's axle leaves the water-side gable 3.1 m up; the wheel turns on that axle.
                return new MovingPart(wheel, new Vector3(0, 3.1f, -4.55f), Basis.Identity, -0.7f, 0, 0);
            case BType.Watermill:
                for (int i = 0; i < 12; i++)
                {
                    float a0 = i * MathF.Tau / 12, a1 = (i + 1) * MathF.Tau / 12;
                    Vector3 p0 = new(MathF.Cos(a0) * 2.7f, MathF.Sin(a0) * 2.7f, 0), p1 = new(MathF.Cos(a1) * 2.7f, MathF.Sin(a1) * 2.7f, 0);
                    foreach (float z in new[] { -0.55f, 0.55f }) m.Beam(p0 + Vector3.Back * z, p1 + Vector3.Back * z, 0.1f, wood);
                    m.Box(p0 * 1.02f, new Vector3(0.12f, 0.12f, 1.2f), wood.Lightened(0.08f));
                    m.Beam(p0.Normalized() * 2.2f, p0.Normalized() * 3.05f, 0.09f, wood.Darkened(0.1f), 0.56f);
                    if (i % 3 == 0)
                        foreach (float z in new[] { -0.55f, 0.55f }) m.Beam(Vector3.Back * z, p0 + Vector3.Back * z, 0.09f, wood);
                }
                m.Box(Vector3.Zero, new Vector3(0.34f, 0.34f, 1.7f), wood.Darkened(0.2f));
                return new MovingPart(LoadKit("rotor_watermill") ?? m.Build(), new Vector3(0, 2.05f, -4.6f), SpinSide, -0.8f, 0, 0);
            case BType.Mill:
                // Built upright, then laid into the spin frame. Round stones on a round stone plinth that reaches the
                // ground, so only the handle shows the turning and nothing floats in front of the mill.
                m.Prism(new Vector3(0, -0.86f, 0), 0.62f, 0.56f, 0.62f, 14, stone.Darkened(0.32f));
                m.Prism(new Vector3(0, -0.24f, 0), 0.6f, 0.6f, 0.12f, 14, stone.Darkened(0.2f));
                m.Prism(new Vector3(0, -0.12f, 0), 0.56f, 0.54f, 0.26f, 14, stone.Darkened(0.12f));
                m.Prism(new Vector3(0, 0.14f, 0), 0.11f, 0.11f, 0.02f, 8, new Color(0.2f, 0.18f, 0.16f));
                m.Box(new Vector3(0.42f, 0.36f, 0), new Vector3(0.08f, 0.46f, 0.08f), wood);
                return new MovingPart(m.Turned(SpinFlat.Inverse()).Build(), new Vector3(-2.9f, 0.86f, 4.4f), SpinFlat, 1.5f, 0, 0);
            case BType.Winery:
                // Screw press over a round vat of must that stands on the ground; the round parts look still while the screw turns.
                m.Box(Vector3.Zero, new Vector3(0.2f, 2.0f, 0.2f), wood).Box(new Vector3(0, 0.72f, 0), new Vector3(2.1f, 0.12f, 0.12f), wood.Darkened(0.12f));
                m.Prism(new Vector3(0, -1.03f, 0), 0.8f, 0.8f, 0.16f, 14, wood.Lightened(0.08f));
                m.Prism(new Vector3(0, -1.65f, 0), 0.98f, 0.94f, 0.6f, 16, wood.Darkened(0.05f));
                m.Prism(new Vector3(0, -1.06f, 0), 0.9f, 0.9f, 0.02f, 16, new Color(0.36f, 0.14f, 0.32f));
                return new MovingPart(m.Turned(SpinFlat.Inverse()).Build(), new Vector3(3.0f, 1.65f, 4.0f), SpinFlat, 0.45f, 0, 0);
            case BType.Smithy:
                m.Box(new Vector3(0.55f, 0, 0), new Vector3(1.15f, 0.09f, 0.09f), wood);
                m.Box(new Vector3(1.12f, 0.03f, 0), new Vector3(0.24f, 0.36f, 0.2f), new Color(0.32f, 0.33f, 0.36f));
                return new MovingPart(m.Build(), new Vector3(-1.95f, 1.5f, 3.0f), Basis.Identity, 5.2f, 2, 0.48f);
            case BType.TideGate:
                m.Box(Vector3.Zero, new Vector3(4.4f, 4.3f, 0.34f), new Color(0.38f, 0.27f, 0.17f));
                if (LoadModel("tidegate_t2") is ArrayMesh && LoadModel("tidegate_door") is ArrayMesh door)
                    return new MovingPart(door, new Vector3(0.04f, 2.2f, 0), Basis.Identity, 0.2f, 1, 2.1f);
                for (int i = 0; i < 6; i++) m.Box(new Vector3(-1.85f + i * 0.74f, 0, 0.19f), new Vector3(0.12f, 4.3f, 0.05f), new Color(0.26f, 0.19f, 0.13f));
                foreach (float y in new[] { -1.45f, 0f, 1.45f }) m.Box(new Vector3(0, y, 0.21f), new Vector3(4.42f, 0.2f, 0.08f), new Color(0.3f, 0.3f, 0.32f));
                return new MovingPart(m.Build(), new Vector3(0, 2.75f, LoadModel("tidegate_t2") != null ? 0 : -1.4f), Basis.Identity, 0.2f, 1, 2.1f);
            default:
                return null;
        }
    }

    /// <summary>A square bar between two points.</summary>
    private MeshKit Beam(Vector3 from, Vector3 to, float half, Color color, float halfDepth = -1)
    {
        Vector3 along = (to - from).Normalized();
        Vector3 side = Math.Abs(along.Z) > 0.9f ? Vector3.Right : along.Cross(Vector3.Back).Normalized();
        Vector3 up = side.Cross(along).Normalized();
        float h1 = half, h2 = halfDepth > 0 ? halfDepth : half;
        Vector3 center = (from + to) / 2;
        Vector3 P(Vector3 end, int a, int b) => end + side * (a * h1) + up * (b * h2);
        Quad(center, P(from, -1, -1), P(to, -1, -1), P(to, 1, -1), P(from, 1, -1), color);
        Quad(center, P(from, -1, 1), P(to, -1, 1), P(to, 1, 1), P(from, 1, 1), color);
        Quad(center, P(from, -1, -1), P(to, -1, -1), P(to, -1, 1), P(from, -1, 1), color);
        Quad(center, P(from, 1, -1), P(to, 1, -1), P(to, 1, 1), P(from, 1, 1), color);
        Quad(center, P(from, -1, -1), P(from, 1, -1), P(from, 1, 1), P(from, -1, 1), color);
        Quad(center, P(to, -1, -1), P(to, 1, -1), P(to, 1, 1), P(to, -1, 1), color);
        return this;
    }

    /// <summary>A two-sided flat sail panel.</summary>
    private MeshKit Panel(Vector3 from, Vector3 to, Vector3 across, float width, Color color)
    {
        Vector3 a = from, b = to, c = to + across * width, d = from + across * width;
        Quad(a + Vector3.Forward, a, b, c, d, color);
        Quad(a + Vector3.Back, a, b, c, d, color.Darkened(0.1f));
        return this;
    }

    /// <summary>Goods carried in front of a villager. Local +Z is forward.</summary>
    public static ArrayMesh Load(Res res)
    {
        var m = new MeshKit();
        Vector3 hands = new(0, 1.02f, 0.46f);
        switch (res)
        {
            case Res.Wood:
                // Three round logs, two below and one on top, bark brown with pale cut ends.
                foreach ((float x, float y, float r) in new[] { (-0.11f, 1.06f, 0.1f), (0.11f, 1.06f, 0.11f), (0f, 1.24f, 0.1f) })
                    m.Cylinder(new Vector3(x, y, -0.2f), new Vector3(x, y, 1.05f), r, r * 0.92f, 8, new Color(0.42f, 0.3f, 0.19f), new Color(0.8f, 0.64f, 0.42f));
                break;
            case Res.Stone:
                // A rough block: overlapping turned chunks in slightly different greys read as one faceted stone.
                m.Box(hands, new Vector3(0.46f, 0.3f, 0.38f), Rock, 0.2f).Box(hands + new Vector3(0.05f, 0.08f, -0.04f), new Vector3(0.38f, 0.26f, 0.34f), Rock.Lightened(0.06f), 0.75f);
                m.Box(hands + new Vector3(-0.08f, 0.16f, 0.05f), new Vector3(0.26f, 0.16f, 0.24f), Rock.Darkened(0.08f), -0.4f);
                m.Box(hands + new Vector3(0.12f, -0.02f, 0.12f), new Vector3(0.18f, 0.22f, 0.2f), Rock.Lightened(0.12f), 1.1f);
                break;
            case Res.Berries:
                // A basket heaped with single berries (red, dark purple, blue) and a few leaves.
                m.Prism(hands + Vector3.Down * 0.18f, 0.22f, 0.3f, 0.28f, 10, Thatch.Darkened(0.1f)).Prism(hands + Vector3.Up * 0.08f, 0.31f, 0.31f, 0.04f, 10, Thatch.Darkened(0.35f), false);
                Color[] berries = { new(0.7f, 0.12f, 0.18f), new(0.32f, 0.1f, 0.3f), new(0.22f, 0.24f, 0.5f), new(0.8f, 0.2f, 0.22f) };
                for (int i = 0; i < 26; i++)
                {
                    float a = i * 2.399963f, r = 0.26f * MathF.Sqrt((i + 0.5f) / 26f), y = 0.1f + 0.1f * (1 - r / 0.26f);
                    Vector3 at = hands + new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
                    m.Prism(at, 0.045f, 0.03f, 0.05f, 6, berries[i % 4]).Prism(at + Vector3.Up * 0.05f, 0.03f, 0f, 0.025f, 6, berries[i % 4].Lightened(0.08f));
                }
                foreach (float a in new[] { 0.6f, 2.8f, 4.6f }) m.Box(hands + new Vector3(MathF.Cos(a) * 0.2f, 0.17f, MathF.Sin(a) * 0.2f), new Vector3(0.14f, 0.015f, 0.07f), new Color(0.26f, 0.46f, 0.2f), a);
                break;
            case Res.Fish:
                // A shallow wicker basket with three fish laid across it, heads and tails over the rim.
                m.Prism(hands + Vector3.Down * 0.12f, 0.24f, 0.3f, 0.2f, 10, Thatch.Darkened(0.1f));
                m.Prism(hands + Vector3.Up * 0.06f, 0.31f, 0.31f, 0.04f, 10, Thatch.Darkened(0.35f), false);
                m.Fish(hands + new Vector3(0.03f, 0.1f, -0.15f), 1.5f, 0.5f);
                m.Fish(hands + new Vector3(-0.04f, 0.1f, 0.0f), -1.62f, 0.54f);
                m.Fish(hands + new Vector3(0.02f, 0.1f, 0.15f), 1.66f, 0.46f);
                break;
            case Res.Grain:
                m.Prism(hands + Vector3.Down * 0.2f, 0.25f, 0.32f, 0.36f, 8, new Color(0.84f, 0.7f, 0.33f)).Prism(hands + Vector3.Up * 0.16f, 0.32f, 0.08f, 0.24f, 8, new Color(0.9f, 0.77f, 0.38f));
                break;
            case Res.Flour:
                m.Prism(hands + Vector3.Down * 0.2f, 0.25f, 0.32f, 0.36f, 8, new Color(0.93f, 0.9f, 0.8f)).Prism(hands + Vector3.Up * 0.16f, 0.32f, 0.08f, 0.24f, 8, new Color(0.96f, 0.94f, 0.86f));
                break;
            case Res.Grapes:
                m.Prism(hands + Vector3.Down * 0.18f, 0.22f, 0.3f, 0.3f, 9, Thatch).Prism(hands + Vector3.Up * 0.1f, 0.27f, 0.08f, 0.14f, 8, new Color(0.36f, 0.16f, 0.45f));
                break;
            case Res.Wine:
                m.Prism(hands + Vector3.Down * 0.28f, 0.1f, 0.26f, 0.32f, 9, new Color(0.66f, 0.38f, 0.24f)).Prism(hands + Vector3.Up * 0.04f, 0.26f, 0.07f, 0.28f, 9, new Color(0.7f, 0.42f, 0.27f));
                break;
            case Res.Meat:
                // A haunch of venison with the bone end showing.
                Color flesh = new(0.55f, 0.22f, 0.17f), fat = new(0.86f, 0.74f, 0.62f), bone = new(0.9f, 0.86f, 0.76f);
                m.Cylinder(hands + new Vector3(0, 0, -0.34f), hands + new Vector3(0, 0.02f, 0.06f), 0.12f, 0.2f, 9, flesh, fat);
                m.Cylinder(hands + new Vector3(0, 0.02f, 0.06f), hands + new Vector3(0, 0.04f, 0.3f), 0.2f, 0.07f, 9, flesh.Darkened(0.08f), fat);
                m.Cylinder(hands + new Vector3(0, 0.04f, 0.28f), hands + new Vector3(0, 0.05f, 0.5f), 0.04f, 0.035f, 6, bone);
                m.Cylinder(hands + new Vector3(0, 0.05f, 0.48f), hands + new Vector3(0, 0.05f, 0.56f), 0.06f, 0.05f, 6, bone);
                break;
            case Res.Tools:
                m.Box(hands, new Vector3(0.5f, 0.12f, 0.4f), Timber);
                for (int i = 0; i < 3; i++) m.Box(hands + new Vector3(-0.15f + i * 0.15f, 0.1f, 0), new Vector3(0.08f, 0.06f, 0.48f), new Color(0.58f, 0.6f, 0.63f));
                break;
            default:
                m.Box(hands, new Vector3(0.56f, 0.08f, 0.42f), Timber);
                for (int i = 0; i < 3; i++) m.Prism(hands + new Vector3(-0.17f + i * 0.17f, 0.04f, 0), 0.12f, 0.08f, 0.13f, 7, new Color(0.74f, 0.49f, 0.24f));
                break;
        }
        return m.Build();
    }

    /// <summary>A fish lying flat, head toward <paramref name="yaw"/>: a body tapering to a forked tail, dark back, silver belly, eyes.</summary>
    public MeshKit Fish(Vector3 centre, float yaw, float length)
    {
        Vector3 along = new(MathF.Sin(yaw), 0, MathF.Cos(yaw)), side = new(along.Z, 0, -along.X);
        Color back = new(0.36f, 0.45f, 0.5f), belly = new(0.8f, 0.82f, 0.8f), fin = new(0.42f, 0.44f, 0.42f);
        float[] widths = { 0.07f, 0.11f, 0.12f, 0.1f, 0.075f, 0.05f, 0.035f };
        for (int k = 0; k < widths.Length; k++)
        {
            float t = (0.5f - (k + 0.5f) / widths.Length) * length * 0.82f;
            float w = widths[k] * length * 1.9f, seg = length * 0.82f / widths.Length * 1.05f;
            Vector3 c = centre + along * t;
            Box(c, new Vector3(w, 0.06f * length * 1.9f, seg), belly, yaw);
            Box(c + Vector3.Up * 0.035f * length * 1.9f, new Vector3(w * 0.7f, 0.03f * length * 1.9f, seg), back, yaw);
        }
        Vector3 tail = centre - along * length * 0.45f;
        Box(tail + side * 0.04f * length, new Vector3(0.025f, 0.02f, 0.16f * length), fin, yaw + 0.55f);
        Box(tail - side * 0.04f * length, new Vector3(0.025f, 0.02f, 0.16f * length), fin, yaw - 0.55f);
        Box(centre + along * length * 0.3f + Vector3.Up * 0.03f * length, new Vector3(widths[1] * length * 2.0f, 0.025f, 0.025f), new Color(0.08f, 0.08f, 0.08f), yaw);
        return this;
    }

    /// <summary>A fire-hardened wooden hunting spear held upright at the right hand, leaning a little forward.</summary>
    public static ArrayMesh Spear()
    {
        var m = new MeshKit();
        Vector3 butt = new(0.34f, 0.05f, 0.05f), tip = new(0.34f, 2.25f, 0.42f);
        m.Cylinder(butt, butt.Lerp(tip, 0.88f), 0.028f, 0.024f, 6, new Color(0.55f, 0.42f, 0.27f));
        m.Cylinder(butt.Lerp(tip, 0.88f), tip, 0.024f, 0.002f, 6, new Color(0.2f, 0.14f, 0.1f));
        return m.Build();
    }

    /// <summary>The wild band's food store: a hide-covered heap weighed down with stones, a drying rack hung with
    /// strips of meat and split fish, and baskets of berries and shellfish.</summary>
    public static ArrayMesh FoodCache()
    {
        var m = new MeshKit();
        Color hide = new(0.55f, 0.41f, 0.27f), pole = new(0.33f, 0.24f, 0.15f), meat = new(0.5f, 0.2f, 0.15f), fish = new(0.62f, 0.5f, 0.36f);
        m.Prism(new Vector3(-1.2f, 0, 0), 1.1f, 0.5f, 0.55f, 9, hide.Darkened(0.15f)).Prism(new Vector3(-1.2f, 0.55f, 0), 0.5f, 0.1f, 0.25f, 9, hide);
        foreach (float a in new[] { 0.4f, 2.2f, 4.1f }) m.Prism(new Vector3(-1.2f + MathF.Cos(a) * 0.95f, 0, MathF.Sin(a) * 0.95f), 0.16f, 0.1f, 0.2f, 6, Rock);
        foreach (float x in new[] { 0.4f, 2.4f })
        {
            m.Cylinder(new Vector3(x, 0, -0.5f), new Vector3(x + 0.05f, 1.7f, -0.2f), 0.05f, 0.04f, 5, pole);
            m.Cylinder(new Vector3(x, 0, 0.5f), new Vector3(x + 0.05f, 1.7f, 0.2f), 0.05f, 0.04f, 5, pole);
        }
        m.Cylinder(new Vector3(0.35f, 1.62f, 0), new Vector3(2.5f, 1.62f, 0), 0.04f, 0.04f, 5, pole);
        for (int i = 0; i < 7; i++) m.Box(new Vector3(0.6f + i * 0.28f, 1.3f, 0), new Vector3(i % 3 == 2 ? 0.1f : 0.07f, 0.6f, 0.03f), i % 3 == 2 ? fish : meat.Lightened(i % 2 * 0.08f));
        m.Basket(new Vector3(1.0f, 0, 1.1f), 0.32f, 0.36f, new Color(0.62f, 0.16f, 0.28f));
        m.Basket(new Vector3(1.8f, 0, 1.2f), 0.3f, 0.3f, new Color(0.24f, 0.26f, 0.34f));
        return m.Build();
    }

    /// <summary>A wild berry bush about 1 m tall: a lumpy leafy mound dotted with red and dark purple berries.</summary>
    public static ArrayMesh BerryBush()
    {
        var m = new MeshKit();
        Color leaf = new(0.24f, 0.42f, 0.2f);
        (Vector3 At, float R)[] lumps = { (Vector3.Zero, 0.75f), (new Vector3(0.5f, 0, 0.3f), 0.5f), (new Vector3(-0.45f, 0, 0.25f), 0.55f), (new Vector3(0.1f, 0, -0.5f), 0.5f) };
        for (int i = 0; i < lumps.Length; i++)
        {
            var (at, r) = lumps[i];
            Color c = leaf.Lightened(i * 0.04f);
            m.Prism(at, r * 0.75f, r, r * 0.6f, 9, c.Darkened(0.12f)).Prism(at + Vector3.Up * r * 0.6f, r, r * 0.45f, r * 0.55f, 9, c);
        }
        Color[] berries = { new(0.75f, 0.1f, 0.16f), new(0.32f, 0.08f, 0.3f) };
        for (int i = 0; i < 40; i++)
        {
            float a = i * 2.399963f, h = 0.25f + (i * 37 % 11) / 11f * 0.75f, r = 0.75f * MathF.Sqrt(1 - (h - 0.2f) / 1.0f) + 0.08f;
            m.Prism(new Vector3(MathF.Cos(a) * r, h, MathF.Sin(a) * r * 0.9f), 0.045f, 0.03f, 0.06f, 5, berries[i % 2]);
        }
        return m.Build();
    }

    /// <summary>A shallow basket of mussels, cockles and limpets gathered on the shore.</summary>
    public static ArrayMesh ShellfishLoad()
    {
        var m = new MeshKit();
        Vector3 hands = new(0, 1.02f, 0.46f);
        m.Prism(hands + Vector3.Down * 0.12f, 0.24f, 0.3f, 0.2f, 10, Thatch.Darkened(0.1f)).Prism(hands + Vector3.Up * 0.06f, 0.31f, 0.31f, 0.04f, 10, Thatch.Darkened(0.35f), false);
        Color[] shells = { new(0.18f, 0.2f, 0.28f), new(0.86f, 0.8f, 0.7f), new(0.7f, 0.6f, 0.5f), new(0.24f, 0.26f, 0.34f) };
        for (int i = 0; i < 22; i++)
        {
            float a = i * 2.399963f, r = 0.25f * MathF.Sqrt((i + 0.5f) / 22f);
            Vector3 at = hands + new Vector3(MathF.Cos(a) * r, 0.08f + 0.06f * (1 - r / 0.25f), MathF.Sin(a) * r);
            // Mussels are long and dark, cockles round and pale.
            if (i % 2 == 0) m.Box(at, new Vector3(0.05f, 0.03f, 0.1f), shells[i % 4], a);
            else m.Prism(at, 0.045f, 0.015f, 0.035f, 6, shells[i % 4]);
        }
        return m.Build();
    }

    public static ArrayMesh Crops()
    {
        var m = new MeshKit();
        for (int i = 0; i < 9; i++)
        for (int k = 0; k < 17; k++)
        for (int s = 0; s < 3; s++)
        {
            float a = (i * 7 + k * 13 + s * 5) * 1.37f;
            float x = -8 + i * 2 + MathF.Cos(a) * 0.3f, z = -8 + k + MathF.Sin(a) * 0.3f;
            float h = 0.5f + MathF.Sin(a * 2.3f) * 0.1f;
            m.Prism(new Vector3(x, 0, z), 0.055f, 0.022f, h, 3, Colors.White);
            m.Prism(new Vector3(x, h - 0.04f, z), 0.075f, 0.018f, 0.22f, 4, new Color(1.08f, 1.03f, 0.82f));
        }
        return m.Build();
    }

    public static ArrayMesh Tree()
    {
        if (LoadKit("tree_pine") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        // The canopy starts above head height so a woodcutter's swing stays visible.
        m.Prism(Vector3.Zero, 0.42f, 0.26f, 3.2f, 5, new Color(0.34f, 0.24f, 0.16f));
        m.Prism(Vector3.Up * 2.8f, 2.5f, 0.15f, 3.6f, 6, Colors.White);
        m.Prism(Vector3.Up * 5.0f, 1.8f, 0, 3.2f, 6, new Color(0.9f, 0.9f, 0.9f));
        return m.Build();
    }

    /// <summary>Round-crowned tree for the lowlands. White faces take the seasonal leaf color.</summary>
    public static ArrayMesh Broadleaf()
    {
        if (LoadKit("tree_broad") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        m.Prism(Vector3.Zero, 0.46f, 0.3f, 3.0f, 6, new Color(0.36f, 0.26f, 0.17f));
        m.Prism(Vector3.Up * 2.7f, 1.5f, 2.7f, 1.3f, 8, new Color(0.86f, 0.86f, 0.86f));
        m.Prism(Vector3.Up * 4.0f, 2.7f, 2.3f, 1.2f, 8, Colors.White);
        m.Prism(Vector3.Up * 5.2f, 2.3f, 0.9f, 1.3f, 8, new Color(0.95f, 0.95f, 0.95f));
        m.Prism(new Vector3(1.5f, 3.6f, 0.8f), 0.8f, 1.15f, 0.8f, 6, new Color(0.9f, 0.9f, 0.9f));
        m.Prism(new Vector3(1.5f, 4.4f, 0.8f), 1.15f, 0.4f, 0.8f, 6, Colors.White);
        return m.Build();
    }

    /// <summary>Slim pale-trunked tree.</summary>
    public static ArrayMesh Birch()
    {
        if (LoadKit("tree_crooked") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        m.Prism(Vector3.Zero, 0.24f, 0.13f, 4.4f, 5, new Color(0.78f, 0.76f, 0.7f));
        foreach (float y in new[] { 0.9f, 1.8f, 2.8f, 3.6f }) m.Prism(Vector3.Up * y, 0.22f, 0.2f, 0.09f, 5, new Color(0.2f, 0.18f, 0.16f));
        m.Prism(Vector3.Up * 3.1f, 0.7f, 1.55f, 1.3f, 7, new Color(0.9f, 0.9f, 0.9f));
        m.Prism(Vector3.Up * 4.4f, 1.55f, 1.05f, 1.5f, 7, Colors.White);
        m.Prism(Vector3.Up * 5.9f, 1.05f, 0.1f, 1.5f, 7, new Color(0.95f, 0.95f, 0.95f));
        return m.Build();
    }

    public static ArrayMesh Reeds()
    {
        var m = new MeshKit();
        for (int i = 0; i < 7; i++)
        {
            float a = i * 2.4f, r = 0.08f + i * 0.06f, h = 1.0f + (i % 3) * 0.28f;
            var p = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            m.Prism(p, 0.04f, 0.012f, h, 3, new Color(0.56f, 0.6f, 0.3f));
            if (i % 2 == 0) m.Prism(p + Vector3.Up * (h - 0.12f), 0.05f, 0.03f, 0.26f, 4, new Color(0.42f, 0.3f, 0.18f));
        }
        return m.Build();
    }

    public static ArrayMesh Sheep()
    {
        var m = new MeshKit();
        Color wool = new(0.9f, 0.88f, 0.8f), dark = new(0.2f, 0.18f, 0.17f);
        m.Box(new Vector3(0, 0.62f, 0), new Vector3(0.62f, 0.56f, 1.0f), wool).Box(new Vector3(0, 0.84f, 0.1f), new Vector3(0.48f, 0.2f, 0.72f), wool.Lightened(0.1f));
        m.Box(new Vector3(0, 0.86f, 0.62f), new Vector3(0.3f, 0.34f, 0.36f), dark);
        foreach ((float x, float z) in new[] { (-0.2f, -0.34f), (0.2f, -0.34f), (-0.2f, 0.34f), (0.2f, 0.34f) }) m.Box(new Vector3(x, 0.18f, z), new Vector3(0.11f, 0.38f, 0.11f), dark);
        return m.Build();
    }

    public static ArrayMesh Cow()
    {
        var m = new MeshKit();
        Color hide = new(0.52f, 0.33f, 0.2f), patch = new(0.88f, 0.84f, 0.75f), dark = new(0.2f, 0.15f, 0.12f);
        m.Box(new Vector3(0, 0.98f, 0), new Vector3(0.82f, 0.82f, 1.85f), hide).Box(new Vector3(0.3f, 1.12f, -0.3f), new Vector3(0.26f, 0.5f, 0.7f), patch);
        m.Box(new Vector3(0, 1.26f, 1.08f), new Vector3(0.46f, 0.5f, 0.62f), hide).Box(new Vector3(0, 1.1f, 1.4f), new Vector3(0.34f, 0.24f, 0.14f), patch);
        foreach (int s in new[] { -1, 1 }) m.Box(new Vector3(s * 0.28f, 1.58f, 1.02f), new Vector3(0.09f, 0.16f, 0.09f), patch);
        foreach ((float x, float z) in new[] { (-0.28f, -0.68f), (0.28f, -0.68f), (-0.28f, 0.68f), (0.28f, 0.68f) }) m.Box(new Vector3(x, 0.3f, z), new Vector3(0.17f, 0.62f, 0.17f), dark);
        m.Box(new Vector3(0, 0.95f, -0.98f), new Vector3(0.07f, 0.72f, 0.07f), dark);
        return m.Build();
    }

    public static ArrayMesh Chicken()
    {
        var m = new MeshKit();
        Color body = new(0.86f, 0.72f, 0.48f);
        m.Box(new Vector3(0, 0.27f, 0), new Vector3(0.24f, 0.24f, 0.36f), body).Box(new Vector3(0, 0.46f, 0.17f), new Vector3(0.13f, 0.2f, 0.13f), body);
        m.Box(new Vector3(0, 0.59f, 0.17f), new Vector3(0.04f, 0.08f, 0.13f), new Color(0.78f, 0.14f, 0.12f)).Box(new Vector3(0, 0.46f, 0.27f), new Vector3(0.05f, 0.04f, 0.09f), new Color(0.92f, 0.62f, 0.14f));
        m.Box(new Vector3(0, 0.36f, -0.2f), new Vector3(0.1f, 0.2f, 0.1f), body.Darkened(0.2f));
        foreach (int s in new[] { -1, 1 }) m.Box(new Vector3(s * 0.07f, 0.08f, 0), new Vector3(0.035f, 0.16f, 0.035f), new Color(0.92f, 0.62f, 0.14f));
        return m.Build();
    }

    public static ArrayMesh Deer()
    {
        var m = new MeshKit();
        Color hide = new(0.6f, 0.42f, 0.26f), belly = new(0.84f, 0.76f, 0.62f), dark = new(0.28f, 0.2f, 0.14f);
        m.Box(new Vector3(0, 1.0f, 0), new Vector3(0.48f, 0.56f, 1.35f), hide).Box(new Vector3(0, 0.76f, 0), new Vector3(0.36f, 0.1f, 1.1f), belly);
        m.Box(new Vector3(0, 1.42f, 0.72f), new Vector3(0.22f, 0.66f, 0.26f), hide).Box(new Vector3(0, 1.74f, 0.88f), new Vector3(0.24f, 0.24f, 0.46f), hide);
        foreach (int s in new[] { -1, 1 })
        {
            m.Box(new Vector3(s * 0.11f, 2.05f, 0.76f), new Vector3(0.04f, 0.42f, 0.04f), dark).Box(new Vector3(s * 0.2f, 2.2f, 0.8f), new Vector3(0.2f, 0.035f, 0.035f), dark);
            foreach (float z in new[] { -0.5f, 0.5f }) m.Box(new Vector3(s * 0.17f, 0.38f, z), new Vector3(0.09f, 0.76f, 0.09f), dark);
        }
        return m.Build();
    }

    public static ArrayMesh Gull()
    {
        // Herring gull: white body and head, pale grey wings with black tips, yellow beak.
        return Bird(new Color(0.95f, 0.95f, 0.92f), new Color(0.95f, 0.95f, 0.92f), new Color(0.66f, 0.69f, 0.72f), new Color(0.12f, 0.12f, 0.13f), new Color(0.95f, 0.75f, 0.2f), 1f, 0.12f);
    }

    /// <summary>Greylag goose: brown-grey body, dark neck, grey wings with darker tips, orange beak, a longer neck.</summary>
    public static ArrayMesh Goose() =>
        Bird(new Color(0.55f, 0.5f, 0.44f), new Color(0.3f, 0.28f, 0.25f), new Color(0.5f, 0.48f, 0.45f), new Color(0.28f, 0.27f, 0.26f), new Color(0.92f, 0.55f, 0.25f), 1.15f, 0.3f);

    /// <summary>A flying bird facing local +Z, wings spread along X (the flap shader bends them by |x|).
    /// Body tapers from breast to tail; each wing is four panels narrowing outward and swept back.</summary>
    private static ArrayMesh Bird(Color body, Color head, Color wing, Color tip, Color beak, float size, float neck)
    {
        var m = new MeshKit();
        float k = size;
        m.Cylinder(new Vector3(0, 0, -0.3f * k), new Vector3(0, 0, 0.12f * k), 0.035f * k, 0.085f * k, 7, body);
        m.Cylinder(new Vector3(0, 0, 0.12f * k), new Vector3(0, 0.02f * k, (0.2f + neck) * k), 0.085f * k, 0.045f * k, 7, head);
        m.Cylinder(new Vector3(0, 0.02f * k, (0.2f + neck) * k), new Vector3(0, 0.025f * k, (0.26f + neck) * k), 0.05f * k, 0.04f * k, 7, head);
        m.Cylinder(new Vector3(0, 0.02f * k, (0.26f + neck) * k), new Vector3(0, 0.01f * k, (0.34f + neck) * k), 0.022f * k, 0.004f, 5, beak);
        // A short fanned tail.
        m.Box(new Vector3(0, 0, -0.34f * k), new Vector3(0.13f * k, 0.012f, 0.12f * k), wing.Darkened(0.05f));
        float[] chord = { 0.26f, 0.22f, 0.17f, 0.11f };
        float[] sweep = { 0f, -0.03f, -0.07f, -0.12f };
        for (int side = -1; side <= 1; side += 2)
        for (int p = 0; p < 4; p++)
        {
            float x = side * (0.07f + 0.13f * p + 0.065f) * k;
            Color c = p == 3 ? tip : p == 2 ? wing.Lerp(tip, 0.25f) : wing;
            m.Box(new Vector3(x, 0.005f * p, (0.03f + sweep[p]) * k), new Vector3(0.135f * k, 0.014f, chord[p] * k), c);
        }
        return m.Build();
    }

    /// <summary>Small sailing boat. Local +Z is the bow.</summary>
    public static ArrayMesh Boat()
    {
        var m = new MeshKit();
        Color hull = new(0.4f, 0.27f, 0.16f), deck = new(0.58f, 0.43f, 0.27f), sail = new(0.9f, 0.84f, 0.68f);
        m.Box(new Vector3(0, 0.3f, 0), new Vector3(1.5f, 0.6f, 4.2f), hull).Box(new Vector3(0, 0.34f, 2.5f), new Vector3(0.95f, 0.5f, 1.0f), hull, 0.78f);
        m.Box(new Vector3(0, 0.61f, 0), new Vector3(1.22f, 0.05f, 3.7f), deck);
        foreach (float z in new[] { -1.1f, 0.2f, 1.2f }) m.Box(new Vector3(0, 0.68f, z), new Vector3(1.24f, 0.1f, 0.2f), hull.Darkened(0.2f));
        m.Box(new Vector3(0, 2.7f, 0.3f), new Vector3(0.13f, 4.2f, 0.13f), hull.Darkened(0.2f));
        // A fore-and-aft sail stays readable from the shore while the boat runs along the channel.
        m.Beam(new Vector3(0.1f, 4.1f, -1.5f), new Vector3(0.1f, 4.1f, 1.9f), 0.05f, hull);
        m.Beam(new Vector3(0.1f, 1.25f, -1.5f), new Vector3(0.1f, 1.25f, 1.9f), 0.05f, hull);
        m.Panel(new Vector3(0.1f, 1.28f, -1.45f), new Vector3(0.1f, 1.28f, 1.85f), Vector3.Up, 2.8f, sail);
        return m.Build();
    }

    /// <summary>A Neolithic dugout canoe facing local +Z: a hollowed log with a paddler kneeling near the stern, no sail.</summary>
    public static ArrayMesh Dugout()
    {
        var m = new MeshKit();
        Color log = new(0.36f, 0.25f, 0.15f), inside = new(0.55f, 0.4f, 0.24f), skin = new(0.72f, 0.52f, 0.38f), hide = new(0.48f, 0.36f, 0.24f);
        m.Box(new Vector3(0, 0.25f, 0), new Vector3(0.95f, 0.5f, 4.6f), log).Box(new Vector3(0, 0.27f, 2.55f), new Vector3(0.6f, 0.4f, 0.6f), log, 0.6f).Box(new Vector3(0, 0.27f, -2.55f), new Vector3(0.6f, 0.4f, 0.6f), log, 0.6f);
        m.Box(new Vector3(0, 0.49f, 0), new Vector3(0.7f, 0.03f, 4.1f), inside);
        // Paddler: a kneeling body, head, and a paddle reaching into the water on one side.
        m.Box(new Vector3(0, 0.75f, -1.2f), new Vector3(0.45f, 0.55f, 0.35f), hide).Box(new Vector3(0, 1.15f, -1.2f), new Vector3(0.24f, 0.26f, 0.24f), skin);
        m.Beam(new Vector3(0.25f, 1.1f, -1.0f), new Vector3(0.75f, -0.1f, -1.7f), 0.05f, log.Darkened(0.2f));
        // A small catch in the bow.
        m.Box(new Vector3(0, 0.58f, 1.3f), new Vector3(0.4f, 0.15f, 0.7f), new Color(0.6f, 0.62f, 0.6f));
        return m.Build();
    }

    /// <summary>A long, low river barge facing local +Z: a flat hull piled with sacks and barrels, a steering oar and a small sail.</summary>
    public static ArrayMesh Barge()
    {
        var m = new MeshKit();
        Color hull = new(0.33f, 0.23f, 0.15f), deck = new(0.5f, 0.38f, 0.24f), sack = new(0.78f, 0.7f, 0.5f), barrel = new(0.45f, 0.3f, 0.18f), sail = new(0.82f, 0.62f, 0.45f);
        m.Box(new Vector3(0, 0.35f, 0), new Vector3(2.6f, 0.7f, 9f), hull).Box(new Vector3(0, 0.4f, 4.9f), new Vector3(2f, 0.55f, 0.9f), hull, 0.75f);
        m.Box(new Vector3(0, 0.72f, 0), new Vector3(2.3f, 0.05f, 8.4f), deck);
        foreach (float z in new[] { -2.6f, -1.2f, 1.2f, 2.6f }) m.Box(new Vector3(0, 1.05f, z), new Vector3(1.9f, 0.6f, 1.2f), sack);
        foreach (float x in new[] { -0.5f, 0.5f }) m.Prism(new Vector3(x, 0.75f, 0), 0.34f, 0.34f, 0.8f, 8, barrel);
        m.Box(new Vector3(0, 2.4f, 3.6f), new Vector3(0.12f, 3.4f, 0.12f), hull.Darkened(0.2f));
        m.Panel(new Vector3(0.08f, 1.5f, 2.3f), new Vector3(0.08f, 1.5f, 3.55f), Vector3.Up, 2.2f, sail);
        m.Beam(new Vector3(0, 1.2f, -4.4f), new Vector3(0, 0.2f, -6f), 0.08f, hull.Darkened(0.2f));
        return m.Build();
    }

    public static ArrayMesh Person()
    {
        var m = new MeshKit();
        m.Prism(Vector3.Zero, 0.46f, 0.3f, 1.08f, 6, Colors.White);
        m.Prism(Vector3.Up * 1.08f, 0.26f, 0.24f, 0.42f, 6, new Color(0.86f, 0.68f, 0.52f));
        return m.Build();
    }

    /// <summary>A cut tree stump left where villagers felled a tree.</summary>
    public static ArrayMesh Stump()
    {
        var m = new MeshKit();
        m.Prism(Vector3.Zero, 0.34f, 0.27f, 0.42f, 9, new Color(0.36f, 0.26f, 0.17f));
        m.Prism(Vector3.Up * 0.42f, 0.27f, 0.25f, 0.02f, 9, new Color(0.74f, 0.6f, 0.4f));
        foreach (float a in new[] { 0.4f, 2.5f, 4.4f })
            m.Prism(new Vector3(MathF.Cos(a) * 0.3f, 0, MathF.Sin(a) * 0.3f), 0.1f, 0.03f, 0.14f, 5, new Color(0.33f, 0.24f, 0.16f));
        return m.Build();
    }

    public static ArrayMesh Grave() => new MeshKit().Box(new Vector3(0, 0.38f, 0), new Vector3(0.62f, 0.76f, 0.2f), new Color(0.56f, 0.56f, 0.53f)).Build();

    /// <summary>
    /// A building site for a 2x2-tile (8 m) plot: corner stakes with a marking line, scaffold poles, a timber stack
    /// and a stone pile. Larger plots scale it uniformly.
    /// </summary>
    public static ArrayMesh Site()
    {
        var m = new MeshKit();
        Color pole = new(0.46f, 0.33f, 0.2f), fresh = new(0.7f, 0.55f, 0.36f), line = new(0.86f, 0.82f, 0.7f);
        Vector3[] corners = { new(-3.1f, 0, -3.1f), new(3.1f, 0, -3.1f), new(3.1f, 0, 3.1f), new(-3.1f, 0, 3.1f) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 c = corners[i], next = corners[(i + 1) % 4];
            m.Box(c + new Vector3(0, 0.45f, 0), new Vector3(0.12f, 0.9f, 0.12f), fresh);
            m.Beam(c + new Vector3(0, 0.55f, 0), next + new Vector3(0, 0.55f, 0), 0.02f, line);
            // Scaffold: a tall pole just outside each corner, tied by two ledgers along each side.
            Vector3 outside = c * 1.1f, nextOutside = next * 1.1f;
            m.Box(outside + new Vector3(0, 2.3f, 0), new Vector3(0.13f, 4.6f, 0.13f), pole);
            foreach (float h in new[] { 1.7f, 3.5f }) m.Beam(outside + new Vector3(0, h, 0), nextOutside + new Vector3(0, h, 0), 0.06f, pole);
        }
        // Timber stack beside the plot.
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 3 - row; i++)
                m.Box(new Vector3(3.9f, 0.15f + row * 0.27f, -1.2f + i * 0.3f + row * 0.15f), new Vector3(2.6f, 0.26f, 0.26f), i % 2 == 0 ? fresh : fresh.Darkened(0.12f));
        // Rough stone on the other side.
        m.Box(new Vector3(-3.9f, 0.25f, 1.4f), new Vector3(0.9f, 0.5f, 0.8f), Rock, 0.4f)
            .Box(new Vector3(-3.8f, 0.2f, 0.5f), new Vector3(0.7f, 0.4f, 0.6f), Rock.Darkened(0.1f), -0.3f)
            .Box(new Vector3(-3.85f, 0.6f, 1.0f), new Vector3(0.6f, 0.35f, 0.5f), Rock.Lightened(0.08f), 0.9f);
        return m.Build();
    }

    /// <summary>A village well: stone ring, two posts, a little roof and a windlass with a bucket.</summary>
    public static ArrayMesh Well()
    {
        if (LoadModel("prop_well") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color stone = Rock.Darkened(0.05f), wood = new(0.42f, 0.29f, 0.18f);
        m.Prism(Vector3.Zero, 0.95f, 0.9f, 0.85f, 12, stone).Prism(new Vector3(0, 0.84f, 0), 0.72f, 0.72f, 0.02f, 12, new Color(0.12f, 0.16f, 0.2f));
        foreach (int s in new[] { -1, 1 }) m.Box(new Vector3(s * 0.78f, 1.3f, 0), new Vector3(0.12f, 1.7f, 0.12f), wood);
        m.Box(new Vector3(0, 1.7f, 0), new Vector3(1.6f, 0.1f, 0.1f), wood).Box(new Vector3(0, 1.35f, 0), new Vector3(0.18f, 0.22f, 0.18f), wood.Darkened(0.2f));
        m.Gable(new Vector3(0, 2.15f, 0), 1.9f, 1.4f, 0.55f, new Color(0.45f, 0.33f, 0.2f));
        return m.Build();
    }

    /// <summary>A town fountain: octagonal basin, a column and an upper bowl.</summary>
    public static ArrayMesh Fountain()
    {
        if (LoadModel("prop_fountain") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color stone = new(0.68f, 0.65f, 0.58f), water = new(0.25f, 0.42f, 0.5f);
        m.Prism(Vector3.Zero, 1.7f, 1.7f, 0.6f, 8, stone).Prism(new Vector3(0, 0.55f, 0), 1.5f, 1.5f, 0.02f, 8, water);
        m.Prism(new Vector3(0, 0.5f, 0), 0.22f, 0.18f, 1.4f, 8, stone).Prism(new Vector3(0, 1.75f, 0), 0.25f, 0.7f, 0.25f, 10, stone);
        m.Prism(new Vector3(0, 1.98f, 0), 0.6f, 0.6f, 0.02f, 10, water).Prism(new Vector3(0, 2.0f, 0), 0.08f, 0.02f, 0.5f, 6, stone);
        return m.Build();
    }

    /// <summary>An early 20th-century water tower: brick shaft, riveted tank and a conical roof, about 17 m.</summary>
    public static ArrayMesh WaterTower()
    {
        if (LoadModel("prop_watertower") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color brick = new(0.55f, 0.3f, 0.22f), tank = new(0.45f, 0.48f, 0.5f), roof = new(0.3f, 0.33f, 0.36f);
        m.Prism(Vector3.Zero, 2.4f, 2.0f, 11f, 12, brick).Prism(new Vector3(0, 11f, 0), 2.1f, 3.6f, 1.2f, 12, brick.Darkened(0.1f));
        m.Prism(new Vector3(0, 12.2f, 0), 3.6f, 3.6f, 3.2f, 16, tank).Prism(new Vector3(0, 15.4f, 0), 3.8f, 0.3f, 1.8f, 16, roof);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            m.Box(new Vector3(MathF.Cos(a) * 2.02f, 5.5f, MathF.Sin(a) * 2.02f), new Vector3(0.3f, 1.6f, 0.3f), new Color(0.75f, 0.78f, 0.8f), -a);
        }
        return m.Build();
    }

    /// <summary>A 20th-century district heating boiler house: brick hall with a 24 m chimney at local (2.5, 0, -1.5).</summary>
    public static ArrayMesh BoilerHouse()
    {
        if (LoadModel("prop_boilerhouse") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color brick = new(0.52f, 0.29f, 0.22f), roof = new(0.3f, 0.31f, 0.33f), glass = new(0.3f, 0.42f, 0.5f);
        m.House(9f, 6f, 5f, 1.4f, brick, roof);
        m.Windows(9f, 6f, 3.4f, glass);
        m.Prism(new Vector3(2.5f, 0, -1.5f), 1.1f, 0.65f, 24f, 12, brick.Darkened(0.12f));
        m.Prism(new Vector3(2.5f, 23.4f, -1.5f), 0.75f, 0.75f, 0.6f, 12, new Color(0.22f, 0.22f, 0.23f));
        return m.Build();
    }

    /// <summary>A parish church (image-made if present): long nave along local Z with the bell tower at +Z (the fallback is drawn along X).</summary>
    public static ArrayMesh Church()
    {
        if (LoadModel("landmark_church") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color stone = new(0.62f, 0.6f, 0.55f), slate = new(0.3f, 0.33f, 0.38f);
        m.House(14f, 8f, 7f, 4f, stone, slate, new Vector3(2f, 0, 0));
        m.Box(new Vector3(-7f, 8f, 0), new Vector3(4.5f, 16f, 4.5f), stone.Darkened(0.05f)).Prism(new Vector3(-7f, 16f, 0), 3.2f, 0.05f, 6f, 4, slate);
        return m.Build();
    }
    /// <summary>A harbour quay with a treadwheel crane (image-made if present); local +Z faces the water.</summary>
    public static ArrayMesh Quay()
    {
        if (LoadModel("landmark_quay") is ArrayMesh real) return real;
        var m = new MeshKit();
        Color wood = new(0.45f, 0.32f, 0.2f), stone = new(0.6f, 0.58f, 0.52f);
        m.Box(new Vector3(0, 0.9f, 0), new Vector3(12f, 0.3f, 6f), wood).Box(new Vector3(0, 0.45f, 2.9f), new Vector3(12f, 0.9f, 0.4f), stone);
        m.Box(new Vector3(-4f, 4.5f, 0), new Vector3(3.5f, 7f, 3.5f), wood.Darkened(0.1f)).Beam(new Vector3(-4f, 7.5f, 0), new Vector3(-4f, 8.5f, 4.5f), 0.15f, wood);
        return m.Build();
    }
    /// <summary>A street dug up to lay a water main: open trench, a length of pipe beside it and a spoil heap. 4 m long along local X.</summary>
    public static ArrayMesh Trench()
    {
        var m = new MeshKit();
        Color soil = new(0.36f, 0.27f, 0.19f), deep = new(0.18f, 0.13f, 0.1f), pipe = new(0.3f, 0.3f, 0.32f), plank = new(0.6f, 0.47f, 0.3f);
        m.Box(new Vector3(0, 0.02f, 0), new Vector3(3.8f, 0.04f, 1.1f), deep).Box(new Vector3(0, 0.05f, 0.75f), new Vector3(3.8f, 0.1f, 0.4f), soil);
        m.Prism(new Vector3(0.2f, 0, -1.05f), 0.55f, 0.15f, 0.55f, 7, soil.Lightened(0.05f)).Prism(new Vector3(-1.2f, 0, -0.95f), 0.4f, 0.12f, 0.4f, 7, soil);
        m.Beam(new Vector3(-1.6f, 0.2f, 1.15f), new Vector3(1.4f, 0.2f, 1.15f), 0.16f, pipe);
        foreach (float x in new[] { -1.9f, 1.9f }) m.Box(new Vector3(x, 0.45f, 0), new Vector3(0.08f, 0.9f, 0.08f), plank).Box(new Vector3(x, 0.8f, 0), new Vector3(0.06f, 0.08f, 1.3f), plank);
        return m.Build();
    }
    /// <summary>An Industrial-era factory (image-made only), 23 m along local X, gate at +Z, chimney at a back corner.</summary>
    public static ArrayMesh Factory() => LoadModel("landmark_factory");

    /// <summary>A 20th-century railway station (image-made only), 22 m along local X, entrance at +Z.</summary>
    public static ArrayMesh Station() => LoadModel("landmark_station");

    /// <summary>A cast-iron street lamp post, 4 m, with its arm over local +Z. The lantern is a separate mesh.</summary>
    public static ArrayMesh LampPost()
    {
        var m = new MeshKit();
        Color iron = new(0.16f, 0.17f, 0.18f);
        m.Prism(Vector3.Zero, 0.16f, 0.12f, 0.35f, 8, iron).Prism(new Vector3(0, 0.35f, 0), 0.07f, 0.05f, 3.55f, 8, iron);
        m.Beam(new Vector3(0, 3.7f, 0), new Vector3(0, 3.85f, 0.55f), 0.03f, iron);
        m.Prism(new Vector3(0, 3.95f, 0.55f), 0.16f, 0.02f, 0.18f, 6, iron);
        return m.Build();
    }

    /// <summary>The glass of a street lamp, hung at the end of <see cref="LampPost"/>'s arm; lit by its material at night.</summary>
    public static ArrayMesh Lantern() => new MeshKit().Prism(new Vector3(0, 3.62f, 0.55f), 0.09f, 0.14f, 0.34f, 6, Colors.White).Build();

    /// <summary>A wooden wheelbarrow pushed in front of a porter from the Middle Ages. Local +Z is forward.</summary>
    public static ArrayMesh Barrow()
    {
        var m = new MeshKit();
        Color wood = new(0.5f, 0.35f, 0.21f), dark = new(0.3f, 0.21f, 0.13f);
        // Wheel first: a prism along Y turned onto the X axis, centred at (0, 0.25, 1.55).
        m.Prism(new Vector3(0.25f, -0.04f, 1.55f), 0.26f, 0.26f, 0.08f, 10, dark).Turned(new Basis(Vector3.Back, MathF.PI / 2));
        m.Box(new Vector3(0, 0.5f, 1.0f), new Vector3(0.72f, 0.07f, 0.86f), wood);
        foreach (int s in new[] { -1, 1 })
        {
            m.Box(new Vector3(s * 0.36f, 0.62f, 1.0f), new Vector3(0.05f, 0.22f, 0.86f), wood.Darkened(0.08f));
            m.Beam(new Vector3(s * 0.26f, 0.45f, 1.6f), new Vector3(s * 0.3f, 0.86f, 0.3f), 0.035f, dark);
            m.Box(new Vector3(s * 0.25f, 0.2f, 0.75f), new Vector3(0.05f, 0.42f, 0.05f), dark);
        }
        m.Box(new Vector3(0, 0.62f, 1.43f), new Vector3(0.72f, 0.22f, 0.05f), wood.Darkened(0.08f));
        return m.Build();
    }

    /// <summary>A heap of grey boulders about 3 m across that fills a cave mouth before the band digs it out.</summary>
    public static ArrayMesh Boulders()
    {
        var m = new MeshKit();
        Color rock = new(0.5f, 0.5f, 0.48f);
        m.Prism(Vector3.Zero, 1.5f, 1.1f, 0.8f, 7, rock.Darkened(0.1f));
        for (int i = 0; i < 7; i++)
        {
            float a = i * 2.39f, r = 0.5f + (i % 3) * 0.45f;
            m.Prism(new Vector3(MathF.Cos(a) * r, 0.2f + (i % 2) * 0.5f, MathF.Sin(a) * r * 0.6f), 0.55f + (i % 3) * 0.15f, 0.25f, 0.6f + (i % 2) * 0.3f, 6, i % 2 == 0 ? rock : rock.Darkened(0.2f));
        }
        return m.Build();
    }

    /// <summary>Demolition rubble: broken brick and plaster heaped with a few old beams.</summary>
    public static ArrayMesh Rubble()
    {
        var m = new MeshKit();
        Color brick = new(0.55f, 0.32f, 0.24f), plaster = new(0.72f, 0.68f, 0.6f), beam = new(0.32f, 0.23f, 0.15f);
        m.Prism(Vector3.Zero, 1.6f, 0.25f, 0.9f, 7, brick.Darkened(0.1f)).Prism(new Vector3(1.1f, 0, 0.6f), 1.0f, 0.2f, 0.6f, 6, plaster);
        for (int i = 0; i < 9; i++)
        {
            float a = i * 2.39f, r = 0.4f + (i % 4) * 0.35f;
            m.Box(new Vector3(MathF.Cos(a) * r, 0.15f + (i % 3) * 0.12f, MathF.Sin(a) * r), new Vector3(0.28f, 0.14f, 0.18f), i % 2 == 0 ? brick : plaster.Darkened(0.1f), a);
        }
        m.Beam(new Vector3(-1.4f, 0.1f, -0.6f), new Vector3(0.9f, 0.55f, 0.4f), 0.09f, beam).Beam(new Vector3(-0.6f, 0.1f, 1.0f), new Vector3(1.5f, 0.35f, -0.5f), 0.08f, beam);
        return m.Build();
    }
    /// <summary>One unit of a storehouse's goods pile; a store shows up to four of each kind as its stock grows.</summary>
    public static ArrayMesh GoodsPile(int kind)
    {
        var m = new MeshKit();
        switch (kind)
        {
            case 0: // logs
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < 3 - row; i++)
                        m.Box(new Vector3(0, 0.17f + row * 0.3f, -0.32f + i * 0.32f + row * 0.16f), new Vector3(1.5f, 0.29f, 0.29f), i % 2 == 0 ? new Color(0.55f, 0.4f, 0.25f) : new Color(0.48f, 0.34f, 0.21f));
                break;
            case 1: // stone blocks
                m.Box(new Vector3(-0.25f, 0.25f, 0), new Vector3(0.7f, 0.5f, 0.6f), Rock.Darkened(0.18f), 0.2f).Box(new Vector3(0.35f, 0.2f, 0.1f), new Vector3(0.55f, 0.4f, 0.5f), Rock.Darkened(0.25f), -0.3f);
                m.Box(new Vector3(0.05f, 0.62f, 0.05f), new Vector3(0.55f, 0.32f, 0.45f), Rock.Darkened(0.1f), 0.6f);
                break;
            case 2: // grain and flour sacks
                Color sack = new(0.76f, 0.68f, 0.5f);
                m.Prism(new Vector3(-0.28f, 0, 0), 0.26f, 0.2f, 0.62f, 8, sack).Prism(new Vector3(0.28f, 0, 0.05f), 0.26f, 0.2f, 0.58f, 8, sack.Darkened(0.06f));
                m.Prism(new Vector3(0, 0, -0.42f), 0.25f, 0.19f, 0.6f, 8, sack.Lightened(0.05f));
                break;
            default: // barrels
                Color stave = new(0.5f, 0.33f, 0.19f), hoop = new(0.25f, 0.24f, 0.24f);
                foreach (float x in new[] { -0.3f, 0.32f })
                {
                    m.Prism(new Vector3(x, 0, 0), 0.25f, 0.29f, 0.38f, 10, stave, false).Prism(new Vector3(x, 0.38f, 0), 0.29f, 0.25f, 0.38f, 10, stave);
                    m.Prism(new Vector3(x, 0.12f, 0), 0.275f, 0.28f, 0.05f, 10, hoop, false).Prism(new Vector3(x, 0.6f, 0), 0.28f, 0.27f, 0.05f, 10, hoop, false);
                }
                break;
        }
        return m.Build();
    }

    public static ArrayMesh Cargo() => new MeshKit().Box(Vector3.Zero, new Vector3(0.62f, 0.42f, 0.62f), Colors.White).Build();

    public static ArrayMesh GrassTuft()
    {
        if (LoadKit("scatter_grass") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        Color[] shades = { new(0.34f, 0.56f, 0.24f), new(0.42f, 0.62f, 0.27f), new(0.29f, 0.49f, 0.22f) };
        for (int i = 0; i < 5; i++)
        {
            float a = i * 2.4f, r = 0.08f + i * 0.055f;
            m.Prism(new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r), 0.075f, 0.01f, 0.34f + (i % 3) * 0.11f, 3, shades[i % 3]);
        }
        return m.Build();
    }

    public static ArrayMesh Flowers()
    {
        // A small clump of meadow flowers: thin stems, two leaves, and open flat flower heads (a ring of petals
        // round a darker or yellow centre) in buttercup yellow, daisy white and cranesbill purple.
        var m = new MeshKit();
        Color stem = new(0.3f, 0.5f, 0.22f);
        (Color Petal, Color Centre)[] kinds =
        {
            (new Color(0.96f, 0.84f, 0.22f), new Color(0.85f, 0.62f, 0.12f)),
            (new Color(0.95f, 0.95f, 0.9f), new Color(0.95f, 0.78f, 0.2f)),
            (new Color(0.66f, 0.42f, 0.78f), new Color(0.42f, 0.24f, 0.5f)),
        };
        for (int i = 0; i < 4; i++)
        {
            float a = i * 1.9f, r = 0.12f + i * 0.08f, h = 0.24f + (i % 3) * 0.05f;
            var p = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            var (petal, centre) = kinds[i % 3];
            m.Prism(p, 0.012f, 0.008f, h, 3, stem);
            m.Box(p + new Vector3(0.04f, 0.06f, 0), new Vector3(0.1f, 0.008f, 0.035f), stem.Lightened(0.05f), a);
            m.Prism(p + Vector3.Up * h, 0.03f, 0.075f, 0.018f, 7, petal);
            m.Prism(p + Vector3.Up * (h + 0.018f), 0.025f, 0.012f, 0.012f, 5, centre);
        }
        return m.Build();
    }

    public static ArrayMesh Pebbles()
    {
        // Three weathered field stones half sunk in the grass: rounded tops, warm greys, moss on the largest.
        var m = new MeshKit();
        (Vector3 At, float R, float H, Color C)[] stones =
        {
            (Vector3.Zero, 0.34f, 0.24f, new Color(0.56f, 0.55f, 0.5f)),
            (new Vector3(0.42f, 0, 0.18f), 0.18f, 0.13f, new Color(0.5f, 0.48f, 0.44f)),
            (new Vector3(-0.14f, 0, 0.38f), 0.13f, 0.09f, new Color(0.62f, 0.6f, 0.55f)),
        };
        foreach (var s in stones)
        {
            m.Prism(s.At + Vector3.Down * 0.05f, s.R, s.R * 0.88f, s.H * 0.55f, 7, s.C);
            m.Prism(s.At + Vector3.Up * s.H * 0.5f, s.R * 0.88f, s.R * 0.45f, s.H * 0.4f, 7, s.C.Lightened(0.06f));
        }
        m.Prism(new Vector3(0.05f, 0.22f, -0.04f), 0.17f, 0.06f, 0.05f, 7, new Color(0.34f, 0.42f, 0.22f));
        return m.Build();
    }

    public static ArrayMesh Puff()
    {
        var m = new MeshKit();
        // A rounded puff: three stacked rings make a ball instead of a hexagonal drum.
        m.Prism(Vector3.Down * 0.55f, 0.3f, 0.62f, 0.25f, 10, Colors.White);
        m.Prism(Vector3.Down * 0.3f, 0.62f, 0.72f, 0.3f, 10, Colors.White, false);
        m.Prism(Vector3.Zero, 0.72f, 0.55f, 0.25f, 10, Colors.White, false);
        m.Prism(Vector3.Up * 0.25f, 0.55f, 0.15f, 0.25f, 10, Colors.White);
        return m.Build();
    }

    /// <summary>A stook: sheaves of cut grain stood together to dry, about 1.4 m tall.</summary>
    public static ArrayMesh Stook()
    {
        var m = new MeshKit();
        Color straw = new(0.86f, 0.72f, 0.4f), dark = new(0.72f, 0.58f, 0.3f);
        for (int k = 0; k < 4; k++)
        {
            float a = k * MathF.PI / 2 + 0.4f;
            m.Box(new Vector3(MathF.Cos(a) * 0.25f, 0.6f, MathF.Sin(a) * 0.25f), new Vector3(0.32f, 1.2f, 0.32f), k % 2 == 0 ? straw : dark, a);
        }
        m.Box(new Vector3(0, 1.3f, 0), new Vector3(0.5f, 0.25f, 0.5f), straw);
        return m.Build();
    }

    /// <summary>One firework spark: a small white cube, tinted per instance and drawn additively.</summary>
    public static ArrayMesh Spark()
    {
        var m = new MeshKit();
        m.Box(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f), Colors.White);
        return m.Build();
    }

    /// <summary>A horse and two-wheeled cart, facing local +Z (horse in front).</summary>
    public static ArrayMesh Cart()
    {
        var m = new MeshKit();
        Color horse = new(0.42f, 0.28f, 0.18f), dark = new(0.2f, 0.14f, 0.1f), wood = new(0.55f, 0.42f, 0.27f), load = new(0.78f, 0.68f, 0.45f);
        m.Box(new Vector3(0, 1.15f, 1.4f), new Vector3(0.55f, 0.6f, 1.5f), horse).Box(new Vector3(0, 1.6f, 2.25f), new Vector3(0.3f, 0.6f, 0.4f), horse);
        foreach (float x in new[] { -0.18f, 0.18f })
        foreach (float z in new[] { 0.85f, 1.95f })
            m.Box(new Vector3(x, 0.45f, z), new Vector3(0.12f, 0.9f, 0.12f), dark);
        m.Box(new Vector3(0, 0.95f, -0.8f), new Vector3(1.3f, 0.35f, 1.9f), wood).Box(new Vector3(0, 1.3f, -0.8f), new Vector3(1.1f, 0.4f, 1.5f), load);
        foreach (float x in new[] { -0.72f, 0.72f }) m.Box(new Vector3(x, 0.5f, -0.8f), new Vector3(0.1f, 1f, 1f), dark);
        m.Box(new Vector3(0, 1.0f, 0.4f), new Vector3(0.6f, 0.08f, 0.9f), wood);
        return m.Build();
    }

    /// <summary>A Bronze and Iron Age ox cart facing local +Z: two pale oxen under a yoke, a plank cart on solid disc wheels.</summary>
    public static ArrayMesh OxCart()
    {
        var m = new MeshKit();
        Color ox = new(0.74f, 0.66f, 0.54f), dark = new(0.22f, 0.16f, 0.11f), horn = new(0.88f, 0.84f, 0.72f), wood = new(0.5f, 0.38f, 0.24f), load = new(0.74f, 0.64f, 0.4f);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            // Rounded body with a slight hump at the shoulders, the head held low and forward.
            m.Cylinder(new Vector3(x, 1.0f, 0.95f), new Vector3(x, 1.05f, 2.2f), 0.27f, 0.31f, 9, ox, ox.Darkened(0.08f));
            m.Cylinder(new Vector3(x, 1.08f, 2.2f), new Vector3(x, 1.0f, 2.55f), 0.3f, 0.2f, 9, ox);
            m.Cylinder(new Vector3(x, 0.98f, 2.55f), new Vector3(x, 0.86f, 2.95f), 0.17f, 0.12f, 8, ox.Lightened(0.04f), new Color(0.3f, 0.24f, 0.2f));
            foreach (int s in new[] { -1, 1 })
            {
                m.Cylinder(new Vector3(x + s * 0.1f, 1.04f, 2.62f), new Vector3(x + s * 0.26f, 1.16f, 2.66f), 0.03f, 0.012f, 5, horn);
                m.Cylinder(new Vector3(x + s * 0.13f, 1.1f, 2.6f), new Vector3(x + s * 0.2f, 1.02f, 2.56f), 0.04f, 0.02f, 5, ox.Darkened(0.1f));
            }
            foreach (float lx in new[] { -0.15f, 0.15f })
            foreach (float z in new[] { 1.1f, 2.25f })
                m.Cylinder(new Vector3(x + lx, 0.85f, z), new Vector3(x + lx, 0.02f, z), 0.07f, 0.045f, 6, ox.Darkened(0.12f), dark);
            m.Cylinder(new Vector3(x, 1.02f, 0.92f), new Vector3(x, 0.55f, 0.85f), 0.025f, 0.02f, 4, ox.Darkened(0.2f));
        }
        // Yoke across both necks, and the pole back to the cart.
        m.Cylinder(new Vector3(-0.75f, 1.3f, 2.45f), new Vector3(0.75f, 1.3f, 2.45f), 0.06f, 0.06f, 6, wood.Darkened(0.2f));
        m.Cylinder(new Vector3(0, 1.22f, 2.45f), new Vector3(0, 0.95f, 0.0f), 0.05f, 0.05f, 6, wood);
        // Cart: plank bed with low side rails, sacks, and two solid wooden wheels.
        m.Box(new Vector3(0, 0.95f, -0.8f), new Vector3(1.3f, 0.12f, 1.8f), wood);
        foreach (int s in new[] { -1, 1 }) m.Box(new Vector3(s * 0.62f, 1.12f, -0.8f), new Vector3(0.06f, 0.24f, 1.8f), wood.Darkened(0.1f));
        foreach ((float sx, float sz) in new[] { (-0.28f, -0.4f), (0.28f, -0.4f), (-0.28f, -1.15f), (0.28f, -1.15f) })
            m.Cylinder(new Vector3(sx, 1.0f, sz), new Vector3(sx, 1.42f, sz), 0.22f, 0.16f, 8, load, load.Lightened(0.06f));
        foreach (int s in new[] { -1, 1 })
        {
            m.Cylinder(new Vector3(s * 0.7f, 0.5f, -0.8f), new Vector3(s * 0.82f, 0.5f, -0.8f), 0.5f, 0.5f, 14, dark, wood.Darkened(0.15f));
            m.Cylinder(new Vector3(s * 0.82f, 0.5f, -0.8f), new Vector3(s * 0.9f, 0.5f, -0.8f), 0.1f, 0.08f, 8, wood);
        }
        return m.Build();
    }

    /// <summary>A small 20th-century lorry facing local +Z: cab in front, a covered load bed behind.</summary>
    public static ArrayMesh Lorry()
    {
        var m = new MeshKit();
        Color cab = new(0.22f, 0.36f, 0.3f), glass = new(0.25f, 0.3f, 0.36f), canvas = new(0.6f, 0.56f, 0.44f), black = new(0.1f, 0.1f, 0.1f);
        m.Box(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 0.4f, 5.2f), black);
        m.Box(new Vector3(0, 1.45f, 1.75f), new Vector3(1.9f, 1.1f, 1.5f), cab).Box(new Vector3(0, 1.75f, 2.51f), new Vector3(1.6f, 0.45f, 0.04f), glass);
        m.Box(new Vector3(0, 1.7f, -0.8f), new Vector3(2f, 1.5f, 3.4f), canvas);
        foreach (float x in new[] { -0.95f, 0.95f })
        foreach (float z in new[] { 1.7f, -1.8f })
            m.Box(new Vector3(x, 0.4f, z), new Vector3(0.25f, 0.8f, 0.8f), black);
        return m.Build();
    }

    /// <summary>A small skater seen from afar: dark coat, red scarf, pale face, leaning forward a little.</summary>
    public static ArrayMesh Skater()
    {
        var m = new MeshKit();
        Color coat = new(0.18f, 0.2f, 0.26f), scarf = new(0.75f, 0.15f, 0.12f), skin = new(0.86f, 0.7f, 0.58f), legs = new(0.12f, 0.12f, 0.12f);
        m.Box(new Vector3(0.1f, 0.4f, -0.12f), new Vector3(0.14f, 0.8f, 0.14f), legs).Box(new Vector3(-0.05f, 0.4f, 0.12f), new Vector3(0.14f, 0.8f, 0.14f), legs);
        m.Box(new Vector3(0.05f, 1.15f, 0), new Vector3(0.36f, 0.75f, 0.46f), coat).Box(new Vector3(0.07f, 1.56f, 0), new Vector3(0.3f, 0.1f, 0.38f), scarf);
        m.Box(new Vector3(0.08f, 1.75f, 0), new Vector3(0.24f, 0.26f, 0.24f), skin);
        return m.Build();
    }

    /// <summary>A market stall: a trestle table of goods under a striped awning, open towards local +Z.</summary>
    public static ArrayMesh MarketStall()
    {
        var m = new MeshKit();
        Color wood = new(0.48f, 0.34f, 0.22f), red = new(0.72f, 0.2f, 0.16f), cream = new(0.9f, 0.86f, 0.74f);
        m.Box(new Vector3(0, 0.8f, 0.2f), new Vector3(2.6f, 0.1f, 1.1f), wood);
        foreach (float x in new[] { -1.25f, 1.25f })
        foreach (float z in new[] { -0.5f, 0.9f })
            m.Box(new Vector3(x, z < 0 ? 1.25f : 1f, z), new Vector3(0.08f, z < 0 ? 2.5f : 2f, 0.08f), wood);
        for (int s = 0; s < 6; s++)
            m.Box(new Vector3(-1.3f + s * 0.52f + 0.26f, 2.3f, 0.2f), new Vector3(0.52f, 0.06f, 1.9f), s % 2 == 0 ? red : cream, 0);
        Color[] goods = { new(0.8f, 0.5f, 0.15f), new(0.45f, 0.55f, 0.2f), new(0.6f, 0.2f, 0.25f), new(0.85f, 0.75f, 0.4f) };
        for (int g = 0; g < 4; g++) m.Box(new Vector3(-0.9f + g * 0.6f, 0.95f, 0.2f), new Vector3(0.45f, 0.2f, 0.6f), goods[g]);
        return m.Build();
    }

    /// <summary>One 4 m length of railway along local X: ballast, sleepers and two rails.</summary>
    public static ArrayMesh RailTrack()
    {
        var m = new MeshKit();
        Color ballast = new(0.46f, 0.43f, 0.4f), sleeper = new(0.3f, 0.22f, 0.15f), steel = new(0.42f, 0.42f, 0.45f);
        m.Box(new Vector3(0, 0.05f, 0), new Vector3(4f, 0.1f, 2.6f), ballast);
        for (int k = 0; k < 6; k++) m.Box(new Vector3(-1.67f + k * 0.667f, 0.13f, 0), new Vector3(0.24f, 0.07f, 2.3f), sleeper);
        foreach (float z in new[] { -0.72f, 0.72f }) m.Box(new Vector3(0, 0.22f, z), new Vector3(4f, 0.12f, 0.1f), steel);
        return m.Build();
    }

    /// <summary>A stone tunnel mouth facing +X, where the railway enters the hills.</summary>
    /// <summary>
    /// A weathered rock outcrop about 14 m across and 7 m high, the mouth of a cave framed in its face (+Z).
    /// The opening itself is <see cref="CaveMouth"/>, scaled by how far the band has dug.
    /// </summary>
    public static ArrayMesh Cave()
    {
        var m = new MeshKit();
        Color rock = new(0.43f, 0.41f, 0.37f), dark = new(0.32f, 0.31f, 0.28f), warm = new(0.5f, 0.45f, 0.38f), moss = new(0.3f, 0.36f, 0.2f);
        // A heaped outcrop of weathered slabs and lumps, half sunk into the hillside.
        m.Prism(new Vector3(0, -1.2f, -1.5f), 7.6f, 3.4f, 7.6f, 7, rock);
        m.Prism(new Vector3(-4.6f, -1f, 0.6f), 3.9f, 1.4f, 5f, 6, dark);
        m.Prism(new Vector3(4.8f, -1f, 0.2f), 3.5f, 1.1f, 4.4f, 5, warm);
        m.Prism(new Vector3(-1.8f, 4.6f, -2.4f), 3.4f, 1.8f, 2.4f, 5, warm);
        m.Prism(new Vector3(2.2f, 5f, -0.6f), 2.6f, 1.1f, 1.8f, 6, dark);
        m.Prism(new Vector3(0.4f, 6.3f, -2f), 2.6f, 1.0f, 1.2f, 6, moss);
        m.Prism(new Vector3(-5.4f, 3.6f, 0f), 1.5f, 0.5f, 0.8f, 5, moss);
        m.Prism(new Vector3(5.1f, 3.1f, 0.6f), 1.2f, 0.4f, 0.6f, 5, moss);
        // Overhanging slabs over the mouth and rough jambs either side of it.
        m.Box(new Vector3(-2.2f, 1.3f, 5.2f), new Vector3(1.5f, 3.1f, 2.4f), dark, 0.2f).Box(new Vector3(2.25f, 1.2f, 5.2f), new Vector3(1.6f, 2.9f, 2.4f), rock, -0.15f);
        m.Box(new Vector3(-0.3f, 3.2f, 5.3f), new Vector3(5.8f, 1.1f, 2.6f), warm, 0.08f).Box(new Vector3(0.6f, 4f, 4.6f), new Vector3(4.2f, 0.9f, 2.4f), dark, -0.12f);
        // Boulders fallen from the face, and spoil from the digging heaped beside the mouth.
        m.Prism(new Vector3(-4.4f, -0.3f, 5.6f), 1.1f, 0.5f, 1.1f, 5, dark).Prism(new Vector3(5.6f, -0.3f, 4.8f), 0.8f, 0.3f, 0.8f, 5, rock);
        m.Prism(new Vector3(3.8f, -0.25f, 7f), 1.6f, 0.3f, 0.75f, 7, new Color(0.42f, 0.33f, 0.24f));
        return m.Build();
    }

    /// <summary>The dark opening of the cave, roughly arched: 2.9 m wide and 2.8 m high, its sill at the origin.</summary>
    public static ArrayMesh CaveMouth()
    {
        var m = new MeshKit();
        Color black = new(0.03f, 0.025f, 0.02f);
        m.Box(new Vector3(0, 1.1f, 0), new Vector3(2.9f, 2.2f, 2f), black).Box(new Vector3(0, 2.35f, 0), new Vector3(2.2f, 0.4f, 2f), black).Box(new Vector3(0.1f, 2.65f, 0), new Vector3(1.3f, 0.3f, 2f), black);
        return m.Build();
    }

    /// <summary>A camp hearth: a ring of stones round a few charred logs (the flames are drawn separately).</summary>
    public static ArrayMesh Hearth()
    {
        var m = new MeshKit();
        // A ring of rounded field stones, some soot-blackened on the inside, round a bed of grey ash and glowing embers.
        for (int i = 0; i < 10; i++)
        {
            float a = i * MathF.Tau / 10, r = 0.2f + (i * 7 % 5) * 0.025f;
            Color c = (i % 3 == 0 ? Rock.Darkened(0.12f) : Rock).Lerp(new Color(0.2f, 0.18f, 0.16f), i % 4 == 1 ? 0.35f : 0f);
            var at = new Vector3(MathF.Cos(a) * 1.15f, -0.04f, MathF.Sin(a) * 1.15f);
            m.Prism(at, r, r * 0.85f, 0.16f, 7, c).Prism(at + Vector3.Up * 0.16f, r * 0.85f, r * 0.4f, 0.12f, 7, c.Lightened(0.05f));
        }
        m.Prism(new Vector3(0, -0.02f, 0), 0.95f, 0.85f, 0.05f, 12, new Color(0.34f, 0.32f, 0.3f));
        foreach ((float x, float z) in new[] { (0.2f, 0.1f), (-0.25f, 0.2f), (0.05f, -0.3f), (-0.1f, -0.05f), (0.35f, -0.2f) })
            m.Prism(new Vector3(x, 0.02f, z), 0.09f, 0.05f, 0.04f, 5, new Color(1.3f, 0.45f, 0.12f));
        Color charred = new(0.16f, 0.11f, 0.08f), bark = new(0.36f, 0.26f, 0.17f);
        // Three logs laid star-wise, their inner ends burnt black.
        foreach (float a in new[] { 0.3f, 2.4f, 4.4f })
        {
            Vector3 outer = new(MathF.Cos(a) * 0.95f, 0.1f, MathF.Sin(a) * 0.95f), inner = new(MathF.Cos(a) * 0.15f, 0.2f, MathF.Sin(a) * 0.15f);
            m.Cylinder(outer, outer.Lerp(inner, 0.55f), 0.08f, 0.08f, 7, bark, new Color(0.7f, 0.55f, 0.36f));
            m.Cylinder(outer.Lerp(inner, 0.55f), inner, 0.08f, 0.06f, 7, charred);
        }
        // Hides pegged out to dry and a heap of firewood beside the hearth.
        m.Box(new Vector3(2.6f, 0.03f, 1.2f), new Vector3(1.6f, 0.04f, 1.1f), new Color(0.55f, 0.42f, 0.3f), 0.4f);
        for (int i = 0; i < 4; i++) m.Beam(new Vector3(-2.4f, 0.12f + i * 0.1f, 1.4f - i * 0.15f), new Vector3(-1.4f, 0.12f + i * 0.1f, 2f - i * 0.1f), 0.07f, new Color(0.42f, 0.3f, 0.2f));
        return m.Build();
    }

    public static ArrayMesh TunnelPortal()
    {
        var m = new MeshKit();
        Color stone = new(0.5f, 0.47f, 0.42f), dark = new(0.04f, 0.04f, 0.05f);
        m.Box(new Vector3(-3f, 3.5f, 0), new Vector3(6f, 7f, 11f), stone.Darkened(0.15f));
        m.Box(new Vector3(0.05f, 2.6f, 0), new Vector3(0.1f, 5.2f, 4.4f), dark);
        m.Box(new Vector3(0.2f, 5.9f, 0), new Vector3(0.4f, 1.4f, 6.4f), stone).Box(new Vector3(0.2f, 3f, 2.9f), new Vector3(0.4f, 6f, 1.4f), stone).Box(new Vector3(0.2f, 3f, -2.9f), new Vector3(0.4f, 6f, 1.4f), stone);
        return m.Build();
    }

    /// <summary>A steam train facing +X: locomotive and tender at the front, three carriages behind; about 40 m long.</summary>
    public static ArrayMesh Train()
    {
        var m = new MeshKit();
        Color black = new(0.12f, 0.12f, 0.13f), red = new(0.45f, 0.12f, 0.1f), green = new(0.16f, 0.3f, 0.22f), roof = new(0.25f, 0.25f, 0.27f), brass = new(0.7f, 0.58f, 0.3f);
        // Locomotive: frame, boiler, smokebox and chimney, cab.
        m.Box(new Vector3(0, 0.9f, 0), new Vector3(9f, 0.5f, 2.6f), black);
        m.Box(new Vector3(0.8f, 2.0f, 0), new Vector3(6f, 1.8f, 1.8f), green).Box(new Vector3(4f, 2.0f, 0), new Vector3(0.8f, 1.9f, 1.9f), black);
        m.Box(new Vector3(3.3f, 3.4f, 0), new Vector3(0.6f, 1.2f, 0.6f), black).Box(new Vector3(1f, 3.05f, 0), new Vector3(0.6f, 0.5f, 0.6f), brass);
        m.Box(new Vector3(-3.2f, 2.5f, 0), new Vector3(2.4f, 2.8f, 2.7f), green).Box(new Vector3(-3.2f, 4f, 0), new Vector3(2.8f, 0.2f, 3f), roof);
        m.Box(new Vector3(-6.2f, 1.7f, 0), new Vector3(3.4f, 1.8f, 2.5f), black);
        for (int c = 0; c < 3; c++)
        {
            float x = -12.6f - c * 9.6f;
            m.Box(new Vector3(x, 0.8f, 0), new Vector3(9f, 0.4f, 2.4f), black);
            m.Box(new Vector3(x, 2.2f, 0), new Vector3(9f, 2.4f, 2.8f), red).Box(new Vector3(x, 3.5f, 0), new Vector3(9.2f, 0.25f, 3f), roof);
            for (int w = 0; w < 5; w++)
                foreach (float z in new[] { -1.42f, 1.42f }) m.Box(new Vector3(x - 3.4f + w * 1.7f, 2.5f, z), new Vector3(1f, 0.8f, 0.04f), new Color(0.75f, 0.7f, 0.5f));
        }
        return m.Build();
    }

    /// <summary>A flame: two crossed upright quads, bright yellow at the base fading to nothing at the top (drawn additively).</summary>
    public static ArrayMesh Flame()
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var hot = new Color(1.6f, 1.15f, 0.4f);
        var mid = new Color(1.4f, 0.45f, 0.08f);
        for (int plane = 0; plane < 2; plane++)
        {
            Vector3 side = plane == 0 ? new Vector3(1.6f, 0, 0) : new Vector3(0, 0, 1.6f);
            Vector3 up = new(0, 2.6f, 0), top = new(0, 5.2f, 0);
            Vector3[] quad = { -side, side, side + up, -side, side + up, -side + up, -side * 0.7f + up, side * 0.7f + up, top };
            Color[] tint = { hot, hot, mid, hot, mid, mid, mid, mid, Colors.Black };
            vertices.AddRange(quad);
            colors.AddRange(tint);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = vertices.ConvertAll(_ => Vector3.Up).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A pool of firelight on the ground before a door: a thin disc drawn additively at night.</summary>
    public static ArrayMesh Glow()
    {
        // A fan bright at the centre and black (no light added) at the rim, so the pool has a soft edge.
        const int sides = 16;
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            vertices.Add(Vector3.Zero); colors.Add(Colors.White);
            vertices.Add(new Vector3(MathF.Cos(a1) * 1.9f, 0, MathF.Sin(a1) * 1.9f)); colors.Add(Colors.Black);
            vertices.Add(new Vector3(MathF.Cos(a0) * 1.9f, 0, MathF.Sin(a0) * 1.9f)); colors.Add(Colors.Black);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = vertices.ConvertAll(_ => Vector3.Up).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
