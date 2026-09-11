using VEngine.Engine.Core;
using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Audio;

/// <summary>
/// P/Invoke wrapper for the native C++ spatial audio mixer DLL.
/// Per-sample distance attenuation, stereo panning, and low-pass filter.
/// </summary>
internal static class NativeAudioMixer
{
    private const string DLL = "audio_mixer";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mixer_create(int sampleRate, int channels);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mixer_destroy(IntPtr mixer);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mixer_play(
        NativeResource mixer,
        IntPtr pcmData, int sampleCount, int dataChannels,
        float volume, float pan, int loop
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mixer_stop(NativeResource mixer, int voiceId);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mixer_stop_all(NativeResource mixer);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mixer_set_spatial(
        NativeResource mixer, int voiceId,
        float volume, float pan, float lowpass
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mixer_mix(NativeResource mixer, IntPtr output, int maxSamples);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mixer_active_voices(NativeResource mixer);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mixer_voice_finished(NativeResource mixer, int voiceId);

    public static bool IsAvailable()
    {
        try
        {
            var handle = mixer_create(48000, 2);
            if (handle != IntPtr.Zero) mixer_destroy(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
