namespace VEngine.Engine.Graphics;

/// <summary>
/// Defines a named animation: frame sequence, FPS, looping, ping-pong,
/// per-animation hitbox, damage box, and origin point.
/// </summary>
internal class SpriteAnimation
{
    public readonly string Name;
    public readonly int[] Frames;
    public readonly int[] EffectiveFrames;
    public readonly int Fps;
    public readonly bool Looped;
    public readonly bool PingPong;
    public readonly (int X, int Y, int W, int H)? Hitbox;
    public readonly (int X, int Y, int W, int H)? DamageBox;
    public readonly (int X, int Y)? Origin;

    public SpriteAnimation(string name, int[] frames, int fps, bool looped,
        bool pingPong = false,
        (int X, int Y, int W, int H)? hitbox = null,
        (int X, int Y)? origin = null,
        (int X, int Y, int W, int H)? damageBox = null)
    {
        Name = name;
        Frames = frames;
        Fps = fps;
        Looped = looped;
        PingPong = pingPong;
        Hitbox = hitbox;
        DamageBox = damageBox;
        Origin = origin;
        EffectiveFrames = BuildEffectiveFrames(frames, pingPong);
    }

    private static int[] BuildEffectiveFrames(int[] frames, bool pingPong)
    {
        if (!pingPong || frames.Length <= 2) return frames;
        var result = new int[frames.Length + frames.Length - 2];
        frames.CopyTo(result, 0);
        for (int i = 0; i < frames.Length - 2; i++)
            result[frames.Length + i] = frames[frames.Length - 2 - i];
        return result;
    }
}
