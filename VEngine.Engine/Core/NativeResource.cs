using Microsoft.Win32.SafeHandles;

namespace VEngine.Engine.Core;

internal sealed class NativeResource : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Action<IntPtr> _release;

    public NativeResource(IntPtr handle, Action<IntPtr> release) : base(true)
    {
        _release = release;
        SetHandle(handle);
        if (IsInvalid) throw new InvalidOperationException("Native resource creation failed.");
    }

    protected override bool ReleaseHandle()
    {
        _release(handle);
        return true;
    }
}
