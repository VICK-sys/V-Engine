# V-Engine

V-Engine is a C# game engine with a 2D renderer, Lua scripting, physics, and asset tools.
The engine ships as a .NET 10 library.
SDL2 provides windows, input, and audio.
Rendering uses OpenGL 3.3 through Silk.NET.
MoonSharp runs Lua scenes and plugins.

## Features

- Scenes, entity groups, behaviors, timers, tweens, and signals.
- Fixed timestep updates with a default rate of 60 Hz.
- Sprite animation, tilemaps, particles, text, lighting, and post-processing.
- Rigid body physics, collision queries, joints, water, and fluid simulation.
- Keyboard, mouse, and gamepad input with named action bindings.
- Lua scene scripts, script reloads, and declarative plugins.
- UI widgets, dialogue, save data, and localization.
- Texture processing, animated GIFs, and video playback.
- Grid pathfinding and tile collision queries.
- PCM audio mixing with volume, panning, and filtering.
- Browser-based sprite and level editors.

## Requirements

The packaged runtime supports Windows x64.
Applications require a graphics driver that supports OpenGL 3.3.
Self-contained applications do not require a separate .NET installation.

Building from source requires:

- .NET 10 SDK.
- CMake 3.21 or newer.
- Visual Studio 2022 with the C++ workload and Windows SDK.
- Internet access for the first package restore and native dependency download.

## Build

Run these commands from the project root:

```powershell
.\vengine.bat native
dotnet build VEngine.sln
```

The native build downloads pinned SDL2 and FFmpeg archives and checks their SHA-256 hashes.
It compiles all ten C++ libraries and stages the runtime in `artifacts/native/Release/win-x64/`.
Project references copy these files into build and publish outputs.

Managed development and headless tests can run without the native build.
Native integration tests report skips when runtime libraries are absent.

## Package

Build and test the engine packages:

```powershell
.\vengine.bat pack
```

The command creates these files in `artifacts/packages/`:

- `VEngine.Engine.0.1.0.nupkg`: the engine, native runtime assets, and dependency notices.
- `VEngine.Native.win-x64.0.1.0.zip`: the native runtime for applications that use project references.
- `SHA256SUMS.0.1.0.txt`: checksums for both packages.

Verify installation and standalone publishing from an isolated consumer:

```powershell
.\Tools\verify-package.ps1 -Graphics
```

This check publishes folder and single-file applications.
It runs both applications with a restricted search path.
The graphics option also checks an OpenGL texture upload in a hidden window.

GitHub Actions builds and tests packages on Windows.
Download build artifacts from a successful workflow run.
The workflow does not publish packages to NuGet.org.

## Use the engine

Add the package from a local package directory:

```powershell
dotnet add MyGame.csproj package VEngine.Engine --version 0.1.0 --source C:\packages
```

Set `RuntimeIdentifier` to `win-x64` in the application project.
Create a `Game` instance and start a `Scene` or `LuaScene`.
Alternatively, reference `VEngine.Engine/VEngine.Engine.csproj` after building the native runtime.

Place application assets in an `Assets/` folder beside the executable.
Configure the application project to copy assets into build and publish outputs.
The engine resolves asset paths through `Eng.Asset()`.
Provide a TrueType font at `Assets/ui/default-font.ttf` for default text and UI widgets.

Publish a standalone application:

```powershell
dotnet publish MyGame.csproj -c Release -r win-x64 --self-contained true
```

For single-file publishing, also set `PublishSingleFile=true` and `IncludeNativeLibrariesForSelfExtract=true`.
Keep the published assets and dependency notices with the application.
Trimming and Native AOT are not supported.
Game assets and an executable game are not included in the engine package.

## Tests

Run all available tests:

```powershell
dotnet test VEngine.Tests
```

Require every native integration test to run:

```powershell
$env:VENGINE_REQUIRE_NATIVE = '1'
dotnet test VEngine.Tests
```

Tests cover native loading, physics, sorting, fluid simulation, pathfinding, tile queries, image processing, noise, PCM mixing, GIFs, and video.
Native physics and sorting tests compare results against managed controls.
Media tests use local synthetic fixtures.
Audio tests use an SDL dummy device.

## Tools

| Command | Action |
| --- | --- |
| `.\vengine.bat build` | Build the solution |
| `.\vengine.bat release` | Build the Release configuration |
| `.\vengine.bat native` | Build and stage the native runtime |
| `.\vengine.bat pack` | Test and package the engine |
| `.\vengine.bat test` | Run engine tests |
| `.\vengine.bat clean` | Remove .NET build outputs |
| `.\vengine.bat editor` | Open the sprite editor |
| `.\vengine.bat levels` | Open the level editor |
| `.\vengine.bat flowchart` | Open the engine flowchart |

The editors export JSON files for the engine asset loaders.

## Project layout

| Path | Contents |
| --- | --- |
| `VEngine.Engine/` | Engine source and package configuration |
| `VEngine.Tests/` | Engine tests and synthetic media fixtures |
| `Native/` | C++ libraries and pinned dependency manifest |
| `Tools/` | Editors, build scripts, and package verification |
| `docs/` | Subsystem and workflow documentation |
| `artifacts/` | Local build outputs and packages |

## Native libraries

All ten native libraries have engine APIs or execution paths.
`NativeRuntime.Inspect()` reports runtime library availability.
Physics, group sorting, fluid simulation, and GIF decoding retain managed paths.
Advanced noise functions, video, pathfinding, image processing, PCM mixing, and tile queries use native libraries.

See [native libraries](docs/native-libs.md) for API details and backend controls.
Other platforms do not have packaged runtimes or verified deployment support.

## Documentation

- [Engine architecture](docs/architecture.md)
- [Lua API](docs/lua-api.md)
- [Plugins](docs/plugins.md)
- [Tools and assets](docs/tools.md)
- [Testing](docs/testing.md)
- [Native libraries](docs/native-libs.md)
- [Engine flowchart](docs/engine-flowchart.md)

## License

No project license file is included.
Third-party dependencies retain their respective license terms.
Runtime packages include dependency notices in `licenses/`.
