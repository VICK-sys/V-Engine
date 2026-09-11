using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Input;

namespace VEngine.Tests;

public class FixedInputTests
{
    private readonly Game _game;
    private const SDL.SDL_Scancode Key = SDL.SDL_Scancode.SDL_SCANCODE_SPACE;

    public FixedInputTests()
    {
        Eng.InitHeadless();
        _game = new Game("Input test", 800, 480);
        Eng.Context = new EngineContext { Game = _game };
        Eng.Actions.Bind("jump", Key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void KeyboardPress_ReachesSceneAndHooksOnceAfterRenderFrames(int framesWithoutUpdate)
    {
        var scene = new InputScene();
        var preUpdate = new List<InputState>();
        var postUpdate = new List<InputState>();
        _game.PushScene(scene);
        Eng.OnPreUpdate.Subscribe(_ => preUpdate.Add(ReadInput()));
        Eng.OnPostUpdate.Subscribe(_ => postUpdate.Add(ReadInput()));
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN);
        Assert.True(Eng.Actions.Pressed("jump"));

        for (int i = 0; i < framesWithoutUpdate; i++)
        {
            Eng.Input.BeginFrame();
            Assert.False(Eng.Actions.Pressed("jump"));
        }

        _game.FixedUpdate();
        _game.FixedUpdate();
        Eng.Input.BeginFrame();
        _game.FixedUpdate();

        var expected = new[]
        {
            new InputState(true, false, true),
            new InputState(false, false, true),
            new InputState(false, false, true)
        };
        Assert.Equal(expected, scene.Inputs);
        Assert.Equal(expected, preUpdate);
        Assert.Equal(expected, postUpdate);
    }

    [Fact]
    public void KeyboardTap_BuffersPressAndReleaseWithoutHoldingKey()
    {
        var scene = new InputScene();
        _game.PushScene(scene);
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN);
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYUP);
        Eng.Input.BeginFrame();

        Assert.False(Eng.Actions.Down("jump"));
        Assert.False(Eng.Actions.Pressed("jump"));
        Assert.False(Eng.Actions.Released("jump"));

        _game.FixedUpdate();
        _game.FixedUpdate();

        Assert.Equal(new[]
        {
            new InputState(true, true, false),
            new InputState(false, false, false)
        }, scene.Inputs);
    }

    [Fact]
    public void KeyboardConsumption_PreservesRenderFrameEdges()
    {
        var scene = new InputScene();
        _game.PushScene(scene);
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN);

        _game.FixedUpdate();
        Assert.True(Eng.Actions.Pressed("jump"));
        _game.FixedUpdate();
        Assert.True(Eng.Actions.Pressed("jump"));
        Assert.False(scene.Inputs[1].Pressed);

        Eng.Input.BeginFrame();
        Assert.False(Eng.Actions.Pressed("jump"));
        SendKey(SDL.SDL_EventType.SDL_KEYUP);
        _game.FixedUpdate();
        Assert.True(Eng.Actions.Released("jump"));
        Assert.False(Eng.Actions.Down("jump"));
        Eng.Input.BeginFrame();
        Assert.False(Eng.Actions.Released("jump"));
    }

    [Fact]
    public void KeyboardRepeat_DoesNotCreateAnotherPress()
    {
        var scene = new InputScene();
        _game.PushScene(scene);
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN);
        _game.FixedUpdate();
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN, repeat: 1);
        _game.FixedUpdate();

        Assert.Equal(new InputState(true, false, true), scene.Inputs[0]);
        Assert.Equal(new InputState(false, false, true), scene.Inputs[1]);
    }

    [Fact]
    public void MouseClick_ReachesOnlyOneFixedUpdate()
    {
        var presses = new List<bool>();
        Eng.OnPreUpdate.Subscribe(_ => presses.Add(Eng.Mouse.IsPressed(MouseButton.Left)));
        Eng.Mouse.BeginFrame();
        Eng.Mouse.SimulatePress(MouseButton.Left);

        _game.FixedUpdate();
        _game.FixedUpdate();
        Eng.Mouse.BeginFrame();
        _game.FixedUpdate();

        Assert.Equal(new[] { true, false, false }, presses);
        Assert.True(Eng.Mouse.IsDown(MouseButton.Left));
    }

    [Fact]
    public void ThrowingUpdate_RestoresRenderInputAndConsumesEdges()
    {
        Eng.Input.BeginFrame();
        SendKey(SDL.SDL_EventType.SDL_KEYDOWN);
        Eng.Mouse.SimulatePress(MouseButton.Left);
        var unsubscribe = Eng.OnPreUpdate.Subscribe(_ => throw new InvalidOperationException("Update probe"));

        var error = Assert.Throws<InvalidOperationException>(() => _game.FixedUpdate());

        Assert.Equal("Update probe", error.Message);
        Assert.True(Eng.Input.IsPressed(Key));
        Assert.False(Eng.Mouse.IsPressed(MouseButton.Left));
        unsubscribe();
        var scene = new InputScene();
        _game.PushScene(scene);
        _game.FixedUpdate();

        Assert.Equal(new InputState(false, false, true), Assert.Single(scene.Inputs));
    }

    private static void SendKey(SDL.SDL_EventType type, byte repeat = 0)
    {
        Eng.Input.ProcessEvent(new SDL.SDL_Event
        {
            key = new SDL.SDL_KeyboardEvent
            {
                type = type,
                repeat = repeat,
                keysym = new SDL.SDL_Keysym { scancode = Key }
            }
        });
    }

    private static InputState ReadInput() => new(
        Eng.Actions.Pressed("jump"),
        Eng.Actions.Released("jump"),
        Eng.Actions.Down("jump"));

    private readonly record struct InputState(bool Pressed, bool Released, bool Down);

    private sealed class InputScene : Scene
    {
        public List<InputState> Inputs { get; } = new();

        public override void Update(float dt) => Inputs.Add(ReadInput());
    }
}
