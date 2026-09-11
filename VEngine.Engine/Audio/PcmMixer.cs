using VEngine.Engine.Core;

namespace VEngine.Engine.Audio;

public sealed class PcmMixer : IDisposable
{
    private readonly NativeResource _handle;
    private readonly object _lock = new();
    public int SampleRate { get; }

    public PcmMixer(int sampleRate = 44100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        NativeRuntime.Require("audio_mixer");
        SampleRate = sampleRate;
        _handle = new(NativeAudioMixer.mixer_create(sampleRate, 2), NativeAudioMixer.mixer_destroy);
    }

    public unsafe int Play(ReadOnlySpan<short> samples, int channels = 2, float volume = 1, float pan = 0, bool loop = false)
    {
        if (channels != 1 && channels != 2) throw new ArgumentOutOfRangeException(nameof(channels));
        if (samples.IsEmpty || samples.Length % channels != 0) throw new ArgumentException("PCM must contain complete sample frames.", nameof(samples));
        Validate(volume, pan, 0);
        lock (_lock)
        {
            fixed (short* data = samples)
                return NativeAudioMixer.mixer_play(_handle, (IntPtr)data, samples.Length / channels, channels, volume, pan, loop ? 1 : 0);
        }
    }

    public void SetSpatial(int voice, float volume, float pan, float lowpass = 0)
    {
        Validate(volume, pan, lowpass);
        lock (_lock) NativeAudioMixer.mixer_set_spatial(_handle, voice, volume, pan, lowpass);
    }

    public void Stop(int voice)
    {
        lock (_lock) NativeAudioMixer.mixer_stop(_handle, voice);
    }

    public void StopAll()
    {
        lock (_lock) NativeAudioMixer.mixer_stop_all(_handle);
    }

    public bool IsPlaying(int voice)
    {
        lock (_lock) return NativeAudioMixer.mixer_voice_finished(_handle, voice) == 0;
    }

    public int ActiveVoices
    {
        get { lock (_lock) return NativeAudioMixer.mixer_active_voices(_handle); }
    }

    public unsafe void Mix(Span<short> stereoOutput)
    {
        if (stereoOutput.Length % 2 != 0) throw new ArgumentException("Output must contain stereo sample frames.", nameof(stereoOutput));
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            fixed (short* output = stereoOutput)
            {
                int frames = stereoOutput.Length / 2;
                for (int offset = 0; offset < frames;)
                    offset += NativeAudioMixer.mixer_mix(_handle, (IntPtr)(output + offset * 2), System.Math.Min(frames - offset, 8192));
            }
        }
    }

    internal unsafe void AddTo(IntPtr stream, int length, float volume)
    {
        Span<short> mixed = stackalloc short[4096];
        var target = new Span<short>((void*)stream, length / 2);
        for (int offset = 0; offset < target.Length; offset += mixed.Length)
        {
            int count = System.Math.Min(mixed.Length, target.Length - offset);
            Mix(mixed[..count]);
            for (int i = 0; i < count; i++)
                target[offset + i] = (short)System.Math.Clamp(target[offset + i] + (int)(mixed[i] * volume), short.MinValue, short.MaxValue);
        }
    }

    private static void Validate(float volume, float pan, float lowpass)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        if (!float.IsFinite(pan) || pan < -1 || pan > 1) throw new ArgumentOutOfRangeException(nameof(pan));
        if (!float.IsFinite(lowpass) || lowpass < 0 || lowpass > 1) throw new ArgumentOutOfRangeException(nameof(lowpass));
    }

    public void Dispose()
    {
        lock (_lock) _handle.Dispose();
    }
}
