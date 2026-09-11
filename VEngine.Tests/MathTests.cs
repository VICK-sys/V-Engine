using VEngine.Engine.Math;

namespace VEngine.Tests;

public class Vec2Tests
{
    [Fact]
    public void Add() => Assert.Equal(new Vec2(3, 5), new Vec2(1, 2) + new Vec2(2, 3));

    [Fact]
    public void Subtract() => Assert.Equal(new Vec2(-1, -1), new Vec2(1, 2) - new Vec2(2, 3));

    [Fact]
    public void Scale() => Assert.Equal(new Vec2(6, 9), new Vec2(2, 3) * 3f);

    [Fact]
    public void Negate() => Assert.Equal(new Vec2(-1, -2), -new Vec2(1, 2));

    [Fact]
    public void Length() => Assert.Equal(5f, new Vec2(3, 4).Length());

    [Fact]
    public void LengthSquared() => Assert.Equal(25f, new Vec2(3, 4).LengthSquared());

    [Fact]
    public void Normalized()
    {
        var n = new Vec2(0, 10).Normalized();
        Assert.Equal(0, n.X, 0.001f);
        Assert.Equal(1, n.Y, 0.001f);
    }

    [Fact]
    public void NormalizedZero() => Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());

    [Fact]
    public void Dot() => Assert.Equal(11f, Vec2.Dot(new Vec2(1, 2), new Vec2(3, 4)));

    [Fact]
    public void Distance() => Assert.Equal(5f, Vec2.Distance(Vec2.Zero, new Vec2(3, 4)));

    [Fact]
    public void Lerp()
    {
        var result = Vec2.Lerp(Vec2.Zero, new Vec2(10, 20), 0.5f);
        Assert.Equal(5, result.X);
        Assert.Equal(10, result.Y);
    }

    [Fact]
    public void AngleTo()
    {
        float angle = Vec2.AngleTo(Vec2.Zero, new Vec2(1, 0));
        Assert.Equal(0, angle, 0.001f);
    }
}

public class ColorTests
{
    [Fact]
    public void Presets()
    {
        Assert.Equal(255, Color.White.R);
        Assert.Equal(0, Color.Black.R);
        Assert.Equal(0, Color.Transparent.A);
    }

    [Fact]
    public void WithAlpha()
    {
        var c = Color.Red.WithAlpha(128);
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(128, c.A);
    }

    [Fact]
    public void Lerp()
    {
        var result = Color.Lerp(Color.Black, Color.White, 0.5f);
        Assert.Equal(127, result.R);
        Assert.Equal(127, result.G);
        Assert.Equal(127, result.B);
    }

    [Fact]
    public void LerpClamped()
    {
        var over = Color.Lerp(Color.Black, Color.White, 2f);
        Assert.Equal(255, over.R);
        var under = Color.Lerp(Color.Black, Color.White, -1f);
        Assert.Equal(0, under.R);
    }

    [Fact]
    public void ImplicitSdlConversion()
    {
        Color c = new(100, 150, 200, 255);
        SDL2.SDL.SDL_Color sdl = c;
        Assert.Equal(100, sdl.r);
        Assert.Equal(150, sdl.g);
        Color back = sdl;
        Assert.Equal(100, back.R);
    }
}

public class RectTests
{
    [Fact]
    public void Overlaps()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(5, 5, 10, 10);
        Assert.True(a.Overlaps(b));
    }

    [Fact]
    public void NoOverlap()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(20, 20, 10, 10);
        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void Contains()
    {
        var r = new Rect(10, 10, 20, 20);
        Assert.True(r.Contains(15, 15));
        Assert.False(r.Contains(5, 5));
    }

    [Fact]
    public void Properties()
    {
        var r = new Rect(10, 20, 30, 40);
        Assert.Equal(40, r.Right);
        Assert.Equal(60, r.Bottom);
        Assert.Equal(25, r.Center.X);
        Assert.Equal(40, r.Center.Y);
    }

    [Fact]
    public void ImplicitFromTuple()
    {
        Rect r = (1f, 2f, 3f, 4f);
        Assert.Equal(1, r.X);
        Assert.Equal(3, r.W);
    }

    [Fact]
    public void ImplicitToTuple()
    {
        var r = new Rect(1, 2, 3, 4);
        (float X, float Y, float W, float H) t = r;
        Assert.Equal(3, t.W);
    }
}
