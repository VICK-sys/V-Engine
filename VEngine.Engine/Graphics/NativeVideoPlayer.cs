using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Graphics;

/// <summary>
/// P/Invoke wrapper for the native FFmpeg video decoder DLL.
/// </summary>
internal static class NativeVideoPlayer
{
    private const string DLL = "video_player";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr video_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void video_close(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_width(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_height(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern double video_duration(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern double video_fps(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_finished(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_next_frame(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr video_frame_data(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void video_seek(IntPtr vp, double seconds);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void video_rewind(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_has_audio(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_audio_sample_rate(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_audio_channels(IntPtr vp);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int video_get_audio(IntPtr vp, short[] buffer, int maxSamples);

    public static bool IsAvailable()
    {
        try
        {
            var handle = video_open("");
            if (handle != IntPtr.Zero) video_close(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
