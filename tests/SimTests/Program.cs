using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DeskCiv.Sim;

int failures = 0;
void Check(bool ok, string message)
{
    if (ok) return;
    failures++;
    Console.WriteLine("FAIL: " + message);
}

void Invariants(Simulation s, string label)
{
    for (int i = 0; i < (int)Res.Count; i++)
    {
        var r = (Res)i;
        long expected = s.Seeded[i] + s.Produced[i] - s.Consumed[i];
        Check(s.TotalGoods(r) == expected, $"{label}: {r} not conserved, have {s.TotalGoods(r)}, expected {expected}");
    }
    foreach (Building b in s.Buildings)
        for (int i = 0; i < (int)Res.Count; i++)
            Check(b.Stock[i] >= 0 && b.In[i] >= 0 && b.Out[i] >= 0 && b.Out[i] <= b.Stock[i], $"{label}: bad stock on {b.Type} #{b.Id} {(Res)i}");
    Check(s.People.Select(p => p.Slot).Distinct().Count() == s.People.Count, $"{label}: duplicate render slots");
    Check(s.Population <= Rules.MaxPeople, $"{label}: population above cap");
    var occupied = new System.Collections.Generic.HashSet<int>();
    foreach (Building b in s.Buildings)
    {
        int size = Rules.Footprint(b.Type);
        for (int z = b.TileZ; z < b.TileZ + size; z++)
        for (int x = b.TileX; x < b.TileX + size; x++)
            Check(occupied.Add(x + z * Rules.TileNx), $"{label}: buildings overlap at tile {x},{z}");
        byte door = s.Tiles[b.DoorTile];
        Check(door is TileKind.Road or TileKind.PlannedRoad, $"{label}: {b.Type} #{b.Id} door is not on a road (kind {door})");
    }
    foreach (Person p in s.People)
        Check(float.IsFinite(p.X) && float.IsFinite(p.Z) && Math.Abs(p.X) <= WorldMap.SizeX / 2 && Math.Abs(p.Z) <= WorldMap.SizeZ / 2, $"{label}: person out of bounds");
}

if (args.Length >= 2 && args[0] == "diag")
{
    var d = new Simulation(int.Parse(args[1]));
    long[] last = new long[(int)Res.Count];
    int diagYears = args.Length >= 3 ? int.Parse(args[2]) : 50;
    bool diagMixed = args.Length >= 4 && args[3] == "mixed";
    for (int y = 1; y <= diagYears; y++)
    {
        for (int t = 0; t < Rules.TicksPerYear; t++) { d.Night = diagMixed && (d.Tick / 3000) % 2 == 1; d.Step(); }
        if (y % (diagYears > 60 ? 10 : 5) != 0) continue;
        int adults = d.People.Count(p => p.Age >= 14 && p.Age < 60), jobs = d.People.Count(p => p.Job >= 0);
        int pending = d.Buildings.Count(b => !b.Built), homeless = d.People.Count(p => p.Home < 0), hungry = d.People.Count(p => p.Hunger >= 0.9f);
        string made = string.Join(" ", Enumerable.Range(0, (int)Res.Count).Select(i => $"{(Res)i}+{(d.Produced[i] - last[i]) / (diagYears > 60 ? 10 : 5)}/{d.TotalGoods((Res)i)}"));
        Array.Copy(d.Produced, last, last.Length);
        string built = string.Join(" ", Enumerable.Range(0, (int)BType.Count).Select(i => $"{(BType)i}={d.Count((BType)i, true)}"));
        Console.WriteLine($"y{y} pop={d.Population} adults={adults} jobs={jobs} pending={pending} homeless={homeless} hungry={hungry} support={d.Support:F2}\n   per-year/stock: {made}\n   {built}");
    }
    return 0;
}

// Determinism and save/load continuation.
{
    var a = new Simulation(1234);
    var b = new Simulation(1234);
    for (int i = 0; i < Rules.TicksPerYear * 3; i++) { a.Step(); b.Step(); }
    Check(a.StateHash() == b.StateHash(), "same seed diverged");

    using var memory = new MemoryStream();
    using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) a.Save(writer);
    memory.Position = 0;
    using var reader = new BinaryReader(memory);
    Simulation loaded = Simulation.Load(reader);
    Check(loaded.StateHash() == a.StateHash(), "loaded state differs");
    for (int i = 0; i < Rules.TicksPerYear; i++) { a.Step(); loaded.Step(); }
    Check(loaded.StateHash() == a.StateHash(), "loaded world diverged after one year");
    Invariants(loaded, "loaded");
    Console.WriteLine("determinism and save/load checked");
}

// Long runs: 12 worlds through the whole 42-year era plus overrun, in three day/night patterns.
var clock = Stopwatch.StartNew();
int[] seeds = Enumerable.Range(0, 12).Select(i => 1000 + i * 7919).ToArray();
var rows = new string[seeds.Length];
var lockObject = new object();
Parallel.For(0, seeds.Length, n =>
{
    var s = new Simulation(seeds[n]);
    int pattern = n % 3;
    int minPop = int.MaxValue;
    for (int t = 0; t < Rules.TicksPerYear * 50; t++)
    {
        s.Night = pattern switch { 0 => false, 1 => true, _ => (t / 3000) % 2 == 1 };
        s.Step();
        minPop = Math.Min(minPop, s.Population);
        if (t % Rules.TicksPerYear == 0)
            lock (lockObject) Invariants(s, $"seed {seeds[n]} year {t / Rules.TicksPerYear}");
    }
    lock (lockObject)
    {
        Invariants(s, $"seed {seeds[n]} final");
        Check(minPop > 0, $"seed {seeds[n]}: population reached zero");
        Check(s.People.Count(p => p.Age >= 14 && p.Age < 60) < 8 || s.People.Count(p => p.Role == Role.Porter) > 0, $"seed {seeds[n]}: no porters");
        Check(s.Era == 1, $"seed {seeds[n]}: expected era 1 at year 50 (pop {s.Population}, fields {s.Count(BType.Field, true)}, bakeries {s.Count(BType.Bakery, true)})");
        Check(s.Count(BType.Bakery, true) >= 1 && s.Produced[(int)Res.Bread] > 0, $"seed {seeds[n]}: bread chain never ran");
    }
    string name = pattern switch { 0 => "day", 1 => "night", _ => "mixed" };
    rows[n] = $"{seeds[n],6} {name,-5} pop={s.Population,4} min={minPop,3} births={s.Births,4} deaths={s.Deaths,4} migrants={s.Migrants,3} huts={s.Count(BType.Hut),3} fields={s.Count(BType.Field),3} bakeries={s.Count(BType.Bakery),2} bread={s.Produced[(int)Res.Bread],6} food={s.FoodInStores(),5} era={s.Era} porters={s.People.Count(p => p.Role == Role.Porter)} builders={s.People.Count(p => p.Role == Role.Builder)} workers={s.People.Count(p => p.Role == Role.Worker)} roads={s.Tiles.Count(t => t == TileKind.Road)} pending={s.RoadQueue.Count}";
});
foreach (string row in rows) Console.WriteLine(row);
Console.WriteLine($"long runs took {clock.Elapsed.TotalSeconds:F1}s");
// Full runs: every era through the year 2000.
var fullClock = Stopwatch.StartNew();
int[] fullSeeds = { 1000, 16838, 40595, 72271 };
var fullRows = new string[fullSeeds.Length];
Parallel.For(0, fullSeeds.Length, n =>
{
    var s = new Simulation(fullSeeds[n]);
    var pops = new int[Eras.Count];
    int minAfterStart = int.MaxValue;
    int eraTicks = Rules.TicksPerYear * Eras.YearsEach;
    for (int t = 0; t < Rules.TicksPerYear * (Eras.TotalYears + 1); t++)
    {
        s.Night = (t / 3000) % 2 == 1;
        s.Step();
        if (t > Rules.TicksPerYear * 5) minAfterStart = Math.Min(minAfterStart, s.Population);
        if ((t + 1) % eraTicks == 0) pops[Math.Min(Eras.Count - 1, (t + 1) / eraTicks - 1)] = s.Population;
        if (t % (Rules.TicksPerYear * 10) == 0)
            lock (lockObject) Invariants(s, $"full {fullSeeds[n]} year {t / Rules.TicksPerYear}");
    }
    lock (lockObject)
    {
        Invariants(s, $"full {fullSeeds[n]} final");
        Check(s.RunComplete && s.Era == Eras.Count - 1, $"full {fullSeeds[n]}: run did not finish");
        Check(s.History.Count == Eras.Count && s.History.Select(e => e.Era).SequenceEqual(Enumerable.Range(0, Eras.Count)), $"full {fullSeeds[n]}: history has {s.History.Count} eras");
        Check(minAfterStart > 0, $"full {fullSeeds[n]}: population reached zero");
        Check(s.Polders.Any(p => p.Stage == Polder.Done), $"full {fullSeeds[n]}: no sea was reclaimed");
        Check(s.Buildings.Where(b => b.Type != BType.Hut && b.Type != BType.Storehouse).All(b => Eras.UnlockEra(b.Type) <= s.Era), $"full {fullSeeds[n]}: locked building");
        for (int i = 0; i < (int)BType.Count; i++)
            Check(s.Count((BType)i, true) > 0, $"full {fullSeeds[n]}: never built {(BType)i}");
        int roadTiles = Enumerable.Range(0, s.Tiles.Length).Count(i => s.Tiles[i] == TileKind.Road);
        Check(s.Pipes.Count(v => v >= 1) * 10 >= roadTiles * 7, $"full {fullSeeds[n]}: only {s.Pipes.Count(v => v >= 1)} of {roadTiles} road tiles got water mains");
    }
    fullRows[n] = $"{fullSeeds[n],6} era-end pops: {string.Join(" ", pops)} floods={s.Floods} held={s.FloodsHeld} buildings={s.Buildings.Count} urban={s.Urbanized}/{s.Urbanized + s.UrbanSkips} polders={s.Polders.Count(p => p.Stage == Polder.Done)}/{s.Polders.Count} pumps={s.Buildings.Count(b => b.Pump)} widened={s.StreetsWidened} renewed={s.Buildings.Count(b => b.Renewed)} landmarks={string.Join("+", s.Landmarks.Select(l => l.Kind))} mains={s.Pipes.Count(v => v >= 1)}/{s.Pipes.Count(v => v >= 2)}/{Enumerable.Range(0, s.Tiles.Length).Count(i => s.Tiles[i] == TileKind.Road)} at={(s.Polders.Count > 0 ? $"{s.Polders[0].X0 * 4 - 480},{s.Polders[0].Z0 * 4 - 260}" : "-")}";
});
foreach (string row in fullRows) Console.WriteLine(row);
Console.WriteLine($"full runs took {fullClock.Elapsed.TotalSeconds:F1}s");
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
