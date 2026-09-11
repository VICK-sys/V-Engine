# Native libraries

The engine includes ten C++ libraries with C# bindings.
The distributed runtime targets Windows x64.
All libraries have public engine APIs or integrated execution paths.

## Build and package

Run `Tools/build-native.ps1` or `vengine.bat native` from the repository root.
The script downloads dependencies from `Native/dependencies.json` and checks each archive hash before extraction.
Downloads remain in `artifacts/downloads/` for reuse.

CMake builds every module through `Native/CMakeLists.txt`.
The script stages DLLs and dependency notices in `artifacts/native/Release/win-x64/`.
The engine project copies this runtime into project-reference build and publish outputs.
The engine NuGet package stores DLLs under `runtimes/win-x64/native/`.

Run `Tools/package.ps1` to build, test, and package the runtime.
Use `-Version 0.1.1` to set another package version.
Use `-SkipNativeBuild` only when the staged runtime is current.
Packaging requires the complete runtime and passing native integration tests.

Run `Tools/verify-package.ps1` to test an isolated package consumer.
Add `-Graphics` to check an OpenGL texture upload in a hidden window.
Verification publishes both folder and single-file applications without relying on the development search path.

## Runtime dependencies

The dependency manifest pins SDL2, SDL2_ttf, SDL2_mixer, and an FFmpeg LGPL shared build.
The build script preserves dependency notices and copies runtime DLLs from these archives.
The engine libraries use the static MSVC runtime.
They do not require AVX2 instructions.

FFmpeg binaries come from a retained monthly BtbN build.
Upstream retains monthly builds for two years.
Keep downloaded archives when rebuilding older package versions.
Update the manifest URL and SHA-256 together when changing dependencies.

Sources and build information:

- [SDL2](https://github.com/libsdl-org/SDL/tree/release-2.32.10)
- [SDL2_ttf](https://github.com/libsdl-org/SDL_ttf/tree/release-2.24.0)
- [SDL2_mixer](https://github.com/libsdl-org/SDL_mixer/tree/release-2.8.1)
- [FFmpeg source revision](https://github.com/FFmpeg/FFmpeg/tree/1a748fe2cd)
- [FFmpeg build recipes](https://github.com/BtbN/FFmpeg-Builds)

Keep dependency notices with distributed applications.
The engine package does not assign a project license.

## Runtime inspection

Call `NativeRuntime.Inspect()` to check all ten engine libraries and three SDL libraries.
Call `NativeRuntime.IsAvailable(name)` to check one library.
Call `NativeRuntime.Require(name)` to fail with a diagnostic when a required library cannot load.
Checks include required entry points and cache their results for the process lifetime.
Install the runtime before starting the application.

## Physics

`PhysicsWorld` uses `physics_solver` for contact preparation, velocity impulses, and position correction.
The world retains managed broad phase, shape detection, joints, filters, contact events, sleeping, and continuous collision detection.
Both solvers run at the same points in the simulation step.

`UseNativeSolver` defaults to true.
`UsingNativeSolver` reports the selected path.
Set `UseNativeSolver=false` to select the managed solver.
Missing native libraries also select the managed solver.

## Group sorting

`Group.Draw()` uses `batch_sorter` when draw order changes.
Sorting preserves layer, depth, and original-index ordering, including NaN depths.
Invisible entities remain in the group and retain their sorted positions.
The group retains its rendering and visibility logic.

`UseNativeSorting` defaults to true.
`UsingNativeSorting` reports the selected path.
Set `UseNativeSorting=false` to use managed sorting.

## Fluid simulation

`FluidSystem` uses `fluid_solver` for particle substeps.
C# retains emission, body interaction, buoyancy, merging, splitting, and rendering.
`UseNativeSolver=false` selects managed particle simulation.
`UsingNativeSolver` reports the selected path.
The WCSPH mode remains managed.

## Pathfinding

`Pathfinder` owns a native grid and implements `IDisposable`.
Its constructor accepts width, height, and an optional row-major walkability array.
Without an array, every cell starts walkable.

- `FindPath()` returns grid coordinates from start to destination.
- `allowDiagonal` enables eight-direction movement without corner cutting.
- `maxSearch=0` permits a full search.
- A blocked endpoint, unreachable destination, or exhausted search returns an empty path.
- `SetWalkable()` changes one cell.
- `UpdateGrid()` replaces the walkability data.

`Tilemap.CreatePathfinder()` creates a snapshot where solid tiles are blocked.
Update the pathfinder after changing the tilemap.

## Tile collision

`TileCollisionGrid` owns a native solid grid and implements `IDisposable`.
Coordinates use pixels relative to the grid origin.

- `SetSolid()` changes one cell.
- `Overlaps()` tests a rectangle against solid cells.
- `Query()` returns overlapping solid cells in row order.
- `Raycast()` returns hit distance and tile coordinates, or -1 for no hit.
- `HasLineOfSight()` tests a segment against solid cells.

`Tilemap` maintains its native grid when tile solidity changes.
`CollideEntity()` and `CollideEntityOneway()` use native rejection tests for full-tile collision maps.
Maps with custom collision rectangles retain the existing separation path.
Set `UseNativeCollision=false` to select managed entity collision.

`QuerySolidTiles()`, `RaycastTiles()`, and `HasTileLineOfSight()` accept world coordinates.
These queries test full solid tiles and require the native runtime.
They do not use custom collision rectangles.

## Image processing

`ImageProcessing` accepts row-major RGBA byte arrays with four bytes per pixel.
It validates buffer sizes before calling `image_process`.

- `Blur()` returns a new buffer with a separable box blur.
- `Outline()` returns a new buffer with an outline inside the existing dimensions.
- `Tint()`, `PaletteSwap()`, `Grayscale()`, and `Invert()` modify the input buffer.
- Grayscale and inversion preserve alpha.

Pass processed buffers to `GLTexture.FromRGBA()` or `GLTexture.UpdateRGBA()`.
These functions require the native runtime.

## PCM mixing

`PcmMixer` owns a native mixer and implements `IDisposable`.
It accepts signed 16-bit mono or interleaved stereo samples.
Input samples must use the mixer's sample rate.
`Play()` copies samples into native ownership and returns a voice index, or -1 when all 64 voices are occupied.

`SetSpatial()` sets volume, stereo pan, and low-pass filtering.
`Mix()` writes interleaved stereo samples into a caller-provided buffer.
`Stop()`, `StopAll()`, `IsPlaying()`, and `ActiveVoices` control voice lifetime.
Mixer operations synchronize access between playback and control threads.

`AudioManager.PlayPcm()` plays 44100 Hz PCM through the SDL audio callback.
`SetPcmSpatial()`, `StopPcm()`, and `IsPcmPlaying()` use its PCM voice indices.
PCM voice indices are separate from SDL sound channel indices.
Master and sound volume settings affect PCM playback.
`StopAllSounds()` also stops PCM voices.
File-based sound effects and music continue through SDL_mixer.

## GIF and video

`GifSprite` and `AssetLoader` use `gif_decoder` for animated textures.
A managed GIF decoder remains available when the native library is absent.

`VideoSprite` uses `video_player` and FFmpeg for video and audio decoding.
The decoder drains delayed video frames at end of file and flushes decoder state when seeking.
Repeated `LoadVideo()` calls release the previous decoder, texture, staging buffer, and audio device.
Video playback requires the native runtime.

## Noise

`Noise` exposes Perlin, Simplex, and Worley functions through `noise`.
Bulk fill functions validate output dimensions before passing buffers to native code.
The managed fallback provides two-dimensional Perlin noise.
Use the packaged native runtime for the full noise API.

## Verification

`NativeIntegrationTests` requires every library when `VENGINE_REQUIRE_NATIVE=1`.
Tests compare physics, fluid gravity, and sorting against managed controls.
Other checks cover blocked paths, search limits, tile boundaries, image buffers, audio callbacks, GIF frames, video draining, and seeded noise.
The package consumer verifies runtime loading and execution from isolated publish outputs.
