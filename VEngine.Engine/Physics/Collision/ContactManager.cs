using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Contact event info passed to collision/trigger callbacks.
/// </summary>
public struct ContactInfo
{
    public RigidBody BodyA;
    public RigidBody BodyB;
    public Vec2 Normal;
    public Vec2 ContactPoint;
    public float Penetration;
}

/// <summary>
/// Tracks contact lifecycle (enter/stay/exit) and fires events.
/// </summary>
internal class ContactManager
{
    // World-level events
    public readonly Signal<ContactInfo> OnCollisionEnter = new();
    public readonly Signal<ContactInfo> OnCollisionStay = new();
    public readonly Signal<ContactInfo> OnCollisionExit = new();
    public readonly Signal<ContactInfo> OnTriggerEnter = new();
    public readonly Signal<ContactInfo> OnTriggerStay = new();
    public readonly Signal<ContactInfo> OnTriggerExit = new();

    // Track which pairs were active last frame
    private readonly HashSet<long> _previousPairs = new();
    private readonly HashSet<long> _currentPairs = new();

    // Track per-pair state for exit events
    private readonly Dictionary<long, bool> _pairTriggerState = new();
    private readonly Dictionary<long, (RigidBody A, RigidBody B, Vec2 Normal)> _pairBodies = new();

    public void BeginFrame()
    {
        _currentPairs.Clear();
    }

    /// <summary>Register an active contact this frame and fire enter/stay events.</summary>
    public void TrackContact(ContactConstraint cc)
    {
        long key = cc.PairKey;
        _currentPairs.Add(key);

        var info = MakeInfo(cc);
        bool isNew = !_previousPairs.Contains(key);

        if (cc.IsTrigger)
        {
            if (isNew)
            {
                OnTriggerEnter.Emit(info);
                EmitPerBody(cc, info, entering: true, trigger: true);
            }
            else
            {
                OnTriggerStay.Emit(info);
            }
        }
        else
        {
            if (isNew)
            {
                OnCollisionEnter.Emit(info);
                EmitPerBody(cc, info, entering: true, trigger: false);
            }
            else
            {
                OnCollisionStay.Emit(info);
            }
        }

        _pairTriggerState[key] = cc.IsTrigger;
        _pairBodies[key] = (cc.BodyA, cc.BodyB, cc.Normal);
    }

    /// <summary>End frame: fire exit events for pairs that disappeared.</summary>
    public void EndFrame()
    {
        foreach (long key in _previousPairs)
        {
            if (_currentPairs.Contains(key))
                continue;

            // This pair ended
            bool wasTrigger = _pairTriggerState.TryGetValue(key, out bool t) && t;
            _pairBodies.TryGetValue(key, out var bodies);

            var info = new ContactInfo { BodyA = bodies.A, BodyB = bodies.B, Normal = bodies.Normal };

            if (wasTrigger)
            {
                OnTriggerExit.Emit(info);
                EmitOnBody(bodies.A, info, entering: false, trigger: true);
                EmitOnBody(bodies.B, info, entering: false, trigger: true);
            }
            else
            {
                OnCollisionExit.Emit(info);
                EmitOnBody(bodies.A, info, entering: false, trigger: false);
                EmitOnBody(bodies.B, info, entering: false, trigger: false);
            }

            _pairTriggerState.Remove(key);
            _pairBodies.Remove(key);
        }

        // Swap: current becomes previous
        _previousPairs.Clear();
        foreach (long key in _currentPairs)
            _previousPairs.Add(key);
    }

    public void Clear()
    {
        _previousPairs.Clear();
        _currentPairs.Clear();
        _pairTriggerState.Clear();
        _pairBodies.Clear();
        OnCollisionEnter.Clear();
        OnCollisionStay.Clear();
        OnCollisionExit.Clear();
        OnTriggerEnter.Clear();
        OnTriggerStay.Clear();
        OnTriggerExit.Clear();
    }

    private static ContactInfo MakeInfo(ContactConstraint cc)
    {
        var cp = cc.PointCount > 0 ? cc.Point0 : default;
        return new ContactInfo
        {
            BodyA = cc.BodyA,
            BodyB = cc.BodyB,
            Normal = cc.Normal,
            ContactPoint = cp.Position,
            Penetration = cp.Penetration,
        };
    }

    private static void EmitPerBody(ContactConstraint cc, ContactInfo info, bool entering, bool trigger)
    {
        EmitOnBody(cc.BodyA, info, entering, trigger);
        EmitOnBody(cc.BodyB, info, entering, trigger);
    }

    private static void EmitOnBody(RigidBody body, ContactInfo info, bool entering, bool trigger)
    {
        if (body.UserData is not PhysicsBody pb) return;

        if (trigger)
        {
            if (entering) pb.OnTriggerEnter.Emit(info);
            else pb.OnTriggerExit.Emit(info);
        }
        else
        {
            if (entering) pb.OnCollisionEnter.Emit(info);
            else pb.OnCollisionExit.Emit(info);
        }
    }
}
