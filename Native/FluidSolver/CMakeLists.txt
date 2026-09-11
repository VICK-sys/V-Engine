cmake_minimum_required(VERSION 3.16)
project(FluidSolver LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(fluid_solver SHARED fluid_solver.cpp fluid_solver.h)
target_compile_definitions(fluid_solver PRIVATE FLUID_EXPORTS)

if(MSVC)
    target_compile_options(fluid_solver PRIVATE /O2)
else()
    target_compile_options(fluid_solver PRIVATE -O3)
endif()
