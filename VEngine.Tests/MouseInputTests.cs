using VEngine.Engine.Core;
using VEngine.Engine.Input;

namespace VEngine.Tests;

public class MouseInputTests
{
    public MouseInputTests() => Eng.InitHeadless();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumedEdge_DoesNotRepeatInSameFrameOrNextFrame(bool release)
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);
        if (release)
            mouse.SimulateRelease(MouseButton.Left);

        Assert.True(release ? mouse.IsReleased(MouseButton.Left) : mouse.IsPressed(MouseButton.Left));

        mouse.ConsumeBuffered();

        Assert.False(mouse.IsPressed(MouseButton.Left));
        Assert.False(mouse.IsReleased(MouseButton.Left));
        Assert.Equal(!release, mouse.IsDown(MouseButton.Left));

        mouse.BeginFrame();

        Assert.False(mouse.IsPressed(MouseButton.Left));
        Assert.False(mouse.IsReleased(MouseButton.Left));
        Assert.Equal(!release, mouse.IsDown(MouseButton.Left));
    }

    [Fact]
    public void NewPress_AfterConsumption_IsDetected()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);
        mouse.ConsumeBuffered();
        mouse.BeginFrame();
        mouse.SimulateRelease(MouseButton.Left);
        mouse.ConsumeBuffered();
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);

        Assert.True(mouse.IsPressed(MouseButton.Left));
        Assert.True(mouse.IsDown(MouseButton.Left));
        Assert.False(mouse.IsReleased(MouseButton.Left));
    }

    [Fact]
    public void Press_IsDetected()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();

        mouse.SimulatePress(MouseButton.Left);

        Assert.True(mouse.IsPressed(MouseButton.Left));
        Assert.True(mouse.IsDown(MouseButton.Left));
        Assert.False(mouse.IsReleased(MouseButton.Left));
    }

    [Fact]
    public void Release_IsDetected()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();

        mouse.SimulatePress(MouseButton.Left);
        mouse.BeginFrame(); // next frame
        mouse.SimulateRelease(MouseButton.Left);

        Assert.True(mouse.IsReleased(MouseButton.Left));
        Assert.False(mouse.IsDown(MouseButton.Left));
    }

    [Fact]
    public void Press_ClearedAfterBeginFrame()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);

        // Next frame — pressed should move to buffer
        mouse.BeginFrame();

        // Still visible via buffer (not consumed yet)
        Assert.True(mouse.IsPressed(MouseButton.Left));

        // Consume clears the buffer
        mouse.ConsumeBuffered();
        Assert.False(mouse.IsPressed(MouseButton.Left));
    }

    [Fact]
    public void Buffer_SurvivesUntilConsumed()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);

        // Simulate: render frame clears, but no fixed update runs
        mouse.BeginFrame();
        Assert.True(mouse.IsPressed(MouseButton.Left), "Buffer should keep the press");

        // Another render frame — still buffered
        mouse.BeginFrame();
        Assert.True(mouse.IsPressed(MouseButton.Left), "Buffer should still be alive");

        // Fixed update consumes it
        mouse.ConsumeBuffered();
        Assert.False(mouse.IsPressed(MouseButton.Left), "Should be cleared after consume");
    }

    [Fact]
    public void LeftButton_IsSDLButton1_NotZero()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();

        // MouseButton.Left = 1, this is what SDL uses
        mouse.SimulatePress(MouseButton.Left);

        // Verify Left (1) works
        Assert.True(mouse.IsPressed(MouseButton.Left));
        Assert.True(mouse.IsDown(MouseButton.Left));
    }

    [Fact]
    public void AllButtons_Independent()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();

        mouse.SimulatePress(MouseButton.Left);
        mouse.SimulatePress(MouseButton.Right);

        Assert.True(mouse.IsPressed(MouseButton.Left));
        Assert.True(mouse.IsPressed(MouseButton.Right));
        Assert.False(mouse.IsPressed(MouseButton.Middle));
    }

    [Fact]
    public void Release_Buffer_SurvivesUntilConsumed()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Right);
        mouse.BeginFrame();
        mouse.SimulateRelease(MouseButton.Right);

        // Next render frame — release should be buffered
        mouse.BeginFrame();
        Assert.True(mouse.IsReleased(MouseButton.Right), "Release buffer should survive");

        mouse.ConsumeBuffered();
        Assert.False(mouse.IsReleased(MouseButton.Right), "Should be cleared after consume");
    }

    [Fact]
    public void Down_PersistsAcrossFrames()
    {
        var mouse = Eng.Mouse;
        mouse.BeginFrame();
        mouse.SimulatePress(MouseButton.Left);

        // Down should persist across frames (not a single-frame event)
        mouse.BeginFrame();
        Assert.True(mouse.IsDown(MouseButton.Left));

        mouse.BeginFrame();
        Assert.True(mouse.IsDown(MouseButton.Left));

        // Until released
        mouse.SimulateRelease(MouseButton.Left);
        Assert.False(mouse.IsDown(MouseButton.Left));
    }

    [Fact]
    public void LuaMapping_ZeroMapsToLeft()
    {
        // Lua calls mouse_pressed(0) which the binding maps to (MouseButton)(0 + 1) = Left
        var luaBtn = 0;
        var mapped = (MouseButton)(luaBtn + 1);
        Assert.Equal(MouseButton.Left, mapped);

        var luaBtn2 = 2;
        var mapped2 = (MouseButton)(luaBtn2 + 1);
        Assert.Equal(MouseButton.Right, mapped2);
    }
}
