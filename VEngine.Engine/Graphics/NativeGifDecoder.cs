using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Graphics;

/// <summary>
/// P/Invoke wrapper for the native C++ GIF decoder.
/// Decodes all frames upfront into a single native buffer.
/// C# streams one frame at a time to a reusable GPU texture.
/// </summary>
internal static class NativeGifDecoder
{
    private const string DLL = "gif_decoder";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr gif_open(string path);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void gif_close(IntPtr g);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int gif_frame_count(IntPtr g);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int gif_width(IntPtr g);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int gif_height(IntPtr g);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int gif_frame_delay(IntPtr g, int frame);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr gif_frame_data(IntPtr g, int frame);

    public static bool IsAvailable()
    {
        try
        {
            var h = gif_open("");
            if (h != IntPtr.Zero) gif_close(h);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
