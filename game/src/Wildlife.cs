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
    private bool _frozen, _night;
    private Flock _geese;
    /// <summary>A hunt in the wild years: the first deer group is drawn to the hunt, flees when the hunters strike, and one may fall.</summary>
    private bool _hunt, _huntStruck, _deerDown;
    private (float X, float Z)[] _deerHomes;
    /// <summary>At night: a lantern on each fishing boat and a pair of lamps on each cart or lorry (additive points).</summary>
    private MultiMesh _lights;

    /// <summary>The season (0 spring ... 3 winter): geese pass over in spring and autumn.</summary>
    public int Season { get; set; }

    /// <summary>The street tile being dug for mains (from the sim), or -1: traffic keeps out of the trench.</summary>
    public int DigTile { get; set; } = -1;

    /// <summary>Night: boats lie at their moorings off the village and only a few vehicles are about.</summary>
    public bool Night
    {
        set
        {
            if (value == _night) return;
            _night = value;
            ApplyCounts();
        }
    }

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
        // A skein of geese in a V, crossing high over the estuary in spring (inland) and autumn (out to sea).
        _geese = AddFlock(MeshKit.Gull(), 11, 1);
        _geese.Mesh.VisibleInstanceCount = 0;
        _lights = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = MeshKit.Spark(), InstanceCount = 5 + 2 * 24, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _lights,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
        });
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
            int traffic = (4 + _huts / 5) * (_night ? 3 : 10) / 10;
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

    public void SetHunt(bool on, float x, float z, bool struck, bool won)
    {
        if (_herds.Length < 4) return;
        Flock deer = _herds[3];
        int group = deer.Agents.Length / 2;
        if (_deerHomes == null)
        {
            _deerHomes = new (float, float)[deer.Agents.Length];
            for (int i = 0; i < deer.Agents.Length; i++) _deerHomes[i] = (deer.Agents[i].HomeX, deer.Agents[i].HomeZ);
        }
        float ax = x - _centerX, az = z - _centerZ, al = MathF.Max(1f, MathF.Sqrt(ax * ax + az * az));
        (ax, az) = (ax / al, az / al);
        if (on && !_hunt)
        {
            // The deer graze 18 m beyond where the hunters gather, on the far side from the camp.
            (float hx, float hz) = (x + ax * 18, z + az * 18);
            for (int i = 0; i < group; i++)
            {
                ref Agent a = ref deer.Agents[i];
                (float px, float pz) = LandNear(hx, hz, 7f);
                (a.X, a.Z, a.TX, a.TZ, a.HomeX, a.HomeZ) = (px, pz, px, pz, hx, hz);
                a.Wait = R(0, 5);
                // The deer that will fall grazes apart, just beyond the hunters' ring, so it drops where it stands.
                if (i == 0 && won)
                {
                    (px, pz) = LandNear(x + ax * 8, z + az * 8, 2f);
                    (a.X, a.Z, a.TX, a.TZ, a.HomeX, a.HomeZ, a.Wait) = (px, pz, px, pz, px, pz, 1e6f);
                }
            }
        }
        if (on && struck && !_huntStruck)
        {
            // Break cover: the herd bolts away from the hunters; if the hunt is won, one deer is down.
            (float fx, float fz) = (x + ax * 70, z + az * 70);
            deer.Speed = 6f;
            for (int i = won ? 1 : 0; i < group; i++)
            {
                ref Agent a = ref deer.Agents[i];
                (a.HomeX, a.HomeZ) = (fx, fz);
                (a.TX, a.TZ) = LandNear(fx, fz, 8f);
                a.Wait = 2;
            }
            _deerDown = won;
        }
        if (!on && _hunt)
        {
            deer.Speed = 1.7f;
            _deerDown = false;
            for (int i = 0; i < group; i++) (deer.Agents[i].HomeX, deer.Agents[i].HomeZ) = _deerHomes[i];
            deer.Agents[0].Wait = 0;
        }
        _hunt = on;
        _huntStruck = on && struck;
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
        StepGeese();
        StepTrader(delta);
        StepTraffic(_carts, delta, tiles);
        StepTraffic(_lorries, delta, tiles);
        StepLights();
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
            // The deer brought down in a hunt lies on its side where it fell until the hunt is over.
            if (_deerDown && i == 0 && _herds.Length > 3 && flock == _herds[3])
            {
                (a.TX, a.TZ, a.Moving) = (a.X, a.Z, 0);
                WriteLying(flock.Buffer, i, flock.Scale, a.Yaw, a.X, _map.Height(a.X, a.Z) + 0.25f * flock.Scale, a.Z);
                continue;
            }
            float dx = a.TX - a.X, dz = a.TZ - a.Z, distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < 0.2f)
            {
                a.Moving = 0;
                a.Wait -= delta;
                if (a.Wait <= 0)
                {
                    // At night the farm animals bunch up close round their resting place and stand longer between steps.
                    bool folded = _night && _herds.Length > 3 && flock != _herds[3];
                    (float x, float z) = LandNear(a.HomeX, a.HomeZ, folded ? flock.Roam * 0.25f : flock.Roam);
                    if (Open(tiles, x, z)) (a.TX, a.TZ) = (x, z);
                    a.Wait = R(3, 13) * (folded ? 3f : 1f);
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

    private void StepLights()
    {
        if (!_night)
        {
            _lights.VisibleInstanceCount = 0;
            return;
        }
        int n = 0;
        var lantern = new Color(1f, 0.72f, 0.3f);
        for (int i = 0; i < _boats.Mesh.VisibleInstanceCount && n < _lights.InstanceCount; i++)
        {
            ref Agent b = ref _boats.Agents[i];
            _lights.SetInstanceTransform(n, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.9f), new Vector3(b.X, 2.6f, b.Z)));
            _lights.SetInstanceColor(n++, lantern);
        }
        for (int which = 0; which < 2; which++)
        {
            Flock f = which == 0 ? _carts : _lorries;
            bool lorry = which == 1;
            var lamp = lorry ? new Color(1f, 0.95f, 0.8f) : new Color(1f, 0.7f, 0.35f);
            for (int i = 0; i < f.Mesh.VisibleInstanceCount && n + 1 < _lights.InstanceCount; i++)
            {
                ref Agent a = ref f.Agents[i];
                if (a.TX < 0) continue;
                var forward = new Vector3(MathF.Sin(a.Yaw), 0, MathF.Cos(a.Yaw));
                var right = new Vector3(forward.Z, 0, -forward.X);
                float y = _map.Height(a.HomeX, a.HomeZ) + (lorry ? 0.9f : 1.6f);
                var nose = new Vector3(a.HomeX, y, a.HomeZ) + forward * (lorry ? 2.65f : 2.5f);
                for (int side = -1; side <= 1; side += 2)
                {
                    _lights.SetInstanceTransform(n, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.55f), nose + right * (0.7f * side)));
                    _lights.SetInstanceColor(n++, lamp);
                }
            }
        }
        _lights.VisibleInstanceCount = n;
    }

    /// <summary>
    /// Every 70 s in spring and autumn a V of geese crosses the map in about 35 s, 32 m up, passing near the village.
    /// Spring skeins fly inland (west) and autumn ones out to sea (east).
    /// </summary>
    private void StepGeese()
    {
        if (Season is not (0 or 2))
        {
            _geese.Mesh.VisibleInstanceCount = 0;
            return;
        }
        // Offset so a skein is already near the village a few seconds after start.
        float t = (_clock + 15f) % 70f;
        if (t > 35f)
        {
            _geese.Mesh.VisibleInstanceCount = 0;
            return;
        }
        int pass = (int)((_clock + 15f) / 70f);
        float sign = Season == 0 ? -1f : 1f, slant = MathF.Sin(pass * 1.9f) * 0.35f;
        var dir = new Vector2(sign, slant).Normalized();
        var lead = new Vector2(_centerX, _centerZ + MathF.Cos(pass * 2.7f) * 40f) + dir * Mathf.Lerp(-260f, 260f, t / 35f);
        float heading = MathF.Atan2(dir.X, dir.Y);
        var back = -dir;
        var across = new Vector2(-dir.Y, dir.X);
        for (int i = 0; i < _geese.Agents.Length; i++)
        {
            // Leader first, then alternate arms of the V, each bird a little behind and out to the side.
            int rank = (i + 1) / 2, arm = i % 2 == 0 ? 1 : -1;
            var p = lead + back * (rank * 5f) + across * (arm * rank * 4f);
            float bob = MathF.Sin(_clock * 1.3f + i) * 0.4f;
            Write(_geese.Buffer, i, 3f, heading, p.X, 32f + bob, p.Y, 1, i * 0.37f);
        }
        _geese.Mesh.VisibleInstanceCount = _geese.Agents.Length;
        _geese.Mesh.Buffer = _geese.Buffer;
    }

    private void StepGulls(float delta)
    {
        int boats = _boats.Mesh.VisibleInstanceCount;
        for (int i = 0; i < _gulls.Agents.Length; i++)
        {
            ref Agent g = ref _gulls.Agents[i];
            // X/Z keep each gull's own haunt. By day a third of the flock trails the fishing boats, wheeling low over
            // one boat each; by night, or with no boats out, they drift back to their haunts.
            if (g.X == 0 && g.Z == 0) (g.X, g.Z) = (g.HomeX, g.HomeZ);
            bool follow = !_night && boats > 0 && i % 3 == 0;
            float tx = follow ? _boats.Agents[i / 3 % boats].X : g.X, tz = follow ? _boats.Agents[i / 3 % boats].Z : g.Z;
            float ease = Math.Min(1f, delta * (follow ? 0.6f : 0.15f));
            g.HomeX += (tx - g.HomeX) * ease;
            g.HomeZ += (tz - g.HomeZ) * ease;
            g.Yaw += g.TZ * delta;
            float r = follow ? g.TX * 0.4f : g.TX;
            float x = g.HomeX + MathF.Cos(g.Yaw) * r, z = g.HomeZ + MathF.Sin(g.Yaw) * r;
            float heading = g.TZ > 0 ? -g.Yaw : -g.Yaw + MathF.PI;
            float height = (follow ? g.Wait * 0.6f : g.Wait) + MathF.Sin(_clock * 0.3f + g.Phase) * 1.6f;
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
                // After a failed placement, wait a couple of seconds before searching again.
                if (a.Wait > 0)
                {
                    a.Wait -= delta;
                    continue;
                }
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
                if (start < 0)
                {
                    a.Wait = 2f;
                    continue;
                }
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
                for (int k = 0; k < 4; k++)
                {
                    bool inside = k switch { 0 => tx > 0, 1 => tx < nx - 1, 2 => tz > 0, _ => tz < Rules.TileNz - 1 };
                    int side = k switch { 0 => target - 1, 1 => target + 1, 2 => target - nx, _ => target + nx };
                    if (inside && tiles[side] == TileKind.Road && side != back && side != DigTile && R() * ++count < 1f) next = side;
                }
                if (next < 0) next = back >= 0 && tiles[back] == TileKind.Road ? back : -1;
                a.TZ = target;
                a.TX = next;
                continue;
            }
            float step = Math.Min(dist, f.Speed * delta);
            a.X += dx / dist * step;
            a.Z += dz / dist * step;
            // Turn smoothly into the new street instead of snapping round at the tile centre.
            a.Yaw = LerpAngle(a.Yaw, MathF.Atan2(dx, dz), Math.Min(1f, delta * 5f));
            // Keep to the right-hand side of the street.
            float lane = 0.9f, x = a.X + MathF.Cos(a.Yaw) * -lane, z = a.Z - MathF.Sin(a.Yaw) * -lane;
            (a.HomeX, a.HomeZ) = (x, z);
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
            float side = MathF.Sign(_centerZ - _map.RiverCenter(_centerX));
            float moving = 1, heading = b.TX > 0 ? MathF.PI / 2 : -MathF.PI / 2;
            if (_night)
            {
                // Home to a mooring off the village, in a row along the bank, and lie still till morning.
                float mooring = _centerX + 10f + i * 7f;
                float gap = mooring - b.X;
                if (MathF.Abs(gap) > 0.3f)
                {
                    b.TX = MathF.Sign(gap);
                    b.X += b.TX * Math.Min(MathF.Abs(gap), 2.3f * delta);
                    heading = b.TX > 0 ? MathF.PI / 2 : -MathF.PI / 2;
                }
                else moving = 0;
                // Drift across to the village side of the channel on the way in.
                b.HomeZ = Mathf.MoveToward(b.HomeZ, side * 0.85f, delta * 0.05f);
            }
            else
            {
                // Each boat keeps its own pace, so the fleet spreads out again after mooring together.
                b.X += b.TX * (1.9f + (i * 0.37f) % 1f * 0.8f) * delta;
                if (b.X > far) b.TX = -1;
                if (b.X < near) b.TX = 1;
                // By day each boat drifts back to its own fishing lane.
                float lane0 = ((i * 0.37f) % 1f) - 0.5f;
                b.HomeZ = Mathf.MoveToward(b.HomeZ, lane0, delta * 0.03f);
            }
            // Boats keep to the channel, inside its width at this point.
            float lane = (10f + WorldMap.EstuaryWidth(b.X) * 0.8f) * b.HomeZ;
            float z = _map.RiverCenter(b.X) + lane;
            b.Z = z;
            b.Yaw = heading;
            Write(_boats.Buffer, i, 1.15f, heading, b.X, 0.03f - _boatDraft, z, moving, b.Phase);
        }
        _boats.Mesh.Buffer = _boats.Buffer;
    }

    private static float LerpAngle(float from, float to, float t)
    {
        float difference = (to - from + MathF.PI) % MathF.Tau;
        if (difference < 0) difference += MathF.Tau;
        return from + (difference - MathF.PI) * t;
    }

    /// <summary>An animal lying on its side: turned a quarter about its own length (yaw, then roll 90 degrees), legs still.</summary>
    private static void WriteLying(float[] b, int index, float s, float yaw, float x, float y, float z)
    {
        int n = index * Floats;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        b[n] = 0; b[n + 1] = -cos * s; b[n + 2] = sin * s; b[n + 3] = x;
        b[n + 4] = s; b[n + 5] = 0; b[n + 6] = 0; b[n + 7] = y;
        b[n + 8] = 0; b[n + 9] = sin * s; b[n + 10] = cos * s; b[n + 11] = z;
        b[n + 12] = 1; b[n + 13] = 1; b[n + 14] = 1; b[n + 15] = 1;
        b[n + 16] = 0; b[n + 17] = 0; b[n + 18] = 0; b[n + 19] = 0;
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
