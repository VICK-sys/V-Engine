@echo off
if "%1"=="build" (
    dotnet build VEngine.sln
) else if "%1"=="clean" (
    dotnet clean VEngine.sln
) else if "%1"=="release" (
    dotnet build VEngine.sln -c Release
) else if "%1"=="test" (
    dotnet test VEngine.Tests
) else if "%1"=="editor" (
    start "" "Tools\sprite-editor.html"
) else if "%1"=="levels" (
    start "" "Tools\level-editor.html"
) else if "%1"=="flowchart" (
    start "" "Tools\flowchart.html"
) else (
    echo Usage: vengine [command]
    echo.
    echo Commands:
    echo   build     Compile the project
    echo   clean     Clean build artifacts
    echo   release   Compile the Release configuration
    echo   test      Run engine tests
    echo   editor    Open sprite editor in browser
    echo   levels    Open level editor in browser
    echo   flowchart Open engine flowchart in browser
)
