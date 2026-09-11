using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Physics;

/// <summary>
/// P/Invoke wrapper for the native C++ physics solver DLL.
/// Handles broad phase, narrow phase, and sequential impulse solving in native code.
/// </summary>
internal static class NativePhysicsSolver
{
    private const string DLL = "physics_solver";
    private const int MAX_CONTACTS_PER_PAIR = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactNative
    {
        public int BodyA;
        public int BodyB;
        public float NormalX, NormalY;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_CONTACTS_PER_PAIR)]
        public float[] PointX;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_CONTACTS_PER_PAIR)]
        public float[] PointY;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_CONTACTS_PER_PAIR)]
        public float[] Penetration;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_CONTACTS_PER_PAIR)]
        public float[] NormalImpulse;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_CONTACTS_PER_PAIR)]
        public float[] TangentImpulse;
        public int PointCount;
    }

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr physics_create(int bodyCapacity, float cellSize);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void physics_destroy(IntPtr solver);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void physics_upload_bodies(
        IntPtr solver, int count,
        float[] posX, float[] posY, float[] angle,
        float[] velX, float[] velY, float[] angVel,
        float[] invMass, float[] invInertia,
        float[] friction, float[] restitution,
        float[] aabbMinX, float[] aabbMinY,
        float[] aabbMaxX, float[] aabbMaxY,
        int[] shapeType,
        float[] circleRadius, float[] circleCX, float[] circleCY,
        float[] polyVertsX, float[] polyVertsY,
        float[] polyNormalsX, float[] polyNormalsY,
        int[] polyCount,
        int[] bodyType, int[] isSleeping, int[] isTrigger
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void physics_solve(
        IntPtr solver,
        float dt,
        float gravityX, float gravityY,
        int velocityIterations,
        int positionIterations
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void physics_download_bodies(
        IntPtr solver,
        float[] posX, float[] posY, float[] angle,
        float[] velX, float[] velY, float[] angVel
    );

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int physics_contact_count(IntPtr solver);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int physics_download_contacts(
        IntPtr solver,
        [Out] ContactNative[] contacts, int maxContacts
    );

    public static bool IsAvailable()
    {
        try
        {
            var handle = physics_create(1, 64f);
            if (handle != IntPtr.Zero) physics_destroy(handle);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }
}
