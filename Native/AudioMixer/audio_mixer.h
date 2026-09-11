// audio_mixer.h — Native spatial audio mixer for V-Engine
// Per-sample distance attenuation, stereo panning, and low-pass filter.
// Processes raw PCM audio buffers via P/Invoke.

#pragma once

#ifdef _WIN32
    #ifdef MIXER_EXPORTS
        #define MIXER_API __declspec(dllexport)
    #else
        #define MIXER_API __declspec(dllimport)
    #endif
#else
    #define MIXER_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define MIXER_MAX_VOICES 64

// ── Mixer handle ────────────────────────────────────────────

typedef struct AudioMixerImpl* AudioMixerHandle;

// ── Lifecycle ───────────────────────────────────────────────

MIXER_API AudioMixerHandle mixer_create(int sample_rate, int channels);
MIXER_API void mixer_destroy(AudioMixerHandle mixer);

// ── Voice management ────────────────────────────────────────

// Add a voice with raw PCM data (S16, interleaved stereo or mono).
// Returns voice ID (0..63), or -1 if full.
MIXER_API int mixer_play(
    AudioMixerHandle mixer,
    const short* pcm_data, int sample_count, int data_channels,
    float volume, float pan, int loop
);

// Stop a voice.
MIXER_API void mixer_stop(AudioMixerHandle mixer, int voice_id);

// Stop all voices.
MIXER_API void mixer_stop_all(AudioMixerHandle mixer);

// ── Spatial update (call per frame per voice) ───────────────

// Set spatial parameters for a voice.
// distance: 0 = at listener, 1 = at max range
// pan: -1 = full left, 0 = center, 1 = full right
// lowpass: 0 = no filter, 1 = full muffling (applied per-sample)
MIXER_API void mixer_set_spatial(
    AudioMixerHandle mixer, int voice_id,
    float volume, float pan, float lowpass
);

// ── Mix output ──────────────────────────────────────────────

// Mix all active voices into the output buffer (S16 interleaved stereo).
// Called from the audio callback. Returns number of samples written per channel.
MIXER_API int mixer_mix(
    AudioMixerHandle mixer,
    short* output, int max_samples
);

// ── Query ───────────────────────────────────────────────────

MIXER_API int mixer_active_voices(AudioMixerHandle mixer);
MIXER_API int mixer_voice_finished(AudioMixerHandle mixer, int voice_id);

#ifdef __cplusplus
}
#endif
