using System.Runtime.InteropServices;
using SDL2;
using VEngine.Engine.Audio;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using VEngine.Engine.Physics.Fluid;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Tests;

public sealed class NativeFactAttribute : FactAttribute
{
    public NativeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VENGINE_REQUIRE_NATIVE") != "1" &&
            NativeRuntime.Inspect().Any(pair => !pair.Value))
            Skip = "Build the complete native runtime to run native integration tests.";
    }
}

[Trait("Category", "Native")]
public class NativeIntegrationTests
{
    public NativeIntegrationTests()
    {
        Assert.All(NativeRuntime.Inspect(), pair => Assert.True(pair.Value, pair.Key));
        Eng.InitHeadless();
    }

    [NativeFact]
    public void RuntimeContainsAllEngineAndSdlLibraries()
    {
        Assert.Equal(13, NativeRuntime.Inspect().Count);
        Assert.Throws<ArgumentException>(() => NativeRuntime.IsAvailable("missing"));
    }

    [NativeFact]
    public void SortMatchesManagedOrderingAcrossGrowthRemovalAndNan()
    {
        var native = new Group();
        var managed = new Group { UseNativeSorting = false };
        var random = new Random(418);
        Assert.True(native.UsingNativeSorting);
        Assert.False(managed.UsingNativeSorting);
        for (int step = 0; step < 3; step++)
        {
            for (int i = 0; i < 40; i++)
            {
                var entity = new Entity { Layer = random.Next(-2, 3), ZOrder = i % 7 == 0 ? float.NaN : random.Next(-3, 4), Visible = false };
                native.Add(entity);
                managed.Add(entity);
            }
            native.Draw();
            managed.Draw();
            Assert.Equal(managed.Members, native.Members);
            var removed = native.Members[7];
            native.Remove(removed, false);
            managed.Remove(removed, false);
        }
        native.Destroy();
        managed.Destroy();
    }

    [NativeFact]
    public void PhysicsMatchesManagedMotionContactsAndFiltering()
    {
        static (PhysicsWorld World, RigidBody Body, int[] Events) Create(bool native)
        {
            var world = new PhysicsWorld { UseNativeSolver = native };
            world.Settings.Gravity = new Vec2(0, 200);
            var floor = world.CreateBody(BodyType.Static, 0, 100);
            floor.SetShape(new BoxShape(400, 20));
            var ball = world.CreateBody(BodyType.Dynamic, 0, 0);
            ball.SetShape(new CircleShape(10));
            ball.LinearVelocity = new Vec2(12, 0);
            var events = new int[1];
            world.OnCollisionEnter.Subscribe(_ => events[0]++);
            return (world, ball, events);
        }
        var native = Create(true);
        var managed = Create(false);
        Assert.True(native.World.UsingNativeSolver);
        Assert.False(managed.World.UsingNativeSolver);
        for (int i = 0; i < 180; i++)
        {
            native.World.Step(1f / 60);
            managed.World.Step(1f / 60);
            Assert.InRange(Vec2.Distance(native.Body.Position, managed.Body.Position), 0, 0.001f);
            Assert.InRange(Vec2.Distance(native.Body.LinearVelocity, managed.Body.LinearVelocity), 0, 0.001f);
            Assert.Equal(managed.World.ContactCount, native.World.ContactCount);
        }
        Assert.True(native.Events[0] > 0);
        Assert.Equal(managed.Events[0], native.Events[0]);
        Assert.InRange(native.Body.Position.Y, 70, 90);
        native.Body.Filter = new CollisionFilter { CategoryBits = 1, MaskBits = 0 };
        native.Body.SetAwake();
        native.World.Step(1f / 60);
        Assert.Equal(0, native.World.ContactCount);
    }

    [NativeFact]
    public void PathfinderHonorsWallsUpdatesAndSearchLimits()
    {
        using var pathfinder = new Pathfinder(5, 3);
        pathfinder.SetWalkable(2, 0, false);
        pathfinder.SetWalkable(2, 1, false);
        var path = pathfinder.FindPath(0, 0, 4, 0);
        Assert.Equal((0, 0), path[0]);
        Assert.Equal((4, 0), path[^1]);
        Assert.Contains((2, 2), path);
        Assert.All(path, point => Assert.True(pathfinder.IsWalkable(point.X, point.Y)));
        Assert.Empty(pathfinder.FindPath(0, 0, 4, 0, maxSearch: 1));
        pathfinder.SetWalkable(2, 2, false);
        Assert.Empty(pathfinder.FindPath(0, 0, 4, 0));
        pathfinder.UpdateGrid(Enumerable.Repeat(true, 15).ToArray());
        Assert.Equal(5, pathfinder.FindPath(0, 0, 4, 0).Length);
        pathfinder.SetWalkable(0, 0, false);
        Assert.Empty(pathfinder.FindPath(0, 0, 4, 0));
    }

    [NativeFact]
    public void PathfinderPreventsDiagonalCornerCuttingAndUseAfterDispose()
    {
        var pathfinder = new Pathfinder(2, 2, [true, false, false, true]);
        Assert.Empty(pathfinder.FindPath(0, 0, 1, 1, true));
        Assert.Single(pathfinder.FindPath(0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pathfinder.UpdateGrid([true]));
        pathfinder.Dispose();
        pathfinder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pathfinder.FindPath(0, 0, 1, 1));
    }

    [NativeFact]
    public void TileGridMatchesBruteForceAabbQueries()
    {
        using var grid = new TileCollisionGrid(8, 5, 16);
        var cells = new List<(int X, int Y)> { (0, 0), (3, 2), (7, 4) };
        foreach (var cell in cells) grid.SetSolid(cell.X, cell.Y, true);
        var random = new Random(12);
        for (int i = 0; i < 100; i++)
        {
            float x = random.Next(-40, 160), y = random.Next(-30, 100);
            float width = random.Next(1, 80), height = random.Next(1, 80);
            var expected = cells.Where(cell => x < (cell.X + 1) * 16 && x + width > cell.X * 16 &&
                y < (cell.Y + 1) * 16 && y + height > cell.Y * 16).ToArray();
            Assert.Equal(expected, grid.Query(x, y, width, height));
            Assert.Equal(expected.Length > 0, grid.Overlaps(x, y, width, height));
        }
        Assert.True(grid.Overlaps(0, 0, 0.0001f, 0.0001f));
        Assert.False(grid.Overlaps(16, 0, 16, 16));
        grid.SetSolid(0, 0, false);
        Assert.False(grid.Overlaps(0, 0, 16, 16));
    }

    [NativeFact]
    public void TileRaysEnterFromOutsideAndRespectDistanceAndEdits()
    {
        using var grid = new TileCollisionGrid(4, 4, 16);
        grid.SetSolid(2, 1, true);
        Assert.Equal(64f, grid.Raycast(-32, 24, 1, 0, 100, out int x, out int y));
        Assert.Equal((2, 1), (x, y));
        Assert.Equal(-1f, grid.Raycast(-32, 24, 1, 0, 63, out _, out _));
        Assert.Equal(-1f, grid.Raycast(-32, 24, -1, 0, 100, out _, out _));
        Assert.False(grid.HasLineOfSight(-32, 24, 60, 24));
        grid.SetSolid(2, 1, false);
        Assert.True(grid.HasLineOfSight(-32, 24, 60, 24));
        grid.SetSolid(3, 1, true);
        Assert.Equal(-1f, grid.Raycast(64, 24, 1, 0, 100, out _, out _));
        Assert.Equal(0f, grid.Raycast(64, 24, -1, 0, 100, out _, out _));
        Assert.False(grid.HasLineOfSight(56, 24, 56, 24));
    }

    [NativeFact]
    public void ImageOperationsPreserveAlphaAndUseRgbaBuffers()
    {
        byte[] pixels = [255, 128, 0, 64, 0, 0, 255, 255];
        ImageProcessing.Invert(pixels, 2, 1);
        Assert.Equal(new byte[] { 0, 127, 255, 64, 255, 255, 0, 255 }, pixels);
        ImageProcessing.Tint(pixels, 2, 1, 255, 0, 255);
        Assert.Equal(new byte[] { 0, 0, 255, 64, 255, 0, 0, 255 }, pixels);
        ImageProcessing.PaletteSwap(pixels, 2, 1, [0, 0, 255, 0], [4, 5, 6, 7]);
        Assert.Equal(new byte[] { 4, 5, 6, 7, 255, 0, 0, 255 }, pixels);
        ImageProcessing.Grayscale(pixels, 2, 1);
        Assert.Equal(pixels[0], pixels[1]);
        Assert.Equal(pixels[1], pixels[2]);
        Assert.Equal(7, pixels[3]);
        Assert.Throws<ArgumentException>(() => ImageProcessing.Invert([0], 1, 1));
    }

    [NativeFact]
    public void ImageBlurAndOutlineHaveObservableControls()
    {
        byte[] pixels = new byte[3 * 3 * 4];
        pixels[16] = pixels[19] = 255;
        Assert.Equal(pixels, ImageProcessing.Blur(pixels, 3, 3, 0));
        var blur = ImageProcessing.Blur(pixels, 3, 3, 1);
        Assert.Equal(28, blur[16]);
        Assert.Equal(63, blur[0]);
        var outline = ImageProcessing.Outline(pixels, 3, 3, 1, 0, 255, 0);
        Assert.Equal(0, outline[3]);
        Assert.Equal(255, outline[7]);
        Assert.Equal(255, outline[16]);
        Assert.Equal(0, pixels[7]);
    }

    [NativeFact]
    public void MixerOwnsSamplesPansLoopsAndFillsLargeBuffers()
    {
        using var mixer = new PcmMixer();
        short[] source = [10000, -10000];
        int voice = mixer.Play(source, 1, pan: -1, loop: true);
        Array.Clear(source);
        short[] output = new short[20000];
        mixer.Mix(output);
        Assert.InRange(output[0], 9998, 10000);
        Assert.Equal(0, output[1]);
        Assert.InRange(output[^2], -10000, -9998);
        Assert.True(mixer.IsPlaying(voice));
        mixer.StopAll();
        mixer.Mix(output);
        Assert.All(output, value => Assert.Equal(0, value));
        Assert.False(mixer.IsPlaying(voice));
    }

    [NativeFact]
    public void MixerFiltersAndFinishesOnExactBufferBoundary()
    {
        using var mixer = new PcmMixer();
        short[] source = [10000, 10000, 10000, 10000];
        int voice = mixer.Play(source, 1, pan: 1);
        mixer.SetSpatial(voice, 1, 1, 1);
        short[] output = new short[8];
        mixer.Mix(output);
        Assert.InRange(output[1], 499, 501);
        Assert.True(output[3] > output[1]);
        Assert.False(mixer.IsPlaying(voice));
        mixer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => mixer.Mix(output));
    }

    [NativeFact]
    public void SdlAudioManagerPlaysNativePcmThroughAudioCallback()
    {
        SDL.SDL_SetHint("SDL_AUDIODRIVER", "dummy");
        Assert.Equal(0, SDL.SDL_InitSubSystem(SDL.SDL_INIT_AUDIO));
        var manager = new AudioManager();
        try
        {
            Assert.True(manager.HasNativeMixer);
            int voice = manager.PlayPcm(Enumerable.Repeat((short)1000, 256).ToArray(), 1);
            Assert.True(SpinWait.SpinUntil(() => !manager.IsPcmPlaying(voice), TimeSpan.FromSeconds(3)));
        }
        finally { manager.Shutdown(); SDL.SDL_QuitSubSystem(SDL.SDL_INIT_AUDIO); }
    }

    [NativeFact]
    public void GifDecodesBothFramesAndRejectsInvalidFiles()
    {
        Assert.Equal(IntPtr.Zero, NativeGifDecoder.gif_open("missing.gif"));
        IntPtr handle = NativeGifDecoder.gif_open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native.gif"));
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.Equal(2, NativeGifDecoder.gif_frame_count(handle));
            Assert.Equal(16, NativeGifDecoder.gif_width(handle));
            Assert.Equal(16, NativeGifDecoder.gif_height(handle));
            Assert.Equal(500, NativeGifDecoder.gif_frame_delay(handle, 0));
            byte[] first = new byte[1024], second = new byte[1024];
            Marshal.Copy(NativeGifDecoder.gif_frame_data(handle, 0), first, 0, first.Length);
            Marshal.Copy(NativeGifDecoder.gif_frame_data(handle, 1), second, 0, second.Length);
            Assert.False(first.SequenceEqual(second));
        }
        finally { NativeGifDecoder.gif_close(handle); }
    }

    [NativeFact]
    public void VideoDrainsDelayedFramesAndRewindsAudio()
    {
        Assert.Equal(IntPtr.Zero, NativeVideoPlayer.video_open("missing.mp4"));
        IntPtr handle = NativeVideoPlayer.video_open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native.mp4"));
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.Equal(16, NativeVideoPlayer.video_width(handle));
            Assert.Equal(16, NativeVideoPlayer.video_height(handle));
            Assert.Equal(10, NativeVideoPlayer.video_fps(handle));
            Assert.Equal(1, NativeVideoPlayer.video_has_audio(handle));
            for (int pass = 0; pass < 2; pass++)
            {
                int frames = 0, audio = 0;
                short[] samples = new short[88200];
                while (NativeVideoPlayer.video_next_frame(handle) != 0)
                {
                    Assert.NotEqual(IntPtr.Zero, NativeVideoPlayer.video_frame_data(handle));
                    frames++;
                    audio += NativeVideoPlayer.video_get_audio(handle, samples, 44100);
                }
                Assert.Equal(10, frames);
                Assert.True(audio > 30000);
                NativeVideoPlayer.video_rewind(handle);
                Assert.Equal(0, NativeVideoPlayer.video_finished(handle));
            }
        }
        finally { NativeVideoPlayer.video_close(handle); }
    }

    [NativeFact]
    public void FluidUsesNativeSolverAndMatchesGravityControl()
    {
        var native = new FluidSystem(2, 16, 1) { SubSteps = 1, MergeInterval = int.MaxValue, BoundsRight = 1000, BoundsBottom = 1000 };
        var managed = new FluidSystem(2, 16, 1) { UseNativeSolver = false, SubSteps = 1, MergeInterval = int.MaxValue, BoundsRight = 1000, BoundsBottom = 1000 };
        try
        {
            Assert.True(native.UsingNativeSolver);
            Assert.False(managed.UsingNativeSolver);
            native.Emit(new Vec2(100, 100), Vec2.Zero);
            managed.Emit(new Vec2(100, 100), Vec2.Zero);
            for (int i = 0; i < 10; i++)
            {
                native.Update(1f / 60);
                managed.Update(1f / 60);
                Assert.InRange(Vec2.Distance(native.Particles[0].Position, managed.Particles[0].Position), 0, 0.01f);
            }
            Assert.True(native.Particles[0].Position.Y > 100);
            native.Emit(new Vec2(300, 100), Vec2.Zero);
            native.Emit(new Vec2(500, 100), Vec2.Zero);
            native.Update(1f / 60);
            Assert.Equal(3, native.ActiveCount);
            Assert.True(native.Capacity > 2);
        }
        finally { native.Destroy(); managed.Destroy(); }
    }

    [NativeFact]
    public void NoiseBulkOutputMatchesScalarAndSeedControl()
    {
        Assert.True(Noise.IsAvailable);
        Noise.Seed(42);
        float[] samples = new float[12];
        Noise.PerlinFill(samples, 4, 3, 0.5f, 0.25f, 8);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(Noise.Perlin2D((x + 0.5f) / 8, (y + 0.25f) / 8), samples[y * 4 + x], 5);
        float control = Noise.Simplex3D(0.21f, 0.38f, 0.72f);
        Noise.Seed(51);
        Assert.NotEqual(control, Noise.Simplex3D(0.21f, 0.38f, 0.72f));
        Noise.Seed(42);
        Assert.Equal(control, Noise.Simplex3D(0.21f, 0.38f, 0.72f));
        Noise.WorleyFill(samples, 4, 3, 0.5f, 0.25f, 8);
        Assert.True(samples.Distinct().Count() > 1);
        Assert.Throws<ArgumentException>(() => Noise.PerlinFill(new float[1], 4, 3, 0, 0, 1));
    }

    [NativeFact]
    public void MediaDecodersAcceptUnicodePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"vengine-{Guid.NewGuid():N}-\u96ea");
        string gif = root + ".gif", video = root + ".mp4";
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native.gif"), gif);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native.mp4"), video);
            IntPtr gifHandle = NativeGifDecoder.gif_open(gif);
            IntPtr videoHandle = NativeVideoPlayer.video_open(video);
            try
            {
                Assert.NotEqual(IntPtr.Zero, gifHandle);
                Assert.NotEqual(IntPtr.Zero, videoHandle);
                Assert.Equal(1, NativeVideoPlayer.video_next_frame(videoHandle));
            }
            finally { NativeGifDecoder.gif_close(gifHandle); NativeVideoPlayer.video_close(videoHandle); }
        }
        finally { File.Delete(gif); File.Delete(video); }
    }
}
