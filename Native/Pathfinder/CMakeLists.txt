cmake_minimum_required(VERSION 3.16)
project(Pathfinder LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(pathfinder SHARED pathfinder.cpp pathfinder.h)
target_compile_definitions(pathfinder PRIVATE PATHFINDER_EXPORTS)

if(MSVC)
    target_compile_options(pathfinder PRIVATE /O2 /arch:AVX2 /fp:fast)
else()
    target_compile_options(pathfinder PRIVATE -O3 -mavx2 -ffast-math)
endif()
