using System;

namespace DeskCiv.Sim;

/// <summary>Deterministic river estuary opening to the sea in the east, generated from a seed. Contains no engine dependencies.</summary>
public sealed class WorldMap
{
    public const float SizeX = 960f, SizeZ = 520f, Cell = 2.5f, WaterLevel = 0f;

    public readonly int Seed, Nx, Nz;
    private readonly float[] _heights, _base, _river, _reclaimed;

    public WorldMap(int seed)
    {
        Seed = seed;
        Nx = (int)(SizeX / Cell) + 1;
        Nz = (int)(SizeZ / Cell) + 1;
        _heights = new float[Nx * Nz];
        _river = new float[Nx];
        _reclaimed = new float[Nx * Nz];
        for (int x = 0; x < Nx; x++)
            _river[x] = Fbm((x * Cell - SizeX / 2) * 0.002f, 0.5f, seed + 1, 2) * 130f + 20f;

        for (int z = 0; z < Nz; z++)
        for (int x = 0; x < Nx; x++)
        {
            float wx = x * Cell - SizeX / 2, wz = z * Cell - SizeZ / 2;
            float sea = wx - CoastX(wz);
            float distance = MathF.Max(0, MathF.Abs(wz - _river[x]) - EstuaryWidth(wx));
            float valley = Smooth(14f, 170f, distance);
            float lowland = 1f - 0.78f * Smooth(-210f, -30f, sea);
            float land = 2.2f + (valley * 26f + Fbm(wx * 0.0035f, wz * 0.0035f, seed, 4) * (5f + valley * 32f)) * lowland;
            float ground = Lerp(-3f, land, Smooth(7f, 22f, distance));
            _heights[x + z * Nx] = Lerp(ground, -4.5f, Smooth(-24f, 12f, sea));
        }
        _base = (float[])_heights.Clone();
    }

    public float BaseHeightAtVertex(int x, int z) => _base[x + z * Nx];

    public float ReclaimedAtVertex(int x, int z) => _reclaimed[x + z * Nx];

    /// <summary>Reshapes one terrain vertex for land won from the sea. Amount above zero marks it as reclaimed.</summary>
    public void SetReclaimed(int x, int z, float height, float amount)
    {
        _heights[x + z * Nx] = height;
        _reclaimed[x + z * Nx] = amount;
    }

    /// <summary>World X of the sea shore at this Z. Land lies to the west.</summary>
    public float CoastX(float z) => 292f + Fbm(0.37f, z * 0.0042f, Seed + 7, 3) * 58f;

    /// <summary>Extra half-width of the river as it opens toward the sea.</summary>
    public static float EstuaryWidth(float x)
    {
        float t = Smooth(-60f, 300f, x);
        return t * t * 78f;
    }

    public float HeightAtVertex(int x, int z) => _heights[x + z * Nx];

    public float RiverCenter(float x)
    {
        float f = Math.Clamp((x + SizeX / 2) / Cell, 0, Nx - 1.001f);
        int i = (int)f;
        return Lerp(_river[i], _river[i + 1], f - i);
    }

    public float RiverOffset(float x, float z) => z - RiverCenter(x);

    public float Height(float x, float z)
    {
        float fx = Math.Clamp((x + SizeX / 2) / Cell, 0, Nx - 1.001f);
        float fz = Math.Clamp((z + SizeZ / 2) / Cell, 0, Nz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = _heights[ix + iz * Nx], b = _heights[ix + 1 + iz * Nx];
        float c = _heights[ix + (iz + 1) * Nx], d = _heights[ix + 1 + (iz + 1) * Nx];
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    public float Slope(float x, float z)
    {
        const float e = 2.5f;
        float gx = Height(x + e, z) - Height(x - e, z), gz = Height(x, z + e) - Height(x, z - e);
        return MathF.Sqrt(gx * gx + gz * gz) / (2 * e);
    }

    public bool Walkable(float x, float z) =>
        MathF.Abs(x) < SizeX / 2 - 4 && MathF.Abs(z) < SizeZ / 2 - 4 && Height(x, z) > WaterLevel + 0.25f;

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float Smooth(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static float Fbm(float x, float z, int seed, int octaves)
    {
        float sum = 0, amplitude = 1, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += ValueNoise(x, z, seed + i * 101) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            x *= 2.03f;
            z *= 2.03f;
        }
        return sum / total;
    }

    private static float ValueNoise(float x, float z, int seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float tx = x - ix, tz = z - iz;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        return Lerp(Lerp(Hash(ix, iz, seed), Hash(ix + 1, iz, seed), tx),
            Lerp(Hash(ix, iz + 1, seed), Hash(ix + 1, iz + 1, seed), tx), tz);
    }

    private static float Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (float)uint.MaxValue * 2f - 1f;
        }
    }
}
