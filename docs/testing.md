# Testing

The test project references the engine library.
Tests use local data and do not call external media services.
`Eng.InitHeadless()` provides engine subsystems without creating a window.

## Managed tests

Run the suite:

```powershell
dotnet test VEngine.Tests
```

Core tests cover input buffering, entity updates, scenes, timers, collision, physics, fluids, assets, and UI state.
Native integration tests skip when the complete runtime is absent.

Use a separate output directory to verify a managed-only build:

```powershell
dotnet test VEngine.sln --artifacts-path artifacts/managed-tests -p:VEngineNativeDirectory=missing
```

## Native integration

Build the native runtime and require native tests:

```powershell
.\Tools\build-native.ps1
$env:VENGINE_REQUIRE_NATIVE = '1'
dotnet test VEngine.Tests
```

A missing library or entry point fails required native tests.
The tests compare native physics, group sorting, and fluid gravity with managed controls.
Other assertions cover pathfinding, tile queries, image buffers, PCM ownership, audio callbacks, GIF decoding, video draining, and seeded noise.
Audio callback tests use the SDL dummy driver.
These tests do not measure speaker output.

The small GIF and MP4 fixtures contain synthetic test patterns.
The MP4 contains ten frames, delayed frames, and a 440 Hz tone.
The GIF contains two frames with 500 ms delays.

## Package consumers

Build and verify the package:

```powershell
.\Tools\package.ps1
.\Tools\verify-package.ps1 -Graphics
```

Verification installs the package into a temporary consumer with its own package cache.
It publishes a self-contained folder and a self-contained single-file application.
Both processes run with a restricted search path.
The probe checks native loading, physics, image processing, pathfinding, PCM mixing, SDL_ttf, and SDL_mixer.
The graphics option also uploads and reads an OpenGL texture in a hidden window.

GitHub Actions runs managed tests, native tests, packaging, and package consumer verification on Windows.
The workflow uploads packages after all checks pass.
