using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using StbImageSharp;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Animated GIF entity. Two modes:
/// - Native (gif_decoder.dll): decodes in C++, single reusable GPU texture, minimal memory
/// - Managed fallback: StbImageSharp, one texture per frame
///
/// Usage:
///   var gif = new GifSprite("effects/explosion.gif", 100, 200);
///   scene.Add(gif);
/// </summary>
public class GifSprite : KinematicEntity
{
    // ── Native mode ──
    private IntPtr _nativeHandle;
    private bool _useNative;
    private bool _ownedHandle; // true if we opened it (vs from preload cache)
    private GLTexture? _streamTexture;    // single reusable texture
    private int _nativeFrameCount;
    private int[] _nativeDelays = null!;
    private int _lastUploadedFrame = -1;

    // ── Managed fallback ──
    private readonly List<GLTexture> _frames = new();
    private readonly List<float> _delays = new();

    // ── Shared state ──
    private int _currentFrame;
    private float _timer;
    private bool _playing = true;
    private bool _looped = true;
    private bool _finished;

    public float Speed = 1f;
    public Color Color = Color.White;
    public bool FlipX;
    public bool FlipY;
    public bool Looped { get => _looped; set => _looped = value; }
    public bool Finished => _finished;
    public bool Playing => _playing;
    public int CurrentFrame => _currentFrame;
    public int FrameCount => _useNative ? _nativeFrameCount : _frames.Count;

    private static bool? _nativeAvailable;

    public GifSprite(float x = 0, float y = 0) : base(x, y) { }

    public GifSprite LoadGif(string path)
    {
        string fullPath = Path.IsPathRooted(path) ? path : Eng.Asset(path);

        // Try native decoder first
        _nativeAvailable ??= NativeGifDecoder.IsAvailable();

        if (_nativeAvailable.Value)
        {
            // Check preload cache first
            _nativeHandle = Core.AssetLoader.GetCachedGif(fullPath);
            if (_nativeHandle != IntPtr.Zero)
            {
                _ownedHandle = false; // shared from cache
            }
            else
            {
                _nativeHandle = NativeGifDecoder.gif_open(fullPath);
                _ownedHandle = true; // we opened it
            }

            if (_nativeHandle != IntPtr.Zero)
            {
                _useNative = true;
                int memMB = NativeGifDecoder.gif_width(_nativeHandle)
                          * NativeGifDecoder.gif_height(_nativeHandle)
                          * 4 * NativeGifDecoder.gif_frame_count(_nativeHandle)
                          / (1024 * 1024);
                Console.WriteLine($"[GIF] Native decoder: {path} ({NativeGifDecoder.gif_frame_count(_nativeHandle)} frames, ~{memMB} MB)");
                _nativeFrameCount = NativeGifDecoder.gif_frame_count(_nativeHandle);
                int w = NativeGifDecoder.gif_width(_nativeHandle);
                int h = NativeGifDecoder.gif_height(_nativeHandle);
                BaseWidth = w;
                BaseHeight = h;

                _nativeDelays = new int[_nativeFrameCount];
                for (int i = 0; i < _nativeFrameCount; i++)
                    _nativeDelays[i] = NativeGifDecoder.gif_frame_delay(_nativeHandle, i);

                // Upload first frame
                UploadNativeFrame(0);
                return this;
            }
        }

        // Managed fallback
        Console.WriteLine($"[GIF] Managed fallback: {path} (native unavailable)");
        LoadGifManaged(fullPath);
        return this;
    }

    /// <summary>Max frames to load in managed mode. Prevents GPU memory explosion.</summary>
    private const int MaxManagedFrames = 60;

    private void LoadGifManaged(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);

        foreach (var frame in ImageResult.AnimatedGifFramesFromStream(stream))
        {
            if (_frames.Count >= MaxManagedFrames)
            {
                Console.WriteLine($"[GIF] Managed mode: capped at {MaxManagedFrames} frames (use gif_decoder.dll for full playback)");
                break;
            }

            var tex = GLTexture.FromRGBA(Eng.GL.Api, frame.Width, frame.Height, frame.Data);
            _frames.Add(tex);
            _delays.Add(frame.DelayInMs > 0 ? frame.DelayInMs / 1000f : 0.1f);

            if (_frames.Count == 1)
            {
                BaseWidth = frame.Width;
                BaseHeight = frame.Height;
            }
        }

        if (_frames.Count == 0)
        {
            stream.Position = 0;
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var tex = GLTexture.FromRGBA(Eng.GL.Api, image.Width, image.Height, image.Data);
            _frames.Add(tex);
            _delays.Add(0.1f);
            BaseWidth = image.Width;
            BaseHeight = image.Height;
        }
    }

    private void UploadNativeFrame(int frame)
    {
        if (_lastUploadedFrame == frame) return;
        _lastUploadedFrame = frame;

        IntPtr data = NativeGifDecoder.gif_frame_data(_nativeHandle, frame);
        if (data == IntPtr.Zero) return;

        int w = (int)BaseWidth, h = (int)BaseHeight;

        if (_streamTexture == null)
        {
            // First frame: create empty texture then fill it (avoids managed byte[] allocation)
            byte[] blank = new byte[w * h * 4];
            _streamTexture = GLTexture.FromRGBA(Eng.GL.Api, w, h, blank);
        }
        // Update pixels from native pointer directly — zero copy
        _streamTexture.UpdateRGBA(data);
    }

    public void Play() { _playing = true; _finished = false; }
    public void Pause() => _playing = false;
    public void Stop() { _playing = false; _currentFrame = 0; _timer = 0; _finished = false; }
    public void SetFrame(int index) { _currentFrame = System.Math.Clamp(index, 0, FrameCount - 1); _timer = 0; }

    public override void Update(float dt)
    {
        base.Update(dt);
        if (!_playing || FrameCount <= 1 || _finished) return;

        float delay = _useNative
            ? (_nativeDelays[_currentFrame] > 0 ? _nativeDelays[_currentFrame] / 1000f : 0.1f)
            : _delays[_currentFrame];

        _timer += dt * Speed;

        while (_timer >= delay)
        {
            _timer -= delay;
            _currentFrame++;

            if (_currentFrame >= FrameCount)
            {
                if (_looped)
                    _currentFrame = 0;
                else
                {
                    _currentFrame = FrameCount - 1;
                    _finished = true;
                    _playing = false;
                    break;
                }
            }

            delay = _useNative
                ? (_nativeDelays[_currentFrame] > 0 ? _nativeDelays[_currentFrame] / 1000f : 0.1f)
                : _delays[_currentFrame];
        }

        // Upload current frame to GPU (native mode)
        if (_useNative)
            UploadNativeFrame(_currentFrame);
    }

    public override void Draw()
    {
        GLTexture? tex;
        if (_useNative)
            tex = _streamTexture;
        else
            tex = _frames.Count > 0 ? _frames[_currentFrame] : null;

        if (tex == null || !Visible) return;

        float r = Color.R / 255f, g = Color.G / 255f, b = Color.B / 255f, a = Color.A / 255f;

        Eng.GL.DrawTextureWorld(tex,
            0, 0, tex.Width, tex.Height,
            Position.X, Position.Y, Width, Height,
            ScrollFactor, r, g, b, a,
            Angle, FlipX, FlipY);
    }

    protected override void OnDestroy()
    {
        _streamTexture?.Dispose();
        if (_nativeHandle != IntPtr.Zero && _ownedHandle)
        {
            NativeGifDecoder.gif_close(_nativeHandle);
        }
        _nativeHandle = IntPtr.Zero;

        // Managed cleanup
        foreach (var tex in _frames)
            tex.Dispose();
        _frames.Clear();
    }
}
