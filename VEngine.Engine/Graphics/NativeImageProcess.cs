using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Graphics;

/// <summary>
/// P/Invoke wrapper for the native C++ image processing DLL.
/// Blur, outline, tint, palette swap, grayscale, invert on RGBA buffers.
/// </summary>
internal static class NativeImageProcess
{
    private const string DLL = "image_process";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_blur(
        byte[] input, byte[] output,
        int width, int height, int radius
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_outline(
        byte[] input, byte[] output,
        int width, int height, int thickness,
        byte r, byte g, byte b, byte a
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_tint(
        byte[] pixels, int width, int height,
        byte r, byte g, byte b, byte a
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_palette_swap(
        byte[] pixels, int width, int height,
        byte[] fromColors, byte[] toColors,
        int count, int tolerance
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_grayscale(byte[] pixels, int width, int height);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void imgproc_invert(byte[] pixels, int width, int height);

    public static bool IsAvailable()
    {
        try
        {
            byte[] test = new byte[16];
            imgproc_invert(test, 2, 2);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
