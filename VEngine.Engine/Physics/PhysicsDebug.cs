using System;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Joints;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics;

/// <summary>
/// Debug visualization for the physics system.
/// Draws filled shapes, contact points, joints, and velocity indicators.
/// Hooks into Eng.OnPostDraw when enabled.
/// </summary>
public static class PhysicsDebug
{
    private static Action? _unsubscribe;

    internal static void Enable()
    {
        if (_unsubscribe != null) return;
        _unsubscribe = Eng.OnPostDraw.Subscribe(DrawAll);
    }

    internal static void Disable()
    {
        _unsubscribe?.Invoke();
        _unsubscribe = null;
    }

    private static void DrawAll()
    {
        var world = Eng.Physics;
        if (!world.DebugDraw) return;

        // Draw shapes (fill then outline)
        foreach (var body in world.Bodies)
        {
            if (body.Shape == null) continue;
            GetBodyColors(body, out byte fr, out byte fg, out byte fb, out byte fa,
                                out byte or, out byte og, out byte ob, out byte oa);
            DrawShapeFill(body, fr, fg, fb, fa);
            DrawShapeOutline(body, or, og, ob, oa);
        }

        // Draw contacts
        foreach (var body in world.Bodies)
        {
            if (body.Shape == null || body.Type == BodyType.Static) continue;
            // Velocity arrow
            var vel = body.LinearVelocity;
            if (vel.LengthSquared() > 100f)
            {
                var tip = body.Position + vel.Normalized() * MathF.Min(vel.Length() * 0.15f, 30f);
                Draw.Line(body.Position.X, body.Position.Y, tip.X, tip.Y, 255, 255, 100, 80);
            }
        }

        // Draw joints
        foreach (var joint in world.Joints)
            DrawJoint(joint);
    }

    private static void GetBodyColors(RigidBody body,
        out byte fr, out byte fg, out byte fb, out byte fa,
        out byte or, out byte og, out byte ob, out byte oa)
    {
        if (body.IsTrigger)
        {
            // Trigger: purple, very transparent fill
            (fr, fg, fb, fa) = (160, 50, 200, 35);
            (or, og, ob, oa) = (200, 80, 240, 160);
        }
        else if (body.IsSleeping)
        {
            (fr, fg, fb, fa) = (60, 60, 70, 60);
            (or, og, ob, oa) = (90, 90, 100, 120);
        }
        else if (body.Type == BodyType.Static)
        {
            (fr, fg, fb, fa) = (30, 80, 90, 80);
            (or, og, ob, oa) = (60, 170, 190, 200);
        }
        else if (body.Type == BodyType.Kinematic)
        {
            (fr, fg, fb, fa) = (90, 90, 30, 80);
            (or, og, ob, oa) = (220, 220, 60, 200);
        }
        else
        {
            // Dynamic: green/teal
            (fr, fg, fb, fa) = (30, 90, 60, 80);
            (or, og, ob, oa) = (60, 210, 120, 220);
        }
    }

    private static void DrawShapeFill(RigidBody body, byte r, byte g, byte b, byte a)
    {
        if (body.Shape is CircleShape circle)
        {
            var center = body.Position + circle.Center.Rotate(body.Angle);
            Draw.FillCircle(center.X, center.Y, circle.Radius, r, g, b, a);
        }
        else if (body.Shape is PolygonShape poly)
        {
            // Use AABB fill as approximation for unrotated, or the actual bounds
            var aabb = body.AABB;
            Draw.FillRect(aabb.Min.X, aabb.Min.Y, aabb.Width, aabb.Height, r, g, b, (byte)(a * 2 / 3));
        }
    }

    private static void DrawShapeOutline(RigidBody body, byte r, byte g, byte b, byte a)
    {
        if (body.Shape is CircleShape circle)
        {
            var center = body.Position + circle.Center.Rotate(body.Angle);
            Draw.Circle(center.X, center.Y, circle.Radius, r, g, b, a);
            // Direction tick
            var edge = center + Vec2.FromAngle(body.Angle) * circle.Radius;
            Draw.Line(center.X, center.Y, edge.X, edge.Y, r, g, b, (byte)(a * 3 / 4));
        }
        else if (body.Shape is PolygonShape poly)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var v1 = poly.GetWorldVertex(i, body.Position, body.Angle);
                var v2 = poly.GetWorldVertex((i + 1) % poly.Count, body.Position, body.Angle);
                Draw.ThickLine(v1.X, v1.Y, v2.X, v2.Y, 1.5f, r, g, b, a);
            }
        }
    }

    private static void DrawJoint(Joint joint)
    {
        var anchorA = joint.WorldAnchorA;
        var bodyAPos = joint.BodyA.Position;

        if (joint.BodyB != null)
        {
            var anchorB = joint.WorldAnchorB;
            var bodyBPos = joint.BodyB.Position;

            // Anchor dots
            Draw.FillCircle(anchorA.X, anchorA.Y, 3, 255, 200, 80, 220);
            Draw.FillCircle(anchorB.X, anchorB.Y, 3, 255, 200, 80, 220);

            // Lines from body center to anchor
            Draw.Line(bodyAPos.X, bodyAPos.Y, anchorA.X, anchorA.Y, 180, 160, 80, 100);
            Draw.Line(bodyBPos.X, bodyBPos.Y, anchorB.X, anchorB.Y, 180, 160, 80, 100);

            // Connecting line
            Draw.ThickLine(anchorA.X, anchorA.Y, anchorB.X, anchorB.Y, 1.5f, 240, 220, 80, 180);
        }
        else if (joint is MouseJoint mouse)
        {
            // Mouse joint: dashed feel via thin line + target dot
            Draw.Line(anchorA.X, anchorA.Y, mouse.Target.X, mouse.Target.Y, 255, 120, 80, 180);
            Draw.FillCircle(mouse.Target.X, mouse.Target.Y, 4, 255, 120, 80, 200);
            Draw.Circle(mouse.Target.X, mouse.Target.Y, 8, 255, 120, 80, 100);
        }
    }
}
