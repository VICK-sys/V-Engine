using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StbImageSharp;
using VEngine.Engine.Graphics;

namespace VEngine.Engine.Core;

/// <summary>
/// Asynchronous asset preloader. Loads image bytes on a background thread,
/// uploads to GPU on the main thread. Use for loading screens and stutter-free
/// scene transitions.
///
/// Usage:
///   var loader = Eng.Loader;
///   loader.Enqueue("sprites/hero.png", "sprites/enemy.png", "levels/map.json");
///   loader.Start();
///   // In update loop:
///   loader.ProcessUploads(maxPerFrame: 2);
///   if (loader.Done) SwitchToGameScene();
///   // Progress: loader.Progress (0-1)
/// </summary>
public class AssetLoader
{
    private readonly ConcurrentQueue<LoadedAsset> _uploadQueue = new();
    private readonly List<string> _pending = new();
    private int _totalCount;
    private int _loadedCount;
    private bool _started;

    /// <summary>Progress from 0 (nothing loaded) to 1 (all done). Thread-safe.</summary>
    public float Progress => _totalCount == 0 ? 1f : (float)_loadedCount / _totalCount;

    /// <summary>True when all assets are loaded and uploaded to GPU.</summary>
    public bool Done => _started && _loadedCount >= _totalCount && _uploadQueue.IsEmpty;

    /// <summary>Number of assets remaining (background + upload queue).</summary>
    public int Remaining => _totalCount - _loadedCount;

    /// <summary>Enqueue asset paths for background loading.</summary>
    public void Enqueue(params string[] paths)
    {
        foreach (var p in paths)
            _pending.Add(p);
    }

    /// <summary>Start loading all enqueued assets on a background thread.</summary>
    public void Start()
    {
        _totalCount = _pending.Count;
        _loadedCount = 0;
        _started = true;

        var paths = _pending.ToArray();
        _pending.Clear();

        Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    LoadAsset(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AssetLoader] Failed to load '{path}': {ex.Message}");
                    Interlocked.Increment(ref _loadedCount);
                }
            }
        });
    }

    /// <summary>
    /// Process pending GPU uploads on the main thread. Call each frame.
    /// Returns the number of textures uploaded this frame.
    /// </summary>
    public int ProcessUploads(int maxPerFrame = 2)
    {
        int uploaded = 0;
        while (uploaded < maxPerFrame && _uploadQueue.TryDequeue(out var asset))
        {
            try
            {
                if (asset.Type == AssetType.Audio)
                {
                    // Audio: load on main thread via SDL_mixer
                    Eng.Audio.LoadSound(asset.Path);
                }
                else if (asset.Image != null)
                {
                    // Pre-decoded PNG/JPG — upload directly
                    Eng.GL.GetOrCreateTextureFromData(asset.Path, asset.Image.Width, asset.Image.Height, asset.Image.Data);
                }
                else
                {
                    // BMP or path-only — load via standard path on main thread
                    Eng.GL.GetOrCreateTexture(asset.Path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssetLoader] Upload failed for '{asset.Path}': {ex.Message}");
            }

            Interlocked.Increment(ref _loadedCount);
            uploaded++;
        }
        return uploaded;
    }

    /// <summary>Scan a directory recursively and enqueue all loadable assets.</summary>
    public void EnqueueDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".wav":
                case ".ogg":
                case ".mp3":
                case ".flac":
                case ".gif":
                case ".mp4":
                case ".avi":
                case ".mkv":
                case ".webm":
                case ".mov":
                    EnqueueAll(file);
                    break;
                // Skip: .json, .ttf, .lua, .txt, etc. (loaded on demand)
            }
        }
    }

    /// <summary>Scan the engine's Assets folder and enqueue everything loadable.</summary>
    public void EnqueueAllAssets()
    {
        EnqueueDirectory(Eng.AssetsPath);
    }

    /// <summary>Reset the loader for reuse.</summary>
    public void Reset()
    {
        _pending.Clear();
        while (_uploadQueue.TryDequeue(out _)) { }
        _totalCount = 0;
        _loadedCount = 0;
        _started = false;
    }

    /// <summary>Enqueue multiple assets with automatic type detection.</summary>
    public void EnqueueAll(params string[] paths)
    {
        foreach (var p in paths)
        {
            var ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
            switch (ext)
            {
                case ".wav":
                case ".ogg":
                case ".mp3":
                case ".flac":
                    EnqueueAudio(p);
                    break;
                case ".gif":
                    _pending.Add("gif:" + p);
                    break;
                case ".mp4":
                case ".avi":
                case ".mkv":
                case ".webm":
                case ".mov":
                    // Videos are decoded on-demand (too large to preload frames)
                    // Just verify the file exists during preload
                    _pending.Add(p);
                    break;
                case ".ttf":
                    _pending.Add(p);
                    break;
                default:
                    _pending.Add(p);
                    break;
            }
        }
    }

    /// <summary>Preload an audio file so it's cached for instant playback.</summary>
    public void EnqueueAudio(string path)
    {
        _pending.Add("audio:" + path);
    }

    // ── GIF Cache ──
    private static readonly ConcurrentDictionary<string, IntPtr> _gifCache = new();

    /// <summary>Check if a GIF has been preloaded. Returns the native handle or IntPtr.Zero.</summary>
    public static IntPtr GetCachedGif(string fullPath)
    {
        var key = Path.GetFullPath(fullPath);
        return _gifCache.TryGetValue(key, out var handle) ? handle : IntPtr.Zero;
    }

    private void LoadAsset(string path)
    {
        // GIF preload (prefixed)
        if (path.StartsWith("gif:"))
        {
            var gifPath = path.Substring(4);
            try
            {
                var fullPath = System.IO.Path.IsPathRooted(gifPath) ? gifPath : Eng.Asset(gifPath);
                var cacheKey = Path.GetFullPath(fullPath);
                if (_gifCache.ContainsKey(cacheKey))
                {
                    Interlocked.Increment(ref _loadedCount);
                    return;
                }

                // Decode on background thread (this is the slow part — stb_image decodes all frames)
                var handle = NativeGifDecoder.IsAvailable()
                    ? NativeGifDecoder.gif_open(fullPath)
                    : IntPtr.Zero;

                if (handle != IntPtr.Zero)
                {
                    _gifCache[cacheKey] = handle;
                    int frames = NativeGifDecoder.gif_frame_count(handle);
                    int w = NativeGifDecoder.gif_width(handle);
                    int h = NativeGifDecoder.gif_height(handle);
                    Console.WriteLine($"[AssetLoader] GIF preloaded: {gifPath} ({frames} frames, {w}x{h})");
                }
                else
                {
                    Console.WriteLine($"[AssetLoader] GIF preload skipped (no native decoder): {gifPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssetLoader] GIF failed: '{gifPath}': {ex.Message}");
            }
            Interlocked.Increment(ref _loadedCount);
            return;
        }

        // Audio preload (prefixed)
        if (path.StartsWith("audio:"))
        {
            var audioPath = path.Substring(6);
            try
            {
                var fullPath = System.IO.Path.IsPathRooted(audioPath) ? audioPath : Eng.Asset(audioPath);
                // Queue for main-thread loading (SDL_mixer requires main thread)
                _uploadQueue.Enqueue(new LoadedAsset(fullPath, null, null, AssetType.Audio));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssetLoader] Audio not found: '{audioPath}': {ex.Message}");
                Interlocked.Increment(ref _loadedCount);
            }
            return;
        }

        var full = System.IO.Path.IsPathRooted(path) ? path : Eng.Asset(path);

        // Skip if already cached
        if (Eng.GL.HasTexture(full))
        {
            Interlocked.Increment(ref _loadedCount);
            return;
        }

        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".json" or ".ttf" or ".lua"
            or ".mp4" or ".avi" or ".mkv" or ".webm" or ".mov")
        {
            // These don't need GPU upload — videos are decoded on-demand, others loaded lazily
            Interlocked.Increment(ref _loadedCount);
            return;
        }

        // Image decode on background thread
        if (ext == ".bmp")
        {
            _uploadQueue.Enqueue(new LoadedAsset(full, null, null, AssetType.Image));
        }
        else
        {
            ImageResult image;
            using (var stream = File.OpenRead(full))
                image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            _uploadQueue.Enqueue(new LoadedAsset(full, null, image, AssetType.Image));
        }
    }

    private enum AssetType { Image, Audio }
    private record LoadedAsset(string Path, byte[]? RawBytes, ImageResult? Image, AssetType Type);
}
