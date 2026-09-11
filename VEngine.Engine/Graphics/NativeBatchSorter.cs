using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Graphics;

/// <summary>
/// P/Invoke wrapper for the native C++ batch sort + frustum cull DLL.
/// Sorts entities by (layer, zorder) with stable ordering and culls off-screen entities.
/// </summary>
internal static class NativeBatchSorter
{
    private const string DLL = "batch_sorter";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr batch_create(int capacity);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void batch_destroy(IntPtr sorter);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void batch_upload(
        IntPtr sorter, int count,
        int[] layer, float[] zorder, int[] originalIndex, int[] visible,
        float[] screenX, float[] screenY, float[] screenW, float[] screenH,
        float viewportW, float viewportH
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int batch_sort_and_cull(IntPtr sorter);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr batch_get_order(IntPtr sorter);

    public static bool IsAvailable()
    {
        try
        {
            var handle = batch_create(1);
            if (handle != IntPtr.Zero) batch_destroy(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
