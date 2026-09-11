# V-Engine

V-Engine is a C# game engine with a 2D renderer, Lua scripting, physics, and asset tools.
The engine ships as a library for application projects.

The engine targets .NET 10.
SDL2 provides the window, input, and audio integration.
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
- Image loading, animated GIFs, and native video playback.
- Browser-based sprite and level editors.

## Requirements

Building the solution requires the .NET 10 SDK.
Running an application also requires a graphics driver that supports OpenGL 3.3.

Supply SDL2, SDL2_ttf, SDL2_mixer, and their dependencies in the application output directory.
Runtime libraries must match the application architecture.
Native binaries, game assets, and an executable application are not included.

Building the C++ libraries requires CMake and a C++ compiler.
The supplied Windows build scripts target Visual Studio 2022 with the C++ workload and x64 output.
The video library also requires FFmpeg development headers and libraries.

## Build

Run these commands from the project root.

Restore packages:

```powershell
dotnet restore VEngine.sln
```

Build the solution:

```powershell
dotnet build VEngine.sln --no-restore
```

Build the Release configuration:

```powershell
dotnet build VEngine.sln -c Release
```

## Use the engine

Reference `VEngine.Engine/VEngine.Engine.csproj` from an executable .NET 10 project.
Create a `Game` instance and start a `Scene` or `LuaScene`.

Place application assets in an `Assets/` folder beside the executable.
Configure the application project to copy assets and runtime libraries into its output directory.
The engine resolves asset paths through `Eng.Asset()`.
Provide a TrueType font at `Assets/ui/default-font.ttf` for default text and UI widgets.

See the [engine architecture](docs/architecture.md) and [Lua API](docs/lua-api.md) for lifecycle methods and available systems.

## Tests

Run the engine test suite:

```powershell
dotnet test VEngine.Tests
```

The suite runs without external media services.
Headless tests cover core behavior without creating a window.
They do not verify interactive rendering or audio playback.

## Tools

| Command | Action |
| --- | --- |
| `.\vengine.bat build` | Build the solution |
| `.\vengine.bat release` | Build the Release configuration |
| `.\vengine.bat test` | Run engine tests |
| `.\vengine.bat clean` | Remove .NET build outputs |
| `.\vengine.bat editor` | Open the sprite editor |
| `.\vengine.bat levels` | Open the level editor |
| `.\vengine.bat flowchart` | Open the engine flowchart |

The editors export JSON files for the engine asset loaders.

## Project layout

| Path | Contents |
| --- | --- |
| `VEngine.Engine/` | Engine source |
| `VEngine.Tests/` | Engine tests |
| `Native/` | C++ libraries and CMake projects |
| `Tools/` | Browser-based editors and flowchart |
| `docs/` | Subsystem and workflow documentation |

## Native libraries

The native libraries build separately from the .NET solution.
Windows Release builds write DLLs to each library's `build/Release/` directory.
Copy required DLLs into the application output directory.

Fluid simulation, GIF decoding, and noise have managed fallback paths.
Video playback requires the native video library and its FFmpeg dependencies.

Additional libraries cover audio mixing, sorting, image processing, pathfinding, physics, and tilemap collision.
Several wrappers are not connected to engine execution.

See [native library documentation](docs/native-libs.md) for source locations and build commands.

## Current limits

- Native integration and packaging remain incomplete.
- Platform setup outside Windows x64 has not been verified.

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
