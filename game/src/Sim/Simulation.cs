using System;
using System.Collections.Generic;
using System.IO;

namespace DeskCiv.Sim;

/// <summary>Deterministic settlement across eight eras: people, seasons, production, hauling and an indirect director.</summary>
public sealed class Simulation
{
    private const float Speed = 1.7f, HungerPerSecond = 1f / 600f, StarvePerSecond = 1f / 900f, ColdPerSecond = 1f / 1800f;
    private const int CorridorX = 14, CorridorZ = 10;
    public const int WearNx = 192, WearNz = 104, MaxGraves = 300, MaxTrees = 2600;

    public readonly WorldMap Map;
    public Rng Random;
    /// <summary>Draws for cosmetic behaviour (idle wandering, path wear), kept apart so they cannot shift the economy's stream.</summary>
    public Rng Cosmetic;
    public long Tick;
    /// <summary>Where idle villagers gather until GatherUntil (a tick), and when the last gathering began.</summary>
    public float GatherX, GatherZ;
    public long GatherUntil = -1, LastGathering = -Rules.TicksPerYear;
    public readonly List<Person> People = new();
    public readonly List<Building> Buildings = new();
    public readonly List<WorldEvent> Events = new();
    public readonly List<EraRecord> History = new();
    public readonly List<Polder> Polders = new();
    public int PolderVersion;
    private readonly bool[] _ringMark = new bool[Rules.TileNx * Rules.TileNz];
    public readonly List<(float X, float Z)> Graves = new();
    public readonly long[] Produced = new long[(int)Res.Count], Consumed = new long[(int)Res.Count], Seeded = new long[(int)Res.Count];
    public readonly byte[] Wear = new byte[WearNx * WearNz];
    public float[] TreeX = Array.Empty<float>(), TreeZ = Array.Empty<float>();
    public bool[] TreeAlive = Array.Empty<bool>();
    public int TreeVersion, BuildingVersion;
    public float CenterX, CenterZ, HarvestFactor = 1, ColdFactor = 1, Support;
    public bool Night, RunComplete;
    public int Era;
    public int NextPersonId, Births, Deaths, Migrants, ActiveEvent;
    /// <summary>Road tiles planned to widen busy streets (not saved; for tests and logs).</summary>
    public int StreetsWidened;
    /// <summary>Per tile: 0 no pipes, 1 water main, 2 water main and sewer. Dug street by street from the water tower.</summary>
    public readonly byte[] Pipes = new byte[Rules.TileNx * Rules.TileNz];
    public int DigTile = -1, DigWork, MainsVersion, MainsLogged;
    public long MainsWaitUntil;
    /// <summary>A house fire: the house, when it catches, and when the fire is over. -1 when none is due.</summary>
    public int FireBuilding = -1, FiresStarted, FiresPutOut;
    /// <summary>Diagnostic: how many times a mill hand has walked in through the gate (not saved).</summary>
    public int ShiftsWorked;
    public long FireAt = -1, FireEnd = -1;
    /// <summary>The railway: one tile row from a tunnel mouth in the western hills (RailX0) to just past the station (RailX1). -1 when none.</summary>
    public int RailRow = -1, RailX0, RailX1;
    /// <summary>
    /// View-only festivities: until this tick the village celebrates at the gathering point (1 a feast, 2 a new era).
    /// Not saved and not hashed: it only drives the bonfire and fireworks.
    /// </summary>
    public long FestiveUntil = -1;
    public int FestiveKind;
    /// <summary>Newcomers waiting to arrive on the next train (they step off while it stands at the station).</summary>
    public bool MigrantsOnTrain;
    /// <summary>
    /// The first years of the Old Stone Age: a band of hunter-gatherers with no tools and no houses. They shelter in a
    /// cave they dig out of the hillside by hand, hunt deer together, pick berries and gather shellfish on the shore.
    /// Only after <see cref="WildYears"/> do they knap stone axes, fell trees and raise the first huts.
    /// </summary>
    public bool Wild;
    public const float WildYears = 6f;
    private const int WildBand = 30;
    /// <summary>The cave mouth (where people go in), the way it faces (yaw), and how far it has been dug out (0-1).</summary>
    public float CaveX, CaveZ, CaveYaw, CaveDug;
    /// <summary>The current hunt: where the deer are, until when, and whether it will bring meat home.</summary>
    public float HuntX, HuntZ;
    public long HuntUntil = -1, NextHunt;
    public bool HuntWon;
    public int Hunts, HuntsWon;
    public bool Hunting => Tick < HuntUntil;
    /// <summary>The band's fire: by the food cache at first, then 6 m in front of the cave mouth once it can be entered.</summary>
    public float CampX => CaveDug >= 0.3f ? CaveX + MathF.Sin(CaveYaw) * 6 : CenterX;
    public float CampZ => CaveDug >= 0.3f ? CaveZ + MathF.Cos(CaveYaw) * 6 : CenterZ;
    /// <summary>The moment the hunters break cover (the deer flee, one falls if the hunt is won).</summary>
    public long HuntStrike => HuntUntil - (long)(70 / Rules.TickSeconds);
    /// <summary>
    /// The train's timetable, in ticks into its 240 s round: out of the tunnel (0-400), standing at the station
    /// (400-1000), back into the tunnel (1000-1400), away (1400-2400). -1 without a railway.
    /// </summary>
    public int TrainTick => RailRow >= 0 ? (int)(Tick % 2400) : -1;
    public int MilestoneMask;
    public long FloodAt = -1, FloodEnd = -1;
    public float ToolBonus = 1, WineBonus = 1;
    public int Floods, FloodsHeld, PlanWait;
    public bool Flooding => Tick < FloodEnd;
    public readonly byte[] Tiles = new byte[Rules.TileNx * Rules.TileNz];
    public readonly List<int> RoadQueue = new();
    public int RoadVersion;

    private readonly int[] _pathCost = new int[Rules.TileNx * Rules.TileNz];
    private readonly int[] _pathFrom = new int[Rules.TileNx * Rules.TileNz];
    private readonly int[] _pathStamp = new int[Rules.TileNx * Rules.TileNz];
    private readonly PriorityQueue<int, int> _open = new();
    private int _stamp;
    private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 }, StepZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private readonly List<Person> _carriers = new(), _helpers = new();
    private readonly List<(int Priority, int Building, int Resource)> _demands = new();
    private readonly Dictionary<int, Person> _byId = new();
    private readonly Stack<int> _freeSlots = new();
    private bool _anyDead;

    public Simulation(int seed, bool populate = true)
    {
        Map = new WorldMap(seed);
        Random = new Rng(unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 11));
        Cosmetic = new Rng(unchecked((ulong)(uint)seed * 0xD1B54A32D192ED03UL + 29));
        for (int i = Rules.MaxPeople - 1; i >= 0; i--) _freeSlots.Push(i);
        GenerateTrees();
        InitTiles();
        if (populate) Found();
    }

    private void InitTiles()
    {
        for (int z = 0; z < Rules.TileNz; z++)
        for (int x = 0; x < Rules.TileNx; x++)
        {
            (float wx, float wz) = TileCenter(x + z * Rules.TileNx);
            Tiles[x + z * Rules.TileNx] = Map.Walkable(wx, wz) && Map.Slope(wx, wz) < 0.7f ? TileKind.Free : TileKind.Blocked;
        }
    }

    public static (float X, float Z) TileCenter(int tile)
    {
        int x = tile % Rules.TileNx, z = tile / Rules.TileNx;
        return ((x + 0.5f) * Rules.TileSize - WorldMap.SizeX / 2, (z + 0.5f) * Rules.TileSize - WorldMap.SizeZ / 2);
    }

    public static int TileAt(float x, float z)
    {
        int tx = Math.Clamp((int)((x + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1);
        int tz = Math.Clamp((int)((z + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1);
        return tx + tz * Rules.TileNx;
    }

    private static bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < Rules.TileNx && z < Rules.TileNz;

    /// <summary>A* over tiles. With toRoad set, it stops at the nearest road and only crosses open land.</summary>
    private int[] FindPath(int start, int goal, bool toRoad = false, int limit = 9000)
    {
        _stamp++;
        _open.Clear();
        _pathCost[start] = 0;
        _pathFrom[start] = -1;
        _pathStamp[start] = _stamp;
        _open.Enqueue(start, 0);
        int goalX = goal >= 0 ? goal % Rules.TileNx : 0, goalZ = goal >= 0 ? goal / Rules.TileNx : 0;
        for (int expansions = 0; _open.Count > 0 && expansions < limit; expansions++)
        {
            int current = _open.Dequeue();
            byte kind = Tiles[current];
            if (toRoad ? current != start && kind is TileKind.Road or TileKind.PlannedRoad : current == goal)
                return BuildPath(start, current);
            int cx = current % Rules.TileNx, cz = current / Rules.TileNx;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + StepX[d], nz = cz + StepZ[d];
                if (!InGrid(nx, nz)) continue;
                int next = nx + nz * Rules.TileNx;
                if (!CanEnter(next, goal, toRoad)) continue;
                if (d >= 4 && (!CanEnter(cx + StepX[d] + cz * Rules.TileNx, goal, toRoad) || !CanEnter(cx + (cz + StepZ[d]) * Rules.TileNx, goal, toRoad))) continue;
                int step = Tiles[next] switch { TileKind.Road => 6, TileKind.PlannedRoad => 8, _ => 12 };
                int cost = _pathCost[current] + (d >= 4 ? step * 14 / 10 : step);
                if (_pathStamp[next] == _stamp && _pathCost[next] <= cost) continue;
                _pathStamp[next] = _stamp;
                _pathCost[next] = cost;
                _pathFrom[next] = current;
                int estimate = 0;
                if (!toRoad)
                {
                    int ax = Math.Abs(nx - goalX), az = Math.Abs(nz - goalZ);
                    estimate = (Math.Max(ax, az) * 10 + Math.Min(ax, az) * 4) * 7 / 10;
                }
                _open.Enqueue(next, cost + estimate);
            }
        }
        return null;
    }

    private bool CanEnter(int tile, int goal, bool toRoad)
    {
        byte kind = Tiles[tile];
        if (toRoad) return kind is TileKind.Free or TileKind.Road or TileKind.PlannedRoad or TileKind.Rail; // streets cross the railway on the level
        return tile == goal || (kind != TileKind.Blocked && kind != TileKind.Building);
    }

    private int[] BuildPath(int start, int end)
    {
        int length = 0;
        for (int t = end; t != start; t = _pathFrom[t]) length++;
        var path = new int[length];
        for (int t = end, i = length - 1; t != start; t = _pathFrom[t], i--) path[i] = t;
        return path;
    }

    private void PlanRoad(int tile)
    {
        if (Tiles[tile] != TileKind.Free) return;
        Tiles[tile] = TileKind.PlannedRoad;
        RoadQueue.Add(tile);
        RoadVersion++;
        ClearTrees(tile);
    }

    private void ClearTrees(int tile)
    {
        (float x, float z) = TileCenter(tile);
        float clear = Rules.TileSize * 0.8f;
        for (int i = 0; i < TreeX.Length; i++)
        {
            if (!TreeAlive[i] || MathF.Abs(TreeX[i] - x) > clear || MathF.Abs(TreeZ[i] - z) > clear) continue;
            TreeAlive[i] = false;
            TreeVersion++;
        }
    }

    public (float X, float Z) DoorPoint(Building b) => TileCenter(b.DoorTile);

    private static (int X, int Z) DoorOf(int tx, int tz, int size, int direction)
    {
        int cx = tx + size / 2, cz = tz + size / 2;
        return direction switch
        {
            0 => (cx, tz + size),
            1 => (tx + size, cz),
            2 => (cx, tz - 1),
            _ => (tx - 1, cz),
        };
    }

    private static (int X, int Z) FootprintForDoor(int doorX, int doorZ, int size, int direction) => direction switch
    {
        0 => (doorX - size / 2, doorZ - size),
        1 => (doorX - size, doorZ - size / 2),
        2 => (doorX - size / 2, doorZ + 1),
        _ => (doorX + 1, doorZ - size / 2),
    };

    private bool FootprintFree(int tx, int tz, int size, bool keepCorridors = false)
    {
        for (int z = tz; z < tz + size; z++)
        for (int x = tx; x < tx + size; x++)
            if (!InGrid(x, z) || Tiles[x + z * Rules.TileNx] != TileKind.Free || (keepCorridors && OnCorridor(x, z))) return false;
        return true;
    }

    /// <summary>Street lines kept clear of buildings, so the road network can always grow past a full block.</summary>
    private bool OnCorridor(int x, int z)
    {
        if (Buildings.Count == 0) return false;
        int origin = Buildings[0].DoorTile;
        int dx = x - origin % Rules.TileNx, dz = z - origin / Rules.TileNx;
        return ((dx % CorridorX) + CorridorX) % CorridorX == 0 || ((dz % CorridorZ) + CorridorZ) % CorridorZ == 0;
    }

    private void SeedRoads(Building store)
    {
        int doorX = store.DoorTile % Rules.TileNx, doorZ = store.DoorTile / Rules.TileNx;
        foreach (int sign in new[] { 1, -1 })
            for (int i = 1; i <= 9; i++)
            {
                int x = doorX + sign * i;
                if (!InGrid(x, doorZ) || Tiles[x + doorZ * Rules.TileNx] != TileKind.Free) break;
                PlanRoad(x + doorZ * Rules.TileNx);
            }
    }

    /// <summary>Opens a new street toward flat free land when the existing streets have no building sites.</summary>
    private void ExtendRoad()
    {
        int roads = 0;
        foreach (byte kind in Tiles)
            if (kind is TileKind.Road or TileKind.PlannedRoad) roads++;
        if (roads == 0 || roads > 90 + Buildings.Count * 9) return;
        float radius = 48 + 13 * MathF.Sqrt(Buildings.Count);
        for (int attempt = 0; attempt < 140; attempt++)
        {
            float angle = Random.R(0, MathF.Tau), distance = MathF.Sqrt(Random.F()) * radius;
            float x = CenterX + MathF.Cos(angle) * distance, z = CenterZ + MathF.Sin(angle) * distance * 0.75f;
            int tile = TileAt(x, z), tx = tile % Rules.TileNx, tz = tile / Rules.TileNx;
            if (!OnCorridor(tx, tz) || !FootprintFree(tx - 1, tz - 1, 3)) continue;
            (float cx, float cz) = TileCenter(tile);
            if (Map.Slope(cx, cz) > 0.32f || Map.RiverOffset(cx, cz) < 8) continue;
            int[] path = FindPath(tile, -1, true, 1200);
            if (path == null || path.Length > 34) continue;
            PlanRoad(tile);
            foreach (int step in path) PlanRoad(step);
            return;
        }
    }

    public float Year => Tick * Rules.TickSeconds / Rules.YearSeconds;
    public float YearFraction => (Tick % Rules.TicksPerYear) / (float)Rules.TicksPerYear;
    public int Season => Math.Min(3, (int)(YearFraction * 4));
    public int Population => People.Count;

    public int FoodInStores()
    {
        int total = 0;
        foreach (Building b in Buildings)
            if (b.Built)
                total += b.Avail(Res.Bread) + b.Avail(Res.Meat) + b.Avail(Res.Fish) + b.Avail(Res.Berries);
        return total;
    }

    public int Count(BType type, bool builtOnly = false)
    {
        int n = 0;
        foreach (Building b in Buildings)
            if (b.Type == type && (!builtOnly || b.Built)) n++;
        return n;
    }

    private void GenerateTrees()
    {
        var rng = new Rng(unchecked((ulong)(uint)Map.Seed * 31 + 5));
        var xs = new List<float>();
        var zs = new List<float>();
        for (int i = 0; i < 12000 && xs.Count < MaxTrees; i++)
        {
            float x = (rng.F() - 0.5f) * WorldMap.SizeX, z = (rng.F() - 0.5f) * WorldMap.SizeZ;
            float h = Map.Height(x, z);
            if (h < 1.6f || rng.F() > WorldMap.Smooth(2f, 18f, h) * 0.9f + 0.16f) continue;
            xs.Add(x);
            zs.Add(z);
        }
        TreeX = xs.ToArray();
        TreeZ = zs.ToArray();
        TreeAlive = new bool[TreeX.Length];
        Array.Fill(TreeAlive, true);
    }

    private void Found()
    {
        (CenterX, CenterZ) = ChooseCenter();
        int center = TileAt(CenterX, CenterZ);
        Building store = AddBuilding(BType.Storehouse, center % Rules.TileNx - 1, center / Rules.TileNx - 1, 0, true);
        (CenterX, CenterZ) = (store.X, store.Z);
        // No streets yet: the band lives wild until it has tools (EndWild lays out the first street).
        Wild = true;
        PlaceCave();
        NextHunt = (long)(60 / Rules.TickSeconds);
        Seed(store, Res.Wood, 44);
        Seed(store, Res.Stone, 16);
        Seed(store, Res.Berries, 90);
        Seed(store, Res.Fish, 40);
        for (int i = 0; i < 20; i++)
        {
            float x = CenterX + Random.R(-10, 10), z = CenterZ + Random.R(-10, 10);
            if (!Map.Walkable(x, z)) (x, z) = (CenterX, CenterZ + 6);
            float age = i < 14 ? Random.R(17, 38) : Random.R(2, 13);
            Spawn(x, z, age, i % 2 == 0, -1);
        }
        Log("A band of hunter-gatherers makes its camp by the estuary, below a hillside cave", CenterX, CenterZ, 3);
    }

    /// <summary>
    /// Picks the cave: open ground at the foot of the steepest rise within reach of the camp. The cave mouth faces
    /// downhill; the rock mass behind it is kept free of later building. No random draws, so the economy is unchanged.
    /// </summary>
    private void PlaceCave()
    {
        float best = float.MinValue;
        (CaveX, CaveZ, CaveYaw) = (CenterX, CenterZ - 24, 0);
        for (int ring = 0; ring < 11; ring++)
            for (int k = 0; k < 32; k++)
            {
                float a = k * MathF.Tau / 32, d = 24 + ring * 5;
                float x = CenterX + MathF.Cos(a) * d, z = CenterZ + MathF.Sin(a) * d;
                if (!Map.Walkable(x, z) || !InGrid(TileAt(x, z) % Rules.TileNx, TileAt(x, z) / Rules.TileNx) || Tiles[TileAt(x, z)] != TileKind.Free) continue;
                float gx = Map.Height(x + 2, z) - Map.Height(x - 2, z), gz = Map.Height(x, z + 2) - Map.Height(x, z - 2);
                float g = MathF.Sqrt(gx * gx + gz * gz);
                if (g < 0.001f) continue;
                (float ux, float uz) = (gx / g, gz / g);
                float rise = Map.Height(x + ux * 9, z + uz * 9) - Map.Height(x, z);
                float score = rise * 2f - d * 0.06f;
                if (score > best) (best, CaveX, CaveZ, CaveYaw) = (score, x, z, MathF.Atan2(-ux, -uz));
            }
        // The rock mass stands 5 m behind the mouth; its tiles are taken out of the town plan.
        float rx = CaveX - MathF.Sin(CaveYaw) * 5, rz = CaveZ - MathF.Cos(CaveYaw) * 5;
        int tile = TileAt(rx, rz), tx = tile % Rules.TileNx, tz = tile / Rules.TileNx;
        int mouth = TileAt(CaveX, CaveZ), sleep = TileAt(CaveX - MathF.Sin(CaveYaw) * 1.5f, CaveZ - MathF.Cos(CaveYaw) * 1.5f);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int t = tx + dx + (tz + dz) * Rules.TileNx;
                // The mouth and the sleeping spot just inside stay open, so people can path into the cave.
                if (InGrid(tx + dx, tz + dz) && t != mouth && t != sleep && Tiles[t] == TileKind.Free) Tiles[t] = TileKind.Blocked;
            }
    }

    private void UpdateWild()
    {
        if (Year >= WildYears)
        {
            EndWild();
            return;
        }
        if (!Night && Tick >= NextHunt && !Hunting) StartHunt();
        if (Hunting && Tick >= HuntStrike && Tick < HuntStrike + 10)
        {
            if (HuntWon && ++HuntsWon % 4 == 1) Log("The hunters bring down a red deer and carry the meat home", HuntX, HuntZ, 1);
            else if (!HuntWon && (Hunts - HuntsWon) % 4 == 1) Log("The deer scatter before the hunters can close in", HuntX, HuntZ, 1);
        }
    }

    /// <summary>
    /// A hunt: the deer graze in a clearing 70-120 m out (the most open of a dozen spots); the men walk out, stalk
    /// them, break cover, and carry the kill home.
    /// </summary>
    private void StartHunt()
    {
        int fewest = int.MaxValue;
        for (int i = 0; i < 12; i++)
        {
            float a = Random.R(0, MathF.Tau), d = Random.R(70, 120);
            float x = CenterX + MathF.Cos(a) * d, z = CenterZ + MathF.Sin(a) * d;
            if (!Map.Walkable(x, z) || Map.Height(x, z) < 1.2f) continue;
            int trees = 0;
            for (int t = 0; t < TreeX.Length; t++)
                if (TreeAlive[t] && Dist2(x, z, TreeX[t], TreeZ[t]) < 16 * 16) trees++;
            if (trees < fewest) (fewest, HuntX, HuntZ) = (trees, x, z);
        }
        if (fewest < int.MaxValue)
        {
            HuntUntil = Tick + (long)(240 / Rules.TickSeconds);
            HuntWon = Random.F() < 0.6f;
            Hunts++;
            NextHunt = Tick + (long)(Random.R(480, 900) / Rules.TickSeconds);
            return;
        }
        NextHunt = Tick + 600;
    }

    /// <summary>The band has learned to knap stone axes: the camp becomes a village with its first street.</summary>
    private void EndWild()
    {
        Wild = false;
        HuntUntil = -1;
        HuntWon = false;
        // The whole band gathers at the fire to see the first stone axes.
        Gather(CampX, CampZ, 90, true);
        foreach (Building b in Buildings)
            if (b.Type == BType.Storehouse)
            {
                SeedRoads(b);
                break;
            }
        Log("The band learns to knap stone axes and starts to fell trees for its first huts", CenterX, CenterZ, 3);
    }

    /// <summary>
    /// A day's foraging for a wild band member with bare hands: join the hunt, dig at the cave, pick berries, or
    /// gather shellfish on the shore. By night nobody hunts or goes berrying: they dig or gather on the shore near
    /// camp. Returns false now and then, so some stay by the fire.
    /// </summary>
    private bool TryForage(Person p)
    {
        if (Random.F() < 0.15f) return false;
        int hunters = 0, diggers = 0;
        foreach (Person o in People)
            if (o.Act == Act.Forage)
            {
                if (o.A == 0) hunters++;
                else if (o.A == 3) diggers++;
            }
        int kind;
        float x, z;
        if (Hunting && Tick < HuntUntil - (long)(150 / Rules.TickSeconds) && !p.Female && p.Age < 45 && hunters < 5)
        {
            kind = 0;
            float a = p.Id * 2.399963f;
            (x, z) = (HuntX + MathF.Cos(a) * 6, HuntZ + MathF.Sin(a) * 6);
        }
        else if (CaveDug < 1 && diggers < 3)
        {
            kind = 3;
            (x, z) = (CaveX + Random.R(-1.2f, 1.2f), CaveZ + Random.R(-1.2f, 1.2f));
        }
        else if (Season == 3 || Night || Random.F() < 0.35f)
        {
            kind = 2;
            (x, z) = BankPoint(CenterX + Random.R(-35, 35));
        }
        else
        {
            kind = 1;
            (x, z) = (CenterX + Random.R(-55, 55), CenterZ + Random.R(-55, 55));
        }
        if (!Map.Walkable(x, z)) return false;
        p.Act = Act.Forage;
        p.A = kind;
        p.B = -1;
        p.Phase = 0;
        GoTo(p, x, z);
        return true;
    }

    private void AdvanceForage(Person p, float dt)
    {
        if (p.Phase == 0)
        {
            p.Phase = 1;
            // Hunters lie in wait together until the strike; the others work a while at the spot.
            p.Timer = p.A switch
            {
                0 => Math.Max(4f, (HuntStrike - Tick) * Rules.TickSeconds),
                1 => 24f,
                2 => 30f,
                _ => 20f,
            };
            return;
        }
        if (p.Phase == 1)
        {
            p.Timer -= dt;
            if (p.Timer > 0) return;
            int res = -1, n = 0;
            switch (p.A)
            {
                case 0:
                    if (HuntWon) (res, n) = ((int)Res.Meat, 4);
                    break;
                case 1:
                    if (Season != 3) (res, n) = ((int)Res.Berries, 2);
                    break;
                case 2:
                    (res, n) = ((int)Res.Fish, Random.F() < 0.6f ? 1 : 2);
                    break;
                default:
                    bool open = CaveDug >= 1;
                    CaveDug = Math.Min(1, CaveDug + 1f / 45f);
                    if (!open && CaveDug >= 1) Log("The cave is dug out deep enough to shelter the whole band", CaveX, CaveZ, 2);
                    break;
            }
            Building store = n > 0 ? NearestStore(p.X, p.Z) : null;
            if (store == null)
            {
                p.Act = Act.Idle;
                p.A = -1;
                return;
            }
            Produced[res] += n;
            p.CarryRes = (sbyte)res;
            p.CarryN = (byte)n;
            p.B = store.Id;
            p.Phase = 2;
            GoToDoor(p, store);
            return;
        }
        if (p.CarryN > 0 && p.B >= 0) Buildings[p.B].Stock[p.CarryRes] += p.CarryN;
        p.CarryN = 0;
        p.CarryRes = -1;
        p.A = p.B = -1;
        p.Act = Act.Idle;
    }

    private void Seed(Building b, Res r, int n)
    {
        b.Stock[(int)r] += n;
        Seeded[(int)r] += n;
    }

    private (float, float) ChooseCenter()
    {
        float coast = Map.CoastX(Map.RiverCenter(150));
        float bestX = coast - 130, bestZ = Map.RiverCenter(bestX) + WorldMap.EstuaryWidth(bestX) + 50, best = float.MinValue;
        for (int i = 0; i < 500; i++)
        {
            float x = Random.R(coast - 230, coast - 70), z = Map.RiverCenter(x) + WorldMap.EstuaryWidth(x) + Random.R(36, 92);
            if (!Map.Walkable(x, z)) continue;
            float score = -Map.Slope(x, z) * 60 - MathF.Abs(x - (coast - 130)) * 0.03f - MathF.Abs(Map.Height(x, z) - 3f) * 0.4f;
            if (score > best) (best, bestX, bestZ) = (score, x, z);
        }
        return (bestX, bestZ);
    }

    public Person Spawn(float x, float z, float age, bool female, int mother)
    {
        var p = new Person
        {
            Id = NextPersonId++, Slot = _freeSlots.Pop(), X = x, Z = z, TX = x, TZ = z, Age = age, Female = female, Mother = mother,
            MaxAge = Random.R(48, 78), Hunger = Random.R(0, 0.35f),
        };
        People.Add(p);
        _byId[p.Id] = p;
        return p;
    }

    public Building AddBuilding(BType type, int tileX, int tileZ, int direction, bool built)
    {
        int size = Rules.Footprint(type);
        float x = (tileX + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2, z = (tileZ + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
        (int doorX, int doorZ) = DoorOf(tileX, tileZ, size, direction);
        var b = new Building
        {
            Id = Buildings.Count, Type = type, X = x, Z = z, TileX = tileX, TileZ = tileZ, Direction = direction,
            DoorTile = doorX + doorZ * Rules.TileNx, Rotation = direction * MathF.PI / 2, Built = built, Progress = built ? 1 : 0,
        };
        Buildings.Add(b);
        BuildingVersion++;
        for (int tz = tileZ; tz < tileZ + size; tz++)
        for (int tx = tileX; tx < tileX + size; tx++)
        {
            int tile = tx + tz * Rules.TileNx;
            RoadQueue.Remove(tile);
            Tiles[tile] = type == BType.Field ? TileKind.Field : TileKind.Building;
            ClearTrees(tile);
        }
        RoadVersion++;
        PlanRoad(b.DoorTile);
        int[] link = FindPath(b.DoorTile, -1, true);
        if (link != null)
            foreach (int tile in link) PlanRoad(tile);
        return b;
    }

    private bool OnBuildingSite(float x, float z) => Tiles[TileAt(x, z)] != TileKind.Free;

    public void Log(string text, float x, float z, int importance)
    {
        Events.Add(new WorldEvent(Tick, text, x, z, importance));
        if (Events.Count > 64) Events.RemoveAt(0);
    }

    public void Step()
    {
        Tick++;
        if (Tick % 10 == 0) UpdateWorld(1f);
        for (int i = 0; i < People.Count; i++) UpdatePerson(People[i], Rules.TickSeconds);
        if (_anyDead)
        {
            People.RemoveAll(static p => p.Dead);
            _anyDead = false;
        }
        if (Tick % 50 == 0) { AssignHomes(); AssignJobs(); Family(); }
        if (Tick % 10 == 0) Dispatch();
        if (Tick % 100 == 0) Plan();
        if (Tick % 20 == 0) UpdateWear();
        if (Tick % Rules.TicksPerYear == 0) NewYear();
        if (Tick % 600 == 0) Director();
    }

    private void UpdateWorld(float dt)
    {
        if (FloodAt >= 0 && Tick >= FloodAt) StrikeFlood();
        if (FireBuilding >= 0) UpdateFire();
        if (Wild) UpdateWild();
        UpdatePolders(dt);
        int season = Season;
        foreach (Building b in Buildings)
        {
            if (!b.Built) continue;
            if (b.Type == BType.Field && b.Sown && b.Growth < 1 && season < 3)
            {
                b.Growth = Math.Min(1, b.Growth + dt / (0.55f * Rules.YearSeconds));
                if (b.Growth >= 1)
                {
                    float bank = MathF.Abs(Map.RiverOffset(b.X, b.Z)) - WorldMap.EstuaryWidth(b.X);
                    float fertility = 0.85f + 0.3f * (1 - WorldMap.Smooth(10f, 90f, bank));
                    b.Yield = (int)(56 * HarvestFactor * fertility * (1 + 0.28f * Math.Max(0, Era - 1)));
                }
            }
            if (b.Type == BType.Hut)
            {
                if (season == 3 && b.Occupants > 0)
                {
                    // Coal stoves burn less wood from the Industrial era; a house on the district heating takes most of the load off.
                    b.BurnDebt += ColdFactor * dt / 75f * (DistrictHeated(b) ? 0.35f : Era >= 6 ? 0.7f : 1f);
                    while (b.BurnDebt >= 1 && b.Stock[(int)Res.Wood] > b.Out[(int)Res.Wood])
                    {
                        b.BurnDebt -= 1;
                        b.Stock[(int)Res.Wood]--;
                        Consumed[(int)Res.Wood]++;
                    }
                    b.BurnDebt = Math.Min(b.BurnDebt, 2);
                }
                b.Warm = b.Stock[(int)Res.Wood] > 0 || DistrictHeated(b);
            }
        }
    }

    private void UpdatePerson(Person p, float dt)
    {
        if (p.Dead) return;
        p.Age += dt / Rules.YearSeconds;
        p.Hunger = Math.Min(1, p.Hunger + dt * HungerPerSecond);
        if (p.Hunger >= 1) p.Health -= dt * StarvePerSecond;
        else if (p.Hunger < 0.6f) p.Health = Math.Min(1, p.Health + dt / 1800f);
        if (Season == 3 && (p.Home < 0 ? !(Wild && CaveDug >= 0.5f) : !Buildings[p.Home].Warm)) p.Health -= dt * ColdPerSecond * ColdFactor;
        if (p.Age >= p.MaxAge || p.Health <= 0)
        {
            Kill(p);
            return;
        }

        if (p.Moving && !Move(p, dt)) return;
        p.Moving = false;
        Advance(p, dt);
    }

    private bool Move(Person p, float dt)
    {
        float step = Speed * dt * (Tiles[TileAt(p.X, p.Z)] == TileKind.Road ? 1.35f : 1f);
        if (p.Path != null && p.PathIndex < p.Path.Length)
        {
            (float wx, float wz) = TileCenter(p.Path[p.PathIndex]);
            float ax = wx - p.X, az = wz - p.Z, remaining = MathF.Sqrt(ax * ax + az * az);
            if (remaining <= step)
            {
                p.X = wx;
                p.Z = wz;
                p.PathIndex++;
            }
            else
            {
                p.X += ax / remaining * step;
                p.Z += az / remaining * step;
            }
            return false;
        }
        p.Path = null;
        float dx = p.TX - p.X, dz = p.TZ - p.Z, distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= step)
        {
            p.X = p.TX;
            p.Z = p.TZ;
            p.Stuck = 0;
            return true;
        }
        float nx = p.X + dx / distance * step, nz = p.Z + dz / distance * step;
        // Without a path (a short wander, or no route found) people walk straight; they stop at a house wall
        // instead of walking through it, unless the house is where they are going or where they stand.
        int nextTile = TileAt(nx, nz);
        if (Tiles[nextTile] == TileKind.Building && nextTile != TileAt(p.TX, p.TZ) && Tiles[TileAt(p.X, p.Z)] != TileKind.Building)
        {
            p.Stuck = 0;
            return true;
        }
        if (!Map.Walkable(nx, nz))
        {
            float gx = Map.Height(p.X + 2, p.Z) - Map.Height(p.X - 2, p.Z), gz = Map.Height(p.X, p.Z + 2) - Map.Height(p.X, p.Z - 2);
            float g = MathF.Sqrt(gx * gx + gz * gz);
            if (g > 0.0001f)
            {
                nx = p.X + gx / g * step + dx / distance * step * 0.4f;
                nz = p.Z + gz / g * step + dz / distance * step * 0.4f;
            }
            if (!Map.Walkable(nx, nz) || ++p.Stuck > 80)
            {
                p.Stuck = 0;
                return true;
            }
        }
        p.X = nx;
        p.Z = nz;
        return false;
    }

    private void GoTo(Person p, float x, float z, int limit = 9000)
    {
        p.TX = x;
        p.TZ = z;
        p.Moving = true;
        p.Indoors = false;
        p.PathIndex = 0;
        int from = TileAt(p.X, p.Z), to = TileAt(x, z);
        p.Path = from == to || Tiles[to] == TileKind.Blocked ? null : FindPath(from, to, false, limit);
    }

    private void GoToDoor(Person p, Building b)
    {
        (float x, float z) = DoorPoint(b);
        GoTo(p, x, z);
    }

    private void Advance(Person p, float dt)
    {
        switch (p.Act)
        {
            case Act.Idle: Choose(p); break;
            case Act.Wander:
                p.Timer -= dt;
                if (p.Timer <= 0) p.Act = Act.Idle;
                break;
            case Act.Eat: FinishEating(p); break;
            case Act.Haul: AdvanceHaul(p); break;
            case Act.Build: AdvanceBuild(p, dt); break;
            case Act.Work: AdvanceWork(p, dt); break;
            case Act.Forage: AdvanceForage(p, dt); break;
            case Act.Road:
                p.Timer -= dt;
                if (p.Timer > 0) break;
                if (p.Phase == PolderPhase) FinishPolderWork();
                // A digger only counts a stint if they actually reached the trench (a blocked walk can stop short).
                if (p.Phase == MainsPhase && p.A >= 0 && Dist2(p.X, p.Z, TileCenter(p.A).X, TileCenter(p.A).Z) < 4f * 4f) FinishMainsWork(p.A);
                if (Tiles[p.A] == TileKind.PlannedRoad)
                {
                    Tiles[p.A] = TileKind.Road;
                    RoadVersion++;
                }
                p.A = -1;
                p.Act = Act.Idle;
                break;
            case Act.Rest:
                p.Indoors = true;
                if (!RestsNow(p) || p.Hunger >= 0.75f)
                {
                    p.Indoors = false;
                    p.Act = Act.Idle;
                }
                break;
            case Act.Shift:
                // A mill hand works a shift inside, then comes out through the gate: at dusk, when hungry, or when it ends.
                p.Indoors = true;
                p.Timer -= dt;
                if (Night || p.Timer <= 0 || p.Hunger >= 0.75f || p.Role is Role.Porter or Role.Builder)
                {
                    p.Indoors = false;
                    p.Act = Act.Idle;
                    if (p.Home >= 0)
                    {
                        // Out through the gate and home, so the hand does not step straight back in.
                        (float hx, float hz) = DoorPoint(Buildings[p.Home]);
                        p.Act = Act.Wander;
                        p.Timer = 40f;
                        GoTo(p, hx, hz, 300);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Market day: from the Middle Ages, at the start of every sixth of a year (two months) outside winter, stalls stand
    /// by the church door for 90 seconds of daylight. Null when there is no market on.
    /// </summary>
    public (float X, float Z)? MarketPlace()
    {
        if (Era < 4 || Night || Season == 3 || Tick % (Rules.TicksPerYear / 6) >= (long)(90 / Rules.TickSeconds)) return null;
        return ChurchDoor();
    }

    /// <summary>The point in front of the mill gate, or null before there is a mill.</summary>
    private (float X, float Z)? FactoryGate()
    {
        int at = Landmarks.FindIndex(l => l.Kind == 3);
        if (at < 0) return null;
        Landmark factory = Landmarks[at];
        (float fx, float fz) = TileCenter(factory.TileX + factory.TileZ * Rules.TileNx);
        float gate = (factory.HalfZ + 1.3f) * Rules.TileSize;
        return (fx + MathF.Sin(factory.Yaw) * gate, fz + MathF.Cos(factory.Yaw) * gate);
    }

    private bool RestsNow(Person p) => Night && (p.Id % 5 == 0 || Wild && p.Id % 5 < 3);

    private void Choose(Person p)
    {
        if (p.Hunger >= 0.5f && TryEat(p)) return;
        if (RestsNow(p) && p.Hunger < 0.75f && p.Home >= 0)
        {
            Building home = Buildings[p.Home];
            p.Act = Act.Rest;
            GoToDoor(p, home);
            return;
        }
        // The wild band sleeps in the cave once it is dug out far enough to crawl into.
        if (Wild && p.Home < 0 && RestsNow(p) && p.Hunger < 0.75f && CaveDug >= 0.3f)
        {
            p.Act = Act.Rest;
            GoTo(p, CaveX - MathF.Sin(CaveYaw) * 1.5f, CaveZ - MathF.Cos(CaveYaw) * 1.5f);
            return;
        }
        if (p.Age >= 14 && p.Age < 60)
        {
            if (Wild && TryForage(p)) return;
            if (p.Job >= 0 && StartWork(p)) return;
            if (p.Role == Role.Builder && (TryBuild(p) || TryRoad(p) || TryPolderWork(p) || TryMains(p))) return;
            // Farmers and other seasonal workers with nothing to do (winter, or full stores) lay roads and raise dikes.
            if (p.Job >= 0 && (TryRoad(p) || TryPolderWork(p) || TryMains(p))) return;
        }
        Wander(p);
    }

    private bool TryRoad(Person p)
    {
        if (RoadQueue.Count == 0) return false;
        int best = 0;
        float nearest = float.MaxValue;
        for (int i = 0; i < RoadQueue.Count; i++)
        {
            (float x, float z) = TileCenter(RoadQueue[i]);
            float d = Dist2(p.X, p.Z, x, z);
            if (d < nearest) (nearest, best) = (d, i);
        }
        int tile = RoadQueue[best];
        RoadQueue.RemoveAt(best);
        p.Act = Act.Road;
        p.Phase = 0;
        p.A = tile;
        p.Timer = 5f;
        (float tx, float tz) = TileCenter(tile);
        GoTo(p, tx, tz);
        return true;
    }

    /// <summary>Draws idle villagers to one place for a while. Gatherings are at least half a year apart.</summary>
    private bool Gather(float x, float z, float seconds, bool force = false)
    {
        if (!force && Tick - LastGathering < Rules.TicksPerYear / 2) return false;
        (GatherX, GatherZ) = (x, z);
        GatherUntil = Tick + (long)(seconds / Rules.TickSeconds);
        LastGathering = Tick;
        return true;
    }

    private void Wander(Person p)
    {
        float cx = Wild ? CampX : CenterX, cz = Wild ? CampZ : CenterZ, radius = Wild ? (Night ? 6 : 14) : 26;
        if (p.Home >= 0) (cx, cz, radius) = (Buildings[p.Home].X, Buildings[p.Home].Z, p.Age < 14 ? 11 : 16);
        if (p.Age >= 14 && p.Age < 60 && p.Job >= 0)
        {
            (cx, cz) = DoorPoint(Buildings[p.Job]);
            radius = 6;
        }
        else if (p.Role == Role.Porter && p.Id % 4 == 0 && Tick % Rules.TicksPerYear is >= 2400 and < 4800 && TraderVisiting
                 && Landmarks.FindIndex(l => l.Kind == 5) is int quayAt and >= 0)
        {
            // While the crowd watches the trading ship come in, a quarter of the idle porters wait on the quay for its cargo.
            (cx, cz) = TileCenter(Landmarks[quayAt].TileX + Landmarks[quayAt].TileZ * Rules.TileNx);
            radius = 6;
        }
        else if (p.Role == Role.Porter && NearestStore(p.X, p.Z) is Building store)
        {
            (cx, cz) = DoorPoint(store);
            radius = 8;
        }
        // During a gathering (funeral, feast, era celebration) everyone with time on their hands joins the crowd.
        // Workers, porters and builders keep the village running; children, elders and the jobless within reach go.
        bool free = p.Age < 14 || p.Age >= 60 || (p.Job < 0 && p.Role is not (Role.Porter or Role.Builder));
        // A house fire brings every neighbour with a moment to spare, adults too: they pass the buckets.
        if (FireEnd >= 0 && Tick < FireEnd && Dist2(p.X, p.Z, GatherX, GatherZ) < 40 * 40) free = true;
        if (Tick < GatherUntil && free && Dist2(p.X, p.Z, GatherX, GatherZ) < 90 * 90) (cx, cz, radius) = (GatherX, GatherZ, 7);
        // On market day the stalls by the church draw those with time on their hands; working adults stay at work.
        else if (free && MarketPlace() is (float mx, float mz) && Dist2(p.X, p.Z, mx, mz) < 70 * 70) (cx, cz, radius) = (mx, mz, 8);
        // By day, adults with nothing to do go to the mill (Industrial era and later). A quarter of them are mill hands:
        // they walk in through the gate, work a shift, and stream out at dusk. The jobless stand about at the gate.
        else if (!Night && p.Age >= 14 && p.Age < 60 && (p.Job < 0 && p.Id % 2 == 0 || p.Id % 4 == 0) && p.Role is not (Role.Porter or Role.Builder)
                 && FactoryGate() is (float gx, float gz))
        {
            bool hand = p.Id % 4 == 0;
            if (hand && Dist2(p.X, p.Z, gx, gz) < 2.5f * 2.5f)
            {
                p.Act = Act.Shift;
                p.Indoors = true;
                p.Timer = 120f;
                ShiftsWorked++;
                return;
            }
            (cx, cz, radius) = (gx, gz, hand ? 1.5f : 5);
        }
        float x = cx + Cosmetic.R(-radius, radius), z = cz + Cosmetic.R(-radius, radius);
        p.Act = Act.Wander;
        p.Timer = Cosmetic.R(3, 11);
        if (Map.Walkable(x, z)) GoTo(p, x, z, 300);
    }

    private bool TryEat(Person p)
    {
        Building best = null;
        float bestDistance = float.MaxValue;
        foreach (Building b in Buildings)
        {
            if (!b.Built || b.Type == BType.Hut || !HasFood(b)) continue;
            float d = Dist2(p.X, p.Z, b.X, b.Z);
            if (d < bestDistance) (bestDistance, best) = (d, b);
        }
        if (best == null) return false;
        p.Act = Act.Eat;
        p.A = best.Id;
        GoToDoor(p, best);
        return true;
    }

    private static bool HasFood(Building b) => b.Avail(Res.Bread) > 0 || b.Avail(Res.Meat) > 0 || b.Avail(Res.Fish) > 0 || b.Avail(Res.Berries) > 0;

    private void FinishEating(Person p)
    {
        Building b = Buildings[p.A];
        foreach (Res r in new[] { Res.Bread, Res.Meat, Res.Fish, Res.Berries })
        {
            if (b.Avail(r) <= 0) continue;
            b.Stock[(int)r]--;
            Consumed[(int)r]++;
            p.Hunger = Math.Max(0, p.Hunger - 0.5f);
            break;
        }
        p.A = -1;
        p.Act = Act.Idle;
    }

    private bool TryBuild(Person p)
    {
        foreach (Building b in Buildings)
        {
            if (b.Built || !HasMaterials(b) || b.Workers >= 3) continue;
            b.Workers++;
            p.Act = Act.Build;
            p.A = b.Id;
            GoTo(p, b.X + Random.R(-3, 3), b.Z + Random.R(-3, 3));
            return true;
        }
        return false;
    }

    private static bool HasMaterials(Building b)
    {
        int[] cost = Rules.Costs[(int)b.Type];
        for (int i = 0; i < cost.Length; i++)
            if (b.Stock[i] < cost[i]) return false;
        return true;
    }

    private void AdvanceBuild(Person p, float dt)
    {
        Building b = Buildings[p.A];
        if (b.Built)
        {
            ReleaseBuilder(p);
            return;
        }
        b.Progress += dt / Rules.BuildSeconds(b.Type);
        if (b.Progress < 1) return;
        b.Progress = 1;
        b.Built = true;
        int[] cost = Rules.Costs[(int)b.Type];
        for (int i = 0; i < cost.Length; i++)
        {
            b.Stock[i] -= cost[i];
            Consumed[i] += cost[i];
        }
        BuildingVersion++;
        AnnounceFirst(b);
        ReleaseBuilder(p);
    }

    private void ReleaseBuilder(Person p)
    {
        if (p.A >= 0) Buildings[p.A].Workers = Math.Max(0, Buildings[p.A].Workers - 1);
        p.A = -1;
        p.Act = Act.Idle;
    }

    private void AnnounceFirst(Building b)
    {
        int bit = 1 << (int)b.Type;
        if ((MilestoneMask & bit) != 0) return;
        MilestoneMask |= bit;
        string text = b.Type switch
        {
            BType.Hut => "The first homes go up",
            BType.Field => "The first field is cleared",
            BType.Fisher => "Villagers start fishing on the river",
            BType.Quarry => "The first quarry is opened",
            BType.Mill => "The first millstone grinds grain into flour",
            BType.Bakery => "The first oven bakes its first loaves",
            BType.Windmill => "The first windmill catches the sea wind",
            BType.Watermill => "The first water wheel turns by the river",
            BType.Vineyard => "The first vineyard is planted",
            BType.Winery => "The first winery presses its first wine",
            BType.Smithy => "The first forge is lit",
            BType.Dike => "Villagers raise the first stretch of dike",
            BType.TideGate => "The first tide gate is built at the river mouth",
            _ => null,
        };
        if (text != null) Log(text, b.X, b.Z, 2);
    }

    private int Need(Building b, Res r)
    {
        int i = (int)r, target;
        if (!b.Built) target = Rules.Costs[(int)b.Type][i];
        else if (b.Type == BType.Hut && r == Res.Wood && b.Occupants > 0) target = Season >= 2 ? 6 : 3;
        else if (Rules.IsGrinder(b.Type) && !b.Pump && r == Res.Grain) target = 12;
        else if (b.Type == BType.Winery && r == Res.Grapes) target = 6;
        else if (b.Type == BType.Smithy && r is Res.Stone or Res.Wood) target = 3;
        else if (b.Type == BType.Bakery && r == Res.Flour) target = 10;
        else if (b.Type == BType.Bakery && r == Res.Wood) target = 4;
        else return 0;
        return Math.Max(0, target - b.Stock[i] - b.In[i]);
    }

    /// <summary>Matches open deliveries to the nearest free carrier, most urgent first.</summary>
    private void Dispatch()
    {
        _carriers.Clear();
        _helpers.Clear();
        bool buildWork = HasBuildWork();
        foreach (Person p in People)
        {
            if (p.Dead || p.Age < 14 || p.Age >= 60 || p.Hunger >= 0.5f || RestsNow(p) || p.Act is not (Act.Idle or Act.Wander)) continue;
            if (p.Job >= 0) _helpers.Add(p);
            else if (p.Role == Role.Porter || (p.Role == Role.Builder && !buildWork)) _carriers.Add(p);
        }
        if (_carriers.Count == 0 && _helpers.Count == 0) return;

        _demands.Clear();
        foreach (Building b in Buildings)
            for (int i = 0; i < (int)Res.Count; i++)
                if (Need(b, (Res)i) > 0) _demands.Add((DemandPriority(b, (Res)i), b.Id, i));
        _demands.Sort();

        foreach ((int priority, int building, int resource) in _demands)
        {
            Building target = Buildings[building];
            var r = (Res)resource;
            while (true)
            {
                int need = Need(target, r);
                if (need <= 0) break;
                Building source = NearestSource(r, target);
                if (source == null) break;
                Person carrier = TakeCarrier(source, true);
                if (carrier == null) break;
                Reserve(carrier, source, target, r, Math.Min(need, Math.Min(source.Avail(r), CarryLoad)));
            }
        }

        for (int pass = 0; pass < 2; pass++)
            foreach (Building b in Buildings)
            {
                if (!b.Built || Rules.OutputOf(b.Type) is not Res output) continue;
                bool food = output is Res.Bread or Res.Fish or Res.Berries;
                if (food != (pass == 0)) continue;
                Building store = NearestStore(b.X, b.Z);
                while (store != null && b.Avail(output) > 0)
                {
                    Person carrier = TakeCarrier(b, true);
                    if (carrier == null) break;
                    Reserve(carrier, b, store, output, Math.Min(b.Avail(output), CarryLoad));
                }
            }
    }

    private int DemandPriority(Building b, Res r)
    {
        if (!b.Built) return b.Type is BType.Field or BType.Gatherer or BType.Fisher or BType.Mill or BType.Bakery or BType.Woodcutter ? 1 : 2;
        if (b.Type == BType.Hut) return Season >= 2 ? (b.Stock[(int)Res.Wood] == 0 ? 0 : 1) : 4;
        return b.Stock[(int)r] == 0 ? 1 : 2;
    }

    private Person TakeCarrier(Building near, bool allowHelpers)
    {
        List<Person> pool = _carriers.Count > 0 || !allowHelpers ? _carriers : _helpers;
        int best = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < pool.Count; i++)
        {
            float d = Dist2(pool[i].X, pool[i].Z, near.X, near.Z);
            if (d < nearest) (nearest, best) = (d, i);
        }
        if (best < 0) return null;
        Person carrier = pool[best];
        pool.RemoveAt(best);
        return carrier;
    }

    private bool HasBuildWork()
    {
        if (RoadQueue.Count > 0 || DikingPolder() != null) return true;
        foreach (Building b in Buildings)
            if (!b.Built && b.Workers < 3 && HasMaterials(b)) return true;
        return false;
    }

    private Building NearestSource(Res r, Building target)
    {
        Building best = null;
        float bestDistance = float.MaxValue;
        foreach (Building s in Buildings)
        {
            if (s == target || !s.Built || s.Avail(r) <= 0) continue;
            if (s.Type != BType.Storehouse && Rules.OutputOf(s.Type) != r) continue;
            float d = Dist2(s.X, s.Z, target.X, target.Z);
            if (d < bestDistance) (bestDistance, best) = (d, s);
        }
        return best;
    }

    private Building NearestStore(float x, float z)
    {
        Building best = null;
        float bestDistance = float.MaxValue;
        foreach (Building s in Buildings)
        {
            if (s.Type != BType.Storehouse || !s.Built) continue;
            float d = Dist2(s.X, s.Z, x, z);
            if (d < bestDistance) (bestDistance, best) = (d, s);
        }
        return best;
    }

    private void Reserve(Person p, Building from, Building to, Res r, int n)
    {
        from.Out[(int)r] += n;
        to.In[(int)r] += n;
        p.Act = Act.Haul;
        p.Phase = 0;
        p.A = from.Id;
        p.B = to.Id;
        p.Amount = (byte)n;
        p.CarryRes = (sbyte)r;
        p.CarryN = 0;
        GoToDoor(p, from);
    }

    private void AdvanceHaul(Person p)
    {
        Building from = Buildings[p.A], to = Buildings[p.B];
        int r = p.CarryRes;
        if (p.Phase == 0)
        {
            from.Out[r] -= p.Amount;
            from.Stock[r] -= p.Amount;
            p.CarryN = p.Amount;
            p.Phase = 1;
            GoToDoor(p, to);
            return;
        }
        to.In[r] -= p.Amount;
        to.Stock[r] += p.CarryN;
        p.CarryN = 0;
        p.Amount = 0;
        p.CarryRes = -1;
        p.A = p.B = -1;
        p.Act = Act.Idle;
    }

    private bool StartWork(Person p)
    {
        Building b = Buildings[p.Job];
        if (HasSurplus(b.Type) || OutputFull(b)) return false;
        float x = b.X, z = b.Z;
        switch (b.Type)
        {
            case BType.Field:
                bool sow = Season == 0 && !b.Sown, harvest = b.Yield > 0;
                if (!sow && !harvest && !(b.Sown && Season < 3)) return false;
                x += Random.R(-8, 8);
                z += Random.R(-8, 8);
                break;
            case BType.Gatherer:
                if (Season == 3) return false;
                x += Random.R(-42, 42);
                z += Random.R(-42, 42);
                break;
            case BType.Fisher:
                (x, z) = BankPoint(b.X + Random.R(-22, 22));
                break;
            case BType.Woodcutter:
                int tree = NearestTree(b.X, b.Z);
                if (tree < 0) return false;
                p.B = tree;
                float side = 0.35f + (p.Id * 2.399963f) % (MathF.PI - 0.7f);
                x = TreeX[tree] + MathF.Cos(side) * 1.55f;
                z = TreeZ[tree] + MathF.Sin(side) * 1.55f;
                break;
            case BType.Quarry:
                x += Random.R(-7, 7);
                z += Random.R(-7, 7);
                break;
            case BType.Mill:
            case BType.Windmill:
            case BType.Watermill:
                if (b.Stock[(int)Res.Grain] < Batch) return false;
                b.Stock[(int)Res.Grain] -= Batch;
                Consumed[(int)Res.Grain] += Batch;
                break;
            case BType.Vineyard:
                if (Season is 0 or 3) return false;
                x += Random.R(-6, 6);
                z += Random.R(-6, 6);
                break;
            case BType.Winery:
                if (b.Stock[(int)Res.Grapes] < 3) return false;
                b.Stock[(int)Res.Grapes] -= 3;
                Consumed[(int)Res.Grapes] += 3;
                break;
            case BType.Smithy:
                if (b.Stock[(int)Res.Stone] < 1 || b.Stock[(int)Res.Wood] < 1) return false;
                b.Stock[(int)Res.Stone] -= 1;
                b.Stock[(int)Res.Wood] -= 1;
                Consumed[(int)Res.Stone] += 1;
                Consumed[(int)Res.Wood] += 1;
                break;
            case BType.Bakery:
                if (b.Stock[(int)Res.Flour] < Batch || b.Stock[(int)Res.Wood] < 1) return false;
                b.Stock[(int)Res.Flour] -= Batch;
                b.Stock[(int)Res.Wood] -= 1;
                Consumed[(int)Res.Flour] += Batch;
                Consumed[(int)Res.Wood] += 1;
                break;
            default:
                return false;
        }
        if (Rules.IsGrinder(b.Type) || b.Type is BType.Bakery or BType.Winery or BType.Smithy) (x, z) = DoorPoint(b);
        if (!Map.Walkable(x, z)) (x, z) = DoorPoint(b);
        p.Act = Act.Work;
        p.Phase = 0;
        GoTo(p, x, z);
        return true;
    }

    private (float, float) BankPoint(float x)
    {
        x = Math.Clamp(x, -WorldMap.SizeX / 2 + 8, WorldMap.SizeX / 2 - 8);
        float z = Map.RiverCenter(x) + 40 + WorldMap.EstuaryWidth(x);
        for (int i = 0; i < 40 && Map.Height(x, z - 1) > 0.8f; i++) z -= 1;
        return (x, z);
    }

    private int NearestTree(float x, float z)
    {
        int best = -1;
        float bestDistance = 170f * 170f;
        for (int i = 0; i < TreeX.Length; i++)
        {
            if (!TreeAlive[i]) continue;
            float d = Dist2(x, z, TreeX[i], TreeZ[i]);
            if (d < bestDistance) (bestDistance, best) = (d, i);
        }
        return best;
    }

    private void AdvanceWork(Person p, float dt)
    {
        Building b = Buildings[p.Job];
        if (p.Phase == 0)
        {
            if (b.Type == BType.Woodcutter && (p.B < 0 || !TreeAlive[p.B]))
            {
                p.B = -1;
                p.Act = Act.Idle;
                return;
            }
            p.Phase = 1;
            p.Timer = ToolBonus * b.Type switch
            {
                BType.Field => 12f,
                BType.Gatherer => 24f,
                BType.Fisher => 42f,
                BType.Woodcutter => 22f,
                BType.Quarry => 34f,
                BType.Mill => 25f,
                BType.Bakery => 30f,
                BType.Windmill => 15f,
                BType.Watermill => 13f,
                BType.Vineyard => 26f,
                BType.Winery => 34f,
                BType.Smithy => 38f,
                _ => 10f,
            };
            return;
        }
        if (p.Phase == 1)
        {
            p.Timer -= dt;
            if (p.Timer > 0) return;
            FinishCycle(p, b);
            return;
        }
        int r = p.CarryRes;
        if (r >= 0)
        {
            b.Stock[r] += p.CarryN;
            p.CarryN = 0;
            p.CarryRes = -1;
        }
        p.Act = Act.Idle;
    }

    private void FinishCycle(Person p, Building b)
    {
        int amount = 0;
        switch (b.Type)
        {
            case BType.Field:
                if (Season == 0 && !b.Sown)
                {
                    b.Sown = true;
                    b.Growth = 0;
                }
                else if (b.Yield > 0)
                {
                    amount = Math.Min(3 + Era, b.Yield);
                    b.Yield -= amount;
                }
                break;
            case BType.Gatherer: amount = Era == 0 ? 3 : Era == 1 ? 2 : 1; break;
            case BType.Fisher: amount = (Season == 3 ? 1 : Random.F() < 0.7f ? 1 : 2) + (Era == 0 ? 1 : 0); break;
            case BType.Woodcutter:
                amount = 4;
                if (p.B >= 0 && TreeAlive[p.B] && Random.F() < 0.14f)
                {
                    TreeAlive[p.B] = false;
                    TreeVersion++;
                }
                p.B = -1;
                break;
            case BType.Quarry: amount = 1; break;
            case BType.Mill:
            case BType.Windmill:
            case BType.Watermill: amount = Batch; break;
            case BType.Bakery: amount = Batch * 2; break;
            case BType.Vineyard: amount = 2; break;
            case BType.Winery: amount = 2; break;
            case BType.Smithy: amount = 1; break;
        }
        if (amount > 0 && Rules.OutputOf(b.Type) is Res output)
        {
            Produced[(int)output] += amount;
            p.CarryRes = (sbyte)output;
            p.CarryN = (byte)amount;
        }
        p.Phase = 2;
        GoToDoor(p, b);
    }

    private void Kill(Person p)
    {
        if (p.Act == Act.Build) ReleaseBuilder(p);
        if (p.Act == Act.Road && p.A >= 0 && Tiles[p.A] == TileKind.PlannedRoad) RoadQueue.Add(p.A);
        if (p.Act == Act.Haul)
        {
            int r = p.CarryRes;
            if (p.Phase == 0) Buildings[p.A].Out[r] -= p.Amount;
            Buildings[p.B].In[r] -= p.Amount;
        }
        if (p.Act == Act.Work && p.Job >= 0 && Buildings[p.Job].Type == BType.Woodcutter && p.B >= 0) p.B = -1;
        if (p.CarryN > 0)
        {
            Building store = NearestStore(p.X, p.Z) ?? Buildings[0];
            store.Stock[p.CarryRes] += p.CarryN;
            p.CarryN = 0;
        }
        if (p.Spouse >= 0 && _byId.TryGetValue(p.Spouse, out Person spouse)) spouse.Spouse = -1;
        p.Dead = true;
        _anyDead = true;
        _byId.Remove(p.Id);
        _freeSlots.Push(p.Slot);
        Deaths++;
        AddGrave();
        // An elder's burial draws the village to the graveyard.
        if (p.Age >= 50 && Tick - LastGathering >= Rules.TicksPerYear / 2)
        {
            // With a parish church the mourners gather at its door; before that, at the grave.
            (float gx, float gz) = ChurchDoor() ?? Graves[^1];
            if (Gather(gx, gz, 70))
                Log("The village gathers to bury one of its elders", gx, gz, 2);
        }
    }

    /// <summary>A point a few metres in front of the church tower door, or null before there is a church.</summary>
    private (float X, float Z)? ChurchDoor()
    {
        int at = Landmarks.FindIndex(l => l.Kind == 0);
        if (at < 0) return null;
        Landmark church = Landmarks[at];
        (float cx, float cz) = TileCenter(church.TileX + church.TileZ * Rules.TileNx);
        float reach = (Math.Max(church.HalfX, church.HalfZ) + 1.2f) * Rules.TileSize;
        return (cx + MathF.Sin(church.Yaw) * reach, cz + MathF.Cos(church.Yaw) * reach);
    }

    private void AddGrave()
    {
        if (Graves.Count >= MaxGraves) Graves.RemoveAt(0);
        float angle = NextPersonId * 2.399963f + Deaths * 2.399963f, radius = 3 + MathF.Sqrt(Deaths) * 1.5f;
        // Once there is a parish church, new graves go in its churchyard (a spiral around the church lot).
        if (Landmarks.FindIndex(l => l.Kind == 0) is int churchAt and >= 0)
        {
            Landmark church = Landmarks[churchAt];
            (float cx, float cz) = TileCenter(church.TileX + church.TileZ * Rules.TileNx);
            float reach = (Math.Max(church.HalfX, church.HalfZ) + 1.5f) * Rules.TileSize;
            for (int step = 0; step < 40; step++)
            {
                float a = angle + step * 2.399963f, r = reach + (step % 8) * 1.6f;
                float x = cx + MathF.Cos(a) * r, z = cz + MathF.Sin(a) * r;
                if (!Map.Walkable(x, z) || Tiles[TileAt(x, z)] != TileKind.Free) continue;
                if (Graves.Count >= MaxGraves) Graves.RemoveAt(0);
                Graves.Add((x, z));
                return;
            }
        }
        float gx = CenterX - 46 + MathF.Cos(angle) * radius, gz = CenterZ + 30 + MathF.Sin(angle) * radius * 0.7f;
        if (!Map.Walkable(gx, gz)) (gx, gz) = (CenterX + MathF.Cos(angle) * 34, CenterZ + 18 + MathF.Sin(angle) * 10);
        // Step outwards along the spiral until the stone lands on open ground, not in a house, field or road.
        for (int step = 1; step <= 24 && !(Map.Walkable(gx, gz) && Tiles[TileAt(gx, gz)] == TileKind.Free); step++)
        {
            float a = angle + step * 2.399963f, r = radius + step * 1.2f;
            (gx, gz) = (CenterX - 46 + MathF.Cos(a) * r, CenterZ + 30 + MathF.Sin(a) * r * 0.7f);
        }
        Graves.Add((gx, gz));
    }

    private void AssignHomes()
    {
        foreach (Building b in Buildings) b.Occupants = 0;
        foreach (Person p in People)
            if (p.Home >= 0) Buildings[p.Home].Occupants++;

        foreach (Person p in People)
        {
            if (p.Home >= 0) continue;
            int wanted = -1;
            if (p.Mother >= 0 && p.Age < 16 && _byId.TryGetValue(p.Mother, out Person mother)) wanted = mother.Home;
            else if (p.Spouse >= 0 && _byId.TryGetValue(p.Spouse, out Person spouse)) wanted = spouse.Home;
            if (wanted < 0 || !Buildings[wanted].Built || Buildings[wanted].Occupants >= Capacity(Buildings[wanted])) wanted = FreeHut();
            if (wanted < 0) continue;
            p.Home = wanted;
            Buildings[wanted].Occupants++;
        }
    }

    private int FreeHut()
    {
        int best = -1, fewest = int.MaxValue;
        foreach (Building b in Buildings)
            if (b.Type == BType.Hut && b.Built && b.Occupants < Capacity(b) && b.Occupants < fewest) (best, fewest) = (b.Id, b.Occupants);
        return best;
    }

    /// <summary>Moves a couple out of a full hut so that housing does not silently stop births.</summary>
    private void RehouseCouple(Person mother)
    {
        int target = FreeHut();
        if (target < 0 || target == mother.Home || Buildings[target].Occupants > Capacity(Buildings[target]) - 3) return;
        Buildings[mother.Home].Occupants--;
        mother.Home = target;
        Buildings[target].Occupants++;
        if (!_byId.TryGetValue(mother.Spouse, out Person father) || father.Home == target) return;
        if (father.Home >= 0) Buildings[father.Home].Occupants--;
        father.Home = target;
        Buildings[target].Occupants++;
    }

    private void AssignJobs()
    {
        int adults = 0, employed = 0, farmers = 0;
        int[] caps = TradeCaps(), held = new int[caps.Length];
        bool grainGlut = StoredTotal(Res.Grain) > 150 + People.Count;
        foreach (Building b in Buildings)
            if (b.Built && Rules.WorkerSlots(b.Type) > 0) b.Workers = 0;
        foreach (Person p in People)
        {
            // Hands move from trades with a surplus to the ones the village is short of.
            if (p.Job >= 0 && p.Act != Act.Work)
            {
                BType trade = Buildings[p.Job].Type;
                int group = TradeGroup(trade);
                if (group >= 0 && ++held[group] > caps[group]) p.Job = -1;
                else if (trade == BType.Field && grainGlut && ++farmers > Count(BType.Field, true)) p.Job = -1;
            }
            bool eligible = p.Age >= 14 && p.Age < 60;
            if (!eligible && p.Job >= 0 && p.Act != Act.Work) p.Job = -1;
            if (p.Job >= 0 && p.Act != Act.Work && !HasInputs(Buildings[p.Job])) p.Job = -1;
            if (eligible) adults++;
            if (p.Job >= 0)
            {
                Buildings[p.Job].Workers++;
                employed++;
            }
        }
        FillWorkplaces((int)MathF.Ceiling(adults * (Era >= 2 ? 0.72f : 0.62f)), employed);
        AssignRoles(adults);
    }

    /// <summary>Family-sized homes, so a large town shows many houses. Camp shelters and the last two eras hold a little more.</summary>
    /// <summary>Places in a home: a house rebuilt larger in the Industrial era holds two more.</summary>
    private int Capacity(Building b) => HomeSize + (b.Renewed && b.Built ? 2 : 0);

    private int HomeSize => Era switch { 0 => Rules.HutCapacity + 2, <= 6 => Rules.HutCapacity, _ => 7 };

    private int Batch => 2 + Era / 2;
    private int CarryLoad => Rules.CarryCapacity + Era / 2;

    private static int TradeGroup(BType type) => type switch
    {
        BType.Woodcutter => 0,
        BType.Mill or BType.Windmill or BType.Watermill => 1,
        BType.Bakery => 2,
        _ => -1,
    };

    /// <summary>How many workers each trade may hold, so processors do not drain hands from the fields.</summary>
    private int[] TradeCaps()
    {
        int pop = People.Count;
        return new[]
        {
            3 + pop / 10 + (StoredTotal(Res.Wood) < 60 ? 4 : 0),
            2 + pop / 40 + (StoredTotal(Res.Grain) > 100 ? 3 : 0),
            2 + pop / 40 + (StoredTotal(Res.Flour) > 60 ? 3 : 0),
        };
    }

    /// <summary>True when a processor has, or can be brought, enough material for one work cycle.</summary>
    private bool HasInputs(Building b)
    {
        bool Ready(Res r, int n) => b.Stock[(int)r] + b.In[(int)r] >= n || StoredTotal(r) >= n;
        return b.Type switch
        {
            BType.Mill or BType.Windmill or BType.Watermill => Ready(Res.Grain, Batch),
            BType.Bakery => Ready(Res.Flour, Batch) && Ready(Res.Wood, 1),
            BType.Winery => Ready(Res.Grapes, 3),
            BType.Smithy => Ready(Res.Stone, 1) && Ready(Res.Wood, 1),
            _ => true,
        };
    }

    /// <summary>Gives every core trade one worker first, so a small village keeps its whole food chain running.</summary>
    private int StaffCoreTrades(int employed, ref int cursor)
    {
        BType[][] trades =
        {
            new[] { BType.Fisher }, new[] { BType.Gatherer }, new[] { BType.Woodcutter }, new[] { BType.Field },
            new[] { BType.Watermill, BType.Windmill, BType.Mill }, new[] { BType.Bakery },
        };
        foreach (BType[] trade in trades)
        {
            Building open = null;
            bool staffed = false;
            foreach (Building b in Buildings)
            {
                if (!b.Built || b.Pump || Array.IndexOf(trade, b.Type) < 0 || !HasInputs(b)) continue;
                if (b.Workers > 0) { staffed = true; break; }
                open ??= b;
            }
            if (staffed || open == null) continue;
            while (cursor < People.Count && !IsFreeLabourer(People[cursor])) cursor++;
            if (cursor < People.Count)
            {
                Person p = People[cursor++];
                p.Job = open.Id;
                open.Workers++;
                employed++;
            }
            else MoveSpareWorker(open);
        }
        return employed;
    }

    private void FillWorkplaces(int limit, int employed)
    {
        // Finished goods first: whatever is already piled up becomes food before anyone gathers more.
        BType[] priority = StoredTotal(Res.Flour) >= 12 || StoredTotal(Res.Grain) >= 24
            ? new[] { BType.Bakery, BType.Watermill, BType.Windmill, BType.Mill, BType.Fisher, BType.Gatherer, BType.Woodcutter, BType.Field, BType.Quarry, BType.Smithy, BType.Vineyard, BType.Winery }
            : new[] { BType.Fisher, BType.Gatherer, BType.Woodcutter, BType.Field, BType.Watermill, BType.Windmill, BType.Mill, BType.Bakery, BType.Quarry, BType.Smithy, BType.Vineyard, BType.Winery };
        int cursor = 0;
        int[] room = TradeCaps();
        foreach (Person p in People)
            if (p.Job >= 0 && TradeGroup(Buildings[p.Job].Type) is >= 0 and var held) room[held]--;
        employed = StaffCoreTrades(employed, ref cursor);
        foreach (BType type in priority)
        foreach (Building b in Buildings)
        {
            if (b.Type != type || !b.Built || b.Pump || !HasInputs(b)) continue;
            int trade = TradeGroup(type);
            if (trade >= 0 && room[trade] <= 0) continue;
            // Crafts must not pull farmers and food workers away from a village that is short of hands.
            bool craft = type is BType.Smithy or BType.Vineyard or BType.Winery;
            if (b.Workers == 0 && employed >= limit && !craft) MoveSpareWorker(b);
            // A fed village may put a few more adults to work in its crafts.
            int allowed = craft && FoodInStores() >= People.Count * 2 ? limit + Math.Max(2, limit / 4) : limit;
            while (b.Workers < Rules.WorkerSlots(type) && employed < allowed)
            {
                while (cursor < People.Count && !IsFreeLabourer(People[cursor])) cursor++;
                if (cursor >= People.Count) return;
                Person p = People[cursor++];
                p.Job = b.Id;
                b.Workers++;
                employed++;
                if (trade >= 0 && --room[trade] <= 0) break;
            }
        }
    }

    private void AssignRoles(int adults)
    {
        int pending = 0, builders = 0;
        foreach (Building b in Buildings)
            if (!b.Built) pending++;
        int want = Math.Clamp(pending * 2 + RoadQueue.Count / 6 + (DikingPolder() != null ? 4 : 0), adults >= 4 ? 1 : 0, Math.Max(1, adults / 4));
        foreach (Person p in People)
        {
            if (p.Age < 14 || p.Age >= 60) p.Role = Role.None;
            else if (p.Job >= 0) p.Role = Role.Worker;
            else if (p.Role == Role.Builder)
            {
                if (builders < want || p.Act is Act.Build or Act.Road) builders++;
                else p.Role = Role.Porter;
            }
        }
        foreach (Person p in People)
        {
            if (p.Age < 14 || p.Age >= 60 || p.Job >= 0 || p.Role == Role.Builder) continue;
            if (builders < want)
            {
                p.Role = Role.Builder;
                builders++;
            }
            else p.Role = Role.Porter;
        }
    }

    /// <summary>Staffs an empty building from a workplace that still has two workers.</summary>
    private void MoveSpareWorker(Building target)
    {
        foreach (Person p in People)
        {
            if (p.Job < 0 || p.Act == Act.Work || p.Job == target.Id) continue;
            Building current = Buildings[p.Job];
            if (current.Workers < 2) continue;
            current.Workers--;
            p.Job = target.Id;
            target.Workers++;
            return;
        }
    }

    private static bool IsFreeLabourer(Person p) =>
        p.Job < 0 && p.Age >= 14 && p.Age < 60 && p.Act is Act.Idle or Act.Wander or Act.Rest;

    private void Family()
    {
        Person single = null;
        foreach (Person p in People)
        {
            if (p.Spouse >= 0 || p.Age < 16 || p.Age > 45) continue;
            if (single == null) { single = p; continue; }
            if (single.Female == p.Female) continue;
            single.Spouse = p.Id;
            p.Spouse = single.Id;
            // Once there is a parish church, a wedding draws the village to its door (if no gathering was held lately),
            // and the couple themselves walk there unless they are busy with a load or a build.
            if (!Night && ChurchDoor() is (float wx, float wz) && Gather(wx, wz, 90))
            {
                Log("Wedding bells: the village gathers at the church", wx, wz, 2);
                foreach (Person partner in new[] { single, p })
                    if (partner.Act is Act.Idle or Act.Wander)
                    {
                        partner.Act = Act.Wander;
                        partner.Timer = 60f;
                        GoTo(partner, wx + (partner.Female ? 0.7f : -0.7f), wz);
                    }
            }
            single = null;
        }

        int food = FoodInStores();
        float security = Math.Clamp(food / (Math.Max(1, People.Count) * 3f), 0, 1);
        if (security < 0.4f || People.Count >= Eras.MaxPopulation(Era)) return;
        float room = (Eras.MaxPopulation(Era) - People.Count) / (Eras.MaxPopulation(Era) * 0.3f);
        float chance = 0.3f * WineBonus * security * Math.Clamp(room, 0.12f, 1f) / 360f;
        int count = People.Count;
        for (int i = 0; i < count && People.Count < Eras.MaxPopulation(Era); i++)
        {
            Person mother = People[i];
            if (!mother.Female || mother.Spouse < 0 || mother.Age < 16 || mother.Age > 42) continue;
            if (mother.Home < 0)
            {
                // A wild band raises its children in the cave, up to the size a band can feed on the move.
                if (!Wild || CaveDug < 0.5f || People.Count >= WildBand || Random.F() >= chance) continue;
                Spawn(mother.X, mother.Z, 0, Random.F() < 0.5f, mother.Id);
                Births++;
                continue;
            }
            if (Buildings[mother.Home].Occupants >= Capacity(Buildings[mother.Home])) RehouseCouple(mother);
            if (Buildings[mother.Home].Occupants >= Capacity(Buildings[mother.Home]) || Random.F() >= chance) continue;
            Person child = Spawn(mother.X, mother.Z, 0, Random.F() < 0.5f, mother.Id);
            child.Home = mother.Home;
            Buildings[mother.Home].Occupants++;
            Births++;
        }
    }

    private void Plan()
    {
        // Checked before the wait below, which could otherwise skip these ticks forever.
        if (Era >= 4 && People.Count >= 90 && Tick % 3000 == 0) TryStartPolder();
        if (Wild) return;
        if (PlanWait > 0)
        {
            PlanWait--;
            return;
        }
        int pop = People.Count, pending = 0, capacity = 0, homeless = 0;
        foreach (Building b in Buildings)
        {
            if (!b.Built) pending++;
            if (b.Type == BType.Hut) capacity += Capacity(b);
        }
        if (pending >= 1 + pop / 40) return;
        foreach (Person p in People)
            if (p.Home < 0) homeless++;

        int C(BType t) => Count(t);
        int fields = C(BType.Field);
        bool woodShort = StoredTotal(Res.Wood) < 30 + pop / 2;
        var wanted = new List<BType>(8);
        void Want(bool condition, BType type) { if (condition) wanted.Add(type); }
        Want(C(BType.Woodcutter) < 1, BType.Woodcutter);
        Want(C(BType.Gatherer) < (Era == 0 ? 2 + pop / 14 : 2), BType.Gatherer);
        Want(C(BType.Fisher) < (Era == 0 ? 1 + pop / 16 : 1), BType.Fisher);
        Want(woodShort && C(BType.Woodcutter) < 1 + pop / 12, BType.Woodcutter);
        Want(homeless > 0 || capacity < pop + 6 + pop / 5, BType.Hut);
        Want(C(BType.Quarry) < 1 && pop >= 14, BType.Quarry);
        Want(pop >= 18 && fields < Math.Max(1, pop / 10), BType.Field);
        // Polder pumps are windmills too, but they drain water and grind nothing.
        int pumps = 0;
        foreach (Building b in Buildings) if (b.Pump) pumps++;
        int grindingWindmills = C(BType.Windmill) - pumps;
        int grinders = C(BType.Mill) + grindingWindmills + C(BType.Watermill);
        Want(fields >= 1 && grinders < (fields + 1) / 2, BType.Mill);
        Want(fields >= 1 && C(BType.Bakery) < (fields + 1) / 2, BType.Bakery);
        Want(StoredTotal(Res.Grain) > 60 + pop / 2 && grinders < 2 + pop / 18, BType.Mill);
        Want(StoredTotal(Res.Flour) > 40 + pop / 3 && C(BType.Bakery) < 2 + pop / 18, BType.Bakery);
        Want(C(BType.Woodcutter) < 1 + pop / 25, BType.Woodcutter);
        Want(C(BType.Fisher) < Math.Min(4, 1 + pop / 25), BType.Fisher);
        Want(C(BType.Gatherer) < Math.Min(4, 1 + pop / 15), BType.Gatherer);
        Want(C(BType.Quarry) < 1 + pop / 80, BType.Quarry);
        Want(C(BType.Storehouse) < 1 + pop / 120, BType.Storehouse);
        // Each era's signature works are raised once materials allow, even in a lean year.
        if (StoredTotal(Res.Wood) >= 20 && StoredTotal(Res.Stone) >= 14 && FoodInStores() >= pop / 2)
        {
            Want(C(BType.Smithy) < 1, BType.Smithy);
            Want(C(BType.Vineyard) < 1, BType.Vineyard);
            Want(C(BType.Vineyard) >= 1 && C(BType.Winery) < 1, BType.Winery);
            Want(C(BType.Watermill) < 1, BType.Watermill);
            Want(C(BType.Dike) < 4, BType.Dike);
            Want(grindingWindmills < 1, BType.Windmill);
            Want(C(BType.Dike) >= 2 && C(BType.TideGate) < 1, BType.TideGate);
        }
        // Crafts and water works wait until the village can feed itself.
        if (StoredTotal(Res.Wood) >= 28 && StoredTotal(Res.Stone) >= 14 && FoodInStores() >= pop)
        {
            Want(pop >= 30 && C(BType.Vineyard) < 1 + pop / 70, BType.Vineyard);
            Want(C(BType.Vineyard) >= 1 && C(BType.Winery) < 1 + pop / 140, BType.Winery);
            Want(pop >= 36 && C(BType.Smithy) < 1 + pop / 150, BType.Smithy);
            Want(pop >= 40 && C(BType.Watermill) < 1 + pop / 120, BType.Watermill);
            Want(pop >= 48 && grindingWindmills < 1 + pop / 90, BType.Windmill);
            Want(C(BType.Dike) >= 4 && C(BType.TideGate) < 1, BType.TideGate);
            Want(pop >= 44 && C(BType.Dike) < 4 + pop / 25, BType.Dike);
        }
        bool blocked = false, placed = false;
        foreach (BType type in wanted)
        {
            if (Eras.UnlockEra(type) > Era) continue;
            bool bank = Rules.NeedsBank(type);
            int tileX, tileZ, direction;
            if (bank ? !FindBankSite(type, out tileX, out tileZ, out direction) : !FindSite(type, out tileX, out tileZ, out direction))
            {
                if (type == BType.TideGate && RebuildDikeAsGate() || type == BType.Dike && DikeOnField())
                {
                    placed = true;
                    break;
                }
                blocked |= !bank;
                continue;
            }
            AddBuilding(type, tileX, tileZ, direction, false);
            placed = true;
            break;
        }
        if (blocked && RoadQueue.Count < 8) ExtendRoad();
        if (!placed && wanted.Count > 0) PlanWait = 4;
    }

    /// <summary>
    /// On a bank already lined with houses and fields, the village gives up a riverside field for the next stretch of
    /// dike: the dike takes the water-side row of the field and the rest is open ground again.
    /// </summary>
    private bool DikeOnField()
    {
        int fields = 0, size = Rules.Footprint(BType.Dike), fieldSize = Rules.Footprint(BType.Field);
        Building chosen = null;
        float best = 26f;
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Field) continue;
            fields++;
            if (!b.Built || InPolder(b.TileX, b.TileZ)) continue;
            int fx = b.TileX + (fieldSize - size) / 2, fz = b.TileZ;
            float cx = (fx + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2, cz = (fz + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
            float bank = MathF.Abs(Map.RiverOffset(cx, cz)) - WorldMap.EstuaryWidth(cx);
            if (bank >= best || Map.Height(cx, cz) < 0.35f) continue;
            bool busy = false;
            for (int r = 0; r < b.In.Length && !busy; r++) busy = b.In[r] != 0 || b.Out[r] != 0 || b.Stock[r] != 0;
            foreach (Person p in People)
                if (p.Job == b.Id && p.Act == Act.Work || p.A == b.Id || p.B == b.Id) { busy = true; break; }
            if (!busy) (best, chosen) = (bank, b);
        }
        if (chosen == null || fields < 4) return false;
        foreach (Person p in People)
            if (p.Job == chosen.Id) p.Job = -1;
        chosen.Workers = 0;
        for (int z = chosen.TileZ; z < chosen.TileZ + fieldSize; z++)
        for (int x = chosen.TileX; x < chosen.TileX + fieldSize; x++)
            Tiles[x + z * Rules.TileNx] = TileKind.Free;
        int dx = chosen.TileX + (fieldSize - size) / 2, dz = chosen.TileZ;
        (int doorX, int doorZ) = DoorOf(dx, dz, size, 0);
        chosen.Type = BType.Dike;
        (chosen.TileX, chosen.TileZ, chosen.Direction, chosen.Rotation) = (dx, dz, 0, 0);
        chosen.X = (dx + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2;
        chosen.Z = (dz + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
        chosen.DoorTile = doorX + doorZ * Rules.TileNx;
        (chosen.Built, chosen.Progress, chosen.Yield, chosen.Sown, chosen.Growth) = (false, 0, 0, false, 0);
        for (int z = dz; z < dz + size; z++)
        for (int x = dx; x < dx + size; x++)
        {
            int lot = x + z * Rules.TileNx;
            if (Tiles[lot] == TileKind.PlannedRoad) RoadQueue.Remove(lot);
            Tiles[lot] = TileKind.Building;
        }
        PlanRoad(chosen.DoorTile);
        int[] link = FindPath(chosen.DoorTile, -1, true);
        if (link != null)
            foreach (int tile in link) PlanRoad(tile);
        Log("A riverside field gives way to a new stretch of dike", chosen.X, chosen.Z, 1);
        BuildingVersion++;
        RoadVersion++;
        return true;
    }

    /// <summary>On a crowded bank, the tide gate replaces a finished dike on the same footprint.</summary>
    private bool RebuildDikeAsGate()
    {
        Building chosen = null;
        foreach (Building b in Buildings)
            if (b.Type == BType.Dike && b.Built && (chosen == null || b.X > chosen.X)) chosen = b;
        if (chosen == null) return false;
        chosen.Type = BType.TideGate;
        chosen.Built = false;
        chosen.Progress = 0;
        chosen.Workers = 0;
        BuildingVersion++;
        return true;
    }

    private int StoredTotal(Res r)
    {
        int total = 0;
        foreach (Building b in Buildings)
            if (b.Type == BType.Storehouse) total += b.Avail(r);
        return total;
    }

    private static bool OutputFull(Building b) =>
        Rules.OutputOf(b.Type) is Res output && b.Avail(output) >= (b.Type == BType.Field ? 24 : 12);

    private bool HasSurplus(BType type)
    {
        int pop = People.Count;
        return type switch
        {
            BType.Field => StoredTotal(Res.Grain) >= 300 + pop * 3,
            BType.Gatherer => StoredTotal(Res.Berries) >= 80 + pop * 4,
            BType.Fisher => StoredTotal(Res.Fish) >= 80 + pop * 4,
            BType.Quarry => StoredTotal(Res.Stone) >= 70 + pop,
            BType.Woodcutter => StoredTotal(Res.Wood) >= 120 + pop * 3,
            BType.Mill or BType.Windmill or BType.Watermill => StoredTotal(Res.Flour) >= 40 + pop,
            BType.Vineyard => StoredTotal(Res.Grapes) >= 30,
            BType.Winery => StoredTotal(Res.Wine) >= 40,
            BType.Smithy => StoredTotal(Res.Tools) >= 20,
            BType.Bakery => StoredTotal(Res.Bread) >= 60 + pop * 6,
            _ => false,
        };
    }

    private bool FindSite(BType type, out int tileX, out int tileZ, out int direction)
    {
        tileX = tileZ = direction = 0;
        var roads = new List<int>();
        for (int i = 0; i < Tiles.Length; i++)
            if (Tiles[i] is TileKind.Road or TileKind.PlannedRoad) roads.Add(i);
        if (roads.Count == 0) return false;
        int size = Rules.Footprint(type);
        float best = float.MinValue;
        bool found = false;
        for (int attempt = 0; attempt < 240; attempt++)
        {
            int door = roads[Random.N(roads.Count)], d = Random.N(4);
            (int fx, int fz) = FootprintForDoor(door % Rules.TileNx, door / Rules.TileNx, size, d);
            if (!FootprintFree(fx, fz, size, true)) continue;
            float cx = (fx + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2, cz = (fz + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
            if (!Map.Walkable(cx, cz) || Map.Height(cx, cz) < 0.7f || Map.RiverOffset(cx, cz) < 8) continue;
            float slope = Map.Slope(cx, cz);
            if (slope > (type == BType.Field ? 0.2f : 0.34f)) continue;
            float distance = MathF.Sqrt(Dist2(cx, cz, CenterX, CenterZ)), river = MathF.Abs(Map.RiverOffset(cx, cz));
            // Homes gather into a dense quarter around the centre; fields and vineyards keep to its edge.
            int homes = type is BType.Hut or BType.Field or BType.Vineyard ? HomesNear(cx, cz, 20f) : 0;
            bool town = Era >= 4; // early villages keep fields beside the homes, which their food chain needs
            float score = -distance * 0.22f - slope * 18 + type switch
            {
                BType.Hut => town ? -distance * 0.2f + homes * 3.5f : 0f,
                BType.Vineyard => town ? distance * 0.12f - homes * 3f : 0f,
                BType.Field => -river * 0.16f + distance * (town ? 0.18f : 0.1f) - (town ? homes * 3f : 0f) + (InPolder(fx, fz) ? 30f : 0f),
                BType.Fisher => -river * 0.5f,
                BType.Woodcutter => TreesNear(cx, cz) * 0.9f,
                BType.Quarry => Map.Height(cx, cz) * 0.8f,
                BType.Windmill => distance * 0.2f - MathF.Max(0, Map.CoastX(cz) - cx) * 0.2f,
                _ => 0f,
            };
            if (score > best) (best, tileX, tileZ, direction, found) = (score, fx, fz, d, true);
        }
        return found || FindOpenSite(type, size, out tileX, out tileZ, out direction);
    }

    /// <summary>Finds a site on the water's edge. Door faces inland, so the back of the building meets the water.</summary>
    private bool FindBankSite(BType type, out int tileX, out int tileZ, out int direction)
    {
        tileX = tileZ = direction = 0;
        int size = Rules.Footprint(type), pathTries = 0;
        float left = CenterX - 120, right = Math.Min(CenterX + 150, Map.CoastX(CenterZ) - 10);
        List<Building> dikes = type is BType.Dike or BType.TideGate ? Buildings.FindAll(static b => b.Type == BType.Dike) : null;
        for (int attempt = 0; attempt < 96 && pathTries < 14; attempt++)
        {
            bool relaxed = attempt >= 48;
            float x = dikes is { Count: > 0 } && attempt < 32
                ? dikes[Random.N(dikes.Count)].X + (Random.N(2) == 0 ? -1 : 1) * size * Rules.TileSize
                : type == BType.TideGate ? Random.R(Math.Max(left, right - 120), right) : Random.R(left, right);
            (float bankX, float bankZ) = BankPoint(x);
            for (int step = 0; step < (relaxed ? 12 : 5); step++)
            {
                int tile = TileAt(bankX, bankZ + step * Rules.TileSize);
                int fx = tile % Rules.TileNx - size / 2, fz = tile / Rules.TileNx;
                if (!FootprintFree(fx, fz, size)) continue;
                (int doorX, int doorZ) = DoorOf(fx, fz, size, 0);
                if (!InGrid(doorX, doorZ)) continue;
                int door = doorX + doorZ * Rules.TileNx;
                if (Tiles[door] is not (TileKind.Free or TileKind.Road or TileKind.PlannedRoad)) continue;
                float cx = (fx + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2, cz = (fz + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
                if (Map.Height(cx, cz) < (relaxed ? 0.35f : 0.7f) || Map.Slope(cx, cz) > (relaxed ? 0.65f : 0.4f)) continue;
                bool onRoad = Tiles[door] is TileKind.Road or TileKind.PlannedRoad;
                pathTries++;
                int[] link = onRoad ? Array.Empty<int>() : FindPath(door, -1, true, relaxed ? 9000 : 4000);
                if (link == null || link.Length > (relaxed ? 150 : type == BType.TideGate ? 110 : 70)) break;
                (tileX, tileZ) = (fx, fz);
                return true;
            }
        }
        return false;
    }

    /// <summary>Finds free land away from current streets. AddBuilding then lays a road to its door.</summary>
    private bool FindOpenSite(BType type, int size, out int tileX, out int tileZ, out int direction)
    {
        tileX = tileZ = direction = 0;
        float radius = 50 + 13 * MathF.Sqrt(Buildings.Count), maxSlope = type == BType.Field ? 0.2f : 0.34f;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            float angle = Random.R(0, MathF.Tau), distance = MathF.Sqrt(Random.F()) * radius;
            int tile = TileAt(CenterX + MathF.Cos(angle) * distance, CenterZ + MathF.Sin(angle) * distance * 0.75f);
            int fx = tile % Rules.TileNx, fz = tile / Rules.TileNx, d = Random.N(4);
            if (!FootprintFree(fx, fz, size, true)) continue;
            (int doorX, int doorZ) = DoorOf(fx, fz, size, d);
            if (!InGrid(doorX, doorZ)) continue;
            int door = doorX + doorZ * Rules.TileNx;
            if (Tiles[door] is not (TileKind.Free or TileKind.Road or TileKind.PlannedRoad)) continue;
            float cx = (fx + size / 2f) * Rules.TileSize - WorldMap.SizeX / 2, cz = (fz + size / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
            if (!Map.Walkable(cx, cz) || Map.Height(cx, cz) < 0.7f || Map.RiverOffset(cx, cz) < 8 || Map.Slope(cx, cz) > maxSlope) continue;
            bool onRoad = Tiles[door] is TileKind.Road or TileKind.PlannedRoad;
            int[] link = onRoad ? Array.Empty<int>() : FindPath(door, -1, true, 1200);
            if (link == null || link.Length > 34) continue;
            (tileX, tileZ, direction) = (fx, fz, d);
            return true;
        }
        return false;
    }

    /// <summary>
    /// From the Middle Ages the town grows over its oldest inner fields: once a year one idle field surrounded by
    /// homes becomes four houses around a new lane. Farming then moves to new fields at the edge.
    /// </summary>
    /// <summary>Inner fields turned into houses so far (diagnostics only).</summary>
    public int Urbanized, UrbanSkips;

    private void UrbanizeField()
    {
        int fields = 0;
        Building chosen = null;
        float best = float.MaxValue;
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Field) continue;
            fields++;
            if (!b.Built || InPolder(b.TileX, b.TileZ) || HomesNear(b.X, b.Z, 32f) < 3) continue;
            // A field the railway runs through stays a field: houses would stand on the line.
            if (RailRow >= 0 && b.TileZ <= RailRow && RailRow < b.TileZ + Rules.Footprint(BType.Field)) continue;
            bool busy = false;
            for (int r = 0; r < b.In.Length && !busy; r++) busy = b.In[r] != 0 || b.Out[r] != 0 || b.Stock[r] != 0; // grain must be carried away first
            foreach (Person p in People)
                if ((p.Job == b.Id && p.Act == Act.Work) || p.A == b.Id || p.B == b.Id || p.Home == b.Id) { busy = true; break; }
            float d = Dist2(b.X, b.Z, CenterX, CenterZ);
            if (!busy && d < best) (best, chosen) = (d, b);
        }
        // Only a town with grain to spare gives up a field.
        if (chosen == null || fields < 4 || !HasSurplus(BType.Field))
        {
            UrbanSkips++;
            return;
        }
        Urbanized++;
        // Its farmers are free to take a field at the edge of town.
        foreach (Person p in People)
            if (p.Job == chosen.Id) p.Job = -1;
        chosen.Workers = 0;

        int tx = chosen.TileX, tz = chosen.TileZ, size = Rules.Footprint(BType.Field);
        for (int z = tz; z < tz + size; z++)
        for (int x = tx; x < tx + size; x++)
            Tiles[x + z * Rules.TileNx] = TileKind.Free;
        // Four 2x2 houses on the corners face the middle row, which becomes the lane.
        (int X, int Z, int Dir)[] corners = { (tx, tz, 0), (tx + 3, tz, 0), (tx, tz + 3, 2), (tx + 3, tz + 3, 2) };
        foreach ((int x, int z, int dir) in corners[1..]) AddBuilding(BType.Hut, x, z, dir, false);

        // The field itself becomes the first house, so its id stays valid.
        (int hx, int hz, int hd) = corners[0];
        int hutSize = Rules.Footprint(BType.Hut);
        (int doorX, int doorZ) = DoorOf(hx, hz, hutSize, hd);
        chosen.Type = BType.Hut;
        chosen.TileX = hx;
        chosen.TileZ = hz;
        chosen.Direction = hd;
        chosen.Rotation = hd * MathF.PI / 2;
        chosen.X = (hx + hutSize / 2f) * Rules.TileSize - WorldMap.SizeX / 2;
        chosen.Z = (hz + hutSize / 2f) * Rules.TileSize - WorldMap.SizeZ / 2;
        chosen.DoorTile = doorX + doorZ * Rules.TileNx;
        chosen.Built = false;
        chosen.Progress = 0;
        chosen.Yield = 0;
        for (int z = hz; z < hz + hutSize; z++)
        for (int x = hx; x < hx + hutSize; x++)
        {
            // The other houses' road links may have planned a road across this lot before it was claimed.
            int lot = x + z * Rules.TileNx;
            if (Tiles[lot] == TileKind.PlannedRoad) RoadQueue.Remove(lot);
            Tiles[lot] = TileKind.Building;
        }
        PlanRoad(chosen.DoorTile);
        int[] link = FindPath(chosen.DoorTile, -1, true);
        if (link != null)
            foreach (int tile in link) PlanRoad(tile);
        BuildingVersion++;
        RoadVersion++;
    }

    private int HomesNear(float x, float z, float radius)
    {
        int n = 0;
        foreach (Building b in Buildings)
            if (b.Type == BType.Hut && Dist2(x, z, b.X, b.Z) < radius * radius) n++;
        return n;
    }

    private int TreesNear(float x, float z)
    {
        int n = 0;
        for (int i = 0; i < TreeX.Length && n < 40; i += 3)
            if (TreeAlive[i] && Dist2(x, z, TreeX[i], TreeZ[i]) < 2500) n++;
        return n;
    }

    private void UpdateWear()
    {
        foreach (Person p in People)
        {
            if (!p.Moving) continue;
            int x = (int)((p.X + WorldMap.SizeX / 2) / 5f), z = (int)((p.Z + WorldMap.SizeZ / 2) / 5f);
            if (x < 0 || z < 0 || x >= WearNx || z >= WearNz) continue;
            int i = x + z * WearNx;
            if (Wear[i] < 255 && Cosmetic.F() < 0.25f) Wear[i]++;
        }
    }

    /// <summary>From the Iron Age a trading ship visits every third year, in its spring and early summer. Decorative: no goods change hands.</summary>
    public bool TraderVisiting => Era >= 3 && (int)Year % 3 == 1 && YearFraction is > 0.05f and < 0.45f;

    /// <summary>
    /// From the Middle Ages the most-trodden streets near the centre are widened: each year up to six busy road
    /// tiles get a free neighbour across the street planned as road, so main streets grow to two lanes.
    /// </summary>
    private void WidenBusyStreets()
    {
        if (RoadQueue.Count > 24) return;
        int cx = (int)((CenterX + WorldMap.SizeX / 2) / Rules.TileSize), cz = (int)((CenterZ + WorldMap.SizeZ / 2) / Rules.TileSize);
        var busy = new List<(int Wear, int Tile)>();
        for (int z = Math.Max(1, cz - 24); z < Math.Min(Rules.TileNz - 1, cz + 24); z++)
        for (int x = Math.Max(1, cx - 24); x < Math.Min(Rules.TileNx - 1, cx + 24); x++)
        {
            int tile = x + z * Rules.TileNx;
            if (Tiles[tile] != TileKind.Road) continue;
            float wx = (x + 0.5f) * Rules.TileSize, wz = (z + 0.5f) * Rules.TileSize;
            int cell = (int)(wx / (WorldMap.SizeX / (float)WearNx)) + (int)(wz / (WorldMap.SizeZ / (float)WearNz)) * WearNx;
            if (cell >= 0 && cell < Wear.Length && Wear[cell] >= 90) busy.Add((Wear[cell], tile));
        }
        busy.Sort((a, b) => b.Wear != a.Wear ? b.Wear.CompareTo(a.Wear) : a.Tile.CompareTo(b.Tile));
        int planned = 0;
        foreach ((_, int tile) in busy)
        {
            if (planned >= 6) break;
            int nx = Rules.TileNx;
            bool alongX = Tiles[tile - 1] is TileKind.Road or TileKind.PlannedRoad || Tiles[tile + 1] is TileKind.Road or TileKind.PlannedRoad;
            int[] across = alongX ? new[] { tile + nx, tile - nx } : new[] { tile + 1, tile - 1 };
            // Already two lanes wide here.
            if (Tiles[across[0]] is TileKind.Road or TileKind.PlannedRoad || Tiles[across[1]] is TileKind.Road or TileKind.PlannedRoad) continue;
            foreach (int side in across)
                if (Tiles[side] == TileKind.Free && Map.Walkable(TileCenter(side).X, TileCenter(side).Z))
                {
                    PlanRoad(side);
                    planned++;
                    StreetsWidened++;
                    break;
                }
        }
    }

    /// <summary>
    /// From the Industrial era, one old house near the centre a year is pulled down and rebuilt larger on the same plot.
    /// Its family stays on; builders and porters treat it as a site until it stands again.
    /// </summary>
    private void RenewOldHouse()
    {
        Building chosen = null;
        float best = 80f * 80f;
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Hut) continue;
            if (b.Renewed && !b.Built) return; // one rebuild at a time
            if (!b.Built || b.Renewed) continue;
            float d = Dist2(b.X, b.Z, CenterX, CenterZ);
            if (d < best) (best, chosen) = (d, b);
        }
        if (chosen == null) return;
        // The family moves out first, but only if the other homes have room for them.
        int room = 0;
        foreach (Building b in Buildings)
            if (b.Type == BType.Hut && b.Built && b != chosen) room += Math.Max(0, Capacity(b) - b.Occupants);
        if (room < chosen.Occupants) return;
        foreach (Person p in People)
            if (p.Home == chosen.Id) p.Home = -1;
        chosen.Occupants = 0;
        AssignHomes();
        chosen.Renewed = true;
        chosen.Built = false;
        chosen.Warm = false;
        chosen.Progress = 0.2f;
        chosen.Workers = 0;
        BuildingVersion++;
        Log("A family moves out and their old house is pulled down to be rebuilt larger", chosen.X, chosen.Z, 1);
    }

    /// <summary>Town landmarks on their own lots (0 church, 1 water tower, 2 boiler house, 3 factory, 4 railway station, 5 quay). Not buildings: nobody works there.</summary>
    public readonly record struct Landmark(int Kind, int TileX, int TileZ, int HalfX, int HalfZ, float Yaw);
    public readonly List<Landmark> Landmarks = new();

    private bool LotFree(int x, int z, int hx, int hz, bool keepCorridors = true)
    {
        for (int dz = -hz; dz <= hz; dz++)
        for (int dx = -hx; dx <= hx; dx++)
        {
            int tx = x + dx, tz = z + dz;
            if (!InGrid(tx, tz) || Tiles[tx + tz * Rules.TileNx] != TileKind.Free || (keepCorridors && OnCorridor(tx, tz))) return false;
            (float wx, float wz) = TileCenter(tx + tz * Rules.TileNx);
            if (!Map.Walkable(wx, wz)) return false;
        }
        return true;
    }

    /// <summary>
    /// Where a railway along this row, running west from tile x1, would enter the hills: the first tile that cannot be
    /// walked on (the line goes into a tunnel there). -1 if a building or a lot stands in the way first, or if the
    /// open stretch is shorter than 25 tiles.
    /// </summary>
    private int RailEnd(int x1, int row)
    {
        if (row < 0 || row >= Rules.TileNz || x1 >= Rules.TileNx) return -1;
        for (int x = x1; x >= 0; x--)
        {
            int tile = x + row * Rules.TileNx;
            (float wx, float wz) = TileCenter(tile);
            // The line ends where the hills begin, in a tunnel; never at a river bank or creek.
            if (!Map.Walkable(wx, wz)) return x1 - x >= 25 && Map.Height(wx, wz) > 3f ? x + 1 : -1;
            if (Tiles[tile] is not (TileKind.Free or TileKind.Road or TileKind.PlannedRoad or TileKind.Field or TileKind.Blocked)) return -1;
        }
        return x1 >= 25 ? 0 : -1;
    }

    /// <summary>Lays the line: open ground becomes track; streets keep their surface as level crossings.</summary>
    private void LayRail(int stationX, int stationZ, int row, int toCentre)
    {
        (RailRow, RailX0, RailX1) = (row, RailEnd(stationX + 4, row), stationX + 4);
        for (int x = RailX0; x <= RailX1; x++)
        {
            int tile = x + row * Rules.TileNx;
            if (Tiles[tile] != TileKind.Free) continue;
            Tiles[tile] = TileKind.Rail;
            ClearTrees(tile);
        }
        RoadVersion++;
        (float gx, float gz) = TileCenter(stationX + (stationZ + 2 * toCentre) * Rules.TileNx);
        Gather(gx, gz, 150, true);
        Log("The railway reaches the town: the first train pulls into the new station", gx, gz, 3);
    }

    private void AddLandmark(int kind, int x, int z, int hx, int hz, float yaw)
    {
        for (int dz = -hz; dz <= hz; dz++)
        for (int dx = -hx; dx <= hx; dx++)
        {
            int tile = x + dx + (z + dz) * Rules.TileNx;
            RoadQueue.Remove(tile);
            Tiles[tile] = TileKind.Building;
            ClearTrees(tile);
        }
        Landmarks.Add(new Landmark(kind, x, z, hx, hz, yaw));
        RoadVersion++;
    }

    /// <summary>
    /// At each new year: a parish church from the Middle Ages on the free lot nearest the centre, a water tower from
    /// the Industrial era on the highest free ground nearby, a boiler house beside it in the 20th century.
    /// Deterministic scans, no random draws.
    /// </summary>
    private void PlaceLandmarks()
    {
        bool Has(int kind) => Landmarks.Exists(l => l.Kind == kind);
        int cx = (int)((CenterX + WorldMap.SizeX / 2) / Rules.TileSize), cz = (int)((CenterZ + WorldMap.SizeZ / 2) / Rules.TileSize);
        if (Era >= 4 && !Has(0))
        {
            // Nearest free lot, lying north-south or east-west; corridor land only if nothing else is left.
            int best = int.MaxValue, bx = 0, bz = 0;
            bool along = true;
            for (int pass = 0; pass < 2 && best == int.MaxValue; pass++)
            for (int z = cz - 40; z <= cz + 40; z++)
            for (int x = cx - 40; x <= cx + 40; x++)
            {
                int d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (d >= best) continue;
                if (LotFree(x, z, 1, 3, pass == 0)) (best, bx, bz, along) = (d, x, z, true);
                else if (LotFree(x, z, 3, 1, pass == 0)) (best, bx, bz, along) = (d, x, z, false);
            }
            if (best < int.MaxValue)
                AddLandmark(0, bx, bz, along ? 1 : 3, along ? 3 : 1, along ? (bz > cz ? 0 : MathF.PI) : (bx > cx ? MathF.PI / 2 : -MathF.PI / 2));
        }
        if (Era >= 6 && !Has(1))
        {
            float best = float.MinValue;
            int bx = 0, bz = 0;
            for (int z = cz - 45; z <= cz + 45; z++)
            for (int x = cx - 45; x <= cx + 45; x++)
            {
                if (!InGrid(x, z)) continue;
                (float wx, float wz) = TileCenter(x + z * Rules.TileNx);
                float h = Map.Height(wx, wz) - 0.004f * ((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (h > best && LotFree(x, z, 1, 1, false)) (best, bx, bz) = (h, x, z);
            }
            if (best > float.MinValue) AddLandmark(1, bx, bz, 1, 1, 0);
        }
        // A factory from the Industrial era, lying east-west, its gate (+Z) facing the centre.
        if (Era >= 6 && !Has(3))
        {
            int best = int.MaxValue, bx = 0, bz = 0;
            for (int pass = 0; pass < 2 && best == int.MaxValue; pass++)
            for (int z = cz - 45; z <= cz + 45; z++)
            for (int x = cx - 45; x <= cx + 45; x++)
            {
                int d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (d < best && LotFree(x, z, 3, 1, pass == 0)) (best, bx, bz) = (d, x, z);
            }
            if (best < int.MaxValue) AddLandmark(3, bx, bz, 3, 1, bz < cz ? 0 : MathF.PI);
        }
        // A quay from the Renaissance: the strip of bank just downstream of where the trading ship anchors and the crowd
        // watches (CenterX + 25), so nothing else is built where the quay stands.
        if (Era >= 5 && !Has(5))
        {
            float qx = CenterX + 41, river = Map.RiverCenter(qx), side = MathF.Sign(CenterZ - river);
            for (float d = 0; d <= 120; d += 2)
                if (Map.Walkable(qx, river + side * d))
                {
                    int tile = TileAt(qx, river + side * (d + 2));
                    int tx = tile % Rules.TileNx, tz = tile / Rules.TileNx;
                    // Nearest free strip: along the bank first, then a row or two further inland.
                    bool placed = false;
                    for (int back = 0; back <= 2 && !placed; back++)
                    for (int shift = 0; shift <= 20 && !placed; shift++)
                    {
                        int x = tx + (shift % 2 == 0 ? shift / 2 : -(shift + 1) / 2), z = tz + (side > 0 ? back : -back);
                        // Keep clear of the crowd at CenterX + 25: the 18 m quay must start downstream of it.
                        if (TileCenter(x + z * Rules.TileNx).X < CenterX + 36 || !LotFree(x, z, 2, 0, false)) continue;
                        AddLandmark(5, x, z, 2, 0, side > 0 ? MathF.PI : 0);
                        placed = true;
                    }
                    break;
                }
        }
        // A railway station in the 20th century, laid out like the factory: east-west, entrance facing the centre.
        // The line runs west along the row behind it into a tunnel in the hills, so the station goes where that row is
        // clear of buildings (the town's fringe, as real stations did). Without such a lot it stands alone, with no line.
        if (Era >= 7 && !Has(4))
        {
            int best = int.MaxValue, bx = 0, bz = 0;
            bool line = false;
            for (int pass = 0; pass < 3 && best == int.MaxValue; pass++)
            {
                int reach = pass < 2 ? 60 : 45;
                for (int z = cz - reach; z <= cz + reach; z++)
                for (int x = cx - reach; x <= cx + reach; x++)
                {
                    int d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                    if (d >= best || !LotFree(x, z, 3, 1, pass == 0)) continue;
                    if (pass < 2 && RailEnd(x + 4, z + (z < cz ? -2 : 2)) < 0) continue;
                    (best, bx, bz, line) = (d, x, z, pass < 2);
                }
            }
            if (best < int.MaxValue)
            {
                AddLandmark(4, bx, bz, 3, 1, bz < cz ? 0 : MathF.PI);
                if (line) LayRail(bx, bz, bz + (bz < cz ? -2 : 2), bz < cz ? 1 : -1);
            }
        }
        // Fields along the line that have since been built over or cleared get their track too.
        if (RailRow >= 0)
            for (int x = RailX0; x <= RailX1; x++)
                if (Tiles[x + RailRow * Rules.TileNx] == TileKind.Free) Tiles[x + RailRow * Rules.TileNx] = TileKind.Rail;
        if (Era >= 7 && !Has(2) && Landmarks.Find(l => l.Kind == 1) is { Kind: 1 } tower)
        {
            // The free lot nearest the water tower, within 10 tiles.
            int best = int.MaxValue, bx = 0, bz = 0;
            for (int dz = -10; dz <= 10; dz++)
            for (int dx = -10; dx <= 10; dx++)
            {
                int d = dx * dx + dz * dz;
                if (d >= 9 && d < best && LotFree(tower.TileX + dx, tower.TileZ + dz, 1, 1, false)) (best, bx, bz) = (d, tower.TileX + dx, tower.TileZ + dz);
            }
            if (best < int.MaxValue) AddLandmark(2, bx, bz, 1, 1, 0);
        }
    }
    private void NewYear()
    {
        if (Era >= 4) WidenBusyStreets();
        if (Era >= 6) RenewOldHouse();
        PlaceLandmarks();
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Field) continue;
            if (b.Yield > 0) b.Yield = 0;
            b.Sown = false;
            b.Growth = 0;
        }
        if (Era >= 4) UrbanizeField();
        for (int i = 0; i < TreeAlive.Length; i++)
            if (!TreeAlive[i] && Random.F() < 0.2f && !OnBuildingSite(TreeX[i], TreeZ[i]))
            {
                TreeAlive[i] = true;
                TreeVersion++;
            }
        for (int i = 0; i < Wear.Length; i++) Wear[i] = (byte)(Wear[i] * 0.86f);

        HarvestFactor = 1;
        ColdFactor = 1;
        ActiveEvent = 0;
        float roll = Random.F();
        if (Year >= 2)
        {
            if (roll < 0.14f)
            {
                HarvestFactor = 0.55f;
                ActiveEvent = 1;
                (float dx, float dz) = PlaceOf(BType.Field);
                Log("Drought ruins this year's harvest", dx, dz, 3);
            }
            else if (roll < 0.28f)
            {
                ColdFactor = 1.5f;
                ActiveEvent = 2;
                Log("A harsh winter is coming", CenterX, CenterZ, 3);
            }
            else if (roll < 0.36f)
            {
                ActiveEvent = 3;
                (float sx, float sz) = (CenterX, CenterZ);
                bool first = true;
                // Clean water limits an outbreak: town fountains from the Middle Ages, then homes on piped and sewered streets.
                float town = Era >= 4 ? 0.12f : 0.18f;
                foreach (Person p in People)
                {
                    float reach = town;
                    if (Era >= 6 && p.Home >= 0)
                    {
                        int piped = PipedNear(Buildings[p.Home].DoorTile);
                        reach = piped >= 2 ? 0.03f : piped == 1 ? 0.05f : 0.12f;
                    }
                    if (Random.F() < reach)
                    {
                        p.Health = Math.Max(0.08f, p.Health - 0.42f);
                        if (first) (sx, sz, first) = (p.X, p.Z, false);
                    }
                }
                Log("Sickness spreads through the village", sx, sz, 3);
            }
            else if (roll < 0.52f)
            {
                HarvestFactor = 1.35f;
                ActiveEvent = 4;
                (float hx, float hz) = PlaceOf(BType.Storehouse);
                if (Gather(hx, hz, 120))
                {
                    Log("A bumper harvest: the village holds a feast", hx, hz, 2);
                    (FestiveUntil, FestiveKind) = (GatherUntil, 1);
                }
            }
            else if (roll < 0.58f && Era >= 4 && FireBuilding < 0)
            {
                // Some time between late spring and autumn a house catches fire. The same roll picks which one.
                float share = (roll - 0.52f) / 0.06f;
                int huts = 0;
                foreach (Building b in Buildings)
                    if (b.Type == BType.Hut && b.Built && b.Occupants > 0) huts++;
                int pick = (int)(share * huts);
                foreach (Building b in Buildings)
                    if (b.Type == BType.Hut && b.Built && b.Occupants > 0 && pick-- == 0) { FireBuilding = b.Id; break; }
                FireAt = Tick + (long)(Rules.TicksPerYear * (0.3f + 0.4f * share));
            }
        }
        HarvestFactor *= 1 + Support;
        UseCrafts();
        if (Year >= 6 && Random.F() < 0.16f) FloodAt = Tick + (long)(Rules.TicksPerYear * Random.R(0.45f, 0.7f));
    }

    /// <summary>Where a village-wide event is filmed: the first finished building of a type, or the village centre.</summary>
    private (float X, float Z) PlaceOf(BType type)
    {
        foreach (Building b in Buildings)
            if (b.Type == type && b.Built) return (b.X, b.Z);
        return (CenterX, CenterZ);
    }

    /// <summary>Share of a storm surge held back by finished dikes and a tide gate.</summary>
    public float FloodProtection() => Math.Clamp(Count(BType.Dike, true) / 8f + (Count(BType.TideGate, true) > 0 ? 0.35f : 0f), 0f, 0.95f);

    private void StrikeFlood()
    {
        FloodAt = -1;
        FloodEnd = Tick + 1800;
        Floods++;
        // From the Middle Ages a quarter of the surges are great ones that break a stretch of dike. It becomes a building
        // site again, so porters bring stone and wood and builders repair it, and the fields behind it are exposed meanwhile.
        Building breach = null;
        if (Era >= 4 && Count(BType.Dike, true) >= 2 && Random.F() < 0.25f)
        {
            int pick = Floods % Count(BType.Dike, true);
            foreach (Building b in Buildings)
                if (b.Type == BType.Dike && b.Built && pick-- == 0) { breach = b; break; }
            breach.Built = false;
            breach.Progress = 0.4f;
            breach.Workers = 0;
            BuildingVersion++;
        }
        float protection = FloodProtection();
        int lost = 0;
        (float fx, float fz) = breach != null ? (breach.X, breach.Z) : PlaceOf(BType.Dike);
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Field || !b.Built || b.Yield <= 0 && b.Growth <= 0) continue;
            float bank = MathF.Abs(Map.RiverOffset(b.X, b.Z)) - WorldMap.EstuaryWidth(b.X);
            if (bank > 60 || Random.F() < protection) continue;
            b.Yield /= 2;
            b.Growth *= 0.6f;
            if (lost == 0) (fx, fz) = (b.X, b.Z);
            lost++;
        }
        if (breach != null) Log(lost > 0 ? $"A great storm surge breaks a dike and floods {lost} fields" : "A great storm surge breaks a dike; builders hurry to mend it", fx, fz, 3);
        else if (lost == 0)
        {
            FloodsHeld++;
            Log(Count(BType.Dike, true) > 0 ? "A storm surge rises, but the dikes hold the fields" : "A storm surge rises and falls back", fx, fz, 3);
        }
        else Log($"A storm surge breaks through and floods {lost} fields", fx, fz, 3);
    }

    /// <summary>Tools and wine are used up each year and pay back in faster work and more births.</summary>
    private void UseCrafts()
    {
        int workers = 0;
        foreach (Person p in People)
            if (p.Job >= 0) workers++;
        int tools = TakeFromStores(Res.Tools, (workers + 3) / 4);
        ToolBonus = tools > 0 ? 1f - 0.22f * Math.Min(1f, tools * 4f / Math.Max(1, workers)) : 1f;
        int wine = TakeFromStores(Res.Wine, People.Count / 8);
        WineBonus = wine > 0 ? 1f + 0.25f * Math.Min(1f, wine * 8f / Math.Max(1, People.Count)) : 1f;
    }

    private int TakeFromStores(Res r, int wanted)
    {
        int taken = 0;
        foreach (Building b in Buildings)
        {
            if (b.Type != BType.Storehouse || !b.Built || taken >= wanted) continue;
            int n = Math.Min(wanted - taken, b.Avail(r));
            if (n <= 0) continue;
            b.Stock[(int)r] -= n;
            Consumed[(int)r] += n;
            taken += n;
        }
        return taken;
    }

    private void Director()
    {
        int cap = Eras.MaxPopulation(Era);
        float eraProgress = Math.Clamp(Year / Eras.YearsEach - Era, 0f, 1f);
        float expected = Era == 0
            ? Math.Min(cap * 0.8f, 20 + Year)
            : (Eras.MaxPopulation(Era - 1) + (cap - Eras.MaxPopulation(Era - 1)) * eraProgress) * 0.8f;
        float ratio = People.Count / Math.Max(1f, expected);
        Support = ratio < 0.7f ? Math.Min(0.8f, Support + 0.02f) : Math.Max(0, Support - 0.01f);

        bool behind = ratio < 0.55f && Year > 3;
        bool due = People.Count < 8 || (behind && Tick % 6000 == 0);
        // Once a year in the 20th century, a family or two come by train if the town has room for them.
        if (RailRow >= 0 && Tick % Rules.TicksPerYear == 3000 && People.Count < Eras.MaxPopulation(Era) - 8 && HousingRoom() >= 6) due = true;
        if (due && RailRow >= 0) MigrantsOnTrain = true;
        else if (due) ArriveMigrants();
        // They step off a minute after the train pulls in.
        if (MigrantsOnTrain && TrainTick == 600)
        {
            MigrantsOnTrain = false;
            ArriveMigrants();
        }

        // The ship enters the estuary at 5% of the year and anchors about a minute later; the crowd comes down to the
        // bank once it is in sight (240 s into the year, the Director's 4th call) and stays four minutes.
        if (Era >= 3 && (int)Year % 3 == 1 && Tick % Rules.TicksPerYear == 2400)
        {
            float sx = CenterX + 25;
            // Children, elders and the jobless go down to the bank to watch the ship come in.
            float river = Map.RiverCenter(sx), side = MathF.Sign(CenterZ - river), bank = CenterZ;
            for (float d = 0; d <= 120; d += 2)
                if (Map.Walkable(sx, river + side * d))
                {
                    bank = river + side * (d + 4);
                    break;
                }
            // The ship is the rarest sight of all, so it outranks a recent funeral or feast.
            Gather(sx, bank, 240, true);
            // The story camera frames the crowd on the bank, with the anchored ship beyond.
            Log("A trading ship sails up the estuary", sx, bank, 3);
        }
        int era = Eras.Index(Year);
        if (era > Era)
        {
            RecordEra();
            Era = era;
            // From the Middle Ages the era is celebrated on the church square.
            (float ex, float ez) = ChurchDoor() ?? (CenterX, CenterZ);
            Log($"The village enters the {Eras.Names[Era]}", ex, ez, 3);
            Gather(ex, ez, 150, true);
            (FestiveUntil, FestiveKind) = (GatherUntil, 2);
        }
        if (!RunComplete && Year >= Eras.TotalYears)
        {
            RunComplete = true;
            RecordEra();
            Log("The civilisation's journey has reached the year 2000", CenterX, CenterZ, 3);
        }
    }

    private const int MainsPhase = 8;
    private const int PolderPhase = 7, PolderRowTiles = 12, PolderWidthTiles = 13;
    private const float PolderLand = 1.4f, DikeTop = 2.3f, DrainYears = 2.5f;

    /// <summary>Sea plots north of the river mouth, nearest the shore first.</summary>
    private List<(int X0, int Z0, int X1, int Z1)> PolderSlots()
    {
        var near = new List<(int, int, int, int)>();
        var far = new List<(int, int, int, int)>();
        float west = float.MaxValue;
        for (float z = -240; z <= 240; z += 8) west = MathF.Min(west, Map.CoastX(z));
        // A channel stays open down the middle of the river mouth, and the polders take its shallow margins.
        float zMin = float.MinValue;
        for (float x = west - 12; x <= 472; x += 8) zMin = MathF.Max(zMin, Map.RiverCenter(x) + 36);
        int rowStart = (int)((zMin + WorldMap.SizeZ / 2) / Rules.TileSize) + 1;
        for (int z0 = rowStart; z0 + PolderRowTiles < Rules.TileNz - 3; z0 += PolderRowTiles - 1)
        {
            float coast = float.MaxValue;
            for (int z = z0; z < z0 + PolderRowTiles; z++) coast = MathF.Min(coast, Map.CoastX(z * Rules.TileSize - WorldMap.SizeZ / 2));
            int x0 = (int)((coast - 8 + WorldMap.SizeX / 2) / Rules.TileSize);
            int x1 = x0 + PolderWidthTiles;
            if (x1 >= Rules.TileNx - 3) continue;
            near.Add((x0, z0, x1, z0 + PolderRowTiles));
            int x2 = x1 - 1 + PolderWidthTiles;
            if (x2 < Rules.TileNx - 3) far.Add((x1 - 1, z0, x2, z0 + PolderRowTiles));
        }
        near.AddRange(far);
        return near;
    }

    private static int[] RingOf(Polder p)
    {
        var ring = new List<int>();
        for (int x = p.X0; x < p.X1; x++) ring.Add(x + (p.Z1 - 1) * Rules.TileNx);
        for (int z = p.Z1 - 2; z >= p.Z0; z--) ring.Add(p.X1 - 1 + z * Rules.TileNx);
        for (int x = p.X1 - 2; x >= p.X0; x--) ring.Add(x + p.Z0 * Rules.TileNx);
        for (int z = p.Z0 + 1; z < p.Z1 - 1; z++) ring.Add(p.X0 + z * Rules.TileNx);
        // Work begins where the ring touches land and continues around it.
        var ordered = new int[ring.Count];
        for (int i = 0; i < ordered.Length; i++) ordered[i] = ring[(i + p.Start) % ring.Count];
        return ordered;
    }

    private Polder DikingPolder()
    {
        foreach (Polder p in Polders)
            if (p.Stage == Polder.Diking) return p;
        return null;
    }

    private bool InPolder(int tileX, int tileZ)
    {
        foreach (Polder p in Polders)
            if (p.Stage == Polder.Done && tileX >= p.X0 && tileX < p.X1 && tileZ >= p.Z0 && tileZ < p.Z1) return true;
        return false;
    }

    private void TryStartPolder()
    {
        foreach (Polder active in Polders)
            if (active.Stage != Polder.Done) return;
        if (StoredTotal(Res.Stone) < 30 || FoodInStores() < People.Count) return;
        foreach ((int x0, int z0, int x1, int z1) in PolderSlots())
        {
            if (Polders.Exists(p => p.X0 == x0 && p.Z0 == z0)) continue;
            var polder = new Polder { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1 };
            if (!ConnectRing(polder)) continue;
            Polders.Add(polder);
            (float cx, float cz) = TileCenter((x0 + x1) / 2 + (z0 + z1) / 2 * Rules.TileNx);
            Log(Polders.Count == 1 ? "Villagers begin diking land from the sea" : "A new stretch of sea is ringed with dikes", cx, cz, 3);
            return;
        }
    }

    /// <summary>Picks the ring tile where work starts: on a road if possible, otherwise on land a road can reach.</summary>
    private bool ConnectRing(Polder polder)
    {
        int[] ring = RingOf(polder);
        int start = Array.FindIndex(ring, t => Tiles[t] is TileKind.Road or TileKind.PlannedRoad);
        int[] link = Array.Empty<int>();
        if (start < 0)
        {
            int tries = 0;
            // A town that has grown up to the shore can wall off the first few ring tiles, so keep looking further round.
            for (int i = 0; i < ring.Length && tries < 16; i += 3)
            {
                if (Tiles[ring[i]] != TileKind.Free) continue;
                tries++;
                link = FindPath(ring[i], -1, true, Rules.TileNx * Rules.TileNz);
                if (link == null) continue;
                start = i;
                break;
            }
        }
        if (start < 0 || link == null) (start, link) = ShoreStart(ring);
        if (start < 0 || link == null) return false;
        polder.Start = start;
        polder.Ring = RingOf(polder);
        int first = polder.Ring[0];
        if (Tiles[first] == TileKind.Free) Tiles[first] = TileKind.Road;
        foreach (int tile in link) PlanRoad(tile);
        RoadVersion++;
        return true;
    }

    /// <summary>
    /// For a ring lying wholly in the sea: the first ring tile beside a road, or beside free land a road can reach.
    /// Returns the ring index and the road link to plan, or (-1, null).
    /// </summary>
    private (int Start, int[] Link) ShoreStart(int[] ring)
    {
        int tries = 0;
        for (int i = 0; i < ring.Length; i++)
        {
            int tile = ring[i], x = tile % Rules.TileNx, z = tile / Rules.TileNx;
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;
                if (!InGrid(nx, nz)) continue;
                int next = nx + nz * Rules.TileNx;
                if (Tiles[next] is TileKind.Road or TileKind.PlannedRoad) return (i, Array.Empty<int>());
                if (Tiles[next] != TileKind.Free || Array.IndexOf(ring, next) >= 0 || tries >= 16) continue;
                tries++;
                int[] path = FindPath(next, -1, true, Rules.TileNx * Rules.TileNz);
                if (path != null) return (i, path);
            }
        }
        return (-1, null);
    }

    private bool TryPolderWork(Person p)
    {
        Polder polder = DikingPolder();
        if (polder == null || StoredTotal(Res.Stone) < 1) return false;
        SkipOccupiedRing(polder);
        if (polder.Built >= polder.Ring.Length) return false;
        int stand = polder.Ring[Math.Max(0, polder.Built - 1)];
        p.Act = Act.Road;
        p.Phase = PolderPhase;
        p.A = polder.Ring[polder.Built];
        p.Timer = 28f;
        (float x, float z) = TileCenter(stand);
        // Spread the dike workers over the tile instead of stacking them on its centre.
        GoTo(p, x + (p.Id % 3 - 1) * 1.2f, z + (p.Id / 3 % 3 - 1) * 1.2f);
        return true;
    }

    /// <summary>
    /// A house fire. The neighbours run to it and pass buckets; on a street with a water main the hydrant puts it
    /// out, otherwise the house burns down, the family moves out, and builders put up a new one.
    /// </summary>
    private void UpdateFire()
    {
        Building b = Buildings[FireBuilding];
        if (FireEnd < 0)
        {
            if (Tick < FireAt) return;
            if (b.Type != BType.Hut || !b.Built)
            {
                (FireBuilding, FireAt) = (-1, -1);
                return;
            }
            FiresStarted++;
            FireEnd = Tick + (long)(150 / Rules.TickSeconds);
            (float dx, float dz) = DoorPoint(b);
            Gather(dx, dz, 150, true);
            Log("Fire! A house is burning and the neighbours form a bucket chain", b.X, b.Z, 3);
            return;
        }
        if (Tick < FireEnd) return;
        int room = 0;
        foreach (Building h in Buildings)
            if (h.Type == BType.Hut && h.Built && h != b) room += Math.Max(0, Capacity(h) - h.Occupants);
        if (PipedNear(b.DoorTile) >= 1)
        {
            FiresPutOut++;
            Log("A hydrant on the new water main saves the burning house", b.X, b.Z, 2);
        }
        else if (room < b.Occupants)
        {
            // Nowhere to send the family: the bucket chain saves the house, scorched.
            FiresPutOut++;
            Log("The bucket chain saves the house just in time", b.X, b.Z, 2);
        }
        else
        {
            foreach (Person p in People)
                if (p.Home == b.Id) p.Home = -1;
            b.Occupants = 0;
            // From the Industrial era the new house goes up larger; before that it is rebuilt as it was.
            if (Era >= 6) b.Renewed = true;
            b.Built = false;
            b.Warm = false;
            b.Progress = 0.2f;
            b.Workers = 0;
            BuildingVersion++;
            AssignHomes();
            Log("The house burns down; the family moves in with neighbours and it will be rebuilt", b.X, b.Z, 2);
        }
        (FireBuilding, FireAt, FireEnd) = (-1, -1, -1);
    }

    /// <summary>Test switch --test-fireworks: an era celebration starts at the church door (or the centre) now.</summary>
    public void FireworksForTest()
    {
        (float ex, float ez) = ChurchDoor() ?? (CenterX, CenterZ);
        Gather(ex, ez, 150, true);
        (FestiveUntil, FestiveKind) = (GatherUntil, 2);
        Log("The village celebrates (test)", ex, ez, 3);
    }

    /// <summary>Test switch --test-harsh: this year becomes a harsh winter (snow, river ice, cold).</summary>
    public void HarshWinterForTest()
    {
        ActiveEvent = 2;
        ColdFactor = 1.5f;
    }

    /// <summary>Test switch --test-fire: the occupied house nearest the centre catches fire in 20 seconds.</summary>
    public void IgniteForTest()
    {
        if (FireEnd >= 0) return;
        float best = float.MaxValue;
        foreach (Building b in Buildings)
            if (b.Type == BType.Hut && b.Built && b.Occupants > 0 && Dist2(b.X, b.Z, CenterX, CenterZ) < best)
                (best, FireBuilding) = (Dist2(b.X, b.Z, CenterX, CenterZ), b.Id);
        (FireAt, FireEnd) = (Tick + (long)(20 / Rules.TickSeconds), -1);
    }

    /// <summary>The house on fire right now, or -1.</summary>
    public int Burning => FireEnd >= 0 && Tick < FireEnd ? FireBuilding : -1;

    private int MainsStage => Era >= 7 ? 2 : Era >= 6 ? 1 : 0;

    /// <summary>Up to three workers dig up one street tile at a time to lay water mains (Industrial era) and sewers (20th century).</summary>
    private bool TryMains(Person p)
    {
        int stage = MainsStage;
        if (stage == 0 || Tick < MainsWaitUntil) return false;
        if (DigTile < 0 || Pipes[DigTile] >= stage || Tiles[DigTile] != TileKind.Road)
        {
            DigTile = NextDigTile(stage);
            DigWork = 0;
            if (DigTile < 0)
            {
                // Every street is done; look again for new streets in a month.
                MainsWaitUntil = Tick + Rules.TicksPerYear / 12;
                return false;
            }
            if (MainsLogged < stage)
            {
                MainsLogged = stage;
                (float lx, float lz) = TileCenter(DigTile);
                Log(stage == 1 ? "Workers dig up the streets to lay the first water mains" : "Workers dig up the streets again to lay sewers", lx, lz, 2);
            }
        }
        int crew = 0;
        foreach (Person q in People)
            if (q.Act == Act.Road && q.Phase == MainsPhase) crew++;
        if (crew >= 3) return false;
        p.Act = Act.Road;
        p.Phase = MainsPhase;
        p.A = DigTile;
        p.Timer = 20f;
        (float x, float z) = TileCenter(DigTile);
        GoTo(p, x + (p.Id % 3 - 1) * 1.1f, z + (p.Id / 3 % 3 - 1) * 1.1f);
        return true;
    }

    /// <summary>The next street tile to dig: next to a finished one and nearest the water tower, so the network spreads outwards.</summary>
    private int NextDigTile(int stage)
    {
        (float sx, float sz) = (CenterX, CenterZ);
        if (Landmarks.FindIndex(l => l.Kind == 1) is int tower and >= 0)
            (sx, sz) = TileCenter(Landmarks[tower].TileX + Landmarks[tower].TileZ * Rules.TileNx);
        int nx = Rules.TileNx, best = -1, nearest = -1;
        float bestD = float.MaxValue, nearestD = float.MaxValue;
        for (int i = 0; i < Tiles.Length; i++)
        {
            if (Tiles[i] != TileKind.Road || Pipes[i] >= stage) continue;
            (float x, float z) = TileCenter(i);
            float d = Dist2(x, z, sx, sz);
            if (d < nearestD) (nearestD, nearest) = (d, i);
            int tx = i % nx;
            bool touches = tx > 0 && Pipes[i - 1] >= stage || tx < nx - 1 && Pipes[i + 1] >= stage
                || i >= nx && Pipes[i - nx] >= stage || i + nx < Tiles.Length && Pipes[i + nx] >= stage;
            if (touches && d < bestD) (bestD, best) = (d, i);
        }
        return best >= 0 ? best : nearest;
    }

    private void FinishMainsWork(int tile)
    {
        int stage = MainsStage;
        if (tile != DigTile || stage == 0 || Pipes[tile] >= stage) return;
        if (++DigWork < 3) return;
        Pipes[tile] = (byte)stage;
        DigWork = 0;
        DigTile = -1;
        MainsVersion++;
    }

    /// <summary>
    /// District heating (20th century): the hot-water pipes go down with the sewers, so a house is on it once there is
    /// a boiler house and its street has been dug for sewers.
    /// </summary>
    public bool DistrictHeated(Building b) => Era >= 7 && b.Type == BType.Hut && PipedNear(b.DoorTile) >= 2 && Landmarks.Exists(l => l.Kind == 2);

    /// <summary>The best pipes within two tiles of a door.</summary>
    private int PipedNear(int door)
    {
        int nx = Rules.TileNx, cx = door % nx, cz = door / nx, best = 0;
        for (int z = cz - 2; z <= cz + 2; z++)
        for (int x = cx - 2; x <= cx + 2; x++)
            if (InGrid(x, z)) best = Math.Max(best, Pipes[x + z * nx]);
        return best;
    }

    private void SkipOccupiedRing(Polder polder)
    {
        while (polder.Built < polder.Ring.Length && Tiles[polder.Ring[polder.Built]] is TileKind.Building or TileKind.Field) polder.Built++;
    }

    private void FinishPolderWork()
    {
        Polder polder = DikingPolder();
        if (polder == null) return;
        SkipOccupiedRing(polder);
        if (polder.Built < polder.Ring.Length && TakeFromStores(Res.Stone, 1) == 1)
        {
            int tile = polder.Ring[polder.Built++];
            _ringMark[tile] = true;
            if (Tiles[tile] is TileKind.Blocked or TileKind.Free)
            {
                RoadQueue.Remove(tile);
                ClearTrees(tile);
                Tiles[tile] = TileKind.Road;
                RoadVersion++;
            }
            ShapePolder(polder);
        }
        if (polder.Built >= polder.Ring.Length) CloseRing(polder);
    }

    /// <summary>The ring is closed: two wind pumps go up on raised pads inside it.</summary>
    private void CloseRing(Polder polder)
    {
        polder.Stage = Polder.Draining;
        int midX = (polder.X0 + polder.X1) / 2, midZ = (polder.Z0 + polder.Z1) / 2;
        polder.MillA = AddPump(polder, polder.X1 - 4, midZ - 1, 1);
        polder.MillB = AddPump(polder, midX - 1, polder.Z1 - 4, 0);
        ShapePolder(polder);
        (float x, float z) = TileCenter(midX + midZ * Rules.TileNx);
        Log("The dike ring is closed; windmills start pumping water out", x, z, 3);
    }

    private int AddPump(Polder polder, int tileX, int tileZ, int direction)
    {
        const int size = 3;
        for (int z = tileZ; z < tileZ + size; z++)
        for (int x = tileX; x < tileX + size; x++)
            if (!InGrid(x, z) || Tiles[x + z * Rules.TileNx] is not (TileKind.Blocked or TileKind.Free)) return -1;
        (int doorX, int doorZ) = DoorOf(tileX, tileZ, size, direction);
        if (Tiles[doorX + doorZ * Rules.TileNx] != TileKind.Road) return -1;
        for (int z = tileZ; z < tileZ + size; z++)
        for (int x = tileX; x < tileX + size; x++)
        {
            int tile = x + z * Rules.TileNx;
            Tiles[tile] = TileKind.Free;
            _ringMark[tile] = true;
        }
        Building pump = AddBuilding(BType.Windmill, tileX, tileZ, direction, false);
        pump.Pump = true;
        return pump.Id;
    }

    private void UpdatePolders(float dt)
    {
        foreach (Polder p in Polders)
        {
            if (p.Stage != Polder.Draining) continue;
            int mills = (p.MillA >= 0 && Buildings[p.MillA].Built ? 1 : 0) + (p.MillB >= 0 && Buildings[p.MillB].Built ? 1 : 0);
            if (p.MillA < 0 && p.MillB < 0) mills = 1;
            if (mills == 0) continue;
            float before = p.Drain;
            p.Drain = Math.Min(1, p.Drain + dt * mills / (2 * DrainYears * Rules.YearSeconds));
            if ((int)(p.Drain * 40) != (int)(before * 40)) ShapePolder(p);
            if (p.Drain >= 1) FinishDraining(p);
        }
    }

    private void FinishDraining(Polder p)
    {
        p.Stage = Polder.Done;
        ShapePolder(p);
        for (int z = p.Z0 + 1; z < p.Z1 - 1; z++)
        for (int x = p.X0 + 1; x < p.X1 - 1; x++)
        {
            int tile = x + z * Rules.TileNx;
            (float wx, float wz) = TileCenter(tile);
            if (Tiles[tile] == TileKind.Blocked && Map.Walkable(wx, wz) && Map.Slope(wx, wz) < 0.7f) Tiles[tile] = TileKind.Free;
        }
        int midZ = (p.Z0 + p.Z1) / 2;
        for (int x = p.X0; x < p.X1 - 1; x++) PlanRoad(x + midZ * Rules.TileNx);
        RoadVersion++;
        (float cx, float cz) = TileCenter((p.X0 + p.X1) / 2 + midZ * Rules.TileNx);
        Log("The sea has drained away, leaving new land for fields", cx, cz, 3);
    }

    private void ShapePolder(Polder p)
    {
        float drained = WorldMap.Smooth(0f, 1f, p.Drain);
        int firstX = (int)(p.X0 * (float)Rules.TileSize / WorldMap.Cell), lastX = Math.Min(Map.Nx - 1, (int)(p.X1 * (float)Rules.TileSize / WorldMap.Cell) + 1);
        int firstZ = (int)(p.Z0 * (float)Rules.TileSize / WorldMap.Cell), lastZ = Math.Min(Map.Nz - 1, (int)(p.Z1 * (float)Rules.TileSize / WorldMap.Cell) + 1);
        for (int z = firstZ; z <= lastZ; z++)
        for (int x = firstX; x <= lastX; x++)
        {
            float wx = x * WorldMap.Cell - WorldMap.SizeX / 2, wz = z * WorldMap.Cell - WorldMap.SizeZ / 2;
            int tile = TileAt(wx, wz), tx = tile % Rules.TileNx, tz = tile / Rules.TileNx;
            if (tx < p.X0 || tx >= p.X1 || tz < p.Z0 || tz >= p.Z1) continue;
            float ground = Map.BaseHeightAtVertex(x, z);
            if (_ringMark[tile]) Map.SetReclaimed(x, z, MathF.Max(ground, DikeTop), 0.95f);
            else if (ground < PolderLand && drained > 0) Map.SetReclaimed(x, z, WorldMap.Lerp(ground, PolderLand, drained), MathF.Max(0.02f, p.Drain));
        }
        PolderVersion++;
    }

    private void RestorePolders()
    {
        foreach (Polder p in Polders)
        {
            p.Ring = RingOf(p);
            for (int i = 0; i < p.Built; i++)
                if (Tiles[p.Ring[i]] == TileKind.Road) _ringMark[p.Ring[i]] = true;
            foreach (int id in new[] { p.MillA, p.MillB })
            {
                if (id < 0) continue;
                Building mill = Buildings[id];
                for (int z = mill.TileZ; z < mill.TileZ + 3; z++)
                for (int x = mill.TileX; x < mill.TileX + 3; x++) _ringMark[x + z * Rules.TileNx] = true;
            }
            ShapePolder(p);
        }
    }

    private void RecordEra()
    {
        int births = Births, deaths = Deaths, floods = Floods;
        foreach (EraRecord e in History)
        {
            births -= e.Births;
            deaths -= e.Deaths;
            floods -= e.Floods;
        }
        History.Add(new EraRecord(Era, People.Count, Buildings.Count, births, deaths, floods));
    }

    private int HousingRoom()
    {
        int room = 0;
        foreach (Building b in Buildings)
            if (b.Type == BType.Hut && b.Built) room += Math.Max(0, Capacity(b) - b.Occupants);
        return room;
    }

    private void ArriveMigrants()
    {
        int group = 4 + Random.N(4);
        float edgeX = Math.Clamp(CenterX - 130, -WorldMap.SizeX / 2 + 10, WorldMap.SizeX / 2 - 10);
        float edgeZ = Map.RiverCenter(edgeX) + WorldMap.EstuaryWidth(edgeX) + 45;
        if (!Map.Walkable(edgeX, edgeZ)) (edgeX, edgeZ) = (CenterX, CenterZ + 12);
        // Once the railway runs, newcomers come by train and step out of the station gate.
        bool byTrain = false;
        if (RailRow >= 0 && Landmarks.FindIndex(l => l.Kind == 4) is int at and >= 0)
        {
            Landmark station = Landmarks[at];
            (float gx, float gz) = TileCenter(station.TileX + (station.TileZ + (station.Yaw == 0 ? 2 : -2)) * Rules.TileNx);
            if (Map.Walkable(gx, gz)) (edgeX, edgeZ, byTrain) = (gx, gz, true);
        }
        for (int i = 0; i < group && People.Count < Eras.MaxPopulation(Era); i++)
        {
            float x = edgeX + Random.R(-5, 5), z = edgeZ + Random.R(-5, 5);
            // Never inside a building (newcomers off the train appear right by the station).
            if (!Map.Walkable(x, z) || Tiles[TileAt(x, z)] == TileKind.Building) (x, z) = (edgeX, edgeZ);
            Spawn(x, z, Random.R(16, 34), i % 2 == 0, -1);
            Migrants++;
        }
        Log(byTrain ? "Newcomers step off the train at the station" : "A group of newcomers arrives at the estuary", edgeX, edgeZ, 2);
    }

    public long TotalGoods(Res r)
    {
        long total = 0;
        foreach (Building b in Buildings) total += b.Stock[(int)r];
        foreach (Person p in People)
            if (p.CarryRes == (sbyte)r) total += p.CarryN;
        return total;
    }

    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Add(ulong v) { unchecked { h = (h ^ v) * 1099511628211UL; } }
        void AddF(float v) => Add(BitConverter.SingleToUInt32Bits(v));
        Add((ulong)Tick);
        Add(Random.State);
        Add(Cosmetic.State);
        Add((ulong)Era);
        Add((ulong)PlanWait);
        foreach (Polder p in Polders) { Add((ulong)p.Built); Add((ulong)p.Stage); AddF(p.Drain); }
        foreach (Landmark l in Landmarks) { Add((ulong)l.Kind); Add((ulong)l.TileX); Add((ulong)l.TileZ); }
        AddF(GatherX); AddF(GatherZ); Add((ulong)GatherUntil); Add((ulong)LastGathering);
        Add((ulong)FloodAt); Add((ulong)FloodEnd); AddF(ToolBonus); AddF(WineBonus);
        foreach (Person p in People)
        {
            Add((ulong)p.Id);
            AddF(p.X); AddF(p.Z); AddF(p.Age); AddF(p.Hunger); AddF(p.Health); AddF(p.Timer);
            Add((ulong)p.Act); Add((ulong)p.Role); Add(p.Phase); Add(p.CarryN);
            Add((ulong)(long)p.Home); Add((ulong)(long)p.Job);
        }
        foreach (Building b in Buildings)
        {
            Add((ulong)b.Type); AddF(b.Progress); AddF(b.Growth); Add((ulong)b.Yield);
            Add(b.Built ? 1UL : 0UL); Add(b.Renewed ? 1UL : 0UL);
            foreach (int s in b.Stock) Add((ulong)s);
            foreach (int s in b.In) Add((ulong)s);
            foreach (int s in b.Out) Add((ulong)s);
        }
        foreach (byte w in Wear) Add(w);
        foreach (byte t in Tiles) Add(t);
        foreach (int t in RoadQueue) Add((ulong)t);
        foreach (byte pipe in Pipes) Add(pipe);
        Add((ulong)(long)RailRow); Add((ulong)RailX0); Add((ulong)RailX1); Add(MigrantsOnTrain ? 1UL : 0UL);
        Add((ulong)(long)FireBuilding); Add((ulong)FireAt); Add((ulong)FireEnd); Add((ulong)FiresStarted);
        Add((ulong)(long)DigTile); Add((ulong)DigWork); Add((ulong)MainsWaitUntil); Add((ulong)MainsLogged);
        Add(Wild ? 1UL : 0UL); AddF(CaveDug); Add((ulong)HuntUntil); Add((ulong)Hunts);
        return h;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(18);
        w.Write(Map.Seed);
        w.Write(Random.State);
        w.Write(Tick);
        w.Write(CenterX); w.Write(CenterZ); w.Write(HarvestFactor); w.Write(ColdFactor); w.Write(Support);
        w.Write(RunComplete); w.Write(Era); w.Write(NextPersonId); w.Write(Births); w.Write(Deaths); w.Write(Migrants); w.Write(ActiveEvent); w.Write(MilestoneMask);
        w.Write(FloodAt); w.Write(FloodEnd); w.Write(ToolBonus); w.Write(WineBonus); w.Write(Floods); w.Write(FloodsHeld); w.Write(PlanWait);
        foreach (long v in Produced) w.Write(v);
        foreach (long v in Consumed) w.Write(v);
        foreach (long v in Seeded) w.Write(v);
        w.Write(Wear);
        w.Write(Tiles);
        w.Write(RoadVersion);
        w.Write(RoadQueue.Count);
        foreach (int tile in RoadQueue) w.Write(tile);
        foreach (bool alive in TreeAlive) w.Write(alive);
        w.Write(Graves.Count);
        foreach ((float x, float z) in Graves) { w.Write(x); w.Write(z); }
        w.Write(Events.Count);
        foreach (WorldEvent e in Events) { w.Write(e.Tick); w.Write(e.Text); w.Write(e.X); w.Write(e.Z); w.Write(e.Importance); }

        w.Write(History.Count);
        foreach (EraRecord e in History) { w.Write(e.Era); w.Write(e.Population); w.Write(e.Buildings); w.Write(e.Births); w.Write(e.Deaths); w.Write(e.Floods); }
        w.Write(Polders.Count);
        foreach (Polder p in Polders) { w.Write(p.X0); w.Write(p.Z0); w.Write(p.X1); w.Write(p.Z1); w.Write(p.Start); w.Write(p.Built); w.Write(p.Stage); w.Write(p.MillA); w.Write(p.MillB); w.Write(p.Drain); }

        w.Write(Buildings.Count);
        foreach (Building b in Buildings)
        {
            w.Write((byte)b.Type); w.Write(b.Occupants); w.Write(b.Workers); w.Write(b.TileX); w.Write(b.TileZ); w.Write(b.Direction); w.Write(b.DoorTile);
            w.Write(b.X); w.Write(b.Z); w.Write(b.Rotation); w.Write(b.Progress); w.Write(b.Growth); w.Write(b.BurnDebt);
            w.Write(b.Built); w.Write(b.Sown); w.Write(b.Warm); w.Write(b.Pump); w.Write(b.Yield);
            for (int i = 0; i < (int)Res.Count; i++) { w.Write(b.Stock[i]); w.Write(b.In[i]); w.Write(b.Out[i]); }
        }
        w.Write(People.Count);
        foreach (Person p in People)
        {
            w.Write(p.Id); w.Write(p.Slot); w.Write(p.Home); w.Write(p.Spouse); w.Write(p.Mother); w.Write(p.Job); w.Write(p.A); w.Write(p.B);
            w.Write(p.X); w.Write(p.Z); w.Write(p.TX); w.Write(p.TZ); w.Write(p.Age); w.Write(p.MaxAge); w.Write(p.Hunger); w.Write(p.Health); w.Write(p.Timer);
            w.Write(p.Female); w.Write(p.Moving); w.Write(p.Indoors); w.Write((byte)p.Act); w.Write(p.Phase); w.Write(p.Amount); w.Write(p.CarryN); w.Write(p.CarryRes); w.Write(p.Stuck); w.Write(p.PathIndex); w.Write((byte)p.Role); w.Write(p.Path?.Length ?? -1); if (p.Path != null) foreach (int tile in p.Path) w.Write(tile);
        }
        w.Write(GatherX); w.Write(GatherZ); w.Write(GatherUntil); w.Write(LastGathering);
        w.Write(Cosmetic.State);
        int renewed = 0;
        foreach (Building b in Buildings) if (b.Renewed) renewed++;
        w.Write(renewed);
        foreach (Building b in Buildings) if (b.Renewed) w.Write(b.Id);
        w.Write(Landmarks.Count);
        foreach (Landmark l in Landmarks) { w.Write(l.Kind); w.Write(l.TileX); w.Write(l.TileZ); w.Write(l.HalfX); w.Write(l.HalfZ); w.Write(l.Yaw); }
        w.Write(Pipes); w.Write(DigTile); w.Write(DigWork); w.Write(MainsWaitUntil); w.Write(MainsLogged); w.Write(MainsVersion);
        w.Write(FireBuilding); w.Write(FireAt); w.Write(FireEnd); w.Write(FiresStarted); w.Write(FiresPutOut);
        w.Write(RailRow); w.Write(RailX0); w.Write(RailX1);
        w.Write(MigrantsOnTrain);
        w.Write(Wild); w.Write(CaveX); w.Write(CaveZ); w.Write(CaveYaw); w.Write(CaveDug);
        w.Write(HuntX); w.Write(HuntZ); w.Write(HuntUntil); w.Write(NextHunt); w.Write(HuntWon); w.Write(Hunts); w.Write(HuntsWon);
    }

    public static Simulation Load(BinaryReader r)
    {
        int version = r.ReadInt32();
        if (version is not (9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18)) throw new InvalidDataException("Save belongs to an older map");
        var s = new Simulation(r.ReadInt32(), false);
        s.Random.State = r.ReadUInt64();
        s.Tick = r.ReadInt64();
        s.CenterX = r.ReadSingle(); s.CenterZ = r.ReadSingle(); s.HarvestFactor = r.ReadSingle(); s.ColdFactor = r.ReadSingle(); s.Support = r.ReadSingle();
        s.RunComplete = r.ReadBoolean(); s.Era = r.ReadInt32(); s.NextPersonId = r.ReadInt32(); s.Births = r.ReadInt32(); s.Deaths = r.ReadInt32(); s.Migrants = r.ReadInt32(); s.ActiveEvent = r.ReadInt32(); s.MilestoneMask = r.ReadInt32();
        s.FloodAt = r.ReadInt64(); s.FloodEnd = r.ReadInt64(); s.ToolBonus = r.ReadSingle(); s.WineBonus = r.ReadSingle(); s.Floods = r.ReadInt32(); s.FloodsHeld = r.ReadInt32(); s.PlanWait = r.ReadInt32();
        // Saves before version 18 have no meat column.
        int resources = version >= 18 ? (int)Res.Count : (int)Res.Meat;
        for (int i = 0; i < resources; i++) s.Produced[i] = r.ReadInt64();
        for (int i = 0; i < resources; i++) s.Consumed[i] = r.ReadInt64();
        for (int i = 0; i < resources; i++) s.Seeded[i] = r.ReadInt64();
        r.Read(s.Wear, 0, s.Wear.Length);
        r.Read(s.Tiles, 0, s.Tiles.Length);
        s.RoadVersion = r.ReadInt32();
        int queued = r.ReadInt32();
        for (int i = 0; i < queued; i++) s.RoadQueue.Add(r.ReadInt32());
        for (int i = 0; i < s.TreeAlive.Length; i++) s.TreeAlive[i] = r.ReadBoolean();
        int graves = r.ReadInt32();
        for (int i = 0; i < graves; i++) s.Graves.Add((r.ReadSingle(), r.ReadSingle()));
        int events = r.ReadInt32();
        for (int i = 0; i < events; i++) s.Events.Add(new WorldEvent(r.ReadInt64(), r.ReadString(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32()));

        int records = r.ReadInt32();
        for (int i = 0; i < records; i++) s.History.Add(new EraRecord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
        int polders = r.ReadInt32();
        for (int i = 0; i < polders; i++) s.Polders.Add(new Polder { X0 = r.ReadInt32(), Z0 = r.ReadInt32(), X1 = r.ReadInt32(), Z1 = r.ReadInt32(), Start = r.ReadInt32(), Built = r.ReadInt32(), Stage = r.ReadInt32(), MillA = r.ReadInt32(), MillB = r.ReadInt32(), Drain = r.ReadSingle() });

        int buildings = r.ReadInt32();
        for (int i = 0; i < buildings; i++)
        {
            var b = new Building { Id = i, Type = (BType)r.ReadByte(), Occupants = r.ReadInt32(), Workers = r.ReadInt32(), TileX = r.ReadInt32(), TileZ = r.ReadInt32(), Direction = r.ReadInt32(), DoorTile = r.ReadInt32() };
            b.X = r.ReadSingle(); b.Z = r.ReadSingle(); b.Rotation = r.ReadSingle(); b.Progress = r.ReadSingle(); b.Growth = r.ReadSingle(); b.BurnDebt = r.ReadSingle();
            b.Built = r.ReadBoolean(); b.Sown = r.ReadBoolean(); b.Warm = r.ReadBoolean(); b.Pump = r.ReadBoolean(); b.Yield = r.ReadInt32();
            for (int k = 0; k < resources; k++) { b.Stock[k] = r.ReadInt32(); b.In[k] = r.ReadInt32(); b.Out[k] = r.ReadInt32(); }
            s.Buildings.Add(b);
        }
        s.RestorePolders();
        int people = r.ReadInt32();
        var used = new bool[Rules.MaxPeople];
        for (int i = 0; i < people; i++)
        {
            var p = new Person { Id = r.ReadInt32(), Slot = r.ReadInt32(), Home = r.ReadInt32(), Spouse = r.ReadInt32(), Mother = r.ReadInt32(), Job = r.ReadInt32(), A = r.ReadInt32(), B = r.ReadInt32() };
            p.X = r.ReadSingle(); p.Z = r.ReadSingle(); p.TX = r.ReadSingle(); p.TZ = r.ReadSingle(); p.Age = r.ReadSingle(); p.MaxAge = r.ReadSingle(); p.Hunger = r.ReadSingle(); p.Health = r.ReadSingle(); p.Timer = r.ReadSingle();
            p.Female = r.ReadBoolean(); p.Moving = r.ReadBoolean(); p.Indoors = r.ReadBoolean(); p.Act = (Act)r.ReadByte(); p.Phase = r.ReadByte(); p.Amount = r.ReadByte(); p.CarryN = r.ReadByte(); p.CarryRes = r.ReadSByte(); p.Stuck = r.ReadInt32(); p.PathIndex = r.ReadInt32();
            p.Role = (Role)r.ReadByte();
            int pathLength = r.ReadInt32();
            if (pathLength >= 0) { p.Path = new int[pathLength]; for (int k = 0; k < pathLength; k++) p.Path[k] = r.ReadInt32(); }
            s.People.Add(p);
            s._byId[p.Id] = p;
            used[p.Slot] = true;
        }
        s._freeSlots.Clear();
        for (int i = Rules.MaxPeople - 1; i >= 0; i--)
            if (!used[i]) s._freeSlots.Push(i);
        if (version >= 10) { s.GatherX = r.ReadSingle(); s.GatherZ = r.ReadSingle(); s.GatherUntil = r.ReadInt64(); s.LastGathering = r.ReadInt64(); }
        if (version >= 11) s.Cosmetic.State = r.ReadUInt64();
        if (version >= 12) for (int n = r.ReadInt32(); n > 0; n--) s.Buildings[r.ReadInt32()].Renewed = true;
        if (version >= 13) for (int n = r.ReadInt32(); n > 0; n--) s.Landmarks.Add(new Landmark(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadSingle()));
        if (version >= 14)
        {
            r.ReadBytes(s.Pipes.Length).CopyTo(s.Pipes, 0);
            s.DigTile = r.ReadInt32(); s.DigWork = r.ReadInt32(); s.MainsWaitUntil = r.ReadInt64(); s.MainsLogged = r.ReadInt32(); s.MainsVersion = r.ReadInt32();
        }
        if (version >= 15)
        {
            s.FireBuilding = r.ReadInt32(); s.FireAt = r.ReadInt64(); s.FireEnd = r.ReadInt64(); s.FiresStarted = r.ReadInt32(); s.FiresPutOut = r.ReadInt32();
        }
        if (version >= 16) { s.RailRow = r.ReadInt32(); s.RailX0 = r.ReadInt32(); s.RailX1 = r.ReadInt32(); }
        if (version >= 17) s.MigrantsOnTrain = r.ReadBoolean();
        if (version >= 18)
        {
            s.Wild = r.ReadBoolean(); s.CaveX = r.ReadSingle(); s.CaveZ = r.ReadSingle(); s.CaveYaw = r.ReadSingle(); s.CaveDug = r.ReadSingle();
            s.HuntX = r.ReadSingle(); s.HuntZ = r.ReadSingle(); s.HuntUntil = r.ReadInt64(); s.NextHunt = r.ReadInt64(); s.HuntWon = r.ReadBoolean(); s.Hunts = r.ReadInt32(); s.HuntsWon = r.ReadInt32();
        }
        return s;
    }

    private static float Dist2(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return dx * dx + dz * dz;
    }
}
