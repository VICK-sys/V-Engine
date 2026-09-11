using VEngine.Engine.Audio;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;

namespace VEngine.Tests;

public class NativeFallbackTests
{
    [Fact]
    public void OptionalBackendsAndRequiredApisRespectRuntimeAvailability()
    {
        var world = new PhysicsWorld();
        var group = new Group();
        Assert.Equal(NativeRuntime.IsAvailable("physics_solver"), world.UsingNativeSolver);
        Assert.Equal(NativeRuntime.IsAvailable("batch_sorter"), group.UsingNativeSorting);
        world.UseNativeSolver = false;
        group.UseNativeSorting = false;
        Assert.False(world.UsingNativeSolver);
        Assert.False(group.UsingNativeSorting);
        if (!NativeRuntime.IsAvailable("pathfinder"))
            Assert.Throws<DllNotFoundException>(() => new Pathfinder(2, 2));
        if (!NativeRuntime.IsAvailable("image_process"))
            Assert.Throws<DllNotFoundException>(() => ImageProcessing.Invert(new byte[4], 1, 1));
        if (!NativeRuntime.IsAvailable("audio_mixer"))
            Assert.Throws<DllNotFoundException>(() => new PcmMixer());
        if (!NativeRuntime.IsAvailable("noise"))
        {
            Noise.Seed(12);
            Assert.True(float.IsFinite(Noise.Perlin2D(0.25f, 0.75f)));
            Assert.Throws<DllNotFoundException>(() => Noise.Worley2D(0.25f, 0.75f));
        }
        group.Destroy();
    }
}
