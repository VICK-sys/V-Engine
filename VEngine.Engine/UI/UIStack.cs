using System.Collections.Generic;
using VEngine.Engine.Core;

namespace VEngine.Engine.UI;

/// <summary>
/// Layout direction for UIStack.
/// </summary>
public enum StackDirection { Vertical, Horizontal }

/// <summary>
/// Stacks child UI elements vertically or horizontally with configurable spacing.
/// Children are positioned automatically relative to the stack's position.
/// Add to a scene like any entity.
///
/// Usage:
///   var menu = new UIStack(100, 50, StackDirection.Vertical, spacing: 8);
///   menu.AddChild(new UIButton("Play", 0, 0, 120, 30));
///   menu.AddChild(new UIButton("Options", 0, 0, 120, 30));
///   menu.AddChild(new UIButton("Quit", 0, 0, 120, 30));
///   scene.Add(menu);
/// </summary>
public class UIStack : UIElement
{
    private readonly List<UIElement> _children = new();
    private readonly StackDirection _direction;

    /// <summary>Space between children in pixels.</summary>
    public float Spacing { get; set; }

    /// <summary>Padding inside the stack before the first child.</summary>
    public float Padding { get; set; }

    public UIStack(float x, float y, StackDirection direction = StackDirection.Vertical,
        float spacing = 4, float padding = 0) : base(x, y)
    {
        _direction = direction;
        Spacing = spacing;
        Padding = padding;
    }

    /// <summary>Add a child element. Its position will be managed by the stack layout.</summary>
    public UIStack AddChild(UIElement child)
    {
        _children.Add(child);
        return this;
    }

    /// <summary>Remove a child element.</summary>
    public void RemoveChild(UIElement child) => _children.Remove(child);

    /// <summary>All children in layout order.</summary>
    public IReadOnlyList<UIElement> Children => _children;

    public override void Update(float dt)
    {
        float offset = Padding;
        foreach (var child in _children)
        {
            if (!child.Active) continue;
            if (_direction == StackDirection.Vertical)
            {
                child.Position.X = Position.X + Padding;
                child.Position.Y = Position.Y + offset;
                offset += child.Height + Spacing;
            }
            else
            {
                child.Position.X = Position.X + offset;
                child.Position.Y = Position.Y + Padding;
                offset += child.BaseWidth + Spacing;
            }
            child.Update(dt);
        }

        // Update own dimensions to fit children
        if (_direction == StackDirection.Vertical)
        {
            BaseWidth = 0;
            foreach (var c in _children)
                if (c.Active && c.BaseWidth > BaseWidth) BaseWidth = c.BaseWidth;
            BaseWidth += Padding * 2;
            BaseHeight = offset - Spacing + Padding;
        }
        else
        {
            BaseHeight = 0;
            foreach (var c in _children)
                if (c.Active && c.BaseHeight > BaseHeight) BaseHeight = c.BaseHeight;
            BaseHeight += Padding * 2;
            BaseWidth = offset - Spacing + Padding;
        }
    }

    public override void Draw()
    {
        foreach (var child in _children)
            if (child.Visible) child.Draw();
    }

    protected override void OnDestroy()
    {
        foreach (var child in _children)
            child.Destroy();
        _children.Clear();
    }
}
