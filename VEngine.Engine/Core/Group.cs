using System;
using System.Buffers;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

public class Group : Entity
{
    private readonly List<Entity> _members = new();
    private readonly HashSet<Entity> _memberSet = new();
    private bool _drawOrderDirty = true;

    // Stable sort: tag each entity with its pre-sort index, use as tiebreaker
    private static readonly Comparison<(Entity e, int idx)> _stableCompare = (a, b) =>
    {
        int layerCmp = a.e.Layer.CompareTo(b.e.Layer);
        if (layerCmp != 0) return layerCmp;
        int zCmp = a.e.ZOrder.CompareTo(b.e.ZOrder);
        return zCmp != 0 ? zCmp : a.idx.CompareTo(b.idx);
    };
    private List<(Entity e, int idx)>? _sortBuf;

    public IReadOnlyList<Entity> Members => _members;

    public T Add<T>(T entity) where T : Entity
    {
        _members.Add(entity);
        _memberSet.Add(entity);
        _drawOrderDirty = true;
        return entity;
    }

    public bool Remove(Entity entity, bool destroy = true)
    {
        int idx = _members.IndexOf(entity);
        if (idx < 0) return false;

        // Swap-with-last for O(1) removal (draw order re-sorted on next Draw)
        int last = _members.Count - 1;
        if (idx != last)
            _members[idx] = _members[last];
        _members.RemoveAt(last);
        if (!_members.Contains(entity))
            _memberSet.Remove(entity);

        _drawOrderDirty = true;
        if (destroy) entity.Destroy();
        return true;
    }

    public void SortDrawOrder() => _drawOrderDirty = true;

    public override void Update(float dt)
    {
        int count = _members.Count;
        if (count == 0) return;

        var snapshot = ArrayPool<Entity>.Shared.Rent(count);
        _members.CopyTo(snapshot);
        try
        {
            for (int i = count - 1; i >= 0; i--)
            {
                var entity = snapshot[i];
                if (!entity.Active || entity.Destroyed || !_memberSet.Contains(entity)) continue;

                try { entity.Update(dt); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Group] Entity Update error ({entity.GetType().Name}): {ex.Message}");
                }
            }
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(snapshot, clearArray: true);
        }
    }

    public override void Draw()
    {
        if (_drawOrderDirty)
        {
            // Stable sort: tag with original index as tiebreaker
            int n = _members.Count;
            _sortBuf ??= new List<(Entity, int)>(n);
            _sortBuf.Clear();
            for (int i = 0; i < n; i++)
                _sortBuf.Add((_members[i], i));
            _sortBuf.Sort(_stableCompare);
            for (int i = 0; i < n; i++)
                _members[i] = _sortBuf[i].e;
            _drawOrderDirty = false;
        }

        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Visible)
            {
                try { _members[i].Draw(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Group] Entity Draw error ({_members[i].GetType().Name}): {ex.Message}");
                }
            }
        }
    }

    protected override void OnDestroy()
    {
        for (int i = 0; i < _members.Count; i++)
            _members[i].Destroy();
        _members.Clear();
        _memberSet.Clear();
    }
}
