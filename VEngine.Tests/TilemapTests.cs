using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

/// <summary>
/// Tests for Tilemap data layer and collision logic.
/// Tilemap rendering requires GL, but tile access, collision rects,
/// and world-to-tile conversion are testable headless.
/// We test via a mock approach: create entities sized like tiles
/// and verify collision math directly.
/// </summary>
public class TilemapCollisionTests
{
    public TilemapCollisionTests()
    {
        Eng.InitHeadless();
        // SeparateOneway reads Eng.Game.FixedDeltaTime — provide a minimal context with Game
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
    }
    [Fact]
    public void CollisionSeparateWorks()
    {
        // Simulate tile collision without a real tilemap:
        // An immovable tile entity at (32, 0) with size 16x16
        var tile = new Entity(32, 0) { BaseWidth = 16, BaseHeight = 16, Immovable = true };
        var player = new KinematicEntity(30, 0) { BaseWidth = 10, BaseHeight = 10, Velocity = new Vec2(50, 0) };

        var dir = Collision.Separate(player, tile);
        Assert.NotEqual(CollisionDir.None, dir);
        Assert.Equal(0, player.Velocity.X, 0.001f);
    }

    [Fact]
    public void OnewayPlatformFromAbove()
    {
        var platform = new Entity(0, 50) { BaseWidth = 100, BaseHeight = 10, Immovable = true };
        // Player feet at y=42+10=52, platform top at y=50 → 2px penetration
        var player = new KinematicEntity(10, 42) { BaseWidth = 10, BaseHeight = 10, Velocity = new Vec2(0, 100) };

        bool landed = Collision.SeparateOneway(player, platform);
        Assert.True(landed);
        Assert.Equal(0, player.Velocity.Y, 0.001f);
    }

    [Fact]
    public void OnewayPlatformFromBelow()
    {
        var platform = new Entity(0, 50) { BaseWidth = 100, BaseHeight = 10, Immovable = true };
        var player = new KinematicEntity(10, 55) { BaseWidth = 10, BaseHeight = 10, Velocity = new Vec2(0, -50) };

        bool landed = Collision.SeparateOneway(player, platform);
        Assert.False(landed); // moving up, should pass through
    }

    [Fact]
    public void SpatialHashWithCollision()
    {
        var hash = new SpatialHash(32);
        var enemy1 = new Entity(10, 10) { BaseWidth = 20, BaseHeight = 20 };
        var enemy2 = new Entity(200, 200) { BaseWidth = 20, BaseHeight = 20 };
        hash.Insert(enemy1);
        hash.Insert(enemy2);

        var player = new Entity(15, 15) { BaseWidth = 10, BaseHeight = 10 };
        var hits = new List<Entity>();
        Collision.OverlapHash(player, hash, (a, b) => hits.Add(b));

        Assert.Single(hits);
        Assert.Same(enemy1, hits[0]); // only nearby enemy
    }

    [Fact]
    public void CollisionCheckReturnsPushVector()
    {
        var a = new Entity(0, 0) { BaseWidth = 20, BaseHeight = 20 };
        var b = new Entity(15, 0) { BaseWidth = 20, BaseHeight = 20 };

        var result = Collision.Check(a, b);
        Assert.NotEqual(CollisionDir.None, result.Direction);
        Assert.NotEqual(0, result.PushX);
        // Position unchanged (pure query)
        Assert.Equal(0, a.Position.X, 0.001f);
    }

    [Fact]
    public void CircleSeparation()
    {
        var a = new KinematicEntity(0, 0) { Velocity = new Vec2(10, 0) };
        var b = new Entity(8, 0);

        float overlap = Collision.SeparateCircles(a, 5, b, 5);
        Assert.True(overlap > 0);
        Assert.True(a.Position.X < 0); // pushed away
    }
}
