cmake_minimum_required(VERSION 3.16)
project(daisy_mpv_wallpaper LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
find_package(PkgConfig REQUIRED)
pkg_check_modules(MPV REQUIRED IMPORTED_TARGET mpv>=2.4)
pkg_check_modules(WAYLAND REQUIRED IMPORTED_TARGET wayland-client wayland-egl)
pkg_check_modules(EGL REQUIRED IMPORTED_TARGET egl gl)
pkg_check_modules(AVFORMAT REQUIRED IMPORTED_TARGET libavformat libavutil)
find_program(WAYLAND_SCANNER wayland-scanner REQUIRED)
set(PROTOCOL_SOURCES)
foreach(PROTOCOL color-management-v1 presentation-time viewporter)
  set(XML ${CMAKE_CURRENT_SOURCE_DIR}/protocols/${PROTOCOL}.xml)
  add_custom_command(OUTPUT ${PROTOCOL}-client.h ${PROTOCOL}-protocol.c
    COMMAND ${WAYLAND_SCANNER} client-header ${XML} ${PROTOCOL}-client.h
    COMMAND ${WAYLAND_SCANNER} private-code ${XML} ${PROTOCOL}-protocol.c
    DEPENDS ${XML})
  list(APPEND PROTOCOL_SOURCES ${PROTOCOL}-protocol.c)
endforeach()
add_library(daisy_mpv_wallpaper SHARED mpv_wallpaper.cpp ${PROTOCOL_SOURCES})
target_include_directories(daisy_mpv_wallpaper PRIVATE ${CMAKE_CURRENT_BINARY_DIR})
target_link_libraries(daisy_mpv_wallpaper PRIVATE PkgConfig::MPV PkgConfig::WAYLAND PkgConfig::EGL PkgConfig::AVFORMAT pthread)
target_compile_options(daisy_mpv_wallpaper PRIVATE -Wall -Wextra)
add_custom_command(OUTPUT xdg-shell-client.h xdg-shell-protocol.c
  COMMAND ${WAYLAND_SCANNER} client-header ${CMAKE_CURRENT_SOURCE_DIR}/protocols/xdg-shell.xml xdg-shell-client.h
  COMMAND ${WAYLAND_SCANNER} private-code ${CMAKE_CURRENT_SOURCE_DIR}/protocols/xdg-shell.xml xdg-shell-protocol.c
  DEPENDS protocols/xdg-shell.xml)
add_executable(daisy_mpv_surface_test surface_test.cpp xdg-shell-protocol.c)
target_include_directories(daisy_mpv_surface_test PRIVATE ${CMAKE_CURRENT_BINARY_DIR})
target_link_libraries(daisy_mpv_surface_test PRIVATE daisy_mpv_wallpaper PkgConfig::WAYLAND)

add_executable(daisy_mpv_preview preview.cpp)
set_target_properties(daisy_mpv_preview PROPERTIES OUTPUT_NAME "daisy-mpv-preview" BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")
target_link_libraries(daisy_mpv_preview PRIVATE daisy_mpv_wallpaper)
