@echo off
REM Build fluid_solver.dll using CMake + MSVC
REM Requires: Visual Studio with C++ workload, CMake in PATH

if not exist build mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
echo.
echo Built Release\fluid_solver.dll
