cmake_minimum_required(VERSION 3.16)
project(GifDecoder LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(gif_decoder SHARED gif_decoder.cpp gif_decoder.h stb_image.h)
target_compile_definitions(gif_decoder PRIVATE GIF_EXPORTS)

if(MSVC)
    target_compile_options(gif_decoder PRIVATE /O2)
else()
    target_compile_options(gif_decoder PRIVATE -O3)
endif()
