// audio_mixer.cpp — Native spatial audio mixer for V-Engine
// Per-sample distance attenuation, stereo panning, and one-pole low-pass filter.

#include "audio_mixer.h"
#include <cstring>
#include <cmath>
#include <algorithm>
#include <vector>

struct Voice {
    std::vector<short> data;
    int sampleCount;     // total samples per channel
    int dataChannels;    // 1 = mono, 2 = stereo
    int position;        // current sample position
    float volume;
    float pan;           // -1..1
    float lowpass;       // 0..1 (filter coefficient)
    float lpStateL;      // low-pass filter state (left)
    float lpStateR;      // low-pass filter state (right)
    bool active;
    bool loop;
};

struct AudioMixerImpl {
    int sampleRate;
    int outChannels;
    Voice voices[MIXER_MAX_VOICES]{};
    float mixBufL[8192]; // temp float mix buffer
    float mixBufR[8192];
};

// ── Lifecycle ───────────────────────────────────────────────

MIXER_API AudioMixerHandle mixer_create(int sampleRate, int channels) {
    auto* m = new AudioMixerImpl();
    m->sampleRate = sampleRate;
    m->outChannels = channels;
    return m;
}

MIXER_API void mixer_destroy(AudioMixerHandle mixer) {
    delete mixer;
}

// ── Voice management ────────────────────────────────────────

MIXER_API int mixer_play(
    AudioMixerHandle m,
    const short* data, int sampleCount, int dataChannels,
    float volume, float pan, int loop
) {
    if (!data || sampleCount <= 0 || (dataChannels != 1 && dataChannels != 2)) return -1;
    for (int i = 0; i < MIXER_MAX_VOICES; i++) {
        if (!m->voices[i].active) {
            Voice& v = m->voices[i];
            v.data.assign(data, data + sampleCount * dataChannels);
            v.sampleCount = sampleCount;
            v.dataChannels = dataChannels;
            v.position = 0;
            v.volume = volume;
            v.pan = pan;
            v.lowpass = 0;
            v.lpStateL = 0;
            v.lpStateR = 0;
            v.active = true;
            v.loop = loop != 0;
            return i;
        }
    }
    return -1;
}

MIXER_API void mixer_stop(AudioMixerHandle m, int id) {
    if (id >= 0 && id < MIXER_MAX_VOICES)
        m->voices[id].active = false;
}

MIXER_API void mixer_stop_all(AudioMixerHandle m) {
    for (int i = 0; i < MIXER_MAX_VOICES; i++)
        m->voices[i].active = false;
}

// ── Spatial update ──────────────────────────────────────────

MIXER_API void mixer_set_spatial(
    AudioMixerHandle m, int id,
    float volume, float pan, float lowpass
) {
    if (id < 0 || id >= MIXER_MAX_VOICES) return;
    Voice& v = m->voices[id];
    v.volume = volume;
    v.pan = std::clamp(pan, -1.0f, 1.0f);
    v.lowpass = std::clamp(lowpass, 0.0f, 1.0f);
}

// ── Mix ─────────────────────────────────────────────────────

MIXER_API int mixer_mix(AudioMixerHandle m, short* output, int maxSamples) {
    int count = std::min(maxSamples, 8192);

    // Clear mix buffers
    memset(m->mixBufL, 0, count * sizeof(float));
    memset(m->mixBufR, 0, count * sizeof(float));

    for (int vi = 0; vi < MIXER_MAX_VOICES; vi++) {
        Voice& v = m->voices[vi];
        if (!v.active) continue;

        // Panning: equal-power
        float panL = cosf((v.pan + 1.0f) * 0.25f * 3.14159265f);
        float panR = sinf((v.pan + 1.0f) * 0.25f * 3.14159265f);
        float vol = v.volume;
        float lpCoeff = v.lowpass; // 0 = passthrough, 1 = full filter
        float lpAlpha = 1.0f - lpCoeff * 0.95f; // filter smoothing (higher = more filtering)

        for (int i = 0; i < count; i++) {
            if (v.position >= v.sampleCount) {
                if (v.loop) {
                    v.position = 0;
                } else {
                    v.active = false;
                    break;
                }
            }

            float sampleL, sampleR;
            if (v.dataChannels == 2) {
                sampleL = v.data[v.position * 2] / 32768.0f;
                sampleR = v.data[v.position * 2 + 1] / 32768.0f;
            } else {
                float mono = v.data[v.position] / 32768.0f;
                sampleL = mono;
                sampleR = mono;
            }
            v.position++;

            // Low-pass filter (one-pole IIR)
            if (lpCoeff > 0.001f) {
                v.lpStateL += lpAlpha * (sampleL - v.lpStateL);
                v.lpStateR += lpAlpha * (sampleR - v.lpStateR);
                sampleL = v.lpStateL;
                sampleR = v.lpStateR;
            }

            // Volume + pan
            m->mixBufL[i] += sampleL * vol * panL;
            m->mixBufR[i] += sampleR * vol * panR;
            if (v.position == v.sampleCount && !v.loop) v.active = false;
        }
    }

    // Convert to S16 interleaved output
    for (int i = 0; i < count; i++) {
        float l = m->mixBufL[i];
        float r = m->mixBufR[i];

        // Soft clamp
        l = std::clamp(l, -1.0f, 1.0f);
        r = std::clamp(r, -1.0f, 1.0f);

        output[i * 2]     = (short)(l * 32767.0f);
        output[i * 2 + 1] = (short)(r * 32767.0f);
    }

    return count;
}

// ── Query ───────────────────────────────────────────────────

MIXER_API int mixer_active_voices(AudioMixerHandle m) {
    int count = 0;
    for (int i = 0; i < MIXER_MAX_VOICES; i++)
        if (m->voices[i].active) count++;
    return count;
}

MIXER_API int mixer_voice_finished(AudioMixerHandle m, int id) {
    if (id < 0 || id >= MIXER_MAX_VOICES) return 1;
    return m->voices[id].active ? 0 : 1;
}
