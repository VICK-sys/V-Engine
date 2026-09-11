using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Core;

/// <summary>
/// P/Invoke wrapper for the native C++ A* pathfinding DLL.
/// Grid-based with binary heap, 4/8-directional, corner-cut prevention.
/// </summary>
internal static class NativePathfinder
{
    private const string DLL = "pathfinder";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pf_create(int width, int height, int[] walkable);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pf_destroy(IntPtr pf);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pf_update_grid(IntPtr pf, int[] walkable);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pf_set_cell(IntPtr pf, int x, int y, int walkable);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pf_find_path(
        IntPtr pf, int sx, int sy, int ex, int ey,
        int allowDiagonal, int maxSearch
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pf_get_path_x(IntPtr pf);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pf_get_path_y(IntPtr pf);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pf_is_walkable(IntPtr pf, int x, int y);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pf_width(IntPtr pf);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pf_height(IntPtr pf);

    public static bool IsAvailable()
    {
        try
        {
            var handle = pf_create(2, 2, null!);
            if (handle != IntPtr.Zero) pf_destroy(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
