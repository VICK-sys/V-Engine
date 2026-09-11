cmake_minimum_required(VERSION 3.16)
project(TilemapCollision LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(tilemap_collision SHARED tilemap_collision.cpp tilemap_collision.h)
target_compile_definitions(tilemap_collision PRIVATE TILECOL_EXPORTS)

if(MSVC)
    target_compile_options(tilemap_collision PRIVATE /O2)
else()
    target_compile_options(tilemap_collision PRIVATE -O3)
endif()
