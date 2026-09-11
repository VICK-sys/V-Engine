@echo off
REM Build audio_mixer.dll using CMake + MSVC

if not exist build mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
echo.
echo Built Release\audio_mixer.dll
