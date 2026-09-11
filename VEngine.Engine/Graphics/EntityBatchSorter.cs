using System.Runtime.InteropServices;
using VEngine.Engine.Core;

namespace VEngine.Engine.Graphics;

internal sealed class EntityBatchSorter : IDisposable
{
    private readonly NativeResource _handle = new(NativeBatchSorter.batch_create(16), NativeBatchSorter.batch_destroy);
    private int[] _layers = [], _indices = [], _visible = [], _order = [];
    private float[] _z = [], _zero = [];
    private Entity[] _snapshot = [];

    public void Sort(List<Entity> entities)
    {
        int count = entities.Count;
        if (_layers.Length < count)
        {
            int capacity = System.Math.Max(count, _layers.Length * 2);
            _layers = new int[capacity];
            _indices = new int[capacity];
            _visible = new int[capacity];
            _order = new int[capacity];
            _z = new float[capacity];
            _zero = new float[capacity];
            _snapshot = new Entity[capacity];
        }
        for (int i = 0; i < count; i++)
        {
            _snapshot[i] = entities[i];
            _layers[i] = entities[i].Layer;
            _z[i] = entities[i].ZOrder;
            _indices[i] = i;
            _visible[i] = 1;
        }
        NativeBatchSorter.batch_upload(_handle, count, _layers, _z, _indices, _visible, _zero, _zero, _zero, _zero, 0, 0);
        int sorted = NativeBatchSorter.batch_sort_and_cull(_handle);
        if (sorted != count) throw new InvalidOperationException("Native sort returned an invalid entity count.");
        if (count > 0) Marshal.Copy(NativeBatchSorter.batch_get_order(_handle), _order, 0, count);
        GC.KeepAlive(_handle);
        for (int i = 0; i < count; i++) entities[i] = _snapshot[_order[i]];
        Array.Clear(_snapshot, 0, count);
    }

    public void Dispose() => _handle.Dispose();
}
