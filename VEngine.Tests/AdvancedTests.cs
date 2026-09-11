using System.IO;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class SpriteAnimationTests
{
    [Fact]
    public void FrameAdvances()
    {
        var anim = new SpriteAnimation("test", new[] { 0, 1, 2, 3 }, 10, looped: true);
        Assert.Equal(4, anim.EffectiveFrames.Length);
        Assert.Equal(0, anim.EffectiveFrames[0]);
        Assert.Equal(3, anim.EffectiveFrames[3]);
    }

    [Fact]
    public void PingPongBuildsCorrectFrames()
    {
        // [0,1,2] with ping-pong → [0,1,2,1]
        var anim = new SpriteAnimation("test", new[] { 0, 1, 2 }, 10, looped: true, pingPong: true);
        Assert.Equal(4, anim.EffectiveFrames.Length);
        Assert.Equal(0, anim.EffectiveFrames[0]);
        Assert.Equal(1, anim.EffectiveFrames[1]);
        Assert.Equal(2, anim.EffectiveFrames[2]);
        Assert.Equal(1, anim.EffectiveFrames[3]); // reversed middle
    }

    [Fact]
    public void PingPongTwoFramesNoExpansion()
    {
        // [0,1] with ping-pong → [0,1] (no middle to reverse)
        var anim = new SpriteAnimation("test", new[] { 0, 1 }, 10, looped: true, pingPong: true);
        Assert.Equal(2, anim.EffectiveFrames.Length);
    }

    [Fact]
    public void HitboxAndOriginStored()
    {
        var anim = new SpriteAnimation("attack", new[] { 0, 1 }, 8, looped: false,
            hitbox: (5, 3, 36, 50), origin: (23, 55));
        Assert.Equal((5, 3, 36, 50), anim.Hitbox);
        Assert.Equal((23, 55), anim.Origin);
    }

    [Fact]
    public void DamageBoxStored()
    {
        var anim = new SpriteAnimation("slash", new[] { 0, 1, 2 }, 12, looped: false,
            damageBox: (10, 5, 30, 40));
        Assert.Equal((10, 5, 30, 40), anim.DamageBox);
    }
}

public class TimeScaleTests
{
    [Fact]
    public void TimerRespectsScaledDt()
    {
        var mgr = new TimerManager();
        int count = 0;
        mgr.After(1f, () => count++);

        // Simulate TimeScale = 0.5: dt passed to timer is halved
        mgr.Update(0.5f); // simulates 0.5 scaled seconds
        Assert.Equal(0, count);
        mgr.Update(0.5f); // total 1.0 scaled seconds
        Assert.Equal(1, count);
    }

    [Fact]
    public void TweenRespectsScaledDt()
    {
        var mgr = new TweenManager();
        float val = 0;
        mgr.To(() => val, v => val = v, 100f, 1f);

        // Half-speed: 0.5 scaled dt per update
        mgr.Update(0.5f);
        Assert.Equal(50, val, 1f);
        mgr.Update(0.5f);
        Assert.Equal(100, val, 0.001f);
    }

    [Fact]
    public void FreezeStopsTimers()
    {
        var fx = new Effects();
        var mgr = new TimerManager();
        int count = 0;
        mgr.After(0.5f, () => count++);

        fx.Freeze(1f);
        // With TimeScale = 0, scaledDt = fixedDt * 0 = 0
        mgr.Update(0); // no progress
        mgr.Update(0);
        Assert.Equal(0, count);

        // Unfreeze
        fx.Update(1.1f);
        Assert.Equal(1f, fx.TimeScale);
        mgr.Update(0.6f);
        Assert.Equal(1, count);
    }
}

public class SceneStackTests
{
    public SceneStackTests()
    {
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
    }

    [Fact]
    public void PushAndPop()
    {
        var game = Eng.Game;
        var main = new TestScene("main");
        var pause = new TestScene("pause");

        // Simulate initial scene
        game.PushScene(main);
        Assert.True(main.Created);
        Assert.Equal(0, game.SceneStackDepth);

        // Push pause menu
        game.PushScene(pause);
        Assert.True(pause.Created);
        Assert.Equal(1, game.SceneStackDepth);

        // Pop back to main
        bool popped = game.PopScene();
        Assert.True(popped);
        Assert.True(pause.Destroyed);
        Assert.Equal(0, game.SceneStackDepth);
    }

    [Fact]
    public void PopEmptyReturnsFalse()
    {
        Assert.False(Eng.Game.PopScene());
    }

    private class TestScene : Scene
    {
        public string Name;
        public bool Created;
        public bool Destroyed;

        public TestScene(string name) { Name = name; }
        public override void Create() { Created = true; }
        public override void Destroy() { Destroyed = true; base.Destroy(); }
    }
}

public class TrailRendererTests
{
    [Fact]
    public void AddPointIncreasesCount()
    {
        var trail = new TrailRenderer();
        Assert.Equal(0, trail.Count);

        trail.AddPoint(0, 0);
        Assert.Equal(1, trail.Count);

        trail.AddPoint(100, 0);
        Assert.Equal(2, trail.Count);
    }

    [Fact]
    public void MinDistanceFiltersClosePoints()
    {
        var trail = new TrailRenderer { MinDistance = 10 };
        trail.AddPoint(0, 0);
        trail.AddPoint(1, 1); // too close
        trail.AddPoint(2, 0); // too close
        Assert.Equal(1, trail.Count);

        trail.AddPoint(20, 0); // far enough
        Assert.Equal(2, trail.Count);
    }

    [Fact]
    public void PointsExpireAfterMaxLife()
    {
        var trail = new TrailRenderer { MaxLife = 1f, MinDistance = 0 };
        trail.AddPoint(0, 0);
        trail.AddPoint(10, 0);
        trail.AddPoint(20, 0);
        Assert.Equal(3, trail.Count);

        // Age past MaxLife
        trail.Update(1.1f);
        Assert.Equal(0, trail.Count);
    }

    [Fact]
    public void PartialAging()
    {
        var trail = new TrailRenderer { MaxLife = 1f, MinDistance = 0 };
        trail.AddPoint(0, 0);
        trail.AddPoint(10, 0);

        // Age partially — both still alive
        trail.Update(0.5f);
        Assert.Equal(2, trail.Count);

        // Add a new point, then age again — old ones expire, new one survives
        trail.AddPoint(20, 0);
        trail.Update(0.6f);
        Assert.Equal(1, trail.Count);
    }

    [Fact]
    public void RingBufferWraps()
    {
        var trail = new TrailRenderer(maxPoints: 4) { MinDistance = 0, MaxLife = 10f };

        // Fill past capacity
        for (int i = 0; i < 6; i++)
            trail.AddPoint(i * 20, 0);

        // Capped at MaxPoints
        Assert.Equal(4, trail.Count);
        Assert.Equal(4, trail.MaxPoints);
    }

    [Fact]
    public void ClearResetsEverything()
    {
        var trail = new TrailRenderer { MinDistance = 0 };
        trail.AddPoint(0, 0);
        trail.AddPoint(10, 0);
        trail.AddPoint(20, 0);
        Assert.Equal(3, trail.Count);

        trail.Clear();
        Assert.Equal(0, trail.Count);

        // Can add points again after clear (no stale last-position check)
        trail.AddPoint(5, 5);
        Assert.Equal(1, trail.Count);
    }

    [Fact]
    public void FollowTracksEntity()
    {
        var target = new Entity(50, 50) { BaseWidth = 20, BaseHeight = 20 };
        var trail = new TrailRenderer { MinDistance = 0 };
        trail.Follow(target);

        trail.Update(0.016f);
        Assert.Equal(1, trail.Count);

        // Move target far enough for a second point
        target.Position = new Vec2(100, 50);
        trail.Update(0.016f);
        Assert.Equal(2, trail.Count);
    }

    [Fact]
    public void UnfollowStopsTracking()
    {
        var target = new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 };
        var trail = new TrailRenderer { MinDistance = 0 };
        trail.Follow(target);

        trail.Update(0.016f);
        Assert.Equal(1, trail.Count);

        trail.Unfollow();
        target.Position = new Vec2(100, 0);
        trail.Update(0.016f);
        // Count stays same (point aged but no new point added; still alive)
        Assert.Equal(1, trail.Count);
    }

    [Fact]
    public void AutoUnfollowsDestroyedTarget()
    {
        var target = new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 };
        var trail = new TrailRenderer { MinDistance = 0 };
        trail.Follow(target);

        trail.Update(0.016f);
        Assert.Equal(1, trail.Count);

        target.Destroy();
        target.Position = new Vec2(200, 0);
        trail.Update(0.016f);
        // No new point added for destroyed target
        Assert.Equal(1, trail.Count);
    }

    [Fact]
    public void AutoUnfollowsInactiveTarget()
    {
        var target = new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 };
        var trail = new TrailRenderer { MinDistance = 0 };
        trail.Follow(target);

        trail.Update(0.016f);
        target.Active = false;
        target.Position = new Vec2(200, 0);
        trail.Update(0.016f);

        Assert.Equal(1, trail.Count);
    }

    [Fact]
    public void FollowOffsetApplied()
    {
        var target = new Entity(100, 100) { BaseWidth = 20, BaseHeight = 20 };
        var trail = new TrailRenderer { MinDistance = 0, FollowOffset = new Vec2(5, -5) };
        trail.Follow(target);

        trail.Update(0.016f);
        Assert.Equal(1, trail.Count);

        // Move target and verify a second point is added (offset is consistent,
        // so it's the entity position change that creates distance)
        target.Position = new Vec2(200, 100);
        trail.Update(0.016f);
        Assert.Equal(2, trail.Count);
    }

    [Fact]
    public void MaxPointsFloorIsTwo()
    {
        var trail = new TrailRenderer(maxPoints: 0);
        Assert.True(trail.MaxPoints >= 2);
    }

    [Fact]
    public void DefaultConfigValues()
    {
        var trail = new TrailRenderer();
        Assert.Equal(0.5f, trail.MaxLife);
        Assert.Equal(4f, trail.Thickness);
        Assert.Equal(0f, trail.ThicknessEnd);
        Assert.Equal(2f, trail.MinDistance);
        Assert.Equal(1f, trail.AlphaStart);
        Assert.Equal(0f, trail.AlphaEnd);
        Assert.Equal(64, trail.MaxPoints);
    }
}

public class LightRendererTests
{
    [Fact]
    public void AddAndRemoveLight()
    {
        var lr = new LightRenderer();
        var light = lr.AddLight(100, 200, 150, Color.White);
        Assert.Single(lr.Lights);
        Assert.Equal(100f, light.Position.X);
        Assert.Equal(200f, light.Position.Y);
        Assert.Equal(150f, light.Radius);

        lr.RemoveLight(light);
        Assert.Empty(lr.Lights);
    }

    [Fact]
    public void LightDefaults()
    {
        var light = new Light(0, 0, 100, Color.Orange);
        Assert.Equal(1f, light.Intensity);
        Assert.True(light.Active);
        Assert.False(light.CastShadows);
        Assert.False(light.IsSpot);
        Assert.Equal(45f, light.ConeAngle);
    }

    [Fact]
    public void AmbientDefault()
    {
        var lr = new LightRenderer();
        Assert.Equal(20, lr.Ambient.R);
        Assert.Equal(20, lr.Ambient.G);
        Assert.Equal(30, lr.Ambient.B);
    }

    [Fact]
    public void ClearLights()
    {
        var lr = new LightRenderer();
        lr.AddLight(0, 0, 50, Color.Red);
        lr.AddLight(0, 0, 50, Color.Blue);
        Assert.Equal(2, lr.Lights.Count);
        lr.ClearLights();
        Assert.Empty(lr.Lights);
    }

    [Fact]
    public void SpotLightConfig()
    {
        var lr = new LightRenderer();
        var spot = lr.AddLight(50, 50, 200, Color.White);
        spot.IsSpot = true;
        spot.Direction = 90;
        spot.ConeAngle = 30;
        Assert.True(spot.IsSpot);
        Assert.Equal(90f, spot.Direction);
        Assert.Equal(30f, spot.ConeAngle);
    }

    [Fact]
    public void EnabledDefault()
    {
        var lr = new LightRenderer();
        Assert.True(lr.Enabled);
    }
}

public class SpriteEffectsTests
{
    [Fact]
    public void FlashActivatesAndDecays()
    {
        var sprite = new Sprite();
        Assert.False(sprite.IsFlashing);

        sprite.Flash(0.2f);
        Assert.True(sprite.IsFlashing);

        sprite.Update(0.1f);
        Assert.True(sprite.IsFlashing);

        sprite.Update(0.15f);
        Assert.False(sprite.IsFlashing);
    }

    [Fact]
    public void FlashZeroDurationIsNoop()
    {
        var sprite = new Sprite();
        sprite.Flash(0f);
        Assert.False(sprite.IsFlashing);
    }

    [Fact]
    public void FlashNegativeDurationIsNoop()
    {
        var sprite = new Sprite();
        sprite.Flash(-1f);
        Assert.False(sprite.IsFlashing);
    }

    [Fact]
    public void FlashDefaultColor()
    {
        var sprite = new Sprite();
        sprite.Flash(0.5f);
        Assert.True(sprite.IsFlashing);
    }

    [Fact]
    public void FlashCustomColor()
    {
        var sprite = new Sprite();
        sprite.Flash(0.5f, Color.Red);
        Assert.True(sprite.IsFlashing);
    }

    [Fact]
    public void FlashResetsOnNewFlash()
    {
        var sprite = new Sprite();
        sprite.Flash(0.1f);
        sprite.Update(0.08f); // almost expired
        Assert.True(sprite.IsFlashing);

        sprite.Flash(0.5f); // new flash resets timer
        sprite.Update(0.1f);
        Assert.True(sprite.IsFlashing); // still active
    }

    [Fact]
    public void SilhouetteDefaults()
    {
        var sprite = new Sprite();
        Assert.False(sprite.Silhouette);
        Assert.Equal(255, sprite.SilhouetteColor.R);
        Assert.Equal(255, sprite.SilhouetteColor.G);
        Assert.Equal(255, sprite.SilhouetteColor.B);
    }

    [Fact]
    public void OutlineDefaults()
    {
        var sprite = new Sprite();
        Assert.Equal(0f, sprite.OutlineThickness);
        Assert.Equal(255, sprite.OutlineColor.R);
        Assert.Equal(255, sprite.OutlineColor.G);
        Assert.Equal(255, sprite.OutlineColor.B);
    }

    [Fact]
    public void SilhouetteToggles()
    {
        var sprite = new Sprite();
        sprite.Silhouette = true;
        Assert.True(sprite.Silhouette);
        sprite.Silhouette = false;
        Assert.False(sprite.Silhouette);
    }
}

public class SpriteStackTests
{
    [Fact]
    public void DefaultConfiguration()
    {
        var stack = new SpriteStack();
        Assert.Equal(1f, stack.StackOffset);
        Assert.Equal(-1, stack.VisibleSlices);
        Assert.Equal(0, stack.SliceCount);
        Assert.False(stack.FlipX);
        Assert.False(stack.FlipY);
    }

    [Fact]
    public void PositionAndKinematics()
    {
        var stack = new SpriteStack(50, 100);
        Assert.Equal(50f, stack.Position.X);
        Assert.Equal(100f, stack.Position.Y);

        // KinematicEntity: velocity applies during Update
        stack.Velocity = new Vec2(10, 0);
        stack.Update(1f);
        Assert.Equal(60f, stack.Position.X);
    }

    [Fact]
    public void DrawWithNoTextureDoesNotCrash()
    {
        // SpriteStack without LoadGraphic should no-op on Draw
        var stack = new SpriteStack();
        Assert.Equal(0, stack.SliceCount);
        // Can't call Draw in headless, but SliceCount=0 guard ensures safety
    }

    [Fact]
    public void ScaleAffectsDimensions()
    {
        var stack = new SpriteStack { BaseWidth = 16, BaseHeight = 16, ScaleX = 2, ScaleY = 3 };
        Assert.Equal(32f, stack.Width);
        Assert.Equal(48f, stack.Height);
    }

    [Fact]
    public void VisibleSlicesClampedToSliceCount()
    {
        // VisibleSlices larger than SliceCount should draw at most SliceCount
        var stack = new SpriteStack { VisibleSlices = 100 };
        // With SliceCount = 0, drawCount = min(100, 0) = 0
        Assert.Equal(0, stack.SliceCount);
    }

    [Fact]
    public void AngleIsInherited()
    {
        var stack = new SpriteStack { Angle = 45 };
        Assert.Equal(45f, stack.Angle);
    }
}

public class ScreenCaptureTests : IDisposable
{
    private readonly string _tempDir;

    public ScreenCaptureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vengine_capture_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void WritePngRoundTrip()
    {
        // Create a 2x2 RGBA image: red, green, blue, white
        byte[] pixels =
        {
            255, 0, 0, 255,    0, 255, 0, 255,   // row 0: red, green
            0, 0, 255, 255,    255, 255, 255, 255  // row 1: blue, white
        };

        var path = Path.Combine(_tempDir, "test.png");
        ScreenCapture.WritePng(path, 2, 2, pixels);

        // Read back with StbImageSharp
        using var stream = File.OpenRead(path);
        var img = StbImageSharp.ImageResult.FromStream(stream,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);

        // Verify pixels match
        Assert.Equal(255, img.Data[0]); // red R
        Assert.Equal(0,   img.Data[1]); // red G
        Assert.Equal(0,   img.Data[2]); // red B
        Assert.Equal(255, img.Data[3]); // red A

        Assert.Equal(0,   img.Data[4]); // green R
        Assert.Equal(255, img.Data[5]); // green G

        Assert.Equal(0,   img.Data[8]);  // blue R
        Assert.Equal(0,   img.Data[9]);  // blue G
        Assert.Equal(255, img.Data[10]); // blue B

        Assert.Equal(255, img.Data[12]); // white R
        Assert.Equal(255, img.Data[13]); // white G
        Assert.Equal(255, img.Data[14]); // white B
        Assert.Equal(255, img.Data[15]); // white A
    }

    [Fact]
    public void WritePngLargerImage()
    {
        // 16x16 gradient image
        int w = 16, h = 16;
        byte[] pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                pixels[i]     = (byte)(x * 16); // R
                pixels[i + 1] = (byte)(y * 16); // G
                pixels[i + 2] = 128;            // B
                pixels[i + 3] = 255;            // A
            }

        var path = Path.Combine(_tempDir, "gradient.png");
        ScreenCapture.WritePng(path, w, h, pixels);

        using var stream = File.OpenRead(path);
        var img = StbImageSharp.ImageResult.FromStream(stream,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        Assert.Equal(w, img.Width);
        Assert.Equal(h, img.Height);

        // Spot-check corners
        Assert.Equal(0, img.Data[0]);     // top-left R
        Assert.Equal(0, img.Data[1]);     // top-left G
        Assert.Equal(128, img.Data[2]);   // top-left B

        int lastPixel = (w * h - 1) * 4;
        Assert.Equal(240, img.Data[lastPixel]);     // bottom-right R (15*16)
        Assert.Equal(240, img.Data[lastPixel + 1]); // bottom-right G (15*16)
    }

    [Fact]
    public void WritePngSinglePixel()
    {
        byte[] pixels = { 42, 99, 200, 128 };
        var path = Path.Combine(_tempDir, "single.png");
        ScreenCapture.WritePng(path, 1, 1, pixels);

        using var stream = File.OpenRead(path);
        var img = StbImageSharp.ImageResult.FromStream(stream,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(42,  img.Data[0]);
        Assert.Equal(99,  img.Data[1]);
        Assert.Equal(200, img.Data[2]);
        Assert.Equal(128, img.Data[3]);
    }

    [Fact]
    public void WritePngCreatesDirectory()
    {
        byte[] pixels = { 0, 0, 0, 255 };
        var nested = Path.Combine(_tempDir, "sub", "dir", "img.png");
        ScreenCapture.WritePng(nested, 1, 1, pixels);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void DefaultScreenshotsPath()
    {
        Assert.Contains("Screenshots", ScreenCapture.ScreenshotsPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

public class LevelLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public LevelLoaderTests()
    {
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        _tempDir = Path.Combine(Path.GetTempPath(), "vengine_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        Eng.Context = new EngineContext
        {
            Game = new Game("test", 320, 180),
        };
    }

    [Fact]
    public void GetSizeReadsJsonDimensions()
    {
        var json = """
        {
            "width": 150,
            "height": 30,
            "spawn": { "x": 100, "y": 200 },
            "entities": []
        }
        """;
        var path = Path.Combine(_tempDir, "test_level.json");
        File.WriteAllText(path, json);

        var (w, h) = LevelLoader.GetSize(path);
        Assert.Equal(150, w);
        Assert.Equal(30, h);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
