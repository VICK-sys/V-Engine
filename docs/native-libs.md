# V-Engine Native C++ Libraries

> Build instructions and documentation for the native DLLs (fluid solver, GIF decoder, video player, physics solver).

## Native C++ Libraries
Build the libraries separately with CMake from their directories under `Native/`.
Windows Release builds write DLLs to each library's `build/Release/` directory.
Copy required DLLs into the application output directory.
Native binaries are not included.
Fluid simulation, GIF decoding, and noise have managed fallback paths.
Video playback requires the native video library and its FFmpeg dependencies.

```bash
# Build fluid solver
cd Native/FluidSolver && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build GIF decoder
cd Native/GifDecoder && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build video player (requires FFmpeg)
set FFMPEG_DIR=C:\path\to\ffmpeg-shared
cd Native/VideoPlayer && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 -DFFMPEG_DIR=%FFMPEG_DIR% && cmake --build . --config Release

# Build physics solver
cd Native/PhysicsSolver && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build batch sorter
cd Native/BatchSorter && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build audio mixer
cd Native/AudioMixer && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build pathfinder
cd Native/Pathfinder && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build tilemap collision
cd Native/TilemapCollision && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build image processor
cd Native/ImageProcess && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# Build noise
cd Native/Noise && mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release
```

## Physics Solver (PhysicsSolver)
- Native C++ sequential impulse 2D physics solver with SOA body layout
- Broad phase: spatial hash grid with configurable cell size
- Narrow phase: SAT polygon-polygon, circle-circle, circle-polygon
- Solver: warm-started velocity constraints + Baumgarte position correction
- Compiled with `/O2 /arch:AVX2 /fp:fast` for SIMD auto-vectorization
- C API: `physics_create`, `physics_upload_bodies`, `physics_solve`, `physics_download_bodies`, `physics_download_contacts`
- P/Invoke wrapper: `NativePhysicsSolver.cs`
- Falls back to managed C# solver when DLL is not present

## Batch Sorter (BatchSorter)
- Native C++ stable sort + frustum cull for entity draw ordering
- Sorts by (layer, zorder, original_index) for guaranteed stable ordering
- AABB frustum culling eliminates off-screen entities before drawing
- C API: `batch_create`, `batch_upload`, `batch_sort_and_cull`, `batch_get_order`
- P/Invoke wrapper: `NativeBatchSorter.cs`
- Falls back to managed sort in Group.cs when DLL is not present

## Audio Mixer (AudioMixer)
- Native C++ spatial audio mixer with per-sample processing
- 64 simultaneous voices with volume, stereo panning, and one-pole low-pass filter
- Equal-power panning for natural stereo imaging
- Low-pass filter coefficient per voice for distance-based muffling
- Mix output as S16 interleaved stereo, suitable for SDL audio callback
- C API: `mixer_create`, `mixer_play`, `mixer_set_spatial`, `mixer_mix`
- P/Invoke wrapper: `NativeAudioMixer.cs`
- Falls back to SDL_mixer spatial audio when DLL is not present

## Pathfinder (Pathfinder)
- Native C++ A* grid pathfinding with binary min-heap priority queue
- 4-directional (Manhattan) and 8-directional (octile) movement
- Diagonal corner-cut prevention (requires adjacent cardinals to be walkable)
- Configurable max search limit to cap CPU cost
- Dynamic grid updates: `pf_set_cell` for single tiles, `pf_update_grid` for bulk
- C API: `pf_create`, `pf_find_path`, `pf_get_path_x/y`, `pf_set_cell`
- P/Invoke wrapper: `NativePathfinder.cs`

## Tilemap Collision (TilemapCollision)
- Native C++ tilemap collision queries on flat solid grids
- AABB overlap test and region query (returns all solid tiles in a rect)
- DDA raycast through the tile grid with exact hit distance
- Line-of-sight query (A to B, blocked by any solid tile?)
- Dynamic tile updates: `tilecol_set` for single tiles
- C API: `tilecol_create`, `tilecol_aabb_test`, `tilecol_raycast`, `tilecol_line_of_sight`
- P/Invoke wrapper: `NativeTilemapCollision.cs`

## Image Processing (ImageProcess)
- Native C++ RGBA pixel operations with AVX2 auto-vectorization
- Separable box blur (two-pass horizontal+vertical)
- Outline generation: expand alpha silhouette by N pixels in a given color
- Tint: per-pixel RGB multiply
- Palette swap: replace colors within tolerance
- Grayscale (BT.601 luminance) and invert
- C API: `imgproc_blur`, `imgproc_outline`, `imgproc_tint`, `imgproc_palette_swap`, `imgproc_grayscale`, `imgproc_invert`
- P/Invoke wrapper: `NativeImageProcess.cs`

## Noise (Noise)
- Native C++ procedural noise library
- 2D/3D classic Perlin noise with fade/lerp/gradient functions
- 2D Simplex noise (faster, fewer axis artifacts)
- 2D Worley (cellular) noise with jitter control
- Fractal Brownian Motion (multi-octave Perlin sum)
- Batch fill functions for filling entire 2D buffers at once
- Deterministic seeding via permutation table shuffle
- C API: `noise_perlin_2d/3d`, `noise_simplex_2d`, `noise_worley_2d`, `noise_perlin_fill_2d`, etc.
- Managed fallback: `Noise.cs` includes a pure C# Perlin implementation when DLL is unavailable

## GIF & Video Playback

### GifSprite
- Loads animated GIFs via native C++ decoder (stb_image) or managed StbImageSharp fallback
- Native mode: single reusable GPU texture, frames streamed from native memory via `glTexSubImage2D`
- Managed mode: capped at 60 frames (GPU memory limit)
- Preloadable via `preload_assets()` — decoded on background thread, cached for instant use
- Supports: scale, rotation, flip, tint, speed control, loop/once, pause/resume

### VideoSprite
- Decodes video via native FFmpeg DLL (MP4, AVI, MKV, WebM, MOV)
- Video: RGBA frames uploaded to GPU each frame
- Audio: decoded to S16 PCM, queued to SDL audio device
- Muted by default — unmute for focus/playback
- Supports: scale, position, loop, seek, speed control

## Grid Fluid System (GridFluidSystem)

Cellular automaton water simulation that fills containers:
- Water flows down by gravity, spreads sideways, equalizes under pressure
- Per-cell velocity field: water splashes upward on impact, sloshes on mouse interaction
- Deep equalization: submerged cells level out, surface cells stay dynamic
- Rendering: depth gradient (surface → deep blue), animated surface wave, surface highlight line
- Splash particles: auto-emitted at impact points via attached ParticleEmitter
- Mouse interaction: drag to push water, right-drag to carve, middle-click to add
