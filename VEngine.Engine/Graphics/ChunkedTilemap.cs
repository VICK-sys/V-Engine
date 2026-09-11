using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Chunked tilemap for large worlds. Tiles are stored in fixed-size chunks that are
/// loaded/unloaded based on camera proximity. Only chunks near the camera are in memory.
///
/// Usage:
///   var world = new ChunkedTilemap("tileset.png", tileSize: 16, chunkSize: 32);
///   world.SetTile(500, 300, 5);  // works at any coordinate
///   scene.Add(world);            // draws only visible chunks
///
/// For file-backed streaming:
///   world.ChunkLoader = (cx, cy) => LoadChunkFromFile(cx, cy);
///   world.LoadRadius = 3;        // load chunks within 3 chunks of camera
/// </summary>
public class ChunkedTilemap : Entity
{
    private readonly int _tileSize;
    private readonly int _chunkSize; // tiles per chunk edge
    private readonly int _chunkPixels;
    private GLTexture? _texture;
    private int _tilesetCols;
    private readonly Dictionary<long, Chunk> _chunks = new();
    private readonly Entity _tileEntity = new() { Immovable = true };

    // Animated tiles
    private readonly Dictionary<int, TileAnim> _anims = new();
    // Per-tile collision rects
    private readonly Dictionary<int, (int X, int Y, int W, int H)> _collisionRects = new();

    /// <summary>Tiles per chunk edge. Default 32 (32x32 = 1024 tiles per chunk).</summary>
    public int ChunkSize => _chunkSize;

    /// <summary>Tile size in pixels.</summary>
    public int TileSize => _tileSize;

    /// <summary>
    /// Optional callback to load chunk data on demand. Receives (chunkX, chunkY).
    /// Return null if the chunk doesn't exist. Called when a chunk enters the load radius.
    /// </summary>
    public Func<int, int, ChunkData?>? ChunkLoader { get; set; }

    /// <summary>How many chunks around the camera to keep loaded. Default 3.</summary>
    public int LoadRadius { get; set; } = 3;

    /// <summary>Number of chunks currently in memory.</summary>
    public int LoadedChunkCount => _chunks.Count;

    public ChunkedTilemap(string tilesetPath, int tileSize, int chunkSize = 32) : base(0, 0)
    {
        if (tileSize <= 0) throw new ArgumentException("Tile size must be positive");
        if (chunkSize <= 0) throw new ArgumentException("Chunk size must be positive");

        _tileSize = tileSize;
        _chunkSize = chunkSize;
        _chunkPixels = chunkSize * tileSize;
        Immovable = true;

        if (!Path.IsPathRooted(tilesetPath))
            tilesetPath = Eng.Asset(tilesetPath);
        _texture = Eng.GL.GetOrCreateTexture(tilesetPath);
        _tilesetCols = System.Math.Max(1, _texture.Width / tileSize);

        _tileEntity.BaseWidth = tileSize;
        _tileEntity.BaseHeight = tileSize;
    }

    // ── Tile Access (infinite coordinates) ───────────────────────

    public int GetTile(int col, int row)
    {
        var chunk = GetChunk(col, row);
        if (chunk == null) return -1;
        int lc = Mod(col, _chunkSize);
        int lr = Mod(row, _chunkSize);
        return chunk.Tiles[lr * _chunkSize + lc];
    }

    public void SetTile(int col, int row, int tileIndex)
    {
        var chunk = GetOrCreateChunk(col, row);
        int lc = Mod(col, _chunkSize);
        int lr = Mod(row, _chunkSize);
        int i = lr * _chunkSize + lc;
        chunk.Tiles[i] = tileIndex;
        chunk.Solid[i] = tileIndex >= 0;
    }

    public bool IsSolid(int col, int row)
    {
        var chunk = GetChunk(col, row);
        if (chunk == null) return false;
        int lc = Mod(col, _chunkSize);
        int lr = Mod(row, _chunkSize);
        return chunk.Solid[lr * _chunkSize + lc];
    }

    public void SetSolid(int col, int row, bool solid)
    {
        var chunk = GetOrCreateChunk(col, row);
        int lc = Mod(col, _chunkSize);
        int lr = Mod(row, _chunkSize);
        chunk.Solid[lr * _chunkSize + lc] = solid;
    }

    public (int Col, int Row) WorldToTile(float worldX, float worldY)
    {
        return ((int)MathF.Floor(worldX / _tileSize), (int)MathF.Floor(worldY / _tileSize));
    }

    public void SetTileCollisionRect(int tileIndex, int x, int y, int w, int h)
        => _collisionRects[tileIndex] = (x, y, w, h);

    public void AnimateTile(int baseTile, int[] frames, int fps)
    {
        if (frames.Length < 2) return;
        _anims[baseTile] = new TileAnim(frames, System.Math.Max(1, fps));
    }

    // ── Chunk Management ────────────────────────────────────────

    /// <summary>Load/unload chunks based on camera position. Call each frame.</summary>
    public override void Update(float dt)
    {
        // Advance tile animations
        foreach (var anim in _anims.Values)
        {
            anim.Timer += dt;
            while (anim.Timer >= anim.FrameDuration)
            {
                anim.Timer -= anim.FrameDuration;
                anim.CurrentIndex = (anim.CurrentIndex + 1) % anim.Frames.Length;
            }
        }

        // Stream chunks around camera
        var cam = Eng.Camera;
        int camChunkX = (int)MathF.Floor(cam.Position.X / _chunkPixels);
        int camChunkY = (int)MathF.Floor(cam.Position.Y / _chunkPixels);

        // Load chunks in radius
        if (ChunkLoader != null)
        {
            for (int cy = camChunkY - LoadRadius; cy <= camChunkY + LoadRadius; cy++)
            {
                for (int cx = camChunkX - LoadRadius; cx <= camChunkX + LoadRadius; cx++)
                {
                    long key = ChunkKey(cx, cy);
                    if (_chunks.ContainsKey(key)) continue;
                    var data = ChunkLoader(cx, cy);
                    if (data != null)
                    {
                        var chunk = new Chunk(_chunkSize);
                        Array.Copy(data.Tiles, chunk.Tiles, System.Math.Min(data.Tiles.Length, chunk.Tiles.Length));
                        if (data.Solid != null)
                            Array.Copy(data.Solid, chunk.Solid, System.Math.Min(data.Solid.Length, chunk.Solid.Length));
                        else
                            for (int i = 0; i < chunk.Tiles.Length; i++) chunk.Solid[i] = chunk.Tiles[i] >= 0;
                        _chunks[key] = chunk;
                    }
                }
            }
        }

        // Unload distant chunks
        List<long>? toRemove = null;
        foreach (var (key, _) in _chunks)
        {
            int cx = (int)(key >> 32);
            int cy = (int)(key & 0xFFFFFFFF);
            if (System.Math.Abs(cx - camChunkX) > LoadRadius + 1 ||
                System.Math.Abs(cy - camChunkY) > LoadRadius + 1)
            {
                (toRemove ??= new List<long>()).Add(key);
            }
        }
        if (toRemove != null)
            foreach (var key in toRemove) _chunks.Remove(key);
    }

    // ── Collision ────────────────────────────────────────────────

    public CollisionDir CollideEntity(Entity moving)
    {
        var bounds = moving.GetCollisionBounds();
        int startCol = (int)MathF.Floor(bounds.X / _tileSize);
        int endCol = (int)MathF.Floor((bounds.X + bounds.W) / _tileSize);
        int startRow = (int)MathF.Floor(bounds.Y / _tileSize);
        int endRow = (int)MathF.Floor((bounds.Y + bounds.H) / _tileSize);

        var result = CollisionDir.None;
        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                if (!IsSolid(col, row)) continue;
                int tileIdx = GetTile(col, row);
                if (tileIdx < 0) continue;
                ApplyTileCollision(col, row, tileIdx);
                result |= Collision.Separate(moving, _tileEntity);
            }
        }
        return result;
    }

    private void ApplyTileCollision(int col, int row, int tileIndex)
    {
        if (_collisionRects.TryGetValue(tileIndex, out var cr))
        {
            _tileEntity.Position.X = col * _tileSize + cr.X;
            _tileEntity.Position.Y = row * _tileSize + cr.Y;
            _tileEntity.BaseWidth = cr.W;
            _tileEntity.BaseHeight = cr.H;
        }
        else
        {
            _tileEntity.Position.X = col * _tileSize;
            _tileEntity.Position.Y = row * _tileSize;
            _tileEntity.BaseWidth = _tileSize;
            _tileEntity.BaseHeight = _tileSize;
        }
    }

    // ── Rendering ────────────────────────────────────────────────

    public override void Draw()
    {
        if (_texture == null) return;

        var cam = Eng.Camera;
        float viewMinX = cam.Position.X;
        float viewMaxX = cam.Position.X + Eng.Width / cam.Zoom;
        float viewMinY = cam.Position.Y;
        float viewMaxY = cam.Position.Y + Eng.Height / cam.Zoom;

        int startCol = (int)MathF.Floor(viewMinX / _tileSize);
        int endCol = (int)MathF.Floor(viewMaxX / _tileSize);
        int startRow = (int)MathF.Floor(viewMinY / _tileSize);
        int endRow = (int)MathF.Floor(viewMaxY / _tileSize);

        float dstSize = _tileSize * cam.Zoom;

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int tileIndex = GetTile(col, row);
                if (tileIndex < 0) continue;

                if (_anims.TryGetValue(tileIndex, out var anim))
                    tileIndex = anim.Frames[anim.CurrentIndex];

                int srcCol = tileIndex % _tilesetCols;
                int srcRow = tileIndex / _tilesetCols;

                Eng.GL.DrawTextureWorld(
                    _texture,
                    srcCol * _tileSize, srcRow * _tileSize, _tileSize, _tileSize,
                    col * _tileSize, row * _tileSize, _tileSize, _tileSize,
                    ScrollFactor, 1, 1, 1, 1);
            }
        }
    }

    // ── Internal ─────────────────────────────────────────────────

    private Chunk? GetChunk(int col, int row)
    {
        int cx = DivFloor(col, _chunkSize);
        int cy = DivFloor(row, _chunkSize);
        _chunks.TryGetValue(ChunkKey(cx, cy), out var chunk);
        return chunk;
    }

    private Chunk GetOrCreateChunk(int col, int row)
    {
        int cx = DivFloor(col, _chunkSize);
        int cy = DivFloor(row, _chunkSize);
        long key = ChunkKey(cx, cy);
        if (!_chunks.TryGetValue(key, out var chunk))
        {
            chunk = new Chunk(_chunkSize);
            _chunks[key] = chunk;
        }
        return chunk;
    }

    private static long ChunkKey(int cx, int cy) =>
        ((long)cx << 32) | (uint)cy;

    private static int DivFloor(int a, int b) =>
        a >= 0 ? a / b : (a - b + 1) / b;

    private static int Mod(int a, int b)
    {
        int r = a % b;
        return r < 0 ? r + b : r;
    }

    private class Chunk
    {
        public readonly int[] Tiles;
        public readonly bool[] Solid;

        public Chunk(int size)
        {
            Tiles = new int[size * size];
            Solid = new bool[size * size];
            Array.Fill(Tiles, -1);
        }
    }

    private class TileAnim
    {
        public readonly int[] Frames;
        public readonly float FrameDuration;
        public float Timer;
        public int CurrentIndex;

        public TileAnim(int[] frames, int fps)
        {
            Frames = frames;
            FrameDuration = 1f / fps;
        }
    }
}

/// <summary>
/// Data for a single chunk, returned by ChunkLoader callback.
/// </summary>
public class ChunkData
{
    /// <summary>Flat tile array (chunkSize * chunkSize). -1 = empty.</summary>
    public int[] Tiles { get; set; } = Array.Empty<int>();

    /// <summary>Optional solid flags. If null, derived from Tiles (non-negative = solid).</summary>
    public bool[]? Solid { get; set; }
}
