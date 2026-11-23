namespace Starwright.Core;

/// <summary>Deterministic mulberry32 PRNG, matching js/core/noise.js.</summary>
public static class Rng
{
    public static Func<double> MakeRandom(int seed)
    {
        uint a = unchecked((uint)seed);
        return () =>
        {
            a = unchecked(a + 0x6d2b79f5);
            uint t = a;
            t = Imul(t ^ (t >> 15), t | 1);
            t ^= t + Imul(t ^ (t >> 7), t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    static uint Imul(uint x, uint y) => unchecked((uint)((int)x * (int)y));
}

public sealed class NoiseField
{
    static readonly double[] Gx = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0, 1, 0, -1, 0 };
    static readonly double[] Gy = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1 };
    static readonly double[] Gz = { 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1, 0, 1, 0, -1 };

    readonly byte[] _perm;

    NoiseField(byte[] perm) { _perm = perm; }

    public static NoiseField Make(int seed = 1)
    {
        var rnd = Rng.MakeRandom(seed ^ unchecked((int)0x9e3779b9));
        var p = new byte[256];
        for (var i = 0; i < 256; i++) p[i] = (byte)i;
        for (var i = 255; i > 0; i--)
        {
            var j = (int)Math.Floor(rnd() * (i + 1));
            (p[i], p[j]) = (p[j], p[i]);
        }
        var perm = new byte[512];
        for (var i = 0; i < 512; i++) perm[i] = p[i & 255];
        return new NoiseField(perm);
    }

    public double Noise3(double x, double y, double z)
    {
        var fx = (int)Math.Floor(x);
        var fy = (int)Math.Floor(y);
        var fz = (int)Math.Floor(z);
        var X = fx & 255; var Y = fy & 255; var Z = fz & 255;
        x -= fx; y -= fy; z -= fz;
        var u = x * x * x * (x * (x * 6 - 15) + 10);
        var v = y * y * y * (y * (y * 6 - 15) + 10);
        var w = z * z * z * (z * (z * 6 - 15) + 10);
        var perm = _perm;
        var A = perm[X] + Y; var B = perm[X + 1] + Y;
        var AA = perm[A] + Z; var AB = perm[A + 1] + Z; var BA = perm[B] + Z; var BB = perm[B + 1] + Z;
        var x1 = x - 1; var y1 = y - 1; var z1 = z - 1;
        int h;
        h = perm[AA] & 15; var n000 = Gx[h] * x + Gy[h] * y + Gz[h] * z;
        h = perm[BA] & 15; var n100 = Gx[h] * x1 + Gy[h] * y + Gz[h] * z;
        h = perm[AB] & 15; var n010 = Gx[h] * x + Gy[h] * y1 + Gz[h] * z;
        h = perm[BB] & 15; var n110 = Gx[h] * x1 + Gy[h] * y1 + Gz[h] * z;
        h = perm[AA + 1] & 15; var n001 = Gx[h] * x + Gy[h] * y + Gz[h] * z1;
        h = perm[BA + 1] & 15; var n101 = Gx[h] * x1 + Gy[h] * y + Gz[h] * z1;
        h = perm[AB + 1] & 15; var n011 = Gx[h] * x + Gy[h] * y1 + Gz[h] * z1;
        h = perm[BB + 1] & 15; var n111 = Gx[h] * x1 + Gy[h] * y1 + Gz[h] * z1;
        var a0 = n000 + u * (n100 - n000); var a1 = n010 + u * (n110 - n010);
        var b0 = n001 + u * (n101 - n001); var b1 = n011 + u * (n111 - n011);
        var c0 = a0 + v * (a1 - a0); var c1 = b0 + v * (b1 - b0);
        var r = c0 + w * (c1 - c0);
        return r > 1 ? 1 : r < -1 ? -1 : r;
    }

    public double Fbm(double x, double y, double z, int octaves = 5, double lacunarity = 2, double gain = 0.5)
    {
        double sum = 0, amp = 1, norm = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += amp * Noise3(x, y, z);
            norm += amp;
            amp *= gain;
            x = x * lacunarity + 17.13; y = y * lacunarity + 5.71; z = z * lacunarity + 9.37;
        }
        return norm > 0 ? sum / norm : 0;
    }
}
