using System;
using System.Collections.Generic;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Joints;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// A group of connected bodies, contacts, and joints that can be solved independently.
/// Bodies are connected through contacts and joints — separate groups form separate islands.
/// </summary>
internal class Island
{
    public readonly List<RigidBody> Bodies = new();
    public readonly List<ContactConstraint> Contacts = new();
    public readonly List<Joint> Joints = new();

    public void Clear()
    {
        Bodies.Clear();
        Contacts.Clear();
        Joints.Clear();
    }
}

/// <summary>
/// Builds islands from bodies, contacts, and joints via flood-fill.
/// </summary>
internal static class IslandBuilder
{
    // Reusable state to avoid allocations
    private static readonly List<Island> _islands = new();
    private static readonly Stack<RigidBody> _stack = new();
    private static readonly HashSet<int> _visited = new();
    private static readonly HashSet<ContactConstraint> _addedContacts = new();
    private static readonly HashSet<Joint> _addedJoints = new();
    private static readonly Stack<Island> _islandPool = new();

    /// <summary>
    /// Build islands from the given bodies, contacts, and joints.
    /// Returns the list of islands (reused across calls — do not store).
    /// </summary>
    public static List<Island> Build(
        IReadOnlyList<RigidBody> bodies,
        List<ContactConstraint> contacts,
        IReadOnlyList<Joint> joints)
    {
        // Return islands to pool
        for (int i = 0; i < _islands.Count; i++)
        {
            _islands[i].Clear();
            _islandPool.Push(_islands[i]);
        }
        _islands.Clear();
        _visited.Clear();
        _addedContacts.Clear();
        _addedJoints.Clear();

        // Build adjacency: body ID → contacts and joints
        // We flood-fill starting from each unvisited dynamic body

        for (int i = 0; i < bodies.Count; i++)
        {
            var seed = bodies[i];
            if (seed.Type == BodyType.Static) continue;
            if (seed.IsSleeping) continue;
            if (_visited.Contains(seed.Id)) continue;

            var island = _islandPool.Count > 0 ? _islandPool.Pop() : new Island();

            // Flood-fill from seed
            _stack.Clear();
            _stack.Push(seed);
            _visited.Add(seed.Id);

            while (_stack.Count > 0)
            {
                var body = _stack.Pop();
                island.Bodies.Add(body);

                // Find contacts involving this body
                for (int c = 0; c < contacts.Count; c++)
                {
                    var cc = contacts[c];
                    RigidBody? other = null;
                    if (cc.BodyA == body) other = cc.BodyB;
                    else if (cc.BodyB == body) other = cc.BodyA;
                    else continue;

                    // Add contact to island (O(1) dedup via HashSet)
                    if (_addedContacts.Add(cc))
                        island.Contacts.Add(cc);

                    if (other.Type == BodyType.Static) continue;
                    if (_visited.Contains(other.Id)) continue;
                    _visited.Add(other.Id);
                    _stack.Push(other);
                }

                // Find joints involving this body
                for (int j = 0; j < joints.Count; j++)
                {
                    var joint = joints[j];
                    RigidBody? other = null;
                    if (joint.BodyA == body) other = joint.BodyB;
                    else if (joint.BodyB == body) other = joint.BodyA;
                    else continue;

                    if (_addedJoints.Add(joint))
                        island.Joints.Add(joint);

                    if (other == null || other.Type == BodyType.Static) continue;
                    if (_visited.Contains(other.Id)) continue;
                    _visited.Add(other.Id);
                    _stack.Push(other);
                }
            }

            if (island.Bodies.Count > 0)
                _islands.Add(island);
        }

        return _islands;
    }
}
