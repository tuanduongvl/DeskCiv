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
        if (tier >= 3 && type is BType.Woodcutter or BType.Fisher or BType.Gatherer or BType.Bakery or BType.Mill or BType.Winery or BType.Smithy
            && LoadModel("workshop_t3") is ArrayMesh workshop) return workshop;
        // Models are named <type>_t<tier>. An era without its own model borrows the nearest era's, preferring the older one.
        string name = type.ToString().ToLowerInvariant();
        for (int distance = 0; distance <= 4; distance++)
            foreach (int t in new[] { tier - distance, tier + distance })
                if (t is >= 0 and <= 4 && LoadModel($"{name}_t{t}") is ArrayMesh mesh) return mesh;
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
                if (!m.TryKit("prop_pot", new Vector3(1.6f, 0, 2.1f))) m.Prism(new Vector3(1.6f, 0, 2.1f), 0.48f, 0.58f, 0.62f, 7, new Color(0.62f, 0.5f, 0.3f));
                break;
            case BType.Gatherer:
                m.Beam(new Vector3(-1.7f, 0, -1f), new Vector3(0, 2.1f, -0.2f), 0.06f, pole).Beam(new Vector3(1.7f, 0, -1f), new Vector3(0, 2.1f, -0.2f), 0.06f, pole);
                m.Box(new Vector3(0, 1.05f, -0.75f), new Vector3(2.7f, 0.08f, 1.9f), hide);
                m.Prism(new Vector3(-1.1f, 0, 1.2f), 0.42f, 0.56f, 0.56f, 8, new Color(0.62f, 0.5f, 0.3f)).Prism(new Vector3(-1.1f, 0.48f, 1.2f), 0.48f, 0.1f, 0.2f, 7, new Color(0.62f, 0.16f, 0.28f));
                m.Prism(new Vector3(1.1f, 0, 1.2f), 0.38f, 0.5f, 0.5f, 8, new Color(0.62f, 0.5f, 0.3f)).Prism(new Vector3(1.1f, 0.43f, 1.2f), 0.42f, 0.1f, 0.18f, 7, new Color(0.3f, 0.22f, 0.5f));
                break;
            case BType.Fisher:
                foreach (float x in new[] { -1.8f, 1.8f }) m.Beam(new Vector3(x, 0, 0), new Vector3(x, 2.1f, 0), 0.07f, pole);
                m.Beam(new Vector3(-2f, 2.05f, 0), new Vector3(2f, 2.05f, 0), 0.06f, pole);
                for (int i = 0; i < 6; i++) m.Prism(new Vector3(-1.35f + i * 0.54f, 1.4f, 0), 0.05f, 0.13f, 0.56f, 5, new Color(0.56f, 0.66f, 0.72f));
                if (!m.TryKit("prop_canoe", new Vector3(0.3f, 0, 2.3f))) m.Box(new Vector3(0.3f, 0.2f, 2.3f), new Vector3(3.6f, 0.36f, 0.86f), new Color(0.36f, 0.25f, 0.15f));
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
                for (int i = 0; i < 3; i++) m.Box(new Vector3(-0.2f + i * 0.2f, 1.08f + (i == 1 ? 0.14f : 0), 0.42f), new Vector3(0.19f, 0.19f, 1.25f), new Color(0.5f, 0.34f, 0.2f));
                break;
            case Res.Stone:
                m.Box(hands, new Vector3(0.52f, 0.34f, 0.42f), Rock).Box(hands + new Vector3(0.08f, 0.24f, 0), new Vector3(0.3f, 0.18f, 0.28f), Rock.Lightened(0.1f));
                break;
            case Res.Berries:
                m.Prism(hands + Vector3.Down * 0.18f, 0.22f, 0.3f, 0.3f, 9, Thatch).Prism(hands + Vector3.Up * 0.1f, 0.27f, 0.08f, 0.13f, 8, new Color(0.66f, 0.17f, 0.32f));
                break;
            case Res.Fish:
                m.Box(hands, new Vector3(0.5f, 0.1f, 0.34f), new Color(0.48f, 0.34f, 0.2f));
                for (int i = 0; i < 3; i++) m.Box(hands + new Vector3(-0.15f + i * 0.15f, 0.1f, 0), new Vector3(0.11f, 0.1f, 0.5f), new Color(0.55f, 0.69f, 0.76f));
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
        var m = new MeshKit();
        Color white = new(0.95f, 0.95f, 0.92f), grey = new(0.62f, 0.65f, 0.68f);
        m.Box(Vector3.Zero, new Vector3(0.16f, 0.14f, 0.62f), white).Box(new Vector3(0, 0.02f, 0.36f), new Vector3(0.05f, 0.05f, 0.14f), new Color(0.92f, 0.66f, 0.2f));
        foreach (int s in new[] { -1, 1 }) m.Box(new Vector3(s * 0.56f, 0, 0), new Vector3(1.0f, 0.03f, 0.3f), s > 0 ? grey : grey.Lightened(0.05f));
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
        if (LoadKit("scatter_flowers") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        Color[] petals = { new(0.95f, 0.86f, 0.32f), new(0.92f, 0.92f, 0.88f), new(0.78f, 0.42f, 0.72f) };
        for (int i = 0; i < 4; i++)
        {
            float a = i * 1.9f, r = 0.12f + i * 0.08f;
            var p = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            m.Prism(p, 0.025f, 0.018f, 0.34f, 3, new Color(0.3f, 0.5f, 0.22f));
            m.Prism(p + Vector3.Up * 0.33f, 0.1f, 0.13f, 0.06f, 5, petals[i % 3]);
        }
        return m.Build();
    }

    public static ArrayMesh Pebbles()
    {
        if (LoadKit("scatter_pebbles") is ArrayMesh kit) return kit;
        var m = new MeshKit();
        m.Prism(Vector3.Zero, 0.46f, 0.27f, 0.3f, 6, Rock).Prism(new Vector3(0.52f, 0, 0.2f), 0.22f, 0.12f, 0.16f, 5, Rock.Darkened(0.14f));
        return m.Build();
    }

    public static ArrayMesh Puff()
    {
        var m = new MeshKit();
        m.Prism(Vector3.Down * 0.5f, 0.35f, 0.72f, 0.5f, 7, Colors.White);
        m.Prism(Vector3.Zero, 0.72f, 0.3f, 0.5f, 7, Colors.White);
        return m.Build();
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
