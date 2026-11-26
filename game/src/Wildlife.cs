using System;
using DeskCiv.Sim;
using Godot;

namespace DeskCiv;

/// <summary>Decorative animals, birds and boats. They run on the render side and never affect the simulation.</summary>
public partial class Wildlife : Node3D
{
    private const string Shader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled;
uniform int style = 0;
uniform float leg_top = 0.4;
uniform float body_z = 0.0;
uniform float body_len = 1.0;
uniform sampler2D albedo : source_color, hint_default_white, filter_linear_mipmap;
void vertex() {
    float t = TIME + INSTANCE_CUSTOM.y;
    if (style == 0) {
        // Four-legged walk: diagonal leg pairs swing in turn about the hips while the animal moves.
        float moving = INSTANCE_CUSTOM.x;
        if (VERTEX.y < leg_top) {
            float pair = sign(VERTEX.x + 0.001) * sign(VERTEX.z - body_z + 0.001);
            float swing = sin(t * 7.0 + (pair > 0.0 ? 0.0 : 3.14159)) * 0.42 * moving;
            float below = leg_top - VERTEX.y;
            VERTEX.z += below * sin(swing);
            VERTEX.y = leg_top - below * cos(swing);
        }
        VERTEX.y += abs(sin(t * 7.0)) * 0.03 * moving;
        // A slow head nod while grazing.
        float head = smoothstep(body_z + 0.25 * body_len, body_z + 0.5 * body_len, VERTEX.z);
        VERTEX.y -= head * (1.0 - moving) * (0.5 + 0.5 * sin(t * 0.7)) * 0.12 * body_len;
    } else if (style == 1) {
        VERTEX.y += abs(VERTEX.x) * sin(t * 5.5) * 0.55;
    } else if (style == 2) {
        float a = sin(t * 0.9) * 0.05;
        VERTEX.y += VERTEX.x * a + sin(t * 0.7) * 0.07;
    }
}
void fragment() { ALBEDO = COLOR.rgb * texture(albedo, UV).rgb; }";

    private const int Floats = 20;

    private struct Agent
    {
        public float X, Z, TX, TZ, HomeX, HomeZ, Wait, Yaw, Phase, Moving;
    }

    private sealed class Flock
    {
        public MultiMesh Mesh;
        public ShaderMaterial Material;
        public Agent[] Agents;
        public float[] Buffer;
        public float Speed, Roam, Scale;
    }

    private readonly WorldMap _map;
    private readonly float _centerX, _centerZ;
    private readonly Random _random;
    private Flock[] _herds = Array.Empty<Flock>();
    private Flock _gulls, _boats, _trader, _skaters, _carts, _lorries;
    /// <summary>True while a trading ship visits (from the simulation); it sails in, anchors off the village, then leaves.</summary>
    public bool TraderVisiting { get; set; }
    private bool _frozen;

    /// <summary>The river is frozen (harsh winter): the fishing boats stay ashore.</summary>
    public bool Frozen
    {
        set
        {
            if (value == _frozen) return;
            _frozen = value;
            ApplyCounts();
        }
    }
    private float _boatDraft;
    private float _clock;

    public Wildlife() { }

    public Wildlife(WorldMap map, float centerX, float centerZ)
    {
        _map = map;
        _centerX = centerX;
        _centerZ = centerZ;
        _random = new Random(map.Seed ^ 0x51ED);
    }

    public override void _Ready()
    {
        _herds = new[]
        {
            Herd(Real("animal_sheep") ?? MeshKit.Sheep(), 30, 3, 0.9f, 13f, 1.1f),
            Herd(Real("animal_cow") ?? MeshKit.Cow(), 12, 2, 0.75f, 16f, 1.15f),
            Herd(Real("animal_chicken") ?? MeshKit.Chicken(), 22, 4, 1.05f, 6f, 1f),
            Herd(Real("animal_deer") ?? MeshKit.Deer(), 9, 2, 1.7f, 26f, 1.1f, 130f),
        };
        _gulls = AddFlock(MeshKit.Gull(), 18, 1);
        for (int i = 0; i < _gulls.Agents.Length; i++)
        {
            ref Agent g = ref _gulls.Agents[i];
            g.HomeX = Mathf.Lerp(_centerX, _map.CoastX(_centerZ) + 50, R());
            g.HomeZ = _map.RiverCenter(g.HomeX) + R(-55, 55);
            g.TX = R(12, 34);
            g.TZ = R(0.16f, 0.34f) * (R() < 0.5f ? -1 : 1);
            g.Yaw = R(0, MathF.Tau);
            g.Wait = R(12, 21);
            g.Phase = R(0, 10);
        }
        ArrayMesh realBoat = Real("boat");
        _boatDraft = realBoat != null ? 0.55f : 0f; // the generated hull has its keel at y = 0
        _boats = AddFlock(realBoat ?? MeshKit.Boat(), 5, 2);
        _trader = AddFlock(realBoat ?? MeshKit.Boat(), 1, 2);
        _trader.Agents[0].X = _map.CoastX(_centerZ) + 175;
        _trader.Mesh.VisibleInstanceCount = 0;
        // Skaters circle on the frozen river off the village in a harsh winter.
        _skaters = AddFlock(MeshKit.Skater(), 8, 2);
        // Horse carts from the Middle Ages and lorries in the 20th century drive along the streets (style 3: rigid).
        _carts = AddFlock(MeshKit.Cart(), 16, 3);
        _lorries = AddFlock(MeshKit.Lorry(), 24, 3);
        _carts.Speed = 2.6f;
        _lorries.Speed = 6.5f;
        foreach (Flock f in new[] { _carts, _lorries })
        {
            f.Mesh.VisibleInstanceCount = 0;
            for (int i = 0; i < f.Agents.Length; i++) f.Agents[i].TX = -1;
        }
        _skaters.Mesh.VisibleInstanceCount = 0;
        for (int i = 0; i < _skaters.Agents.Length; i++)
        {
            ref Agent s = ref _skaters.Agents[i];
            s.TX = R(5f, 15f);
            s.TZ = R(0.25f, 0.5f) * (R() < 0.5f ? -1 : 1);
            s.Phase = R(0, MathF.Tau);
            s.HomeX = _centerX + R(-10f, 30f);
        }
        for (int i = 0; i < _boats.Agents.Length; i++)
        {
            ref Agent b = ref _boats.Agents[i];
            b.X = Mathf.Lerp(_centerX - 40, _map.CoastX(_centerZ) + 110, R());
            b.TX = R() < 0.5f ? -1 : 1;
            b.HomeZ = R(-0.5f, 0.5f);
            b.Phase = R(0, 10);
        }
    }

    private int _era, _fields, _fishers, _huts;

    /// <summary>Farm animals appear with farming and sailing boats with the Bronze Age. Deer and gulls are always wild.</summary>
    public void SetEra(int era)
    {
        _era = era;
        ApplyCounts();
    }

    /// <summary>Herds grow with the fields and homes, and the fishing fleet with the fishers' huts.</summary>
    public void SetVillage(int fields, int fishers, int huts)
    {
        if (fields == _fields && fishers == _fishers && huts == _huts) return;
        (_fields, _fishers, _huts) = (fields, fishers, huts);
        ApplyCounts();
    }

    private void ApplyCounts()
    {
        if (_gulls == null) return;
        bool farming = _era >= 1;
        int[] wanted = { 4 + 3 * _fields, 1 + _fields, 4 + _huts / 2 };
        for (int i = 0; i < 3; i++) _herds[i].Mesh.VisibleInstanceCount = farming ? Math.Clamp(wanted[i], 0, _herds[i].Agents.Length) : 0;
        _boats.Mesh.VisibleInstanceCount = _era >= 2 && !_frozen ? Math.Clamp(_fishers, 1, _boats.Agents.Length) : 0;
        if (_skaters != null) _skaters.Mesh.VisibleInstanceCount = _frozen ? _skaters.Agents.Length : 0;
        if (_carts != null)
        {
            int traffic = 4 + _huts / 5;
            _carts.Mesh.VisibleInstanceCount = _era is >= 4 and < 7 ? Math.Min(traffic, _carts.Agents.Length) : 0;
            _lorries.Mesh.VisibleInstanceCount = _era >= 7 ? Math.Min(traffic, _lorries.Agents.Length) : 0;
        }
    }

    /// <summary>Texture and leg layout of a herd or boat model.</summary>
    private static void ApplyLook(ShaderMaterial material, Mesh mesh)
    {
        // Legs are the lowest 45% of an animal; the head is at the +Z end.
        Aabb box = mesh.GetAabb();
        material.SetShaderParameter("leg_top", box.Position.Y + box.Size.Y * 0.45f);
        material.SetShaderParameter("body_z", box.Position.Z + box.Size.Z * 0.5f);
        material.SetShaderParameter("body_len", box.Size.Z);
        Texture2D texture = mesh.GetSurfaceCount() > 0 && mesh.SurfaceGetMaterial(0) is StandardMaterial3D textured ? textured.AlbedoTexture : null;
        material.SetShaderParameter("albedo", texture);
    }

    /// <summary>Swaps in regenerated animal and boat models while running (live reload).</summary>
    public void ReloadModels()
    {
        if (_herds.Length < 4 || _boats == null) return;
        Mesh[] meshes =
        {
            Real("animal_sheep") ?? MeshKit.Sheep(), Real("animal_cow") ?? MeshKit.Cow(),
            Real("animal_chicken") ?? MeshKit.Chicken(), Real("animal_deer") ?? MeshKit.Deer(),
        };
        for (int i = 0; i < 4; i++)
        {
            _herds[i].Mesh.Mesh = meshes[i];
            ApplyLook(_herds[i].Material, meshes[i]);
        }
        ArrayMesh boat = Real("boat");
        _boatDraft = boat != null ? 0.55f : 0f;
        _boats.Mesh.Mesh = (Mesh)boat ?? MeshKit.Boat();
        ApplyLook(_boats.Material, _boats.Mesh.Mesh);
        _trader.Mesh.Mesh = _boats.Mesh.Mesh;
        ApplyLook(_trader.Material, _trader.Mesh.Mesh);
    }

    /// <summary>A realistic generated model from assets/models, or null to keep the low-poly one.</summary>
    private static ArrayMesh Real(string name) => MeshKit.LoadModel(name);

    private float R() => (float)_random.NextDouble();
    private float R(float min, float max) => min + (max - min) * R();

    private Flock AddFlock(Mesh mesh, int count, int style)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = Shader } };
        material.SetShaderParameter("style", style);
        ApplyLook(material, mesh);
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = count };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
        });
        return new Flock { Mesh = multi, Material = material, Agents = new Agent[count], Buffer = new float[count * Floats], Scale = 1 };
    }

    private Flock Herd(Mesh mesh, int count, int groups, float speed, float roam, float scale, float distance = 75f)
    {
        Flock flock = AddFlock(mesh, count, 0);
        flock.Speed = speed;
        flock.Roam = roam;
        flock.Scale = scale;
        for (int g = 0; g < groups; g++)
        {
            (float homeX, float homeZ) = LandNear(_centerX, _centerZ, distance);
            for (int i = g * count / groups; i < (g + 1) * count / groups; i++)
            {
                (float x, float z) = LandNear(homeX, homeZ, roam);
                flock.Agents[i] = new Agent { X = x, Z = z, TX = x, TZ = z, HomeX = homeX, HomeZ = homeZ, Wait = R(0, 8), Yaw = R(0, MathF.Tau), Phase = R(0, 10) };
            }
        }
        return flock;
    }

    private (float, float) LandNear(float x, float z, float radius)
    {
        for (int i = 0; i < 40; i++)
        {
            float a = R(0, MathF.Tau), d = MathF.Sqrt(R()) * radius;
            float px = x + MathF.Cos(a) * d, pz = z + MathF.Sin(a) * d;
            if (_map.Walkable(px, pz) && _map.Height(px, pz) > 0.9f && _map.Slope(px, pz) < 0.4f) return (px, pz);
        }
        return (x, z);
    }

    private static bool Open(byte[] tiles, float x, float z)
    {
        if (tiles == null || tiles.Length == 0) return true;
        int tx = Math.Clamp((int)((x + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1);
        int tz = Math.Clamp((int)((z + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1);
        return tiles[tx + tz * Rules.TileNx] is TileKind.Free or TileKind.Road;
    }

    /// <summary>Advances all animals. The renderer calls this only while the wallpaper is visible.</summary>
    public void Step(float delta, byte[] tiles)
    {
        if (_gulls == null) return;
        delta = Math.Min(delta, 0.25f);
        _clock += delta;
        foreach (Flock herd in _herds) StepHerd(herd, delta, tiles);
        StepGulls(delta);
        StepBoats(delta);
        StepSkaters(delta);
        StepTrader(delta);
        StepTraffic(_carts, delta, tiles);
        StepTraffic(_lorries, delta, tiles);
    }

    /// <summary>A ship twice the size of the fishing boats that comes in from the sea and anchors off the village.</summary>
    private void StepTrader(float delta)
    {
        ref Agent t = ref _trader.Agents[0];
        float offshore = _map.CoastX(_centerZ) + 175, anchor = _centerX + 25;
        float target = TraderVisiting ? anchor : offshore;
        float gap = target - t.X;
        if (MathF.Abs(gap) > 0.5f) t.X += MathF.Sign(gap) * Math.Min(MathF.Abs(gap), 3f * delta);
        bool shown = TraderVisiting || t.X < offshore - 1;
        if (shown && _trader.Mesh.VisibleInstanceCount == 0) GD.Print($"DeskCiv: trading ship arriving, anchor at {anchor:F0},{_map.RiverCenter(anchor) - 6:F0}");
        _trader.Mesh.VisibleInstanceCount = shown ? 1 : 0;
        if (!shown) return;
        // Bow points the way it sails; at anchor it swings gently.
        float heading = gap < -0.5f ? -MathF.PI / 2 : MathF.PI / 2;
        if (MathF.Abs(gap) <= 0.5f) heading = -MathF.PI / 2 + MathF.Sin(_clock * 0.07f) * 0.12f;
        float z = _map.RiverCenter(t.X) - 6;
        Write(_trader.Buffer, 0, 2.2f, heading, t.X, 0.03f - _boatDraft * 2.2f / 1.15f, z, 0, 0);
        _trader.Mesh.Buffer = _trader.Buffer;
    }

    private void StepHerd(Flock flock, float delta, byte[] tiles)
    {
        for (int i = 0; i < flock.Agents.Length; i++)
        {
            ref Agent a = ref flock.Agents[i];
            float dx = a.TX - a.X, dz = a.TZ - a.Z, distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < 0.2f)
            {
                a.Moving = 0;
                a.Wait -= delta;
                if (a.Wait <= 0)
                {
                    (float x, float z) = LandNear(a.HomeX, a.HomeZ, flock.Roam);
                    if (Open(tiles, x, z)) (a.TX, a.TZ) = (x, z);
                    a.Wait = R(3, 13);
                }
            }
            else
            {
                float step = Math.Min(distance, flock.Speed * delta);
                float nx = a.X + dx / distance * step, nz = a.Z + dz / distance * step;
                if (_map.Walkable(nx, nz) && Open(tiles, nx, nz))
                {
                    (a.X, a.Z) = (nx, nz);
                    a.Yaw = LerpAngle(a.Yaw, MathF.Atan2(dx, dz), Math.Min(1, delta * 5));
                    a.Moving = 1;
                }
                else (a.TX, a.TZ) = (a.X, a.Z);
            }
            Write(flock.Buffer, i, flock.Scale, a.Yaw, a.X, _map.Height(a.X, a.Z), a.Z, a.Moving, a.Phase);
        }
        flock.Mesh.Buffer = flock.Buffer;
    }

    private void StepGulls(float delta)
    {
        for (int i = 0; i < _gulls.Agents.Length; i++)
        {
            ref Agent g = ref _gulls.Agents[i];
            g.Yaw += g.TZ * delta;
            float x = g.HomeX + MathF.Cos(g.Yaw) * g.TX, z = g.HomeZ + MathF.Sin(g.Yaw) * g.TX;
            float heading = g.TZ > 0 ? -g.Yaw : -g.Yaw + MathF.PI;
            float height = g.Wait + MathF.Sin(_clock * 0.3f + g.Phase) * 1.6f;
            Write(_gulls.Buffer, i, 1, heading, x, height, z, 1, g.Phase);
        }
        _gulls.Mesh.Buffer = _gulls.Buffer;
    }

    /// <summary>
    /// Street traffic: each vehicle drives from road tile to a neighbouring road tile, keeping to the right, and
    /// picks a new street at each junction (never straight back unless it is a dead end). TX holds the tile it is
    /// heading for and TZ the one it came from; -1 means it still has to be placed.
    /// </summary>
    private void StepTraffic(Flock f, float delta, byte[] tiles)
    {
        int shown = (int)f.Mesh.VisibleInstanceCount;
        if (shown <= 0 || tiles == null || tiles.Length == 0) return;
        int nx = Rules.TileNx;
        for (int i = 0; i < shown; i++)
        {
            ref Agent a = ref f.Agents[i];
            int target = (int)a.TX;
            if (target < 0 || target >= tiles.Length || tiles[target] != TileKind.Road)
            {
                // Place it on a random street in the town (within 120 m of the centre if one can be found).
                int start = -1;
                for (int tries = 0; tries < 400 && start < 0; tries++)
                {
                    int t = (int)(R() * tiles.Length) % tiles.Length;
                    if (tiles[t] != TileKind.Road) continue;
                    (float px, float pz) = Simulation.TileCenter(t);
                    if (tries < 300 && (px - _centerX) * (px - _centerX) + (pz - _centerZ) * (pz - _centerZ) > 120f * 120f) continue;
                    start = t;
                }
                if (start < 0) continue;
                (a.X, a.Z) = Simulation.TileCenter(start);
                a.TX = start;
                a.TZ = -1;
                target = start;
            }
            (float gx, float gz) = Simulation.TileCenter(target);
            float dx = gx - a.X, dz = gz - a.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 0.4f)
            {
                // At the tile centre: choose the next street tile.
                int tx = target % nx, tz = target / nx, back = (int)a.TZ, next = -1, count = 0;
                int[] around = { target - 1, target + 1, target - nx, target + nx };
                bool[] ok = { tx > 0, tx < nx - 1, tz > 0, tz < Rules.TileNz - 1 };
                for (int k = 0; k < 4; k++)
                    if (ok[k] && tiles[around[k]] == TileKind.Road && around[k] != back && R() * ++count < 1f) next = around[k];
                if (next < 0) next = back >= 0 && tiles[back] == TileKind.Road ? back : -1;
                a.TZ = target;
                a.TX = next;
                continue;
            }
            float step = Math.Min(dist, f.Speed * delta);
            a.X += dx / dist * step;
            a.Z += dz / dist * step;
            a.Yaw = MathF.Atan2(dx, dz);
            // Keep to the right-hand side of the street.
            float lane = 0.9f, x = a.X + MathF.Cos(a.Yaw) * -lane, z = a.Z - MathF.Sin(a.Yaw) * -lane;
            Write(f.Buffer, i, 1f, a.Yaw, x, _map.Height(x, z) + 0.05f, z, 1, 0);
        }
        f.Mesh.Buffer = f.Buffer;
    }

    /// <summary>Skaters glide in wide loops on the ice off the village, between the bank and mid-river.</summary>
    private void StepSkaters(float delta)
    {
        if (!_frozen) return;
        float side = MathF.Sign(_centerZ - _map.RiverCenter(_centerX));
        for (int i = 0; i < _skaters.Agents.Length; i++)
        {
            ref Agent s = ref _skaters.Agents[i];
            s.Phase += s.TZ * delta;
            float cz = _map.RiverCenter(s.HomeX) + side * WorldMap.EstuaryWidth(s.HomeX) * 0.35f;
            float x = s.HomeX + MathF.Cos(s.Phase) * s.TX, z = cz + MathF.Sin(s.Phase) * s.TX * 0.6f;
            // Face along the loop, the way they are going.
            float heading = MathF.Atan2(-MathF.Sin(s.Phase) * MathF.Sign(s.TZ), MathF.Cos(s.Phase) * 0.6f * MathF.Sign(s.TZ));
            Write(_skaters.Buffer, i, 1f, heading, x, 0.05f, z, 1, s.Phase);
        }
        _skaters.Mesh.Buffer = _skaters.Buffer;
    }

    private void StepBoats(float delta)
    {
        float far = _map.CoastX(_centerZ) + 135, near = Math.Max(_centerX - 90, -WorldMap.SizeX / 2 + 30);
        for (int i = 0; i < _boats.Agents.Length; i++)
        {
            ref Agent b = ref _boats.Agents[i];
            b.X += b.TX * 2.3f * delta;
            if (b.X > far) b.TX = -1;
            if (b.X < near) b.TX = 1;
            // Boats keep to the middle of the channel, inside its width at this point.
            float lane = (10f + WorldMap.EstuaryWidth(b.X) * 0.8f) * b.HomeZ;
            float z = _map.RiverCenter(b.X) + lane;
            Write(_boats.Buffer, i, 1.15f, b.TX > 0 ? MathF.PI / 2 : -MathF.PI / 2, b.X, 0.03f - _boatDraft, z, 1, b.Phase);
        }
        _boats.Mesh.Buffer = _boats.Buffer;
    }

    private static float LerpAngle(float from, float to, float t)
    {
        float difference = (to - from + MathF.PI) % MathF.Tau;
        if (difference < 0) difference += MathF.Tau;
        return from + (difference - MathF.PI) * t;
    }

    private static void Write(float[] b, int index, float s, float yaw, float x, float y, float z, float moving, float phase)
    {
        int n = index * Floats;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        b[n] = cos * s; b[n + 1] = 0; b[n + 2] = sin * s; b[n + 3] = x;
        b[n + 4] = 0; b[n + 5] = s; b[n + 6] = 0; b[n + 7] = y;
        b[n + 8] = -sin * s; b[n + 9] = 0; b[n + 10] = cos * s; b[n + 11] = z;
        b[n + 12] = 1; b[n + 13] = 1; b[n + 14] = 1; b[n + 15] = 1;
        b[n + 16] = moving; b[n + 17] = phase; b[n + 18] = 0; b[n + 19] = 0;
    }
}
