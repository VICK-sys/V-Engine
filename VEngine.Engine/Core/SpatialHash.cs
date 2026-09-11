using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Grid-based spatial hash for broad-phase collision acceleration.
/// Reduces OverlapGroup from O(n*m) to near-O(n) for sparse entity distributions.
///
/// Usage:
///   var hash = new SpatialHash(64); // 64px cells
///   hash.Clear();
///   hash.InsertGroup(enemies);
///   Collision.OverlapHash(player, hash, (a, b) => HandleHit(a, b));
/// </summary>
public class SpatialHash
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly Dictionary<long, List<Entity>> _cells = new();
    private readonly List<List<Entity>> _listPool = new();

    /// <summary>Create a spatial hash with the given cell size (pixels). Larger = fewer cells, more entities per cell.</summary>
    public SpatialHash(float cellSize = 64f)
    {
        if (cellSize <= 0) throw new ArgumentException("Cell size must be positive", nameof(cellSize));
        _cellSize = cellSize;
        _invCellSize = 1f / cellSize;
    }

    /// <summary>Clear all cells. Call once per frame before re-inserting.</summary>
    public void Clear()
    {
        foreach (var cell in _cells.Values)
        {
            cell.Clear();
            _listPool.Add(cell);
        }
        _cells.Clear();
    }

    /// <summary>Insert a single entity into the hash based on its collision bounds.</summary>
    public void Insert(Entity entity)
    {
        if (!entity.Active) return;
        var b = entity.GetCollisionBounds();
        int minCol = (int)MathF.Floor(b.X * _invCellSize);
        int maxCol = (int)MathF.Floor((b.X + b.W) * _invCellSize);
        int minRow = (int)MathF.Floor(b.Y * _invCellSize);
        int maxRow = (int)MathF.Floor((b.Y + b.H) * _invCellSize);

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int col = minCol; col <= maxCol; col++)
            {
                GetOrCreateCell(CellKey(col, row)).Add(entity);
            }
        }
    }

    /// <summary>Insert all active members of a Group.</summary>
    public void InsertGroup(Group group)
    {
        foreach (var entity in group.Members)
            Insert(entity);
    }

    /// <summary>
    /// Query all entities that may overlap the given bounds.
    /// Results may contain duplicates (entities spanning multiple cells).
    /// </summary>
    public void Query(float x, float y, float w, float h, List<Entity> results)
    {
        int minCol = (int)MathF.Floor(x * _invCellSize);
        int maxCol = (int)MathF.Floor((x + w) * _invCellSize);
        int minRow = (int)MathF.Floor(y * _invCellSize);
        int maxRow = (int)MathF.Floor((y + h) * _invCellSize);

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int col = minCol; col <= maxCol; col++)
            {
                if (_cells.TryGetValue(CellKey(col, row), out var cell))
                {
                    foreach (var e in cell)
                        results.Add(e);
                }
            }
        }
    }

    /// <summary>
    /// Query all entities near a given entity (based on its collision bounds).
    /// </summary>
    public void QueryEntity(Entity entity, List<Entity> results)
    {
        var b = entity.GetCollisionBounds();
        Query(b.X, b.Y, b.W, b.H, results);
    }

    // Encode (col, row) as a unique 64-bit key. Works correctly for negative coordinates
    // by offsetting by int.MinValue to ensure the uint cast produces distinct values.
    private static long CellKey(int col, int row) =>
        ((long)(uint)(col - int.MinValue) << 32) | (uint)(row - int.MinValue);

    private List<Entity> GetOrCreateCell(long key)
    {
        if (_cells.TryGetValue(key, out var cell))
            return cell;
        cell = _listPool.Count > 0 ? _listPool[^1] : new List<Entity>();
        if (_listPool.Count > 0) _listPool.RemoveAt(_listPool.Count - 1);
        _cells[key] = cell;
        return cell;
    }
}
