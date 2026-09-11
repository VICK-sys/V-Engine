cmake_minimum_required(VERSION 3.16)
project(Noise LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(noise SHARED noise.cpp noise.h)
target_compile_definitions(noise PRIVATE NOISE_EXPORTS)

if(MSVC)
    target_compile_options(noise PRIVATE /O2 /arch:AVX2 /fp:fast)
else()
    target_compile_options(noise PRIVATE -O3 -mavx2 -ffast-math)
endif()
