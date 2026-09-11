@echo off
REM Build video_player.dll using CMake + MSVC + FFmpeg
REM Requires:
REM   - Visual Studio with C++ workload
REM   - FFmpeg shared build (set FFMPEG_DIR or put in PATH)
REM   - Download from: https://github.com/BtbN/FFmpeg-Builds/releases
REM
REM Example: set FFMPEG_DIR=C:\ffmpeg and run this script

if not defined FFMPEG_DIR (
    echo ERROR: Set FFMPEG_DIR to your FFmpeg installation directory
    echo   Example: set FFMPEG_DIR=C:\ffmpeg
    echo   Download from: https://github.com/BtbN/FFmpeg-Builds/releases
    exit /b 1
)

if not exist build mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64 -DFFMPEG_DIR="%FFMPEG_DIR%"
cmake --build . --config Release
echo.
echo Built Release\video_player.dll
echo Copy FFmpeg runtime DLLs into the application output directory.
