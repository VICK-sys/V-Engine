using System;
using System.Collections.Generic;
using System.IO;
using SDL2;

namespace VEngine.Engine.Audio;

public class AudioManager
{
    private readonly Dictionary<string, IntPtr> _sounds = new();
    private readonly Dictionary<int, float> _channelVolumes = new();
    // Pre-allocated lists to avoid enumerator/list allocations during volume updates
    private readonly List<int> _channelKeys = new();
    private readonly List<int> _finishedChannels = new();
    private IntPtr _music;
    private string? _musicPath;
    private float _masterVolume = 1f;
    private float _soundVolume = 1f;
    private float _musicVolume = 1f;

    // ── Crossfade Music Channels ──
    // Two reserved sound channels for true overlapping crossfade.
    // Music is loaded as Mix_Chunk and played on these dedicated channels.
    private const int SfxChannelCount = 32;
    private const int MusicChannelA = 32;
    private const int MusicChannelB = 33;
    private const int TotalChannels = 34;

    private IntPtr _musicChunkA, _musicChunkB;
    private string? _musicPathA, _musicPathB;
    private int _activeMusicChannel = -1;  // which channel holds the currently-audible track
    private float _crossfadeTimer;
    private float _crossfadeDuration;
    private bool _crossfading;
    private bool _musicUsingChunks; // true when using dual-channel path

    private const int SfxGroup = 1;
    private const int MusicGroup = 2;

    internal AudioManager()
    {
        if (SDL_mixer.Mix_OpenAudio(44100, SDL_mixer.MIX_DEFAULT_FORMAT, 2, 2048) < 0)
            throw new Exception($"SDL_mixer init failed: {SDL.SDL_GetError()}");
        // 32 channels for SFX + 2 reserved for music crossfade
        SDL_mixer.Mix_AllocateChannels(TotalChannels);
        // Group channels so SFX (-1 auto-pick) never steals music channels
        SDL_mixer.Mix_GroupChannels(0, SfxChannelCount - 1, SfxGroup);
        SDL_mixer.Mix_GroupChannels(MusicChannelA, MusicChannelB, MusicGroup);
    }

    // ---- Sound Effects ----

    /// <summary>
    /// Play a sound effect by filename. Loads and caches automatically.
    /// Returns the channel number, or -1 on failure.
    /// </summary>
    public int PlaySound(string path, float volume = 1f, int loops = 0)
    {
        var chunk = GetOrLoadSound(path);
        // Pick a free channel from the SFX group so music channels are never stolen.
        // If none free, GroupAvailable returns -1 and the sound is dropped (same behavior as before).
        int channel = SDL_mixer.Mix_GroupAvailable(SfxGroup);
        if (channel < 0) return -1;
        channel = SDL_mixer.Mix_PlayChannel(channel, chunk, loops);
        if (channel >= 0)
        {
            SDL_mixer.Mix_Volume(channel, ToMixVol(volume * _soundVolume * _masterVolume));
            _channelVolumes[channel] = volume;
        }
        return channel;
    }

    /// <summary>
    /// Preload a sound effect without playing it.
    /// </summary>
    public void LoadSound(string path)
    {
        GetOrLoadSound(path);
    }

    /// <summary>
    /// Stop a specific channel returned by PlaySound.
    /// </summary>
    public void StopChannel(int channel)
    {
        SDL_mixer.Mix_HaltChannel(channel);
        _channelVolumes.Remove(channel);
    }

    /// <summary>
    /// Stop all playing sound effects.
    /// </summary>
    public void StopAllSounds()
    {
        SDL_mixer.Mix_HaltChannel(-1);
        _channelVolumes.Clear();
    }

    // ---- Spatial Sound ----

    /// <summary>
    /// Play a sound at a world position. Volume and stereo pan are computed
    /// based on distance and direction from the camera center.
    /// Returns the channel number, or -1 if too far or no free channel.
    /// </summary>
    public int PlaySoundAt(string path, float worldX, float worldY,
        float volume = 1f, float maxDistance = 500f, int loops = 0)
    {
        var (dx, dy, dist) = GetSpatialOffset(worldX, worldY);
        if (dist >= maxDistance) return -1;

        var chunk = GetOrLoadSound(path);
        int channel = SDL_mixer.Mix_GroupAvailable(SfxGroup);
        if (channel < 0) return -1;
        channel = SDL_mixer.Mix_PlayChannel(channel, chunk, loops);
        if (channel < 0) return -1;

        SDL_mixer.Mix_Volume(channel, ToMixVol(volume * _soundVolume * _masterVolume));
        ApplySpatialPosition(channel, dx, dy, dist, maxDistance);
        return channel;
    }

    /// <summary>
    /// Update the spatial position of a playing channel (for looping spatial sounds).
    /// Call each frame for sounds that follow a moving source.
    /// </summary>
    public void SetSoundPosition(int channel, float worldX, float worldY, float maxDistance = 500f)
    {
        if (channel < 0) return;
        var (dx, dy, dist) = GetSpatialOffset(worldX, worldY);
        if (dist >= maxDistance)
        {
            SDL_mixer.Mix_HaltChannel(channel);
            _channelVolumes.Remove(channel);
            return;
        }
        ApplySpatialPosition(channel, dx, dy, dist, maxDistance);
    }

    /// <summary>
    /// Remove spatial positioning from a channel (restores center pan, full volume).
    /// </summary>
    public void ClearSoundPosition(int channel)
    {
        if (channel < 0) return;
        SDL_mixer.Mix_SetPosition(channel, 0, 0);
    }

    private (float dx, float dy, float dist) GetSpatialOffset(float worldX, float worldY)
    {
        var cam = Core.Eng.Camera;
        float cx = cam.Position.X + Core.Eng.Width / (2f * cam.Zoom);
        float cy = cam.Position.Y + Core.Eng.Height / (2f * cam.Zoom);
        float dx = worldX - cx;
        float dy = worldY - cy;
        return (dx, dy, MathF.Sqrt(dx * dx + dy * dy));
    }

    private static void ApplySpatialPosition(int channel, float dx, float dy, float dist, float maxDistance)
    {
        byte sdlDist = (byte)(dist / maxDistance * 255);
        short angle = 0;
        if (dist > 1f)
        {
            float a = MathF.Atan2(dy, dx) * (180f / MathF.PI);
            angle = (short)(((int)(a + 90) % 360 + 360) % 360);
        }
        SDL_mixer.Mix_SetPosition(channel, angle, sdlDist);
    }

    // ---- Music ----

    /// <summary>
    /// Play background music. Only one track at a time.
    /// Supports WAV, OGG, MP3, FLAC (depending on SDL_mixer build).
    /// </summary>
    public void PlayMusic(string path, bool loop = true, float fadeInMs = 0)
    {
        // Don't restart if already playing this track
        if (_musicPath == path && SDL_mixer.Mix_PlayingMusic() != 0)
            return;

        FreeMusic();

        var fullPath = Path.IsPathRooted(path) ? path : Core.Eng.Asset(path);
        _music = SDL_mixer.Mix_LoadMUS(fullPath);
        if (_music == IntPtr.Zero)
            throw new Exception($"Failed to load music '{path}': {SDL.SDL_GetError()}");
        _musicPath = path;

        SDL_mixer.Mix_VolumeMusic(ToMixVol(_musicVolume * _masterVolume));

        if (fadeInMs > 0)
            SDL_mixer.Mix_FadeInMusic(_music, loop ? -1 : 1, (int)fadeInMs);
        else
            SDL_mixer.Mix_PlayMusic(_music, loop ? -1 : 1);
    }

    /// <summary>
    /// Stop the current music. Optional fade out in milliseconds.
    /// Handles both legacy Mix_Music and chunk-mode crossfade channels.
    /// </summary>
    public void StopMusic(float fadeOutMs = 0)
    {
        // Legacy path
        if (_music != IntPtr.Zero)
        {
            if (fadeOutMs > 0)
                SDL_mixer.Mix_FadeOutMusic((int)fadeOutMs);
            else
                SDL_mixer.Mix_HaltMusic();
        }

        // Chunk-mode path
        if (_musicUsingChunks)
        {
            if (fadeOutMs > 0)
            {
                if (MusicChannelA >= 0) SDL_mixer.Mix_FadeOutChannel(MusicChannelA, (int)fadeOutMs);
                if (MusicChannelB >= 0) SDL_mixer.Mix_FadeOutChannel(MusicChannelB, (int)fadeOutMs);
            }
            else
            {
                SDL_mixer.Mix_HaltChannel(MusicChannelA);
                SDL_mixer.Mix_HaltChannel(MusicChannelB);
                if (_musicChunkA != IntPtr.Zero) { SDL_mixer.Mix_FreeChunk(_musicChunkA); _musicChunkA = IntPtr.Zero; _musicPathA = null; }
                if (_musicChunkB != IntPtr.Zero) { SDL_mixer.Mix_FreeChunk(_musicChunkB); _musicChunkB = IntPtr.Zero; _musicPathB = null; }
                _activeMusicChannel = -1;
                _crossfading = false;
            }
        }
    }

    /// <summary>
    /// True overlapping crossfade between two music tracks. Loads the new track on a
    /// reserved channel, ramps both volumes simultaneously over the duration.
    /// Requires Update(dt) to be called each frame from the game loop.
    /// </summary>
    public void CrossFadeMusic(string path, float durationMs = 1000f, bool loop = true)
    {
        // Same track already playing — no-op
        if (_musicUsingChunks && _activeMusicChannel >= 0)
        {
            string? activePath = _activeMusicChannel == MusicChannelA ? _musicPathA : _musicPathB;
            if (activePath == path) return;
        }

        // Stop any SDL_mixer Mix_Music (legacy PlayMusic path) — we're switching to chunk mode
        if (!_musicUsingChunks && _music != IntPtr.Zero)
        {
            SDL_mixer.Mix_HaltMusic();
            SDL_mixer.Mix_FreeMusic(_music);
            _music = IntPtr.Zero;
            _musicPath = null;
        }
        _musicUsingChunks = true;

        // Load the new chunk on the inactive channel
        var fullPath = Path.IsPathRooted(path) ? path : Core.Eng.Asset(path);
        IntPtr newChunk = SDL_mixer.Mix_LoadWAV(fullPath);
        if (newChunk == IntPtr.Zero)
            throw new Exception($"Failed to load music '{path}': {SDL.SDL_GetError()}");

        int nextChannel = _activeMusicChannel == MusicChannelA ? MusicChannelB : MusicChannelA;

        // Free old chunk on the channel we're about to use
        if (nextChannel == MusicChannelA)
        {
            if (_musicChunkA != IntPtr.Zero)
            {
                SDL_mixer.Mix_HaltChannel(MusicChannelA);
                SDL_mixer.Mix_FreeChunk(_musicChunkA);
            }
            _musicChunkA = newChunk;
            _musicPathA = path;
        }
        else
        {
            if (_musicChunkB != IntPtr.Zero)
            {
                SDL_mixer.Mix_HaltChannel(MusicChannelB);
                SDL_mixer.Mix_FreeChunk(_musicChunkB);
            }
            _musicChunkB = newChunk;
            _musicPathB = path;
        }

        // Start new track at volume 0, fade in via Update loop
        SDL_mixer.Mix_Volume(nextChannel, 0);
        SDL_mixer.Mix_PlayChannel(nextChannel, newChunk, loop ? -1 : 0);

        // Start crossfade — Update() will ramp both channels
        if (durationMs <= 0)
        {
            // Instant swap
            if (_activeMusicChannel >= 0) SDL_mixer.Mix_HaltChannel(_activeMusicChannel);
            SDL_mixer.Mix_Volume(nextChannel, ToMixVol(_musicVolume * _masterVolume));
            _activeMusicChannel = nextChannel;
            _crossfading = false;
            return;
        }

        _crossfadeDuration = durationMs / 1000f;
        _crossfadeTimer = 0;
        _crossfading = true;
        _activeMusicChannel = nextChannel; // the new channel becomes "active"
    }

    /// <summary>
    /// Update the audio manager — drives crossfade ramps. Called each frame by Game.
    /// </summary>
    internal void Update(float dt)
    {
        if (!_crossfading) return;

        _crossfadeTimer += dt;
        float t = System.Math.Clamp(_crossfadeTimer / _crossfadeDuration, 0f, 1f);

        int activeCh = _activeMusicChannel;
        int fadingCh = activeCh == MusicChannelA ? MusicChannelB : MusicChannelA;
        float targetVol = _musicVolume * _masterVolume;

        // Active channel fades in (0 -> target), fading channel fades out (target -> 0)
        SDL_mixer.Mix_Volume(activeCh, ToMixVol(targetVol * t));
        SDL_mixer.Mix_Volume(fadingCh, ToMixVol(targetVol * (1f - t)));

        if (t >= 1f)
        {
            // Crossfade complete — stop the old channel
            SDL_mixer.Mix_HaltChannel(fadingCh);
            // Free the faded-out chunk so it doesn't leak
            if (fadingCh == MusicChannelA && _musicChunkA != IntPtr.Zero)
            {
                SDL_mixer.Mix_FreeChunk(_musicChunkA);
                _musicChunkA = IntPtr.Zero;
                _musicPathA = null;
            }
            else if (fadingCh == MusicChannelB && _musicChunkB != IntPtr.Zero)
            {
                SDL_mixer.Mix_FreeChunk(_musicChunkB);
                _musicChunkB = IntPtr.Zero;
                _musicPathB = null;
            }
            _crossfading = false;
        }
    }

    /// <summary>Pause music playback.</summary>
    public void PauseMusic() => SDL_mixer.Mix_PauseMusic();

    /// <summary>Resume paused music.</summary>
    public void ResumeMusic() => SDL_mixer.Mix_ResumeMusic();

    /// <summary>True if music is currently playing (not paused).</summary>
    public bool IsMusicPlaying => SDL_mixer.Mix_PlayingMusic() != 0 && SDL_mixer.Mix_PausedMusic() == 0;

    /// <summary>True if music is paused.</summary>
    public bool IsMusicPaused => SDL_mixer.Mix_PausedMusic() != 0;

    // ---- Volume ----

    /// <summary>Master volume (0.0 - 1.0). Affects all audio.</summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = System.Math.Clamp(value, 0f, 1f);
            ApplyMusicVolume();
            ApplyAllChannelVolumes();
        }
    }

    /// <summary>Sound effects volume (0.0 - 1.0). Updates all playing channels retroactively.</summary>
    public float SoundVolume
    {
        get => _soundVolume;
        set
        {
            _soundVolume = System.Math.Clamp(value, 0f, 1f);
            ApplyAllChannelVolumes();
        }
    }

    /// <summary>Music volume (0.0 - 1.0).</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = System.Math.Clamp(value, 0f, 1f);
            ApplyMusicVolume();
        }
    }

    // ---- Internal ----

    private IntPtr GetOrLoadSound(string path)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Core.Eng.Asset(path);
        var key = Path.GetFullPath(fullPath);

        if (_sounds.TryGetValue(key, out var chunk))
            return chunk;

        chunk = SDL_mixer.Mix_LoadWAV(fullPath);
        if (chunk == IntPtr.Zero)
            throw new Exception($"Failed to load sound '{path}': {SDL.SDL_GetError()}");

        _sounds[key] = chunk;
        return chunk;
    }

    /// <summary>Prune finished channels. Call periodically (e.g., each frame) to prevent unbounded growth.</summary>
    internal void CleanupFinishedChannels()
    {
        if (_channelVolumes.Count == 0) return;
        _finishedChannels.Clear();
        _channelKeys.Clear();
        foreach (var k in _channelVolumes.Keys) _channelKeys.Add(k);
        for (int i = 0; i < _channelKeys.Count; i++)
        {
            if (SDL_mixer.Mix_Playing(_channelKeys[i]) == 0)
                _finishedChannels.Add(_channelKeys[i]);
        }
        for (int i = 0; i < _finishedChannels.Count; i++)
            _channelVolumes.Remove(_finishedChannels[i]);
    }

    private void ApplyAllChannelVolumes()
    {
        if (_channelVolumes.Count == 0) return;
        _finishedChannels.Clear();
        _channelKeys.Clear();
        foreach (var k in _channelVolumes.Keys) _channelKeys.Add(k);
        for (int i = 0; i < _channelKeys.Count; i++)
        {
            int ch = _channelKeys[i];
            if (SDL_mixer.Mix_Playing(ch) != 0)
                SDL_mixer.Mix_Volume(ch, ToMixVol(_channelVolumes[ch] * _soundVolume * _masterVolume));
            else
                _finishedChannels.Add(ch);
        }
        for (int i = 0; i < _finishedChannels.Count; i++)
            _channelVolumes.Remove(_finishedChannels[i]);
    }

    private void ApplyMusicVolume()
    {
        SDL_mixer.Mix_VolumeMusic(ToMixVol(_musicVolume * _masterVolume));
        // Also update chunk-mode music channel (skip during crossfade so ramp logic isn't overridden)
        if (_musicUsingChunks && !_crossfading && _activeMusicChannel >= 0)
            SDL_mixer.Mix_Volume(_activeMusicChannel, ToMixVol(_musicVolume * _masterVolume));
    }

    private void FreeMusic()
    {
        if (_music != IntPtr.Zero)
        {
            SDL_mixer.Mix_HaltMusic();
            SDL_mixer.Mix_FreeMusic(_music);
            _music = IntPtr.Zero;
            _musicPath = null;
        }
    }

    private static int ToMixVol(float v) =>
        (int)(System.Math.Clamp(v, 0f, 1f) * SDL_mixer.MIX_MAX_VOLUME);

    internal void Shutdown()
    {
        SDL_mixer.Mix_HaltChannel(-1);
        FreeMusic();

        // Free crossfade music chunks
        if (_musicChunkA != IntPtr.Zero) { SDL_mixer.Mix_FreeChunk(_musicChunkA); _musicChunkA = IntPtr.Zero; }
        if (_musicChunkB != IntPtr.Zero) { SDL_mixer.Mix_FreeChunk(_musicChunkB); _musicChunkB = IntPtr.Zero; }

        foreach (var chunk in _sounds.Values)
            SDL_mixer.Mix_FreeChunk(chunk);
        _sounds.Clear();

        SDL_mixer.Mix_CloseAudio();
    }
}
