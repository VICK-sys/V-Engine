using System.Runtime.InteropServices;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;

namespace VEngine.Engine.Physics;

internal static class NativeContactSolver
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BodyState
    {
        public Vec2 Position;
        public float Angle;
        public Vec2 Velocity;
        public float AngularVelocity, InvMass, InvInertia;

        public BodyState(RigidBody body)
        {
            Position = body.Position;
            Angle = body.Angle;
            Velocity = body.LinearVelocity;
            AngularVelocity = body.AngularVelocity;
            InvMass = body.InvMass;
            InvInertia = body.InvInertia;
        }

        public readonly void Apply(RigidBody body)
        {
            body.Position = Position;
            body.Angle = Angle;
            body.LinearVelocity = Velocity;
            body.AngularVelocity = AngularVelocity;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public BodyState A, B;
        public Vec2 Normal, Tangent;
        public float Friction, Restitution;
        public ContactPointData Point0, Point1;
        public int Count;

        public State(ContactConstraint contact)
        {
            A = new(contact.BodyA);
            B = new(contact.BodyB);
            Normal = contact.Normal;
            Tangent = contact.Tangent;
            Friction = contact.Friction;
            Restitution = contact.Restitution;
            Point0 = contact.Point0;
            Point1 = contact.Point1;
            Count = contact.PointCount;
        }

        public readonly void Apply(ContactConstraint contact)
        {
            A.Apply(contact.BodyA);
            B.Apply(contact.BodyB);
            contact.Tangent = Tangent;
            contact.Point0 = Point0;
            contact.Point1 = Point1;
        }
    }

    [DllImport("physics_solver", CallingConvention = CallingConvention.Cdecl)]
    private static extern void physics_contact_presolve(ref State state);
    [DllImport("physics_solver", CallingConvention = CallingConvention.Cdecl)]
    private static extern void physics_contact_velocity(ref State state);
    [DllImport("physics_solver", CallingConvention = CallingConvention.Cdecl)]
    private static extern int physics_contact_position(ref State state);

    public static void PreSolve(ContactConstraint contact)
    {
        var state = new State(contact);
        physics_contact_presolve(ref state);
        state.Apply(contact);
    }

    public static void SolveVelocity(ContactConstraint contact)
    {
        var state = new State(contact);
        physics_contact_velocity(ref state);
        state.Apply(contact);
    }

    public static bool SolvePosition(ContactConstraint contact)
    {
        var state = new State(contact);
        bool solved = physics_contact_position(ref state) != 0;
        state.Apply(contact);
        return solved;
    }
}
