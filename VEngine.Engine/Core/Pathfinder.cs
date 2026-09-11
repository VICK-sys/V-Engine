using System.Runtime.InteropServices;

namespace VEngine.Engine.Core;

public sealed class Pathfinder : IDisposable
{
    private readonly NativeResource _handle;
    public int Width { get; }
    public int Height { get; }

    public Pathfinder(int width, int height, bool[]? walkable = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int count = checked(width * height);
        if (walkable != null && walkable.Length != count)
            throw new ArgumentException("Grid length must match its dimensions.", nameof(walkable));
        NativeRuntime.Require("pathfinder");
        Width = width;
        Height = height;
        var cells = walkable?.Select(value => value ? 1 : 0).ToArray() ?? Enumerable.Repeat(1, count).ToArray();
        _handle = new(NativePathfinder.pf_create(width, height, cells), NativePathfinder.pf_destroy);
    }

    public bool IsWalkable(int x, int y) => NativePathfinder.pf_is_walkable(_handle, x, y) != 0;

    public void SetWalkable(int x, int y, bool walkable)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        NativePathfinder.pf_set_cell(_handle, x, y, walkable ? 1 : 0);
    }

    public void UpdateGrid(bool[] walkable)
    {
        ArgumentNullException.ThrowIfNull(walkable);
        if (walkable.Length != checked(Width * Height)) throw new ArgumentException("Grid length must match its dimensions.");
        NativePathfinder.pf_update_grid(_handle, walkable.Select(value => value ? 1 : 0).ToArray());
    }

    public (int X, int Y)[] FindPath(int startX, int startY, int endX, int endY, bool allowDiagonal = false, int maxSearch = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSearch);
        int count = NativePathfinder.pf_find_path(_handle, startX, startY, endX, endY, allowDiagonal ? 1 : 0, maxSearch);
        var x = new int[count];
        var y = new int[count];
        if (count > 0)
        {
            Marshal.Copy(NativePathfinder.pf_get_path_x(_handle), x, 0, count);
            Marshal.Copy(NativePathfinder.pf_get_path_y(_handle), y, 0, count);
        }
        GC.KeepAlive(_handle);
        return Enumerable.Range(0, count).Select(i => (x[i], y[i])).ToArray();
    }

    public void Dispose() => _handle.Dispose();
}
