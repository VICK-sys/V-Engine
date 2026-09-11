cmake_minimum_required(VERSION 3.16)
project(BatchSorter LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(batch_sorter SHARED batch_sorter.cpp batch_sorter.h)
target_compile_definitions(batch_sorter PRIVATE BATCH_EXPORTS)

if(MSVC)
    target_compile_options(batch_sorter PRIVATE /O2)
else()
    target_compile_options(batch_sorter PRIVATE -O3)
endif()
