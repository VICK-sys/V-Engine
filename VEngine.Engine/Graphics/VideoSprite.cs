using System;
using System.Runtime.InteropServices;
using System.Threading;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Video playback entity. Decodes video via native FFmpeg DLL on a dedicated
/// background thread and uploads frames to the GPU on the main thread.
/// Audio is decoupled from video via a ring buffer for smooth 4K playback.
/// </summary>
public class VideoSprite : KinematicEntity
{
    private IntPtr _handle;
    private GLTexture? _texture;
    private int _videoW, _videoH;
    private double _fps;
    private volatile bool _playing;
    private bool _looped;
    private volatile bool _finished;

    // Audio device
    private bool _hasAudio;
    // Audio clock for the diagnostic drift trace. CurrentTime runs off wall-clock (see Update);
    // the audio hardware plays at its own sample rate. Deriving the audio position from the
    // consumed-bytes counter lets us print (CurrentTime − audioClock) each second so A/V offset
    // is visible. _audioClockBaseTime anchors the clock across seeks/loops, since SDL's queue
    // counter resets to 0 when we clear it.
    private int _sampleRate;
    private long _totalAudioBytesQueued;
    private double _audioClockBaseTime;
    // Cached audio playback position in seconds, refreshed once per main-thread Update.
    // Decoder reads this instead of calling SDL_GetQueuedAudioSize directly — calling SDL
    // from the decoder thread 200+ times/sec contends with main's SDL_QueueAudio and
    // causes main/decoder threads to align instead of interleave, losing frame-ready
    // signals (observed: up+ collapsed 60→10 before this was cached). double reads are
    // atomic enough on x64 for a trace-grade estimate; torn values would drift <1ms from
    // true, well under the 150ms gate slop.
    private double _audioClockCached;
    // Wall-clock stamp captured at the last trace dump; used to cross-check that the
    // game loop's dt is actually tracking real time (if dt is deflated, CurrentTime
    // advances slower than wall-clock, which shows up here as dt-sum < wall-sum).
    private System.Diagnostics.Stopwatch? _wallClockSw;
    private double _wallClockAtLastTrace;
    private uint _audioDevice;
    private short[]? _audioBuf;
    private int _audioChannels;

    // Dedicated decode thread
    private Thread? _decodeThread;
    private volatile bool _stopThread;

    // Video staging (background writes, main reads). _stagedFrameCount is incremented by
    // decoder each time it writes a new frame into _stagingBuf. Main uploads whenever
    // _stagedFrameCount differs from _lastUploadedCount. A monotonic counter (vs the old
    // volatile bool) guarantees main can't miss an edge even if decoder stages faster than
    // main can observe — the count lags forever if missed.
    private IntPtr _stagingBuf;
    private int _stagingSize;
    private long _stagedFrameCount;
    private long _lastUploadedCount;

    // Audio ring buffer (background writes, main reads)
    private short[]? _audioRing;
    private int _audioRingWrite;
    private int _audioRingRead;
    private int _audioRingSize;
    /// <summary>Frames pushed by the decoder since playback start (or last seek / rewind).
    /// Used to throttle silent videos to real-time; audio-driven videos pace via the ring buffer.</summary>
    private long _framesDecoded;
    // Index of the frame most recently uploaded to the GPU (the one the user is actually seeing).
    // Snapshot of _framesDecoded at upload time — used by the trace to compute visible-vs-audible
    // A/V drift, which is different from CurrentTime-vs-audioClock.
    private long _lastUploadedFrameIdx;
    private readonly object _audioLock = new();

    // EOF signaling
    private volatile bool _decodeEOF;

    /// <summary>When true, audio is not queued to the device.</summary>
    public bool Muted = true;

    // ── Diagnostic tracing ────────────────────────────────────
    /// <summary>Flip to see what the decoder, ring buffer, and audio device are doing.
    /// Writes a summary line once per second plus one-shot lines on key events (first frame,
    /// EOF, loop restart, zero-sample audio, long decoder sleeps).</summary>
    public static bool TraceEnabled = true;

    private int _traceFramesDecodedInWindow;
    private int _traceFramesUploadedInWindow;
    private int _traceUpdateTicksInWindow;
    private int _traceFrameReadyTrueInWindow;
    // Wall-clock gap between consecutive Update calls. If main runs smoothly at 60Hz each
    // gap is ~16.67ms. If main's outer loop stalls and fixed-timestep catch-up fires inner
    // Updates back-to-back, we'll see pairs at ~0ms and a big gap right before them.
    private long _traceLastUpdateTimestampTicks;
    private double _traceMaxUpdateGapInWindow;
    private int _traceBurstUpdatesInWindow;
    // Max time a single UploadFromStaging took in the current trace window. A GPU driver
    // stall (e.g., glTexSubImage2D blocking on command-queue pressure) would show up here
    // as tens of ms and could cap up+ regardless of signal-passing correctness.
    private double _traceMaxUploadMsInWindow;
    private int _traceAudioSamplesInWindow;
    private int _traceAudioFedInWindow;
    private int _traceGateSleepsInWindow;
    private int _traceZeroAudioInWindow;
    private double _traceClockAccum;
    private bool _traceFirstFrameLogged;
    private string _traceTag = "?";

    private void Trace(string msg)
    {
        if (!TraceEnabled) return;
        Console.WriteLine($"[VT {_traceTag}] {msg}");
    }

    /// <summary>Playback speed multiplier.</summary>
    public float Speed = 1f;

    /// <summary>Tint/alpha color.</summary>
    public Color Color = Color.White;

    /// <summary>Whether the video loops.</summary>
    public bool Looped { get => _looped; set => _looped = value; }

    /// <summary>True when a non-looped video has finished.</summary>
    public bool Finished => _finished;

    /// <summary>Whether the video is playing.</summary>
    public bool Playing => _playing;

    /// <summary>Video duration in seconds.</summary>
    public double Duration { get; private set; }

    /// <summary>Current playback position in seconds.</summary>
    public double CurrentTime { get; private set; }

    /// <summary>Video FPS.</summary>
    public double Fps => _fps;

    public VideoSprite(float x = 0, float y = 0) : base(x, y) { }

    /// <summary>Open a video file from the Assets folder.</summary>
    public VideoSprite LoadVideo(string path)
    {
        string fullPath = System.IO.Path.IsPathRooted(path) ? path : Eng.Asset(path);

        _handle = NativeVideoPlayer.video_open(fullPath);
        if (_handle == IntPtr.Zero)
        {
            Console.WriteLine($"[VideoSprite] Failed to open: {path}");
            return this;
        }

        _videoW = NativeVideoPlayer.video_width(_handle);
        _videoH = NativeVideoPlayer.video_height(_handle);
        _fps = NativeVideoPlayer.video_fps(_handle);
        Duration = NativeVideoPlayer.video_duration(_handle);

        BaseWidth = _videoW;
        BaseHeight = _videoH;

        _traceTag = System.IO.Path.GetFileName(fullPath);
        Trace($"LoadVideo: {_videoW}x{_videoH} fps={_fps:F2} dur={Duration:F1}s");

        // Allocate native staging buffer
        _stagingSize = _videoW * _videoH * 4;
        _stagingBuf = Marshal.AllocHGlobal(_stagingSize);

        // Decode first frame synchronously (thumbnail)
        if (NativeVideoPlayer.video_next_frame(_handle) == 1)
            UploadFrameDirect();

        // Initialize audio
        if (NativeVideoPlayer.video_has_audio(_handle) == 1)
        {
            _hasAudio = true;
            _sampleRate = NativeVideoPlayer.video_audio_sample_rate(_handle);
            int sampleRate = _sampleRate;
            _audioChannels = NativeVideoPlayer.video_audio_channels(_handle);
            _audioBuf = new short[sampleRate * _audioChannels];

            // Ring buffer: 3 seconds of audio capacity
            _audioRingSize = sampleRate * _audioChannels * 3;
            _audioRing = new short[_audioRingSize];

            var spec = new SDL.SDL_AudioSpec
            {
                freq = sampleRate,
                format = SDL.AUDIO_S16LSB,
                channels = (byte)_audioChannels,
                samples = 8192,
            };
            _audioDevice = SDL.SDL_OpenAudioDevice(null, 0, ref spec, out _, 0);
            if (_audioDevice > 0)
            {
                SDL.SDL_PauseAudioDevice(_audioDevice, 0);
                Console.WriteLine($"[VideoSprite] Audio: {sampleRate}Hz, {_audioChannels}ch, device={_audioDevice}");
            }
            else
            {
                Console.WriteLine($"[VideoSprite] Audio device failed: {SDL.SDL_GetError()}");
                _hasAudio = false;
                // Null out the ring + buffer so the frame-count throttle in DecodeLoop is the
                // sole gate — otherwise the (never-drained) ring would still gate by size-that-
                // never-happens and the decoder would run unthrottled.
                _audioRing = null;
                _audioBuf = null;
                _audioRingSize = 0;
            }
        }

        return this;
    }

    public void Play()
    {
        _playing = true;
        _finished = false;
        StartDecodeThread();
    }

    public void Pause()
    {
        _playing = false;
        StopDecodeThread();
    }

    public void Stop()
    {
        _playing = false;
        _finished = false;
        CurrentTime = 0;
        _framesDecoded = 0;
        StopDecodeThread();
        if (_handle != IntPtr.Zero) NativeVideoPlayer.video_rewind(_handle);
        _stagedFrameCount = 0;
        _lastUploadedCount = 0;
        ClearAudioRing();
        _audioClockBaseTime = 0;
    }

    public void Seek(double seconds)
    {
        if (_handle == IntPtr.Zero) return;
        bool wasPlaying = _playing;
        StopDecodeThread();
        NativeVideoPlayer.video_seek(_handle, seconds);
        CurrentTime = seconds;
        _framesDecoded = _fps > 0 ? (long)(seconds * _fps) : 0;
        _finished = false;
        _stagedFrameCount = 0;
        _lastUploadedCount = 0;
        ClearAudioRing();
        _audioClockBaseTime = seconds;
        if (NativeVideoPlayer.video_next_frame(_handle) == 1)
            UploadFrameDirect();
        if (wasPlaying) Play();
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        if (_handle == IntPtr.Zero) return;

        // Feed audio every tick regardless of video state
        if (_playing) FeedAudioFromRing();

        if (!_playing || _finished) return;

        // Audio is the master clock when we have a real device. Time = (bytes consumed by
        // the sound card) / (bytes per second). Bytes consumed = total ever queued minus
        // whatever SDL still has in its queue. This is sample-accurate and doesn't drift
        // against the waveform the user is hearing.
        //
        // Fall back to wall-clock advancement for silent/muted videos where there's no
        // audio device to anchor us. Speed multiplier only applies to the wall-clock path —
        // with audio, the device plays at its natural rate so CurrentTime naturally tracks it.
        // CurrentTime is wall-clock. Simple and self-consistent: if wall-clock is accurate,
        // decoder produces at 1× real-time, audio plays at 1× real-time (hardware-locked),
        // and they stay in sync. Tying CurrentTime to the audio clock created a feedback
        // loop: brief SDL underrun → audio clock stalls → decoder slows → less audio
        // produced → more underrun → slow motion.
        //
        // Sync comes from the decoder gate being tight enough that the staged (visible)
        // frame is within ~frame-time of the audio clock, not from the CurrentTime math.
        CurrentTime += dt * Speed;

        // Refresh the audio-clock cache for the decoder thread. The decoder uses this to
        // pace itself to what the speakers are actually playing, but doing the SDL query
        // on the decoder thread caused signal-loss contention with main's SDL_QueueAudio.
        if (_hasAudio && _audioDevice > 0 && _sampleRate > 0 && !Muted)
        {
            uint sdlQ = SDL.SDL_GetQueuedAudioSize(_audioDevice);
            long consumed = _totalAudioBytesQueued - (long)sdlQ;
            double ac = _audioClockBaseTime;
            if (consumed > 0)
                ac += consumed / (double)(_sampleRate * _audioChannels * sizeof(short));
            _audioClockCached = ac;
        }

        _traceUpdateTicksInWindow++;

        // Measure wall-clock gap to the previous Update — exposes fixed-timestep catch-up.
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_traceLastUpdateTimestampTicks != 0)
        {
            double gap = (nowTicks - _traceLastUpdateTimestampTicks)
                         / (double)System.Diagnostics.Stopwatch.Frequency;
            if (gap > _traceMaxUpdateGapInWindow) _traceMaxUpdateGapInWindow = gap;
            // "Burst" = two Updates fired within 2ms of each other. Catch-up loops produce
            // these; a smooth 60Hz loop has none.
            if (gap < 0.002) _traceBurstUpdatesInWindow++;
        }
        _traceLastUpdateTimestampTicks = nowTicks;

        // Upload latest staged frame if the decoder has produced at least one new frame
        // since our last upload. Monotonic counter (vs a reset-on-read bool) means no edge
        // can be missed no matter how fast decoder stages between our observations.
        long staged = System.Threading.Interlocked.Read(ref _stagedFrameCount);
        if (staged != _lastUploadedCount)
        {
            _traceFrameReadyTrueInWindow++;
            long uploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            UploadFromStaging();
            double uploadMs = (System.Diagnostics.Stopwatch.GetTimestamp() - uploadStart)
                              * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (uploadMs > _traceMaxUploadMsInWindow) _traceMaxUploadMsInWindow = uploadMs;
            _lastUploadedCount = staged;
            _traceFramesUploadedInWindow++;
        }

        // Periodic trace summary — once per second.
        _traceClockAccum += dt;
        if (TraceEnabled && _traceClockAccum >= 1.0)
        {
            if (_wallClockSw == null) { _wallClockSw = System.Diagnostics.Stopwatch.StartNew(); _wallClockAtLastTrace = 0; }
            double wallNow = _wallClockSw.Elapsed.TotalSeconds;
            double wallDelta = wallNow - _wallClockAtLastTrace;
            _wallClockAtLastTrace = wallNow;

            int ringAvail = 0;
            if (_audioRing != null) lock (_audioLock) ringAvail = (_audioRingWrite - _audioRingRead + _audioRingSize) % _audioRingSize;
            uint sdlQueue = _audioDevice > 0 ? SDL.SDL_GetQueuedAudioSize(_audioDevice) : 0u;

            // Audio clock = bytes the soundcard has actually consumed since the last ring reset,
            // converted to seconds and anchored at the seek/loop base. drift=CurrentTime-audioClock
            // measures queue latency (normal baseline ≈ 200ms). vDrift=visualTime-audioClock is the
            // real user-visible A/V offset: timestamp of the frame currently on screen minus the
            // audio position actually hitting the speakers. Positive = video ahead, negative = behind.
            // Only meaningful when audio is active and not muted; prints "n/a" otherwise.
            string driftStr = "n/a";
            if (_hasAudio && _audioDevice > 0 && _sampleRate > 0 && !Muted)
            {
                long consumed = _totalAudioBytesQueued - (long)sdlQueue;
                double audioClock = _audioClockBaseTime;
                if (consumed > 0)
                    audioClock += consumed / (double)(_sampleRate * _audioChannels * sizeof(short));
                double drift = CurrentTime - audioClock;
                double visualTime = _fps > 0 ? _lastUploadedFrameIdx / _fps : CurrentTime;
                double vDrift = visualTime - audioClock;
                driftStr = $"aClock={audioClock:F2}s drift={drift:+0.000;-0.000;0.000}s vTime={visualTime:F2}s vDrift={vDrift:+0.000;-0.000;0.000}s";
            }

            Trace($"tick t={CurrentTime:F2}s dt-sum={_traceClockAccum:F2}s wall-Δ={wallDelta:F2}s sr={_sampleRate} ch={_audioChannels} decoded={_framesDecoded} dec+={_traceFramesDecodedInWindow}/s up+={_traceFramesUploadedInWindow}/s ticks={_traceUpdateTicksInWindow}/s rdyTrue={_traceFrameReadyTrueInWindow}/s maxGap={_traceMaxUpdateGapInWindow*1000:F1}ms bursts={_traceBurstUpdatesInWindow}/s maxUp={_traceMaxUploadMsInWindow:F1}ms " +
                  $"audioIn+={_traceAudioSamplesInWindow} audioOut+={_traceAudioFedInWindow} ring={ringAvail} sdlQ={sdlQueue}B " +
                  $"gateSleeps={_traceGateSleepsInWindow} zeroAudio={_traceZeroAudioInWindow} {driftStr}");
            _traceClockAccum = 0;
            _traceFramesDecodedInWindow = 0;
            _traceFramesUploadedInWindow = 0;
            _traceUpdateTicksInWindow = 0;
            _traceFrameReadyTrueInWindow = 0;
            _traceMaxUpdateGapInWindow = 0;
            _traceBurstUpdatesInWindow = 0;
            _traceMaxUploadMsInWindow = 0;
            _traceAudioSamplesInWindow = 0;
            _traceAudioFedInWindow = 0;
            _traceGateSleepsInWindow = 0;
            _traceZeroAudioInWindow = 0;
        }

        // Handle EOF
        if (_decodeEOF)
        {
            _decodeEOF = false;
            Trace($"EOF at t={CurrentTime:F2}s decoded={_framesDecoded} looped={_looped}");
            if (_looped)
            {
                StopDecodeThread();
                NativeVideoPlayer.video_rewind(_handle);
                CurrentTime = 0;
                _framesDecoded = 0;
                ClearAudioRing();
                _audioClockBaseTime = 0;
                StartDecodeThread();
                Trace("loop restart");
            }
            else
            {
                _finished = true;
                _playing = false;
                StopDecodeThread();
            }
        }
    }

    // ── Decode Thread ──────────────────────────────────────

    private void StartDecodeThread()
    {
        if (_decodeThread != null && _decodeThread.IsAlive) return;
        _stopThread = false;
        _decodeThread = new Thread(DecodeLoop)
        {
            IsBackground = true,
            Name = "VideoDecoder",
            Priority = ThreadPriority.Normal,
        };
        _decodeThread.Start();
    }

    private void StopDecodeThread()
    {
        _stopThread = true;
        _decodeThread?.Join(500);
        _decodeThread = null;
    }

    private void DecodeLoop()
    {
        while (!_stopThread && _handle != IntPtr.Zero)
        {
            // Back-pressure — TWO gates, either can pause the decoder.
            //
            // 1) Frame-count vs playback time (primary). Keep decoded video <= ~1s ahead of the
            //    playhead. This is the fundamental correctness bound: no matter what audio does,
            //    the decoder can't outpace real time by more than a second.
            // 2) Audio ring buffer (secondary). Prevents overflow for audio-heavy videos.
            //
            // The old code only used the audio-ring gate, which silently failed when:
            //   - Video had no audio stream (silent — ring was null, no gate at all)
            //   - AAC/other audio decoder returned 0 samples (ring never filled, gate never tripped)
            //   - SDL_OpenAudioDevice failed (_hasAudio=false but _audioRing still allocated)
            // All three looked the same to the user: "plays really fast, then stops."
            if (_fps > 0)
            {
                // Hold the decoder just ahead of what the soundcard is actually playing. The
                // audio clock (bytes consumed by the hardware) is the true playhead for A/V
                // sync; gating against it keeps decodedTime ≈ audible + 0.15s, so the staged
                // frame the main thread uploads lands within a human-imperceptible offset
                // (~150ms) of the audio. The prior wall-clock gate (CurrentTime + 1.0s) let
                // the decoder race a full second ahead, producing a fixed ~1.1s lip-sync lag.
                //
                // Silent/muted videos have no audio clock to anchor to, so they fall back to
                // the old wall-clock gate — the original 1s budget is fine when there's no
                // audio stream to stay aligned with.
                //
                // _audioClockCached is refreshed by main once per Update tick; reading it here
                // is a plain memory load, no SDL call. Calling SDL from this thread directly
                // contended with main's SDL_QueueAudio badly enough to collapse upload rate
                // from 60/s to 10/s.
                double decodedTime = _framesDecoded / _fps;
                // Gate width trade-off (see trace log for evidence):
                //   +0.15s → lock-step between decoder and main; up+ collapses to ~10/s
                //           because the audio ring stays at 0 and the two threads ping-pong.
                //   +0.50s → ring buffers ~350ms, decoder and main decouple, up+ stays at 60/s.
                //           vDrift ≈ +0.5s (noticeable but not jarring on non-speech content).
                //   +1.00s → old wall-clock behavior; up+=60 but vDrift=+1.1s (obvious lip-sync).
                // 0.5s picked as the sweet spot; revisit if tests reveal hitch tolerance issues.
                double gateTarget = _hasAudio && _audioDevice > 0 && _sampleRate > 0 && !Muted
                    ? _audioClockCached + 0.5
                    : CurrentTime + 1.0;
                if (decodedTime > gateTarget)
                {
                    _traceGateSleepsInWindow++;
                    Thread.Sleep(5);
                    continue;
                }
            }

            // Audio ring gate only applies when the ring is actually being consumed — i.e. not
            // muted. Without this guard, toggling Muted=true after playback starts would freeze
            // the decoder at the ~1s mark (ring full, never drains).
            if (_audioRing != null && _hasAudio && _audioDevice > 0 && !Muted)
            {
                int available;
                lock (_audioLock)
                {
                    available = (_audioRingWrite - _audioRingRead + _audioRingSize) % _audioRingSize;
                }
                int oneSec = _audioChannels * 48000;
                if (available > oneSec)
                {
                    _traceGateSleepsInWindow++;
                    Thread.Sleep(5);
                    continue;
                }
            }

            // Decode one frame
            if (NativeVideoPlayer.video_next_frame(_handle) == 1)
            {
                // Copy video to staging
                IntPtr src = NativeVideoPlayer.video_frame_data(_handle);
                if (src != IntPtr.Zero && _stagingBuf != IntPtr.Zero)
                {
                    unsafe
                    {
                        Buffer.MemoryCopy((void*)src, (void*)_stagingBuf,
                            _stagingSize, _stagingSize);
                    }
                    // Increment AFTER the staging copy so main never sees an advance without
                    // a corresponding frame to read. Interlocked.Increment provides a full
                    // memory barrier so the copy is visible to main before the count change.
                    System.Threading.Interlocked.Increment(ref _stagedFrameCount);
                    _framesDecoded++;
                    _traceFramesDecodedInWindow++;
                    if (!_traceFirstFrameLogged)
                    {
                        _traceFirstFrameLogged = true;
                        Trace($"first frame decoded (hasAudio={_hasAudio} audioDev={_audioDevice} audioRing={(_audioRing != null ? _audioRingSize.ToString() : "null")})");
                    }
                }

                // Extract audio into ring buffer
                // Skip audio extraction while muted — otherwise the ring fills (nobody drains it
                // when FeedAudioFromRing early-exits on Muted) and the audio gate in the next
                // loop iteration locks the decoder forever at ~1s of decoded video.
                if (_hasAudio && _audioBuf != null && _audioRing != null && !Muted)
                {
                    int maxSamples = _audioBuf.Length / System.Math.Max(_audioChannels, 1);
                    int samples = NativeVideoPlayer.video_get_audio(_handle, _audioBuf, maxSamples);
                    if (samples > 0)
                    {
                        int shorts = samples * _audioChannels;
                        lock (_audioLock)
                        {
                            for (int s = 0; s < shorts; s++)
                            {
                                _audioRing[_audioRingWrite] = _audioBuf[s];
                                _audioRingWrite = (_audioRingWrite + 1) % _audioRingSize;
                            }
                        }
                        _traceAudioSamplesInWindow += shorts;
                    }
                    else
                    {
                        _traceZeroAudioInWindow++;
                    }
                }
                else if (_hasAudio && Muted)
                {
                    // Still drain the native audio buffer so FFmpeg doesn't back up — just drop the samples.
                    int maxSamples = _audioBuf != null ? _audioBuf.Length / System.Math.Max(_audioChannels, 1) : 0;
                    if (_audioBuf != null)
                        NativeVideoPlayer.video_get_audio(_handle, _audioBuf, maxSamples);
                }
            }
            else
            {
                _decodeEOF = true;
                break;
            }
        }
    }

    // ── Audio ──────────────────────────────────────────────

    private void FeedAudioFromRing()
    {
        if (!_hasAudio || _audioDevice == 0 || _audioRing == null || Muted) return;

        // Keep ~200ms buffered in SDL. Needs to be large enough that even a slow Update
        // tick (game running at 30fps = 33ms/frame, or transient 6fps hitch = 166ms/frame)
        // doesn't drain the queue before the next Feed. The audio ring gate caps the ring
        // at 1s (see DecodeLoop) so there's always enough to refill this target.
        uint queued = SDL.SDL_GetQueuedAudioSize(_audioDevice);
        int sr = _sampleRate > 0 ? _sampleRate : 48000;
        int targetBytes = _audioChannels * sr / 5 * sizeof(short); // 200ms
        if (queued >= (uint)targetBytes) return;

        int available;
        lock (_audioLock)
        {
            available = (_audioRingWrite - _audioRingRead + _audioRingSize) % _audioRingSize;
        }
        if (available <= 0) return;

        int toFeed = System.Math.Min(available, (targetBytes - (int)queued) / sizeof(short));
        if (toFeed <= 0) return;

        short[] feed = _audioBuf!;
        if (toFeed > feed.Length) toFeed = feed.Length;

        lock (_audioLock)
        {
            for (int i = 0; i < toFeed; i++)
            {
                feed[i] = _audioRing[_audioRingRead];
                _audioRingRead = (_audioRingRead + 1) % _audioRingSize;
            }
        }

        int bytes = toFeed * sizeof(short);
        var pin = GCHandle.Alloc(feed, GCHandleType.Pinned);
        try
        {
            SDL.SDL_QueueAudio(_audioDevice, pin.AddrOfPinnedObject(), (uint)bytes);
            _traceAudioFedInWindow += toFeed;
            _totalAudioBytesQueued += bytes;
        }
        finally
        {
            pin.Free();
        }
    }

    private void ClearAudioRing()
    {
        lock (_audioLock)
        {
            _audioRingWrite = 0;
            _audioRingRead = 0;
        }
        if (_audioDevice > 0)
            SDL.SDL_ClearQueuedAudio(_audioDevice);
        // Reset the audio clock so CurrentTime doesn't jump backward after a seek/loop.
        _totalAudioBytesQueued = 0;
    }

    // ── Upload ────────────────────────────────────────────

    private void UploadFromStaging()
    {
        if (_stagingBuf == IntPtr.Zero) return;

        if (_texture != null && _texture.Width == _videoW && _texture.Height == _videoH)
            _texture.UpdateRGBA(_stagingBuf);
        else
        {
            _texture?.Dispose();
            _texture = GLTexture.FromNativeRGBA(Eng.GL.Api, _videoW, _videoH, _stagingBuf);
        }
        _lastUploadedFrameIdx = _framesDecoded;
    }

    private void UploadFrameDirect()
    {
        IntPtr data = NativeVideoPlayer.video_frame_data(_handle);
        if (data == IntPtr.Zero) return;

        if (_texture != null && _texture.Width == _videoW && _texture.Height == _videoH)
            _texture.UpdateRGBA(data);
        else
        {
            _texture?.Dispose();
            _texture = GLTexture.FromNativeRGBA(Eng.GL.Api, _videoW, _videoH, data);
        }
        _lastUploadedFrameIdx = _framesDecoded;
    }

    public override void Draw()
    {
        if (_texture == null || !Visible) return;

        float r = Color.R / 255f, g = Color.G / 255f, b = Color.B / 255f, a = Color.A / 255f;

        Eng.GL.DrawTextureWorld(_texture,
            0, 0, _videoW, _videoH,
            Position.X, Position.Y, Width, Height,
            ScrollFactor, r, g, b, a, Angle);
    }

    protected override void OnDestroy()
    {
        _playing = false;
        StopDecodeThread();
        _texture?.Dispose();
        if (_stagingBuf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_stagingBuf);
            _stagingBuf = IntPtr.Zero;
        }
        if (_audioDevice > 0)
        {
            SDL.SDL_CloseAudioDevice(_audioDevice);
            _audioDevice = 0;
        }
        if (_handle != IntPtr.Zero)
        {
            NativeVideoPlayer.video_close(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
