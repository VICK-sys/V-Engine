using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class TextEntityTests
{
    [Fact]
    public void TextContentDirtyFlag()
    {
        var text = new Text("Hello", 10, 20);
        Assert.Equal("Hello", text.Content);

        text.Content = "World";
        Assert.Equal("World", text.Content);
    }

    [Fact]
    public void TextColorChange()
    {
        var text = new Text("Test");
        Assert.Equal(255, text.Color.R);

        text.Color = Color.Red;
        Assert.Equal(255, text.Color.R);
        Assert.Equal(0, text.Color.G);
    }

    [Fact]
    public void TextFontSizeDefault()
    {
        var text = new Text("Test");
        // FontSize only validates when a font path is set (via SetFont)
        // Default state: no font loaded, size = 0
        Assert.Equal(0, text.FontSize);
    }

    [Fact]
    public void TextPosition()
    {
        var text = new Text("Hello", 100, 200);
        Assert.Equal(100, text.Position.X);
        Assert.Equal(200, text.Position.Y);
    }

    [Fact]
    public void TextScrollFactor()
    {
        var text = new Text("HUD");
        text.ScrollFactor = Vec2.Zero; // fixed to screen
        Assert.Equal(0, text.ScrollFactor.X);
        Assert.Equal(0, text.ScrollFactor.Y);
    }

    [Fact]
    public void TextLayer()
    {
        var text = new Text("UI");
        text.Layer = 10;
        Assert.Equal(10, text.Layer);
    }

    [Fact]
    public void TextDestroy()
    {
        var text = new Text("Gone");
        text.Destroy();
        Assert.True(text.Destroyed);
        text.Destroy(); // double destroy no-op
    }

    [Fact]
    public void TextUseAtlasFlag()
    {
        var text = new Text("Atlas");
        Assert.False(text.UseAtlas);
        text.UseAtlas = true;
        Assert.True(text.UseAtlas);
    }

    [Fact]
    public void TextEmptyContent()
    {
        var text = new Text("");
        Assert.Equal("", text.Content);
        text.Content = null!;
        // Should handle null gracefully
    }
}

public class SpriteAnimationLogicTests
{
    [Fact]
    public void SingleFrameAnimation()
    {
        var anim = new SpriteAnimation("idle", new[] { 5 }, 10, looped: true);
        Assert.Single(anim.EffectiveFrames);
        Assert.Equal(5, anim.EffectiveFrames[0]);
    }

    [Fact]
    public void LongAnimation()
    {
        var frames = new int[100];
        for (int i = 0; i < 100; i++) frames[i] = i;
        var anim = new SpriteAnimation("long", frames, 30, looped: true);
        Assert.Equal(100, anim.EffectiveFrames.Length);
    }

    [Fact]
    public void PingPongLongAnimation()
    {
        var frames = new int[] { 0, 1, 2, 3, 4 };
        var anim = new SpriteAnimation("pp", frames, 10, looped: true, pingPong: true);
        // [0,1,2,3,4,3,2,1] = 8 effective frames
        Assert.Equal(8, anim.EffectiveFrames.Length);
        Assert.Equal(0, anim.EffectiveFrames[0]);
        Assert.Equal(4, anim.EffectiveFrames[4]);
        Assert.Equal(3, anim.EffectiveFrames[5]);
        Assert.Equal(1, anim.EffectiveFrames[7]);
    }

    [Fact]
    public void AnimationFpsStored()
    {
        var anim = new SpriteAnimation("fast", new[] { 0, 1 }, 60, looped: false);
        Assert.Equal(60, anim.Fps);
        Assert.False(anim.Looped);
    }

    [Fact]
    public void AllMetadataStored()
    {
        var anim = new SpriteAnimation("attack", new[] { 0, 1, 2 }, 12,
            looped: false, pingPong: true,
            hitbox: (5, 10, 30, 40),
            origin: (15, 50),
            damageBox: (20, 15, 25, 35));

        Assert.True(anim.PingPong);
        Assert.Equal((5, 10, 30, 40), anim.Hitbox);
        Assert.Equal((15, 50), anim.Origin);
        Assert.Equal((20, 15, 25, 35), anim.DamageBox);
    }
}

public class DebugOverlayTests
{
    public DebugOverlayTests() => Eng.InitHeadless();

    [Fact]
    public void ToggleOnOff()
    {
        var debug = new DebugOverlay();
        Assert.False(debug.Enabled);
        debug.Toggle();
        Assert.True(debug.Enabled);
        debug.Toggle();
        Assert.False(debug.Enabled);
    }

    [Fact]
    public void SetEnabled()
    {
        var debug = new DebugOverlay();
        debug.Enabled = true;
        Assert.True(debug.Enabled);
        debug.Enabled = false;
        Assert.False(debug.Enabled);
    }

    [Fact]
    public void ShutdownDoesNotCrash()
    {
        var debug = new DebugOverlay();
        debug.Shutdown(); // no font loaded, should not crash
    }
}

public class ColorMathTests
{
    [Fact]
    public void LerpIdentity()
    {
        var c = Color.Lerp(Color.Red, Color.Red, 0.5f);
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
    }

    [Fact]
    public void LerpMidpoint()
    {
        var c = Color.Lerp(new Color(0, 0, 0), new Color(200, 100, 50), 0.5f);
        Assert.Equal(100, c.R);
        Assert.Equal(50, c.G);
        Assert.Equal(25, c.B);
    }

    [Fact]
    public void WithAlphaPreservesRGB()
    {
        var c = new Color(10, 20, 30, 255).WithAlpha(128);
        Assert.Equal(10, c.R);
        Assert.Equal(20, c.G);
        Assert.Equal(30, c.B);
        Assert.Equal(128, c.A);
    }

    [Fact]
    public void PurplePreset()
    {
        Assert.Equal(124, Color.Purple.R);
        Assert.Equal(58, Color.Purple.G);
        Assert.Equal(237, Color.Purple.B);
    }

    [Fact]
    public void ToStringFormat()
    {
        var c = new Color(10, 20, 30, 40);
        Assert.Equal("Color(10, 20, 30, 40)", c.ToString());
    }
}

public class VolumeOverlayTests
{
    public VolumeOverlayTests() => Eng.InitHeadless();

    [Fact]
    public void ShowAndShutdown()
    {
        var vol = new VolumeOverlay();
        vol.Show();
        // Can't call Update without Audio — but Show + Shutdown should not crash
        vol.Shutdown();
    }

    [Fact]
    public void ShutdownClean()
    {
        var vol = new VolumeOverlay();
        vol.Shutdown(); // no font loaded, should not crash
    }
}
