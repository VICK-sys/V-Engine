using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

public class Sprite : KinematicEntity
{
    // Metadata cache — avoids re-reading and re-parsing JSON for shared spritesheets
    private static readonly Dictionary<string, JsonDocument> _metaCache = new();

    /// <summary>Clear the sprite metadata cache. Called on scene switch to prevent JsonDocument leak.</summary>
    public static void ClearMetaCache()
    {
        foreach (var doc in _metaCache.Values)
            doc.Dispose();
        _metaCache.Clear();
    }

    private GLTexture? _texture;

    // Spritesheet frame info
    private int _frameWidth;
    private int _frameHeight;
    private int _cols;

    // Animation
    private readonly Dictionary<string, SpriteAnimation> _animations = new();
    private SpriteAnimation? _currentAnim;
    private int _currentFrameIndex;
    private float _animTimer;
    private int _frame;

    /// <summary>True when a non-looped animation has finished playing.</summary>
    public bool AnimationFinished { get; private set; }

    /// <summary>Name of the currently playing animation, or null if none.</summary>
    public string? CurrentAnimationName => _currentAnim?.Name;

    /// <summary>Current frame index within the animation's effective frames.</summary>
    public int CurrentFrameIndex => _currentFrameIndex;

    /// <summary>Total number of effective frames in the current animation.</summary>
    public int CurrentFrameCount => _currentAnim?.EffectiveFrames.Length ?? 0;

    /// <summary>Animation speed multiplier. 1 = normal, 0.5 = half speed, 2 = double speed.</summary>
    public float AnimationSpeed = 1f;

    /// <summary>Current raw frame index in the spritesheet. Used by AfterImage for ghost snapshots.</summary>
    public int Frame => _frame;

    // Frame event callbacks: animName -> (frameIndex -> callback)
    private readonly Dictionary<string, Dictionary<int, Action>> _frameEvents = new();

    // Rendering
    public Math.Color Color = Math.Color.White;
    public bool FlipX;
    public bool FlipY;

    // ── Sprite Effects ────────────────────────────────────────

    /// <summary>True while a Flash() is active.</summary>
    public bool IsFlashing => _flashTimer > 0;
    private float _flashTimer;
    private Math.Color _flashColor = Math.Color.White;

    /// <summary>Render as a solid color with the texture's alpha shape.</summary>
    public bool Silhouette;

    /// <summary>Color used for silhouette rendering. Default white.</summary>
    public Math.Color SilhouetteColor = Math.Color.White;

    /// <summary>Pixel outline thickness. 0 = no outline.</summary>
    public float OutlineThickness;

    /// <summary>Outline color. Default white.</summary>
    public Math.Color OutlineColor = Math.Color.White;

    /// <summary>World-space X position for clip masking. Pixels on the wrong side are discarded. -1 = disabled.</summary>
    public float ClipX = -1;

    /// <summary>Clip direction: -1 = show left of ClipX only, 1 = show right of ClipX only.</summary>
    public float ClipDir;

    private static readonly (float X, float Y)[] _outlineOffsets =
    {
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (-1, 1), (1, -1), (1, 1)
    };

    public Sprite(float x = 0, float y = 0) : base(x, y) { }

    /// <summary>
    /// Flash the sprite as a solid color for a duration (seconds). Use for hit feedback.
    /// </summary>
    public void Flash(float duration = 0.1f, Math.Color? color = null)
    {
        if (duration <= 0) return;
        _flashTimer = duration;
        _flashColor = color ?? Math.Color.White;
    }

    /// <summary>
    /// Load a sprite from a JSON metadata file exported by the sprite editor.
    /// Automatically loads the spritesheet and registers all animations.
    /// </summary>
    public Sprite LoadFromMeta(string jsonPath)
    {
        string fullPath;
        if (Path.IsPathRooted(jsonPath))
            fullPath = jsonPath;
        else
            fullPath = Eng.Asset(jsonPath);

        var cacheKey = Path.GetFullPath(fullPath);
        if (!_metaCache.TryGetValue(cacheKey, out var doc))
        {
            var json = File.ReadAllText(fullPath);
            doc = JsonDocument.Parse(json);
            _metaCache[cacheKey] = doc;
        }
        var root = doc.RootElement;

        var imageName = root.GetProperty("image").GetString()!;
        var fw = root.GetProperty("frameWidth").GetInt32();
        var fh = root.GetProperty("frameHeight").GetInt32();

        var jsonDir = Path.GetDirectoryName(fullPath)!;
        var imageFullPath = Path.Combine(jsonDir, imageName);
        if (File.Exists(imageFullPath))
            LoadGraphic(imageFullPath, fw, fh);
        else
            LoadGraphic(imageName, fw, fh);

        if (root.TryGetProperty("animations", out var animsEl))
        {
            foreach (var prop in animsEl.EnumerateObject())
            {
                var name = prop.Name;
                var animObj = prop.Value;

                var framesArr = animObj.GetProperty("frames");
                var frames = new int[framesArr.GetArrayLength()];
                int i = 0;
                foreach (var f in framesArr.EnumerateArray())
                    frames[i++] = f.GetInt32();

                int fps = System.Math.Max(1, animObj.TryGetProperty("fps", out var fpsEl) ? fpsEl.GetInt32() : 10);
                bool looped = !animObj.TryGetProperty("looped", out var loopEl) || loopEl.GetBoolean();
                bool pingPong = animObj.TryGetProperty("pingPong", out var ppEl) && ppEl.GetBoolean();

                (int, int, int, int)? hitbox = null;
                if (animObj.TryGetProperty("hitbox", out var hbEl))
                {
                    hitbox = (
                        hbEl.GetProperty("x").GetInt32(),
                        hbEl.GetProperty("y").GetInt32(),
                        hbEl.GetProperty("w").GetInt32(),
                        hbEl.GetProperty("h").GetInt32()
                    );
                }

                (int, int)? origin = null;
                if (animObj.TryGetProperty("origin", out var ogEl))
                {
                    origin = (
                        ogEl.GetProperty("x").GetInt32(),
                        ogEl.GetProperty("y").GetInt32()
                    );
                }

                (int, int, int, int)? damageBox = null;
                if (animObj.TryGetProperty("damageBox", out var dbEl))
                {
                    damageBox = (
                        dbEl.GetProperty("x").GetInt32(),
                        dbEl.GetProperty("y").GetInt32(),
                        dbEl.GetProperty("w").GetInt32(),
                        dbEl.GetProperty("h").GetInt32()
                    );
                }

                AddAnimation(name, frames, fps, looped, pingPong, hitbox, origin, damageBox);
            }
        }

        return this;
    }

    /// <summary>
    /// Load a single image as the sprite's graphic.
    /// </summary>
    public Sprite LoadGraphic(string path)
    {
        LoadTexture(path);
        _frameWidth = _texture!.Width;
        _frameHeight = _texture.Height;
        _cols = 1;
        BaseWidth = _texture.Width;
        BaseHeight = _texture.Height;
        return this;
    }

    /// <summary>
    /// Load a spritesheet with fixed-size frames.
    /// </summary>
    public Sprite LoadGraphic(string path, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentException($"Frame dimensions must be positive (got {frameWidth}x{frameHeight})");
        LoadTexture(path);
        if (frameWidth > _texture!.Width || frameHeight > _texture.Height)
            throw new ArgumentException($"Frame size ({frameWidth}x{frameHeight}) exceeds texture size ({_texture.Width}x{_texture.Height})");
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _cols = _texture.Width / frameWidth;
        BaseWidth = frameWidth;
        BaseHeight = frameHeight;
        return this;
    }

    /// <summary>
    /// Create a simple filled rectangle as the sprite graphic.
    /// Uses a shared 1x1 white texture — color comes from vertex data.
    /// </summary>
    public Sprite MakeGraphic(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        ReleaseTexture();
        _texture = Eng.GL.WhitePixelTexture;

        Color = new Math.Color(r, g, b, a);
        _frameWidth = 1;
        _frameHeight = 1;
        _cols = 1;
        BaseWidth = width;
        BaseHeight = height;
        return this;
    }

    /// <summary>
    /// Register a named animation from frame indices.
    /// </summary>
    public Sprite AddAnimation(string name, int[] frames, int fps, bool looped = true,
        bool pingPong = false,
        (int X, int Y, int W, int H)? hitbox = null,
        (int X, int Y)? origin = null,
        (int X, int Y, int W, int H)? damageBox = null)
    {
        if (frames.Length == 0)
            throw new ArgumentException($"Animation '{name}' must have at least one frame");
        _animations[name] = new SpriteAnimation(name, frames, System.Math.Max(1, fps), looped, pingPong, hitbox, origin, damageBox);
        return this;
    }

    /// <summary>
    /// Play a named animation.
    /// </summary>
    public void Play(string name, bool restart = false)
    {
        if (_animations.TryGetValue(name, out var anim))
        {
            if (_currentAnim == anim && !restart)
                return;
            _currentAnim = anim;
            _currentFrameIndex = 0;
            _animTimer = 0;
            _frame = anim.EffectiveFrames[0];
            AnimationFinished = !anim.Looped && anim.EffectiveFrames.Length <= 1;
        }
    }

    /// <summary>
    /// Jump to a specific frame index in the current animation.
    /// </summary>
    public void SetFrame(int frameIndex, bool pause = true)
    {
        if (_currentAnim == null) return;
        frameIndex = System.Math.Clamp(frameIndex, 0, _currentAnim.EffectiveFrames.Length - 1);
        _currentFrameIndex = frameIndex;
        _frame = _currentAnim.EffectiveFrames[frameIndex];
        _animTimer = 0;
        if (pause)
            AnimationFinished = true;
    }

    /// <summary>
    /// Advance the sprite animation without running Update (no physics, no AI).
    /// Use for cutscene entities that are frozen but need to animate.
    /// </summary>
    public void TickAnimation(float dt)
    {
        if (_currentAnim == null || _currentAnim.EffectiveFrames.Length <= 1 || AnimationFinished) return;
        _animTimer += dt * AnimationSpeed;
        float frameDuration = 1f / _currentAnim.Fps;
        while (_animTimer >= frameDuration)
        {
            _animTimer -= frameDuration;
            _currentFrameIndex++;
            if (_currentFrameIndex >= _currentAnim.EffectiveFrames.Length)
            {
                if (_currentAnim.Looped) _currentFrameIndex = 0;
                else { _currentFrameIndex = _currentAnim.EffectiveFrames.Length - 1; AnimationFinished = true; }
            }
            _frame = _currentAnim.EffectiveFrames[_currentFrameIndex];
        }
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (_flashTimer > 0) _flashTimer -= dt;

        if (_currentAnim != null && _currentAnim.EffectiveFrames.Length > 1 && !AnimationFinished)
        {
            _animTimer += dt * AnimationSpeed;
            float frameDuration = 1f / _currentAnim.Fps;
            while (_animTimer >= frameDuration)
            {
                _animTimer -= frameDuration;
                _currentFrameIndex++;
                if (_currentFrameIndex >= _currentAnim.EffectiveFrames.Length)
                {
                    if (_currentAnim.Looped)
                    {
                        _currentFrameIndex = 0;
                    }
                    else
                    {
                        _currentFrameIndex = _currentAnim.EffectiveFrames.Length - 1;
                        AnimationFinished = true;
                    }
                }
                _frame = _currentAnim.EffectiveFrames[_currentFrameIndex];

                // Fire frame event callback if registered
                if (_frameEvents.TryGetValue(_currentAnim.Name, out var events) &&
                    events.TryGetValue(_currentFrameIndex, out var callback))
                    callback();
            }
        }
    }

    /// <summary>
    /// Register a callback to fire when a specific animation frame is reached.
    /// Useful for footstep sounds, attack hitbox activation, etc.
    /// </summary>
    public Sprite OnFrame(string animName, int frameIndex, Action callback)
    {
        if (!_frameEvents.TryGetValue(animName, out var events))
        {
            events = new Dictionary<int, Action>();
            _frameEvents[animName] = events;
        }
        events[frameIndex] = callback;
        return this;
    }

    public override void Draw()
    {
        if (_texture == null) return;

        // Clip mask — flush and set shader uniforms around the draw
        bool hasClip = ClipX >= 0;
        if (hasClip)
        {
            Eng.GL.FlushAll();
            float sx = Eng.Camera.WorldToScreen(ClipX, 0).X;
            float wx = Eng.GL.ViewportX + sx * Eng.GL.ViewportW / (float)Eng.GL.LogicalWidth;
            Eng.GL.SpriteShader.Use();
            Eng.GL.SpriteShader.SetFloat("uClipX", wx);
            Eng.GL.SpriteShader.SetFloat("uClipDir", ClipDir);
        }

        var (originOffsetX, originOffsetY) = GetOriginOffset();
        float worldX = Position.X + originOffsetX;
        float worldY = Position.Y + originOffsetY;

        // Frustum culling — expand by outline thickness
        var cam = Eng.Camera;
        float pad = OutlineThickness;
        var screen = cam.Transform(worldX - pad, worldY - pad, ScrollFactor);
        float dstW = (Width + pad * 2) * cam.Zoom;
        float dstH = (Height + pad * 2) * cam.Zoom;
        if (screen.X + dstW < 0 || screen.X > Eng.Width ||
            screen.Y + dstH < 0 || screen.Y > Eng.Height)
            return;

        bool solidMode = IsFlashing || Silhouette;
        bool hasEffect = solidMode || OutlineThickness > 0;

        if (hasEffect)
        {
            Eng.GL.FlushAll();

            // Outline pass — 8 offset copies in solid outline color
            if (OutlineThickness > 0)
            {
                Eng.GL.SpriteShader.SetFloat("uEffect", 1f);
                var oc = OutlineColor;
                for (int d = 0; d < 8; d++)
                {
                    float dx = _outlineOffsets[d].X * OutlineThickness;
                    float dy = _outlineOffsets[d].Y * OutlineThickness;
                    DrawQuad(worldX + dx, worldY + dy, oc.R / 255f, oc.G / 255f, oc.B / 255f, oc.A / 255f);
                }
                Eng.GL.FlushAll();
            }

            // Main sprite — solid color if flashing/silhouette, normal otherwise
            var c = IsFlashing ? _flashColor : (Silhouette ? SilhouetteColor : Color);
            Eng.GL.SpriteShader.SetFloat("uEffect", solidMode ? 1f : 0f);
            DrawQuad(worldX, worldY, c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            Eng.GL.FlushAll();
            Eng.GL.SpriteShader.SetFloat("uEffect", 0f);
        }
        else
        {
            // Normal draw — fully batched
            DrawQuad(worldX, worldY, Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);
        }

        // Reset clip mask
        if (hasClip)
        {
            Eng.GL.FlushAll();
            Eng.GL.SpriteShader.Use();
            Eng.GL.SpriteShader.SetFloat("uClipX", -1f);
        }
    }

    /// <summary>Draw the sprite quad at a given world position with specified color.</summary>
    private void DrawQuad(float worldX, float worldY, float r, float g, float b, float a)
    {
        int col = _frame % System.Math.Max(_cols, 1);
        int row = _frame / System.Math.Max(_cols, 1);
        float srcX = col * _frameWidth;
        float srcY = row * _frameHeight;

        float? pivotX = null, pivotY = null;
        if (_currentAnim?.Origin is var ogPivot && ogPivot.HasValue)
        {
            int ogX = FlipX ? _frameWidth - ogPivot.Value.X : ogPivot.Value.X;
            int ogY = FlipY ? _frameHeight - ogPivot.Value.Y : ogPivot.Value.Y;
            pivotX = ogX * ScaleX;
            pivotY = ogY * ScaleY;
        }

        Eng.GL.DrawTextureWorld(
            _texture!,
            srcX, srcY, _frameWidth, _frameHeight,
            worldX, worldY, Width, Height,
            ScrollFactor, r, g, b, a,
            Angle, FlipX, FlipY, pivotX: pivotX, pivotY: pivotY);
    }

    /// <summary>
    /// Capture the current rendering state for ghost/afterimage snapshots.
    /// Returns all data needed to redraw this frame later.
    /// </summary>
    internal GhostSnapshot CaptureGhost()
    {
        var (ox, oy) = GetOriginOffset();
        return new GhostSnapshot
        {
            Texture = _texture,
            Frame = _frame,
            FrameWidth = _frameWidth,
            FrameHeight = _frameHeight,
            Cols = _cols,
            WorldX = Position.X + ox,
            WorldY = Position.Y + oy,
            FlipX = FlipX,
            FlipY = FlipY,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
        };
    }

    /// <summary>
    /// Draw a ghost copy from a previously captured snapshot.
    /// </summary>
    internal void DrawGhost(in GhostSnapshot ghost, float alpha, Math.Color tint)
    {
        if (ghost.Texture == null) return;

        int cols = System.Math.Max(ghost.Cols, 1);
        int col = ghost.Frame % cols;
        int row = ghost.Frame / cols;
        float srcX = col * ghost.FrameWidth;
        float srcY = row * ghost.FrameHeight;
        float w = ghost.FrameWidth * ghost.ScaleX;
        float h = ghost.FrameHeight * ghost.ScaleY;

        Eng.GL.DrawTextureWorld(
            ghost.Texture, srcX, srcY, ghost.FrameWidth, ghost.FrameHeight,
            ghost.WorldX, ghost.WorldY, w, h,
            ScrollFactor, tint.R / 255f, tint.G / 255f, tint.B / 255f, alpha,
            0f, ghost.FlipX, ghost.FlipY);
    }

    internal struct GhostSnapshot
    {
        public GL.GLTexture? Texture;
        public int Frame, FrameWidth, FrameHeight, Cols;
        public float WorldX, WorldY, ScaleX, ScaleY;
        public bool FlipX, FlipY;
    }

    /// <summary>
    /// Returns collision bounds. Uses the current animation's hitbox if defined.
    /// </summary>
    public override (float X, float Y, float W, float H) GetCollisionBounds()
    {
        if (_currentAnim?.Hitbox is var hb && hb.HasValue)
        {
            int hbX = FlipX ? _frameWidth - hb.Value.X - hb.Value.W : hb.Value.X;
            int hbY = FlipY ? _frameHeight - hb.Value.Y - hb.Value.H : hb.Value.Y;
            var (ox, oy) = GetOriginOffset();
            return (
                Position.X + ox + hbX * ScaleX,
                Position.Y + oy + hbY * ScaleY,
                hb.Value.W * ScaleX,
                hb.Value.H * ScaleY
            );
        }
        return base.GetCollisionBounds();
    }

    /// <summary>
    /// Returns the damage box for the current animation in world space, or null if not defined.
    /// </summary>
    public (float X, float Y, float W, float H)? GetDamageBox()
    {
        if (_currentAnim?.DamageBox is var db && db.HasValue)
        {
            int dbX = FlipX ? _frameWidth - db.Value.X - db.Value.W : db.Value.X;
            int dbY = FlipY ? _frameHeight - db.Value.Y - db.Value.H : db.Value.Y;
            var (ox, oy) = GetOriginOffset();
            return (
                Position.X + ox + dbX * ScaleX,
                Position.Y + oy + dbY * ScaleY,
                db.Value.W * ScaleX,
                db.Value.H * ScaleY
            );
        }
        return null;
    }

    protected override void OnDestroy()
    {
        ReleaseTexture();
    }

    private void ReleaseTexture()
    {
        // Don't dispose cached textures — GLRenderer owns them
        _texture = null;
    }

    /// <summary>
    /// Compute the origin offset for the current animation, accounting for FlipX/FlipY.
    /// Returns (0,0) if no origin is defined.
    /// </summary>
    internal (float X, float Y) GetOriginOffset()
    {
        if (_currentAnim?.Origin is var og && og.HasValue)
        {
            int ogX = FlipX ? _frameWidth - og.Value.X : og.Value.X;
            int ogY = FlipY ? _frameHeight - og.Value.Y : og.Value.Y;
            return (-ogX * ScaleX, -ogY * ScaleY);
        }
        return (0, 0);
    }

    private void LoadTexture(string path)
    {
        if (!Path.IsPathRooted(path))
            path = Eng.Asset(path);

        ReleaseTexture();
        _texture = Eng.GL.GetOrCreateTexture(path);
    }

}
