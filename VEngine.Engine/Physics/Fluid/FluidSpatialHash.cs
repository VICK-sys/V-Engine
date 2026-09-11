using System;
using System.Collections.Generic;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// Spatial hash for SPH particle neighbor queries.
/// Cell size should equal the smoothing radius — guarantees all neighbors
/// within h are found in the 3x3 cell neighborhood.
/// </summary>
internal class FluidSpatialHash
{
    private readonly float _invCellSize;
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly Stack<List<int>> _listPool = new();

    public FluidSpatialHash(float cellSize)
    {
        _invCellSize = 1f / cellSize;
    }

    public void Clear()
    {
        foreach (var kv in _cells)
        {
            kv.Value.Clear();
            _listPool.Push(kv.Value);
        }
        _cells.Clear();
    }

    public void Insert(int index, float x, float y)
    {
        int col = (int)MathF.Floor(x * _invCellSize);
        int row = (int)MathF.Floor(y * _invCellSize);
        long key = CellKey(col, row);

        if (!_cells.TryGetValue(key, out var list))
        {
            list = _listPool.Count > 0 ? _listPool.Pop() : new List<int>();
            _cells[key] = list;
        }
        list.Add(index);
    }

    /// <summary>Query the 3x3 cell neighborhood around (x, y).</summary>
    public void QueryNeighbors(float x, float y, List<int> results)
    {
        int col = (int)MathF.Floor(x * _invCellSize);
        int row = (int)MathF.Floor(y * _invCellSize);

        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                long key = CellKey(col + dc, row + dr);
                if (_cells.TryGetValue(key, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                        results.Add(list[i]);
                }
            }
        }
    }

    private static long CellKey(int col, int row)
    {
        long c = (long)(col - int.MinValue);
        long r = (long)(row - int.MinValue);
        return (c << 32) | (r & 0xFFFFFFFFL);
    }
}
