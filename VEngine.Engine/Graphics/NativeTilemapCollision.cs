using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Graphics;

/// <summary>
/// P/Invoke wrapper for the native C++ tilemap collision DLL.
/// AABB queries, DDA raycast, and line-of-sight on solid tile grids.
/// </summary>
internal static class NativeTilemapCollision
{
    private const string DLL = "tilemap_collision";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tilecol_create(int width, int height, int tileSize, int[] solid);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tilecol_destroy(IntPtr tc);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tilecol_update(IntPtr tc, int[] solid);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tilecol_set(IntPtr tc, int tx, int ty, int solid);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tilecol_aabb_test(IntPtr tc, float x, float y, float w, float h);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tilecol_aabb_query(
        IntPtr tc, float x, float y, float w, float h,
        int[] txOut, int[] tyOut, int maxResults
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern float tilecol_raycast(
        IntPtr tc,
        float ox, float oy, float dx, float dy, float maxDist,
        out int hitTX, out int hitTY
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tilecol_line_of_sight(
        IntPtr tc,
        float ax, float ay, float bx, float by
    );

    public static bool IsAvailable()
    {
        try
        {
            var handle = tilecol_create(2, 2, 16, null!);
            if (handle != IntPtr.Zero) tilecol_destroy(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
