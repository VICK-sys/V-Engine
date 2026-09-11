cmake_minimum_required(VERSION 3.16)
project(PhysicsSolver LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(physics_solver SHARED physics_solver.cpp physics_solver.h)
target_compile_definitions(physics_solver PRIVATE PHYSICS_EXPORTS)

if(MSVC)
    target_compile_options(physics_solver PRIVATE /O2)
else()
    target_compile_options(physics_solver PRIVATE -O3)
endif()
