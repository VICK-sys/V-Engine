using System.Collections.Generic;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Grid-based spatial hash for physics broad phase. Operates on RigidBody AABBs.
/// Mirrors the design of VEngine.Engine.Core.SpatialHash but for the physics system.
/// </summary>
internal class PhysicsSpatialHash
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly Dictionary<long, List<RigidBody>> _cells = new();
    private readonly Stack<List<RigidBody>> _listPool = new();

    public PhysicsSpatialHash(float cellSize = 64f)
    {
        _cellSize = cellSize;
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

    public void Insert(RigidBody body)
    {
        var aabb = body.AABB;
        int minCol = (int)System.MathF.Floor(aabb.Min.X * _invCellSize);
        int minRow = (int)System.MathF.Floor(aabb.Min.Y * _invCellSize);
        int maxCol = (int)System.MathF.Floor(aabb.Max.X * _invCellSize);
        int maxRow = (int)System.MathF.Floor(aabb.Max.Y * _invCellSize);

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int col = minCol; col <= maxCol; col++)
            {
                long key = CellKey(col, row);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = _listPool.Count > 0 ? _listPool.Pop() : new List<RigidBody>();
                    _cells[key] = list;
                }
                list.Add(body);
            }
        }
    }

    /// <summary>
    /// Find all unique overlapping body pairs. Calls callback once per pair.
    /// </summary>
    public void FindPairs(List<(RigidBody, RigidBody)> pairs)
    {
        var seen = new HashSet<long>();

        foreach (var kv in _cells)
        {
            var list = kv.Value;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];

                    // Deduplicate across cells
                    long pairKey = ContactConstraint.MakePairKey(a.Id, b.Id);
                    if (!seen.Add(pairKey))
                        continue;

                    // AABB overlap check
                    if (a.AABB.Overlaps(b.AABB))
                        pairs.Add((a, b));
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
