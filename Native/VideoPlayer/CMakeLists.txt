cmake_minimum_required(VERSION 3.16)
project(VideoPlayer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

find_path(FFMPEG_INCLUDE_DIR libavformat/avformat.h
    HINTS ${FFMPEG_DIR}/include $ENV{FFMPEG_DIR}/include REQUIRED)
find_library(AVFORMAT_LIB avformat
    HINTS ${FFMPEG_DIR}/lib $ENV{FFMPEG_DIR}/lib REQUIRED)
find_library(AVCODEC_LIB avcodec
    HINTS ${FFMPEG_DIR}/lib $ENV{FFMPEG_DIR}/lib REQUIRED)
find_library(AVUTIL_LIB avutil
    HINTS ${FFMPEG_DIR}/lib $ENV{FFMPEG_DIR}/lib REQUIRED)
find_library(SWSCALE_LIB swscale
    HINTS ${FFMPEG_DIR}/lib $ENV{FFMPEG_DIR}/lib REQUIRED)
find_library(SWRESAMPLE_LIB swresample
    HINTS ${FFMPEG_DIR}/lib $ENV{FFMPEG_DIR}/lib REQUIRED)

add_library(video_player SHARED video_player.cpp video_player.h)
target_compile_definitions(video_player PRIVATE VIDEO_EXPORTS)
target_include_directories(video_player PRIVATE ${FFMPEG_INCLUDE_DIR})
target_link_libraries(video_player PRIVATE ${AVFORMAT_LIB} ${AVCODEC_LIB} ${AVUTIL_LIB} ${SWSCALE_LIB} ${SWRESAMPLE_LIB})

if(MSVC)
    target_compile_options(video_player PRIVATE /O2)
endif()
