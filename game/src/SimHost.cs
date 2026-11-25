using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DeskCiv.Sim;

namespace DeskCiv;

public struct BuildingView
{
    public BType Type;
    public float X, Z, Rotation, Progress, Growth;
    public bool Built, Sown, Warm, Renewed, DistrictHeated;
    public int Occupants;
    /// <summary>Pile sizes 0-4 of a storehouse's wood, stone, food sacks and wine barrels, drawn beside it.</summary>
    public byte WoodPile, StonePile, SackPile, BarrelPile;
}

public sealed class WorldSnapshot
{
    public int Population, Food, Season, Calendar, Era, PolderVersion, ActiveEvent, TreeVersion, RoadVersion;
    public float Year, YearFraction, CenterX, CenterZ;
    /// <summary>How far the polder now being pumped dry has drained (0-1), or -1 when none is draining.</summary>
    public float PolderDrain = -1;
    public bool RunComplete, Flooding, TraderVisiting, Gathering;
    public EraRecord[] History = Array.Empty<EraRecord>();
    public BuildingView[] Buildings = Array.Empty<BuildingView>();
    public bool[] TreeAlive = Array.Empty<bool>();
    public byte[] Wear = Array.Empty<byte>(), Tiles = Array.Empty<byte>();
    public (float X, float Z)[] Graves = Array.Empty<(float, float)>();
    public (float X, float Z, Clip Clip)[] Spots = Array.Empty<(float, float, Clip)>();
    public WorldEvent? LastEvent;
    public Simulation.Landmark[] Landmarks = Array.Empty<Simulation.Landmark>();
    /// <summary>Pipes per tile (0 none, 1 water main, 2 plus sewer) and the street tile being dug, or -1.</summary>
    public byte[] Pipes = Array.Empty<byte>();
    public int DigTile = -1, MainsVersion;
    /// <summary>The burning house, if any.</summary>
    public bool Burning;
    /// <summary>The railway row and its west and east tiles, or -1.</summary>
    public int RailRow = -1, RailX0, RailX1;
    public float FireX, FireZ;
}

public sealed class PeopleFrame
{
    public readonly float[] X = new float[Rules.MaxPeople], Y = new float[Rules.MaxPeople], Z = new float[Rules.MaxPeople], Yaw = new float[Rules.MaxPeople];
    public readonly byte[] Kind = new byte[Rules.MaxPeople], Carry = new byte[Rules.MaxPeople], Anim = new byte[Rules.MaxPeople], Variant = new byte[Rules.MaxPeople];
    public int Slots;
}

/// <summary>Runs the simulation on a low-priority thread and publishes immutable views for rendering.</summary>
public sealed class SimHost
{
    private const double SaveSeconds = 300;
    public static readonly string SavePath = Path.Combine(AudioSettings.DataFolder, "world.sav");

    private readonly Simulation _sim;
    private readonly bool _canSave;
    private volatile int _speed;
    private readonly object _gate = new();
    private PeopleFrame _previous = new(), _current = new(), _spare = new();
    private WorldSnapshot _snapshot;
    private long _publishedAt;
    private Thread _thread;
    private volatile bool _running, _night;
    private volatile bool _saveRequested;
    private readonly ManualResetEventSlim _saved = new(false);
    private int _lastTreeVersion = -1, _lastMainsVersion = -1;
    private byte[] _pipes;
    private bool[] _trees;
    private readonly float[] _yaw = new float[Rules.MaxPeople];
    private readonly float[] _lastX = new float[Rules.MaxPeople], _lastZ = new float[Rules.MaxPeople];

    public WorldMap Map => _sim.Map;
    public bool Paused { get; set; }
    public int Speed { get => _speed; set => _speed = Math.Max(1, value); }
    public bool Night { get => _night; set => _night = value; }

    public SimHost(int? seed, bool fresh, bool canSave, int speed, float startYear)
    {
        _canSave = canSave;
        _speed = Math.Max(1, speed);
        _sim = (fresh ? null : TryLoad()) ?? new Simulation(seed ?? (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        for (long t = (long)(startYear * Rules.TicksPerYear); _sim.Tick < t;) _sim.Step();
        Publish(true);
        Publish(true);
    }

    private static Simulation TryLoad()
    {
        if (!File.Exists(SavePath)) return null;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(SavePath));
            return Simulation.Load(reader);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or IndexOutOfRangeException or ArgumentException)
        {
            Godot.GD.PushWarning($"Save could not be read and was set aside: {e.Message}");
            try { File.Move(SavePath, SavePath + ".bad", true); } catch (IOException) { }
            return null;
        }
    }

    /// <summary>Test switch: sets a house on fire shortly. Call before Start.</summary>
    public void IgniteForTest() => _sim.IgniteForTest();

    public void Start()
    {
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "DeskCiv sim" };
        _thread.Start();
    }

    /// <summary>Stops the thread and writes a final save.</summary>
    public void Stop()
    {
        if (_thread == null) return;
        _running = false;
        _thread.Join(3000);
        _thread = null;
        WriteSave();
    }

    private void Loop()
    {
        long interval = (long)(Rules.TickSeconds * Stopwatch.Frequency);
        long next = Stopwatch.GetTimestamp();
        long nextSave = next + (long)(SaveSeconds * Stopwatch.Frequency);
        int counter = 0;
        while (_running)
        {
            if (!Paused)
            {
                _sim.Night = _night;
                long budgetEnd = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 70 / 1000;
                int steps = _speed;
                for (int i = 0; i < steps; i++)
                {
                    _sim.Step();
                    if ((i & 7) == 7 && Stopwatch.GetTimestamp() >= budgetEnd) break;
                }
                Publish(++counter % 10 == 0);
            }
            long now = Stopwatch.GetTimestamp();
            if (now >= nextSave || _saveRequested)
            {
                WriteSave();
                _saveRequested = false;
                _saved.Set();
                nextSave = now + (long)(SaveSeconds * Stopwatch.Frequency);
            }
            next += interval;
            long wait = (next - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
            if (wait > 0) Thread.Sleep((int)wait);
            else next = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Asks the simulation thread to save now and waits briefly. Used when Windows is shutting down.</summary>
    public void SaveNow(int timeoutMs)
    {
        if (_thread == null) return;
        _saved.Reset();
        _saveRequested = true;
        _saved.Wait(timeoutMs);
    }

    private void WriteSave()
    {
        if (!_canSave) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            string temp = SavePath + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp))) _sim.Save(writer);
            File.Move(temp, SavePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Godot.GD.PushWarning($"Could not save world: {e.Message}");
        }
    }

    private void Publish(bool withWorld)
    {
        PeopleFrame frame = _spare;
        Array.Clear(frame.Kind);
        Array.Clear(frame.Carry);
        int slots = 0;
        foreach (Person p in _sim.People)
        {
            int s = p.Slot;
            slots = Math.Max(slots, s + 1);
            frame.X[s] = p.X;
            frame.Y[s] = _sim.Map.Height(p.X, p.Z);
            // Fields are flat plates over sloping ground, so workers stand on the plate rather than the terrain under it.
            if (p.Job >= 0 && p.Act == Act.Work && p.Phase == 1 && _sim.Buildings[p.Job].Type is BType.Field or BType.Vineyard)
                frame.Y[s] = MathF.Max(frame.Y[s], _sim.Map.Height(_sim.Buildings[p.Job].X, _sim.Buildings[p.Job].Z) + 0.36f);
            frame.Z[s] = p.Z;
            frame.Kind[s] = p.Indoors ? (byte)0 : p.Age < 14 ? (byte)3 : p.Age >= 60 ? (byte)4 : p.Female ? (byte)2 : (byte)1;
            frame.Carry[s] = p.CarryN > 0 ? (byte)(p.CarryRes + 1) : (byte)0;
            Clip clip = ClipOf(p);
            // Walkers face the way they actually move along their path. TX/TZ is only the final destination,
            // which on a winding road can lie to the side or behind, so facing it looked like walking backwards.
            float mx = p.X - _lastX[s], mz = p.Z - _lastZ[s], moved = mx * mx + mz * mz;
            _lastX[s] = p.X;
            _lastZ[s] = p.Z;
            if (p.Moving && moved > 1e-4f && moved < 25f)
            {
                float target = MathF.Atan2(mx, mz);
                float turn = MathF.IEEERemainder(target - _yaw[s], MathF.Tau);
                _yaw[s] += turn * (MathF.Abs(turn) > 2.6f ? 1f : 0.6f);
            }
            else if (p.Moving && moved >= 25f)
            {
                float dx = p.TX - p.X, dz = p.TZ - p.Z;
                if (dx * dx + dz * dz > 0.01f) _yaw[s] = MathF.Atan2(dx, dz);
            }
            else if (clip == Clip.Fish)
            {
                _yaw[s] = MathF.PI;
            }
            else if (clip == Clip.Chop && p.B >= 0)
            {
                _yaw[s] = MathF.Atan2(_sim.TreeX[p.B] - p.X, _sim.TreeZ[p.B] - p.Z);
            }
            frame.Yaw[s] = _yaw[s];
            frame.Anim[s] = (byte)clip;
            frame.Variant[s] = OutfitOf(p);
        }
        frame.Slots = slots;
        WorldSnapshot snapshot = withWorld ? BuildSnapshot() : null;
        lock (_gate)
        {
            _spare = _previous;
            _previous = _current;
            _current = frame;
            _publishedAt = Stopwatch.GetTimestamp();
            if (snapshot != null) _snapshot = snapshot;
        }
    }

    private Clip ClipOf(Person p)
    {
        if (p.Moving) return p.CarryN > 0 ? Clip.Carry : Clip.Walk;
        if (p.Act == Act.Build) return Clip.Build;
        if (p.Act == Act.Road) return Clip.Hoe;
        if (p.Act != Act.Work || p.Phase != 1 || p.Job < 0) return Clip.Idle;
        Building b = _sim.Buildings[p.Job];
        return b.Type switch
        {
            BType.Woodcutter => Clip.Chop,
            BType.Field => b.Yield > 0 ? Clip.Reap : _sim.Season == 0 && !b.Sown ? Clip.Sow : Clip.Hoe,
            BType.Quarry => Clip.Mine,
            BType.Gatherer => Clip.Gather,
            BType.Fisher => Clip.Fish,
            BType.Mill or BType.Bakery or BType.Windmill or BType.Watermill or BType.Winery => Clip.Knead,
            BType.Vineyard => Clip.Gather,
            BType.Smithy => Clip.Build,
            _ => Clip.Idle,
        };
    }

    private byte OutfitOf(Person p)
    {
        if (p.Age < 14) return 8;
        if (p.Age >= 60) return 9;
        if (p.Role == Role.Builder || p.Act is Act.Build or Act.Road) return 6;
        if (p.Job < 0) return p.Female ? (byte)1 : (byte)0;
        return _sim.Buildings[p.Job].Type switch
        {
            BType.Woodcutter => 2,
            BType.Field or BType.Gatherer or BType.Vineyard => 3,
            BType.Fisher => 4,
            BType.Quarry => 5,
            BType.Mill or BType.Bakery or BType.Windmill or BType.Watermill or BType.Winery => 7,
            BType.Smithy => 6,
            _ => p.Female ? (byte)1 : (byte)0,
        };
    }

    private (float, float, Clip)[] BuildSpots()
    {
        var spots = new System.Collections.Generic.List<(float, float, Clip)>(64);
        // Start at a different villager each time, so people late in the list are filmed too.
        int count = _sim.People.Count, start = count == 0 ? 0 : (int)(_sim.Tick / 600 % count);
        for (int i = 0; i < count; i++)
        {
            Person p = _sim.People[(start + i) % count];
            Clip clip = ClipOf(p);
            if (clip is Clip.Idle or Clip.Walk or Clip.Carry || p.Indoors) continue;
            spots.Add((p.X, p.Z, clip));
            if (spots.Count >= 64) break;
        }
        return spots.ToArray();
    }

    private WorldSnapshot BuildSnapshot()
    {
        if (_sim.TreeVersion != _lastTreeVersion || _trees == null)
        {
            _trees = (bool[])_sim.TreeAlive.Clone();
            _lastTreeVersion = _sim.TreeVersion;
        }
        if (_sim.MainsVersion != _lastMainsVersion || _pipes == null)
        {
            _pipes = (byte[])_sim.Pipes.Clone();
            _lastMainsVersion = _sim.MainsVersion;
        }
        var buildings = new BuildingView[_sim.Buildings.Count];
        for (int i = 0; i < buildings.Length; i++)
        {
            Building b = _sim.Buildings[i];
            buildings[i] = new BuildingView
            {
                Type = b.Type, X = b.X, Z = b.Z, Rotation = b.Rotation, Progress = b.Progress, Growth = b.Growth,
                Built = b.Built, Sown = b.Sown, Warm = b.Warm, Renewed = b.Renewed, DistrictHeated = _sim.DistrictHeated(b), Occupants = b.Occupants,
            };
            if (b.Type == BType.Storehouse && b.Built)
            {
                int[] s = b.Stock;
                buildings[i].WoodPile = Pile(s[(int)Res.Wood]);
                buildings[i].StonePile = Pile(s[(int)Res.Stone]);
                buildings[i].SackPile = Pile(s[(int)Res.Grain] + s[(int)Res.Flour] + s[(int)Res.Berries] + s[(int)Res.Fish] + s[(int)Res.Bread]);
                buildings[i].BarrelPile = Pile(s[(int)Res.Wine] + s[(int)Res.Grapes]);
            }
        }
        return new WorldSnapshot
        {
            Population = _sim.Population, Food = _sim.FoodInStores(), Season = _sim.Season, Calendar = Eras.Calendar(_sim.Year), Era = _sim.Era, PolderVersion = _sim.PolderVersion,
            ActiveEvent = _sim.ActiveEvent, TreeVersion = _lastTreeVersion, Year = _sim.Year, YearFraction = _sim.YearFraction,
            CenterX = _sim.CenterX, CenterZ = _sim.CenterZ, RunComplete = _sim.RunComplete, Flooding = _sim.Flooding, TraderVisiting = _sim.TraderVisiting, Gathering = _sim.GatherUntil > _sim.Tick, History = _sim.History.ToArray(), Buildings = buildings, TreeAlive = _trees,
            Wear = (byte[])_sim.Wear.Clone(), Tiles = (byte[])_sim.Tiles.Clone(), RoadVersion = _sim.RoadVersion, Graves = _sim.Graves.ToArray(), Spots = BuildSpots(),
            LastEvent = NewsEvent(), PolderDrain = DrainingPolder(), Landmarks = _sim.Landmarks.ToArray(),
            Pipes = _pipes, DigTile = _sim.DigTile, MainsVersion = _lastMainsVersion,
            RailRow = _sim.RailRow, RailX0 = _sim.RailX0, RailX1 = _sim.RailX1,
            Burning = _sim.Burning >= 0, FireX = _sim.Burning >= 0 ? _sim.Buildings[_sim.Burning].X : 0, FireZ = _sim.Burning >= 0 ? _sim.Buildings[_sim.Burning].Z : 0,
        };
    }

    /// <summary>
    /// The event to show: of those logged since the last snapshot, the most important (the latest on a tie), so a
    /// year's news is not hidden by an era change logged in the same tick.
    /// </summary>
    private WorldEvent? NewsEvent()
    {
        int count = _sim.Events.Count;
        if (count == 0) return null;
        // The list is capped, so new entries are found by tick, not by count.
        if (_sim.Events[^1].Tick < _seenTick) _seenTick = -1; // a new world started
        WorldEvent? best = null;
        for (int i = count - 1; i >= 0 && _sim.Events[i].Tick > _seenTick; i--)
            if (best == null || _sim.Events[i].Importance > best.Value.Importance) best = _sim.Events[i];
        if (best != null)
        {
            _news = best;
            _seenTick = _sim.Events[^1].Tick;
        }
        return _news ?? _sim.Events[^1];
    }

    private long _seenTick = -1;
    private WorldEvent? _news;

    private float DrainingPolder()
    {
        float drain = -1;
        foreach (Polder p in _sim.Polders)
            if (p.Stage == Polder.Draining) drain = Math.Max(drain, p.Drain);
        return drain;
    }

    private static byte Pile(int stock) => (byte)(stock <= 0 ? 0 : Math.Min(4, 1 + stock / 40));

    public WorldSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    public (float[] X, float[] Z) TreePositions => (_sim.TreeX, _sim.TreeZ);

    /// <summary>Calls the writer while the two latest frames cannot change.</summary>
    public void ReadPeople(Action<PeopleFrame, PeopleFrame, float> reader)
    {
        lock (_gate)
        {
            double age = (Stopwatch.GetTimestamp() - _publishedAt) / (double)Stopwatch.Frequency;
            float t = Paused ? 1f : (float)Math.Clamp(age / Rules.TickSeconds, 0, 1);
            reader(_previous, _current, t);
        }
    }
}
