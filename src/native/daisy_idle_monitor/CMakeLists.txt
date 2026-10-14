cmake_minimum_required(VERSION 3.20)
project(daisy_idle_monitor VERSION 1.0.0 LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_program(WAYLAND_SCANNER wayland-scanner REQUIRED)
find_package(PkgConfig REQUIRED)
pkg_check_modules(WAYLAND_CLIENT REQUIRED wayland-client)

set(PROTOCOL_XML "${CMAKE_CURRENT_SOURCE_DIR}/ext-idle-notify-v1.xml")
set(PROTOCOL_HEADER "${CMAKE_CURRENT_BINARY_DIR}/ext-idle-notify-v1-client-protocol.h")
set(PROTOCOL_SOURCE "${CMAKE_CURRENT_BINARY_DIR}/ext-idle-notify-v1-protocol.c")

add_custom_command(
    OUTPUT "${PROTOCOL_HEADER}"
    COMMAND ${WAYLAND_SCANNER} client-header "${PROTOCOL_XML}" "${PROTOCOL_HEADER}"
    DEPENDS "${PROTOCOL_XML}"
    COMMENT "Generating Wayland client header for ext-idle-notify-v1"
)

add_custom_command(
    OUTPUT "${PROTOCOL_SOURCE}"
    COMMAND ${WAYLAND_SCANNER} private-code "${PROTOCOL_XML}" "${PROTOCOL_SOURCE}"
    DEPENDS "${PROTOCOL_XML}"
    COMMENT "Generating Wayland client code for ext-idle-notify-v1"
)

add_library(daisy_idle SHARED
    daisy_idle.c
    daisy_idle.h
    "${PROTOCOL_HEADER}"
    "${PROTOCOL_SOURCE}"
)

target_include_directories(daisy_idle
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}"
        "${CMAKE_CURRENT_BINARY_DIR}"
        ${WAYLAND_CLIENT_INCLUDE_DIRS}
)

target_link_libraries(daisy_idle
    PRIVATE
        ${WAYLAND_CLIENT_LIBRARIES}
        pthread
)

set_target_properties(daisy_idle PROPERTIES
    OUTPUT_NAME "daisy_idle"
    PREFIX "lib"
)

add_executable(daisyos-idle-monitor
    daisy_idle.c
    daisy_idle.h
    "${PROTOCOL_HEADER}"
    "${PROTOCOL_SOURCE}"
)

target_compile_definitions(daisyos-idle-monitor PRIVATE BUILD_STANDALONE=1)

target_include_directories(daisyos-idle-monitor
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}"
        "${CMAKE_CURRENT_BINARY_DIR}"
        ${WAYLAND_CLIENT_INCLUDE_DIRS}
)

target_link_libraries(daisyos-idle-monitor
    PRIVATE
        ${WAYLAND_CLIENT_LIBRARIES}
        pthread
)
