using SDL2;
using VEngine.Engine.Audio;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using VEngine.Engine.Physics.Shapes;

foreach (var library in NativeRuntime.Inspect())
{
    if (!library.Value) throw new Exception($"Missing runtime library: {library.Key}");
    Console.WriteLine($"Loaded {library.Key}");
}
using (var pathfinder = new Pathfinder(3, 1))
{
    if (pathfinder.FindPath(0, 0, 2, 0).Length != 3) throw new Exception("Pathfinding failed.");
}
byte[] pixel = [1, 2, 3, 255];
ImageProcessing.Invert(pixel, 1, 1);
if (pixel[0] != 254 || pixel[3] != 255) throw new Exception("Image processing failed.");
using (var mixer = new PcmMixer())
{
    mixer.Play([10000], 1, pan: -1);
    short[] output = new short[2];
    mixer.Mix(output);
    if (output[0] < 9998 || output[1] != 0) throw new Exception("PCM mixing failed.");
}
var world = new PhysicsWorld();
if (!world.UsingNativeSolver) throw new Exception("Physics used the managed fallback.");
world.Settings.Gravity = new Vec2(0, 100);
world.CreateBody(BodyType.Static, 0, 20).SetShape(new BoxShape(100, 10));
var body = world.CreateBody(BodyType.Dynamic);
body.SetShape(new CircleShape(5));
for (int i = 0; i < 120; i++) world.Step(1f / 60);
if (body.Position.Y > 15 || body.Position.Y < 5) throw new Exception("Physics contact solving failed.");
if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) != 0) throw new Exception(SDL.SDL_GetError());
try
{
    if (SDL_ttf.TTF_Init() != 0) throw new Exception(SDL.SDL_GetError());
    SDL_ttf.TTF_Quit();
    if (SDL_mixer.Mix_OpenAudio(44100, SDL.AUDIO_S16LSB, 2, 1024) != 0) throw new Exception(SDL.SDL_GetError());
    SDL_mixer.Mix_CloseAudio();
}
finally { SDL.SDL_Quit(); }

if (args.Contains("--graphics")) VerifyGraphics();
Console.WriteLine("Runtime probe passed.");

static unsafe void VerifyGraphics()
{
    if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) != 0) throw new Exception(SDL.SDL_GetError());
    SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
    SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
    IntPtr window = SDL.SDL_CreateWindow("Runtime verification", 0, 0, 64, 64,
        SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL);
    if (window == IntPtr.Zero) throw new Exception(SDL.SDL_GetError());
    IntPtr context = SDL.SDL_GL_CreateContext(window);
    if (context == IntPtr.Zero) throw new Exception(SDL.SDL_GetError());
    try
    {
        using var gl = Silk.NET.OpenGL.GL.GetApi(SDL.SDL_GL_GetProcAddress);
        using var texture = GLTexture.FromRGBA(gl, 1, 1, new byte[] { 255, 0, 0, 255 });
        gl.BindTexture(Silk.NET.OpenGL.TextureTarget.Texture2D, texture.Handle);
        byte* data = stackalloc byte[4];
        gl.GetTexImage(Silk.NET.OpenGL.TextureTarget.Texture2D, 0, Silk.NET.OpenGL.PixelFormat.Rgba,
            Silk.NET.OpenGL.PixelType.UnsignedByte, data);
        if (data[0] != 255 || data[1] != 0 || data[2] != 0 || data[3] != 255)
            throw new Exception("OpenGL texture upload failed.");
        Console.WriteLine("OpenGL texture upload passed.");
    }
    finally
    {
        SDL.SDL_GL_DeleteContext(context);
        SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
    }
}
