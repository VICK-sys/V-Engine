using System;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Renders a pseudo-3D object by stacking sprite slices vertically.
/// Each frame in the spritesheet is one horizontal cross-section (bottom to top).
/// Set Angle to rotate the stack for a 3D spinning effect.
///
/// Usage:
///   var tree = new SpriteStack(100, 200);
///   tree.LoadGraphic("tree_slices.png", 16, 16);
///   tree.StackOffset = 1;
///   tree.Angle = 45;
///   scene.Add(tree);
/// </summary>
public class SpriteStack : KinematicEntity
{
    private GLTexture? _texture;
    private int _sliceWidth;
    private int _sliceHeight;
    private int _cols;

    /// <summary>Number of slices in the loaded spritesheet.</summary>
    public int SliceCount { get; private set; }

    /// <summary>
    /// World-space pixels between each slice (positive = stacks upward).
    /// Scales with ScaleY. Default 1.
    /// </summary>
    public float StackOffset = 1f;

    /// <summary>Number of slices to draw from the bottom. -1 = all. Use for construction/destruction reveals.</summary>
    public int VisibleSlices = -1;

    /// <summary>Tint and alpha modulation for all slices.</summary>
    public Math.Color Color = Math.Color.White;

    /// <summary>Mirror all slices horizontally.</summary>
    public bool FlipX;

    /// <summary>Mirror all slices vertically.</summary>
    public bool FlipY;

    public SpriteStack(float x = 0, float y = 0) : base(x, y) { }

    /// <summary>
    /// Load a spritesheet where each frame is one horizontal slice.
    /// Frame 0 = bottom, last frame = top. Slice count auto-calculated from texture dimensions.
    /// </summary>
    public SpriteStack LoadGraphic(string path, int sliceWidth, int sliceHeight)
    {
        if (sliceWidth <= 0 || sliceHeight <= 0)
            throw new ArgumentException($"Slice dimensions must be positive (got {sliceWidth}x{sliceHeight})");

        if (!System.IO.Path.IsPathRooted(path))
            path = Eng.Asset(path);

        _texture = Eng.GL.GetOrCreateTexture(path);

        if (sliceWidth > _texture.Width || sliceHeight > _texture.Height)
            throw new ArgumentException(
                $"Slice size ({sliceWidth}x{sliceHeight}) exceeds texture ({_texture.Width}x{_texture.Height})");

        _sliceWidth = sliceWidth;
        _sliceHeight = sliceHeight;
        _cols = _texture.Width / sliceWidth;
        int rows = _texture.Height / sliceHeight;
        SliceCount = _cols * rows;

        BaseWidth = sliceWidth;
        BaseHeight = sliceHeight;
        return this;
    }

    public override void Draw()
    {
        if (_texture == null || SliceCount == 0) return;

        var cam = Eng.Camera;
        float r = Color.R / 255f, g = Color.G / 255f, b = Color.B / 255f, a = Color.A / 255f;
        float w = Width;
        float h = Height;
        float scaledOffset = StackOffset * ScaleY;
        int drawCount = VisibleSlices < 0
            ? SliceCount
            : System.Math.Min(VisibleSlices, SliceCount);

        // Frustum culling — total visual bounding box
        float stackHeight = (drawCount - 1) * scaledOffset;
        var topScreen = cam.Transform(Position.X, Position.Y - stackHeight, ScrollFactor);
        float screenW = w * cam.Zoom;
        float screenTotalH = (h + stackHeight) * cam.Zoom;
        if (topScreen.X + screenW < 0 || topScreen.X > Eng.Width ||
            topScreen.Y + screenTotalH < 0 || topScreen.Y > Eng.Height)
            return;

        // Draw slices bottom (0) to top (drawCount-1)
        for (int i = 0; i < drawCount; i++)
        {
            int col = i % _cols;
            int row = i / _cols;
            float srcX = col * _sliceWidth;
            float srcY = row * _sliceHeight;

            Eng.GL.DrawTextureWorld(
                _texture,
                srcX, srcY, _sliceWidth, _sliceHeight,
                Position.X, Position.Y - i * scaledOffset, w, h,
                ScrollFactor, r, g, b, a,
                Angle, FlipX, FlipY);
        }
    }

    protected override void OnDestroy()
    {
        _texture = null; // Don't dispose — GLRenderer owns cached textures
    }
}
