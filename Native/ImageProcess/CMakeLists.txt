cmake_minimum_required(VERSION 3.16)
project(ImageProcess LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(image_process SHARED image_process.cpp image_process.h)
target_compile_definitions(image_process PRIVATE IMGPROC_EXPORTS)

if(MSVC)
    target_compile_options(image_process PRIVATE /O2 /arch:AVX2 /fp:fast)
else()
    target_compile_options(image_process PRIVATE -O3 -mavx2 -ffast-math)
endif()
