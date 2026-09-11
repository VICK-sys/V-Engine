# V-Engine Testing

> Test structure, running tests, and conventions.

## VEngine.Tests (xUnit)

The test project references only the engine library.
The suite runs without external media services.

- `Eng.InitHeadless()` initializes engine subsystems without a renderer or window
- `InternalsVisibleTo("VEngine.Tests")` on VEngine.Engine for access to internals
- **CoreTests.cs** - timers (After, Every, Cancel, Pause/Resume), tweens (float/Vec2/Color, delay, cancel, easing), sequences (Call, Wait, TweenFloat, Together, Cancel, restart), state machine (transitions, duration), signals (subscribe, emit, unsubscribe during emit), groups (add/remove, draw order), pool (get/release, lifecycle), effects (time scale, flash)
- **MathTests.cs** - Vec2 (add, subtract, scale, negate, length, normalized, dot, distance, lerp, angle), Color (lerp, withAlpha, presets), Rect (overlaps, contains, center, edges)
- **MoreCoreTests.cs** - SpatialHash (insert, query, negative coords, clear, large entities), collision (separate, sweep CCD, circle overlap/separate), behavior (attach, update, destroy), entity lifecycle (active hooks, destroyed guard)
- **AdvancedTests.cs** - sprite animation (frame advance, ping-pong, hitbox/origin), sprite effects (flash timer, silhouette, outline defaults), sprite stack (defaults, kinematics, scale), asset loader (enqueue/progress/reset), chunked tilemap (set/get tiles, negative coords, world-to-tile, chunk creation), input map, trail renderer (ring buffer, follow, aging, auto-unfollow), screen capture (PNG round-trip, gradient, single pixel, directory creation)
- **IntegrationTests.cs** - multi-system integration scenarios, transitions (circle wipe, diamond wipe, pixelate)
- **StressTests.cs** - 10K entity management, mass removal during iteration, CancelAll-from-callback safety
- **FuzzTests.cs** - NaN, Infinity, extreme values, adversarial inputs across all subsystems
- **TextTests.cs** - text rendering, font cache, glyph atlas
- **MouseInputTests.cs** - press/release detection, buffering across render frames for fixed timestep, Lua button mapping (0=left→SDL 1), button independence, down persistence
- **FixedInputTests.cs** - keyboard buffering, fixed updates, lifecycle hooks, render-frame state, and exception cleanup.
- **GroupUpdateTests.cs** - removal during updates, deferred additions, inactive members, and exception handling.
- **PhysicsTests.cs** - physics world, rigid body, collision, joints
- **FluidTests.cs** - fluid system, SPH kernels, spatial hash, pouring, particle budget
- **TilemapTests.cs** - tilemap rendering and collision
- **WaterTests.cs** - water body and fluid interaction tests
- Run with: `dotnet test VEngine.Tests`
