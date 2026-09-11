using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Math;

/// <summary>
/// Procedural noise via native C++ DLL (Perlin, Simplex, Worley).
/// Falls back to a managed Perlin implementation when the DLL is unavailable.
///
/// Usage:
///   Noise.Seed(12345);
///   float height = Noise.Perlin2D(x * 0.1f, y * 0.1f);
///   Noise.PerlinFill(buffer, 256, 256, 0, 0, 50f);
/// </summary>
public static class Noise
{
    private const string DLL = "noise";
    private static bool? _available;

    /// <summary>True if the native noise DLL is available.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue) return _available.Value;
            try
            {
                noise_seed(0);
                _available = true;
            }
            catch (DllNotFoundException) { _available = false; }
            catch (EntryPointNotFoundException) { _available = false; }
            catch { _available = false; }
            return _available.Value;
        }
    }

    // ── Native P/Invoke ────────────────────────────────────

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void noise_seed(uint seed);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_perlin_2d(float x, float y);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_perlin_3d(float x, float y, float z);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_perlin_fbm_2d(float x, float y, int octaves, float lacunarity, float persistence);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_simplex_2d(float x, float y);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_simplex_3d(float x, float y, float z);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern float noise_worley_2d(float x, float y, float jitter);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void noise_perlin_fill_2d(float[] output, int width, int height, float ox, float oy, float scale);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void noise_simplex_fill_2d(float[] output, int width, int height, float ox, float oy, float scale);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void noise_worley_fill_2d(float[] output, int width, int height, float ox, float oy, float scale, float jitter);

    // ── Public API ─────────────────────────────────────────

    /// <summary>Set the noise permutation seed. Same seed = same noise pattern.</summary>
    public static void Seed(uint seed)
    {
        if (IsAvailable) noise_seed(seed);
        else ManagedSeed(seed);
    }

    /// <summary>2D Perlin noise. Returns value in approximately [-1, 1].</summary>
    public static float Perlin2D(float x, float y)
    {
        return IsAvailable ? noise_perlin_2d(x, y) : ManagedPerlin2D(x, y);
    }

    /// <summary>3D Perlin noise. Returns value in approximately [-1, 1].</summary>
    public static float Perlin3D(float x, float y, float z)
    {
        return IsAvailable ? noise_perlin_3d(x, y, z) : ManagedPerlin2D(x + z, y + z);
    }

    /// <summary>Fractal Brownian motion (multi-octave Perlin sum). Returns value in approximately [-1, 1].</summary>
    public static float PerlinFBM(float x, float y, int octaves = 4, float lacunarity = 2f, float persistence = 0.5f)
    {
        if (IsAvailable) return noise_perlin_fbm_2d(x, y, octaves, lacunarity, persistence);
        float total = 0, amp = 1, freq = 1, maxVal = 0;
        for (int i = 0; i < octaves; i++)
        {
            total += ManagedPerlin2D(x * freq, y * freq) * amp;
            maxVal += amp;
            amp *= persistence;
            freq *= lacunarity;
        }
        return maxVal > 0 ? total / maxVal : 0;
    }

    /// <summary>2D Simplex noise (faster than Perlin for higher dimensions, fewer artifacts).</summary>
    public static float Simplex2D(float x, float y)
    {
        return IsAvailable ? noise_simplex_2d(x, y) : ManagedPerlin2D(x, y);
    }

    /// <summary>3D Simplex noise.</summary>
    public static float Simplex3D(float x, float y, float z)
    {
        return IsAvailable ? noise_simplex_3d(x, y, z) : ManagedPerlin2D(x + z, y + z);
    }

    /// <summary>2D Worley (cellular) noise. Returns distance to nearest feature point.</summary>
    public static float Worley2D(float x, float y, float jitter = 1f)
    {
        if (IsAvailable) return noise_worley_2d(x, y, jitter);
        return 0.5f; // managed fallback not implemented
    }

    /// <summary>Fill an array with 2D Perlin values. Faster than per-pixel calls.</summary>
    public static void PerlinFill(float[] output, int width, int height, float offsetX, float offsetY, float scale)
    {
        if (IsAvailable) { noise_perlin_fill_2d(output, width, height, offsetX, offsetY, scale); return; }
        float invScale = scale != 0 ? 1f / scale : 1f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                output[y * width + x] = ManagedPerlin2D((x + offsetX) * invScale, (y + offsetY) * invScale);
    }

    /// <summary>Fill an array with 2D Simplex values.</summary>
    public static void SimplexFill(float[] output, int width, int height, float offsetX, float offsetY, float scale)
    {
        if (IsAvailable) { noise_simplex_fill_2d(output, width, height, offsetX, offsetY, scale); return; }
        PerlinFill(output, width, height, offsetX, offsetY, scale);
    }

    /// <summary>Fill an array with 2D Worley values.</summary>
    public static void WorleyFill(float[] output, int width, int height, float offsetX, float offsetY, float scale, float jitter = 1f)
    {
        if (IsAvailable) { noise_worley_fill_2d(output, width, height, offsetX, offsetY, scale, jitter); return; }
        // Managed fallback
        float invScale = scale != 0 ? 1f / scale : 1f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                output[y * width + x] = 0.5f;
    }

    // ── Managed fallback (simple Perlin) ───────────────────

    private static readonly byte[] _perm = new byte[512];
    private static bool _managedInit;

    private static void ManagedSeed(uint seed)
    {
        byte[] p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        uint s = seed != 0 ? seed : 12345u;
        for (int i = 255; i > 0; i--)
        {
            s = s * 1103515245u + 12345u;
            int j = (int)((s >> 16) % (i + 1));
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 256; i++)
        {
            _perm[i] = p[i];
            _perm[i + 256] = p[i];
        }
        _managedInit = true;
    }

    private static float ManagedFade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float ManagedLerp(float a, float b, float t) => a + t * (b - a);
    private static float ManagedGrad(int hash, float x, float y)
    {
        int h = hash & 7;
        float u = h < 4 ? x : y;
        float v = h < 4 ? y : x;
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -2f * v : 2f * v);
    }

    private static float ManagedPerlin2D(float x, float y)
    {
        if (!_managedInit) ManagedSeed(0);
        int X = (int)MathF.Floor(x) & 255;
        int Y = (int)MathF.Floor(y) & 255;
        x -= MathF.Floor(x);
        y -= MathF.Floor(y);
        float u = ManagedFade(x), v = ManagedFade(y);
        int A = _perm[X] + Y;
        int B = _perm[X + 1] + Y;
        return ManagedLerp(
            ManagedLerp(ManagedGrad(_perm[A], x, y), ManagedGrad(_perm[B], x - 1, y), u),
            ManagedLerp(ManagedGrad(_perm[A + 1], x, y - 1), ManagedGrad(_perm[B + 1], x - 1, y - 1), u),
            v
        ) * 0.5f;
    }
}
