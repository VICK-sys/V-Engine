using VEngine.Engine.Core;

namespace VEngine.Engine.Graphics;

public sealed class TileCollisionGrid : IDisposable
{
    private readonly NativeResource _handle;
    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }

    public TileCollisionGrid(int width, int height, int tileSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        int count = checked(width * height);
        _ = checked(System.Math.Max(width, height) * tileSize);
        NativeRuntime.Require("tilemap_collision");
        Width = width;
        Height = height;
        TileSize = tileSize;
        _handle = new(NativeTilemapCollision.tilecol_create(width, height, tileSize, new int[count]), NativeTilemapCollision.tilecol_destroy);
    }

    public void SetSolid(int x, int y, bool solid)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        NativeTilemapCollision.tilecol_set(_handle, x, y, solid ? 1 : 0);
    }

    public bool Overlaps(float x, float y, float width, float height)
    {
        Validate(x, y, width, height);
        if (width <= 0 || height <= 0) return false;
        return NativeTilemapCollision.tilecol_aabb_test(_handle, x, y, width, height) != 0;
    }

    public (int X, int Y)[] Query(float x, float y, float width, float height)
    {
        Validate(x, y, width, height);
        if (width <= 0 || height <= 0) return [];
        var columns = new int[checked(Width * Height)];
        var rows = new int[columns.Length];
        int count = NativeTilemapCollision.tilecol_aabb_query(_handle, x, y, width, height, columns, rows, columns.Length);
        return Enumerable.Range(0, count).Select(i => (columns[i], rows[i])).ToArray();
    }

    public float Raycast(float x, float y, float dx, float dy, float maxDistance, out int tileX, out int tileY)
    {
        Validate(x, y, dx, dy);
        Validate(maxDistance);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        return NativeTilemapCollision.tilecol_raycast(_handle, x, y, dx, dy, maxDistance, out tileX, out tileY);
    }

    public bool HasLineOfSight(float ax, float ay, float bx, float by)
    {
        Validate(ax, ay, bx, by);
        return NativeTilemapCollision.tilecol_line_of_sight(_handle, ax, ay, bx, by) != 0;
    }

    private static void Validate(params float[] values)
    {
        if (values.Any(value => !float.IsFinite(value) || MathF.Abs(value) > 100_000_000f))
            throw new ArgumentOutOfRangeException(nameof(values));
    }

    public void Dispose() => _handle.Dispose();
}
