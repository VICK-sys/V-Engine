using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Grid-based tilemap. Loads a tileset spritesheet and renders a 2D grid of tiles
/// with camera culling. Provides per-tile collision via CollideEntity().
/// </summary>
public class Tilemap : Entity
{
    private GLTexture? _texture;
    private int _tilesetCols;

    private readonly int _tileSize;
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int[] _tiles;
    private readonly bool[] _solid;
    private TileCollisionGrid? _collisionGrid;
    public bool UseNativeCollision { get; set; } = true;
    public bool UsingNativeCollision => UseNativeCollision && NativeRuntime.IsAvailable("tilemap_collision");

    private TileCollisionGrid CollisionGrid
    {
        get
        {
            ObjectDisposedException.ThrowIf(Destroyed, this);
            if (_collisionGrid == null)
            {
                _collisionGrid = new(_gridWidth, _gridHeight, _tileSize);
                for (int row = 0; row < _gridHeight; row++)
                    for (int col = 0; col < _gridWidth; col++)
                        _collisionGrid.SetSolid(col, row, _solid[row * _gridWidth + col]);
            }
            return _collisionGrid;
        }
    }

    public Pathfinder CreatePathfinder() => new(_gridWidth, _gridHeight, _solid.Select(solid => !solid).ToArray());

    public (int X, int Y)[] QuerySolidTiles(float x, float y, float width, float height) =>
        CollisionGrid.Query(x - Position.X, y - Position.Y, width, height);

    public float RaycastTiles(float x, float y, float dx, float dy, float maxDistance, out int tileX, out int tileY) =>
        CollisionGrid.Raycast(x - Position.X, y - Position.Y, dx, dy, maxDistance, out tileX, out tileY);

    public bool HasTileLineOfSight(float ax, float ay, float bx, float by) =>
        CollisionGrid.HasLineOfSight(ax - Position.X, ay - Position.Y, bx - Position.X, by - Position.Y);

    // Animated tiles
    private readonly Dictionary<int, TileAnim> _anims = new();

    // Per-tile collision rects
    private readonly Dictionary<int, (int X, int Y, int W, int H)> _collisionRects = new();

    // Reusable entity for tile collision
    private readonly Entity _tileEntity = new() { Immovable = true };

    public int TileSize => _tileSize;
    public int GridWidth => _gridWidth;
    public int GridHeight => _gridHeight;
    public int PixelWidth => _gridWidth * _tileSize;
    public int PixelHeight => _gridHeight * _tileSize;

    public Tilemap(string tilesetPath, int tileSize, int gridWidth, int gridHeight) : base(0, 0)
    {
        if (tileSize <= 0) throw new ArgumentException("Tile size must be positive", nameof(tileSize));
        if (gridWidth <= 0 || gridHeight <= 0) throw new ArgumentException("Grid dimensions must be positive");

        _tileSize = tileSize;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _tiles = new int[gridWidth * gridHeight];
        _solid = new bool[gridWidth * gridHeight];
        Array.Fill(_tiles, -1);

        BaseWidth = gridWidth * tileSize;
        BaseHeight = gridHeight * tileSize;
        Immovable = true;

        LoadTileset(tilesetPath);
        _tilesetCols = System.Math.Max(1, _texture!.Width / tileSize);

        _tileEntity.BaseWidth = tileSize;
        _tileEntity.BaseHeight = tileSize;
    }

    // ── Tile Access ──────────────────────────────────────────────

    public int GetTile(int col, int row)
    {
        if (col < 0 || col >= _gridWidth || row < 0 || row >= _gridHeight) return -1;
        return _tiles[row * _gridWidth + col];
    }

    public void SetTile(int col, int row, int tileIndex)
    {
        if (col < 0 || col >= _gridWidth || row < 0 || row >= _gridHeight) return;
        int i = row * _gridWidth + col;
        _tiles[i] = tileIndex;
        _solid[i] = tileIndex >= 0;
        _collisionGrid?.SetSolid(col, row, _solid[i]);
    }

    public bool IsSolid(int col, int row)
    {
        if (col < 0 || col >= _gridWidth || row < 0 || row >= _gridHeight) return false;
        return _solid[row * _gridWidth + col];
    }

    public void SetSolid(int col, int row, bool solid)
    {
        if (col < 0 || col >= _gridWidth || row < 0 || row >= _gridHeight) return;
        _solid[row * _gridWidth + col] = solid;
        _collisionGrid?.SetSolid(col, row, solid);
    }

    public (int Col, int Row) WorldToTile(float worldX, float worldY)
    {
        return ((int)((worldX - Position.X) / _tileSize), (int)((worldY - Position.Y) / _tileSize));
    }

    public void SetTileCollisionRect(int tileIndex, int x, int y, int w, int h)
    {
        _collisionRects[tileIndex] = (x, y, w, h);
    }

    public (int X, int Y, int W, int H)? GetTileCollisionRect(int tileIndex)
    {
        return _collisionRects.TryGetValue(tileIndex, out var r) ? r : null;
    }

    public void ClearTileCollisionRect(int tileIndex)
    {
        _collisionRects.Remove(tileIndex);
    }

    public void AnimateTile(int baseTile, int[] frames, int fps)
    {
        if (frames.Length < 2) return;
        _anims[baseTile] = new TileAnim(frames, System.Math.Max(1, fps));
    }

    // ── Collision ────────────────────────────────────────────────

    public CollisionDir CollideEntity(Entity moving)
    {
        var bounds = moving.GetCollisionBounds();
        if (_collisionRects.Count == 0 && UsingNativeCollision &&
            !CollisionGrid.Overlaps(bounds.X - Position.X, bounds.Y - Position.Y, bounds.W, bounds.H))
            return CollisionDir.None;
        int startCol = System.Math.Max(0, (int)((bounds.X - Position.X) / _tileSize));
        int endCol = System.Math.Min(_gridWidth - 1, (int)((bounds.X + bounds.W - Position.X) / _tileSize));
        int startRow = System.Math.Max(0, (int)((bounds.Y - Position.Y) / _tileSize));
        int endRow = System.Math.Min(_gridHeight - 1, (int)((bounds.Y + bounds.H - Position.Y) / _tileSize));

        var result = CollisionDir.None;
        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int tileIdx = _tiles[row * _gridWidth + col];
                if (!_solid[row * _gridWidth + col] || tileIdx < 0) continue;
                ApplyTileCollision(col, row, tileIdx);
                result |= Collision.Separate(moving, _tileEntity);
            }
        }
        return result;
    }

    public bool CollideEntityOneway(Entity moving)
    {
        var bounds = moving.GetCollisionBounds();
        if (_collisionRects.Count == 0 && UsingNativeCollision &&
            !CollisionGrid.Overlaps(bounds.X - Position.X, bounds.Y - Position.Y, bounds.W, bounds.H))
            return false;
        int startCol = System.Math.Max(0, (int)((bounds.X - Position.X) / _tileSize));
        int endCol = System.Math.Min(_gridWidth - 1, (int)((bounds.X + bounds.W - Position.X) / _tileSize));
        int startRow = System.Math.Max(0, (int)((bounds.Y - Position.Y) / _tileSize));
        int endRow = System.Math.Min(_gridHeight - 1, (int)((bounds.Y + bounds.H - Position.Y) / _tileSize));

        bool hit = false;
        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int tileIdx = _tiles[row * _gridWidth + col];
                if (!_solid[row * _gridWidth + col] || tileIdx < 0) continue;
                ApplyTileCollision(col, row, tileIdx);
                if (Collision.SeparateOneway(moving, _tileEntity))
                    hit = true;
            }
        }
        return hit;
    }

    private void ApplyTileCollision(int col, int row, int tileIndex)
    {
        if (_collisionRects.TryGetValue(tileIndex, out var cr))
        {
            _tileEntity.Position.X = Position.X + col * _tileSize + cr.X;
            _tileEntity.Position.Y = Position.Y + row * _tileSize + cr.Y;
            _tileEntity.BaseWidth = cr.W;
            _tileEntity.BaseHeight = cr.H;
        }
        else
        {
            _tileEntity.Position.X = Position.X + col * _tileSize;
            _tileEntity.Position.Y = Position.Y + row * _tileSize;
            _tileEntity.BaseWidth = _tileSize;
            _tileEntity.BaseHeight = _tileSize;
        }
    }

    // ── Rendering ────────────────────────────────────────────────

    public override void Update(float dt)
    {
        foreach (var anim in _anims.Values)
        {
            anim.Timer += dt;
            while (anim.Timer >= anim.FrameDuration)
            {
                anim.Timer -= anim.FrameDuration;
                anim.CurrentIndex = (anim.CurrentIndex + 1) % anim.Frames.Length;
            }
        }
    }

    public override void Draw()
    {
        if (_texture == null) return;

        var cam = Eng.Camera;
        float viewMinX = cam.Position.X;
        float viewMaxX = cam.Position.X + Eng.Width / cam.Zoom;
        float viewMinY = cam.Position.Y;
        float viewMaxY = cam.Position.Y + Eng.Height / cam.Zoom;

        int startCol = System.Math.Max(0, (int)((viewMinX - Position.X) / _tileSize));
        int endCol = System.Math.Min(_gridWidth - 1, (int)((viewMaxX - Position.X) / _tileSize));
        int startRow = System.Math.Max(0, (int)((viewMinY - Position.Y) / _tileSize));
        int endRow = System.Math.Min(_gridHeight - 1, (int)((viewMaxY - Position.Y) / _tileSize));

        float dstSize = _tileSize * cam.Zoom;

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int tileIndex = _tiles[row * _gridWidth + col];
                if (tileIndex < 0) continue;

                // Resolve animated tile
                if (_anims.TryGetValue(tileIndex, out var anim))
                    tileIndex = anim.Frames[anim.CurrentIndex];

                int srcCol = tileIndex % _tilesetCols;
                int srcRow = tileIndex / _tilesetCols;

                Eng.GL.DrawTextureWorld(
                    _texture,
                    srcCol * _tileSize, srcRow * _tileSize, _tileSize, _tileSize,
                    Position.X + col * _tileSize, Position.Y + row * _tileSize, _tileSize, _tileSize,
                    ScrollFactor,
                    1, 1, 1, 1);
            }
        }
    }

    protected override void OnDestroy()
    {
        _collisionGrid?.Dispose();
        _collisionGrid = null;
        _texture = null; // Don't dispose — texture is in shared cache
    }

    // ── Loading ──────────────────────────────────────────────────

    public static Tilemap Load(string jsonPath)
    {
        string fullPath = Path.IsPathRooted(jsonPath) ? jsonPath : Eng.Asset(jsonPath);
        var json = File.ReadAllText(fullPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var imageName = root.GetProperty("image").GetString()!;
        int tileSize = root.GetProperty("tileSize").GetInt32();
        int gridWidth = root.GetProperty("width").GetInt32();
        int gridHeight = root.GetProperty("height").GetInt32();

        var jsonDir = Path.GetDirectoryName(fullPath)!;
        var imageFullPath = Path.Combine(jsonDir, imageName);
        string tilesetPath = File.Exists(imageFullPath) ? imageFullPath : imageName;

        var tilemap = new Tilemap(tilesetPath, tileSize, gridWidth, gridHeight);

        var tilesArr = root.GetProperty("tiles");
        int i = 0;
        foreach (var t in tilesArr.EnumerateArray())
        {
            if (i >= gridWidth * gridHeight) break;
            int c = i % gridWidth;
            int r = i / gridWidth;
            tilemap.SetTile(c, r, t.GetInt32());
            i++;
        }

        if (root.TryGetProperty("solid", out var solidArr))
        {
            i = 0;
            foreach (var s in solidArr.EnumerateArray())
            {
                if (i >= gridWidth * gridHeight) break;
                int c = i % gridWidth;
                int r = i / gridWidth;
                tilemap.SetSolid(c, r, s.GetInt32() != 0);
                i++;
            }
        }

        if (root.TryGetProperty("animations", out var animsArr))
        {
            foreach (var a in animsArr.EnumerateArray())
            {
                int baseTile = a.GetProperty("tile").GetInt32();
                int fps = a.TryGetProperty("fps", out var fpsEl) ? fpsEl.GetInt32() : 4;
                var framesEl = a.GetProperty("frames");
                var frames = new int[framesEl.GetArrayLength()];
                int j = 0;
                foreach (var f in framesEl.EnumerateArray())
                    frames[j++] = f.GetInt32();
                tilemap.AnimateTile(baseTile, frames, fps);
            }
        }

        if (root.TryGetProperty("collisionRects", out var crObj))
        {
            foreach (var prop in crObj.EnumerateObject())
            {
                if (!int.TryParse(prop.Name, out int tileIdx)) continue;
                var r = prop.Value;
                tilemap.SetTileCollisionRect(tileIdx,
                    r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(),
                    r.GetProperty("w").GetInt32(), r.GetProperty("h").GetInt32());
            }
        }

        doc.Dispose();
        return tilemap;
    }

    // ── Texture Loading ──────────────────────────────────────────

    private void LoadTileset(string path)
    {
        if (!Path.IsPathRooted(path))
            path = Eng.Asset(path);
        _texture = Eng.GL.GetOrCreateTexture(path);
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
