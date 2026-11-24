namespace DeskCiv.Sim;

public enum Res : byte { Wood, Stone, Berries, Fish, Grain, Flour, Bread, Grapes, Wine, Tools, Count }

public enum BType : byte { Storehouse, Hut, Field, Gatherer, Fisher, Woodcutter, Quarry, Mill, Bakery, Windmill, Watermill, Vineyard, Winery, Smithy, Dike, TideGate, Count }

public enum Act : byte { Idle, Wander, Eat, Haul, Build, Work, Rest, Road }

public enum Role : byte { None, Porter, Builder, Worker }

public static class TileKind
{
    public const byte Free = 0, Blocked = 1, Building = 2, Road = 3, PlannedRoad = 4, Field = 5;
}

public sealed class Rng
{
    public ulong State;

    public Rng(ulong seed) => State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong Next()
    {
        ulong x = State;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        State = x;
        return unchecked(x * 2685821657736338717UL);
    }

    public float F() => (Next() >> 40) / (float)(1UL << 24);
    public int N(int max) => (int)((Next() >> 33) % (ulong)max);
    public float R(float min, float max) => min + (max - min) * F();
}

public sealed class Person
{
    public int Id, Slot, Home = -1, Spouse = -1, Mother = -1, Job = -1, A = -1, B = -1;
    public float X, Z, TX, TZ, Age, MaxAge, Hunger, Health = 1, Timer;
    public bool Female, Dead, Moving, Indoors;
    public Act Act;
    public Role Role;
    public byte Phase, Amount, CarryN;
    public sbyte CarryRes = -1;
    public int Stuck, PathIndex;
    public int[] Path;
}

public sealed class Building
{
    public int Id, Occupants, Workers, TileX, TileZ, Direction, DoorTile;
    public BType Type;
    public float X, Z, Rotation, Progress, Growth, BurnDebt;
    public bool Built, Sown, Warm, Pump;
    public int Yield;
    public readonly int[] Stock = new int[(int)Res.Count];
    public readonly int[] In = new int[(int)Res.Count];
    public readonly int[] Out = new int[(int)Res.Count];

    public int Avail(Res r) => Stock[(int)r] - Out[(int)r];
}

/// <summary>A piece of sea being reclaimed: ring dike first, then wind pumps drain it into farmland.</summary>
public sealed class Polder
{
    public const int Diking = 0, Draining = 1, Done = 2;
    public int X0, Z0, X1, Z1, Start, Built, Stage, MillA = -1, MillB = -1;
    public float Drain;
    public int[] Ring = System.Array.Empty<int>();
}

public readonly record struct WorldEvent(long Tick, string Text, float X, float Z, int Importance);

/// <summary>What a finished era left behind, shown in the end-of-run history.</summary>
public readonly record struct EraRecord(int Era, int Population, int Buildings, int Births, int Deaths, int Floods);

public static class Rules
{
    public const float TickSeconds = 0.1f, YearSeconds = 1800f;
    public const int TicksPerYear = 18000, MaxPeople = 300, CarryCapacity = 3, HutCapacity = 6;
    public const int TileSize = 4, TileNx = 240, TileNz = 130;

    public static int Footprint(BType type) => type switch
    {
        BType.Field => 5,
        BType.Vineyard => 4,
        BType.Storehouse or BType.Mill or BType.Bakery or BType.Windmill or BType.Watermill or BType.Winery or BType.Smithy or BType.Dike or BType.TideGate => 3,
        _ => 2,
    };

    /// <summary>Buildings that must stand on the water's edge with their back to the river.</summary>
    public static bool NeedsBank(BType type) => type is BType.Watermill or BType.Dike or BType.TideGate;

    public static bool IsGrinder(BType type) => type is BType.Mill or BType.Windmill or BType.Watermill;

    public static readonly int[][] Costs = BuildCosts();

    private static int[][] BuildCosts()
    {
        var c = new int[(int)BType.Count][];
        void Set(BType t, int wood, int stone)
        {
            c[(int)t] = new int[(int)Res.Count];
            c[(int)t][(int)Res.Wood] = wood;
            c[(int)t][(int)Res.Stone] = stone;
        }
        Set(BType.Storehouse, 10, 4);
        Set(BType.Hut, 4, 1);
        Set(BType.Field, 2, 0);
        Set(BType.Gatherer, 3, 0);
        Set(BType.Fisher, 3, 0);
        Set(BType.Woodcutter, 3, 0);
        Set(BType.Quarry, 4, 0);
        Set(BType.Mill, 6, 4);
        Set(BType.Bakery, 6, 6);
        Set(BType.Windmill, 12, 10);
        Set(BType.Watermill, 12, 8);
        Set(BType.Vineyard, 4, 0);
        Set(BType.Winery, 8, 6);
        Set(BType.Smithy, 8, 10);
        Set(BType.Dike, 2, 6);
        Set(BType.TideGate, 8, 14);
        return c;
    }

    public static Res? OutputOf(BType type) => type switch
    {
        BType.Field => Res.Grain,
        BType.Gatherer => Res.Berries,
        BType.Fisher => Res.Fish,
        BType.Woodcutter => Res.Wood,
        BType.Quarry => Res.Stone,
        BType.Mill or BType.Windmill or BType.Watermill => Res.Flour,
        BType.Bakery => Res.Bread,
        BType.Vineyard => Res.Grapes,
        BType.Winery => Res.Wine,
        BType.Smithy => Res.Tools,
        _ => null,
    };

    public static int WorkerSlots(BType type) => type switch
    {
        BType.Field or BType.Gatherer or BType.Fisher or BType.Woodcutter or BType.Quarry or BType.Vineyard => 2,
        BType.Mill or BType.Bakery or BType.Windmill or BType.Watermill or BType.Winery or BType.Smithy => 1,
        _ => 0,
    };

    public static float Radius(BType type) => type switch
    {
        BType.Field => 10f,
        BType.Storehouse => 5.5f,
        BType.Hut => 3.6f,
        _ => 4.4f,
    };

    public static float BuildSeconds(BType type) => type switch
    {
        BType.Field => 20f,
        BType.Hut => 45f,
        BType.Storehouse or BType.Mill or BType.Bakery or BType.Windmill or BType.Watermill or BType.Winery or BType.Smithy or BType.TideGate => 80f,
        _ => 40f,
    };

    public static string SeasonName(int season) => season switch { 0 => "Spring", 1 => "Summer", 2 => "Autumn", _ => "Winter" };

}

/// <summary>The eight eras of a run. Each lasts 42 simulated years, about 21 real hours.</summary>
public static class Eras
{
    public const int Count = 8, YearsEach = 42, TotalYears = Count * YearsEach;

    public static readonly string[] Names =
    {
        "Old Stone Age", "New Stone Age", "Bronze Age", "Iron Age and Antiquity", "Middle Ages", "Renaissance and Age of Sail", "Industrial Age", "20th Century",
    };

    /// <summary>Calendar year at each era boundary. Negative years are BCE.</summary>
    private static readonly int[] Boundaries = { -10000, -7000, -3300, -1200, 500, 1450, 1760, 1900, 2000 };
    private static readonly int[] PopulationCaps = { 32, 80, 120, 160, 200, 240, 270, Rules.MaxPeople };

    public static int StartOf(int era) => Boundaries[era];

    public static int EndOf(int era) => Boundaries[era + 1];

    public static int Index(float year) => System.Math.Clamp((int)(year / YearsEach), 0, Count - 1);

    public static int MaxPopulation(int era) => PopulationCaps[System.Math.Clamp(era, 0, Count - 1)];

    public static int Calendar(float year)
    {
        int era = Index(year);
        float t = System.Math.Clamp(year / YearsEach - era, 0f, 1f);
        return (int)System.MathF.Round(Boundaries[era] + (Boundaries[era + 1] - Boundaries[era]) * t);
    }

    public static string FormatCalendar(int calendar, System.IFormatProvider culture) =>
        calendar < 0 ? $"{(-calendar).ToString("N0", culture)} BC" : $"AD {calendar}";

    /// <summary>First era in which the village may raise this building.</summary>
    public static int UnlockEra(BType type) => type switch
    {
        BType.Field or BType.Mill or BType.Bakery => 1,
        BType.Vineyard or BType.Winery or BType.Smithy => 2,
        BType.Watermill or BType.Dike => 3,
        BType.Windmill or BType.TideGate => 4,
        _ => 0,
    };
}
