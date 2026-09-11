cmake_minimum_required(VERSION 3.16)
project(AudioMixer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(audio_mixer SHARED audio_mixer.cpp audio_mixer.h)
target_compile_definitions(audio_mixer PRIVATE MIXER_EXPORTS)

if(MSVC)
    target_compile_options(audio_mixer PRIVATE /O2 /arch:AVX2 /fp:fast)
else()
    target_compile_options(audio_mixer PRIVATE -O3 -mavx2 -ffast-math)
endif()
