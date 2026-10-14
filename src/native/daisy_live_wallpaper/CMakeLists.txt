cmake_minimum_required(VERSION 3.16)
project(daisy_live_wallpaper VERSION 1.0.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(PkgConfig REQUIRED)

pkg_check_modules(AVFORMAT REQUIRED IMPORTED_TARGET libavformat)
pkg_check_modules(AVCODEC  REQUIRED IMPORTED_TARGET libavcodec)
pkg_check_modules(AVUTIL   REQUIRED IMPORTED_TARGET libavutil)
pkg_check_modules(SWSCALE  REQUIRED IMPORTED_TARGET libswscale)
pkg_check_modules(LIBDRM   REQUIRED IMPORTED_TARGET libdrm)

include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/src
)

set(SOURCES
    src/hw_decoder.cpp
    src/dmabuf_exporter.cpp
    src/wallpaper_engine.cpp
    src/live_wallpaper_api.cpp
)

add_library(daisy_live_wallpaper SHARED ${SOURCES})

target_link_libraries(daisy_live_wallpaper
    PRIVATE
        PkgConfig::AVFORMAT
        PkgConfig::AVCODEC
        PkgConfig::AVUTIL
        PkgConfig::SWSCALE
        PkgConfig::LIBDRM
        pthread
        dl
)

set_target_properties(daisy_live_wallpaper PROPERTIES
    OUTPUT_NAME "daisy_live_wallpaper"
    SOVERSION 1
    VERSION 1.0.0
)

add_executable(daisy_wallpaper_test src/test_main.cpp)
target_link_libraries(daisy_wallpaper_test
    PRIVATE
        daisy_live_wallpaper
        pthread
)
