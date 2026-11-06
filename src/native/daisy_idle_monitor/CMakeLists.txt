cmake_minimum_required(VERSION 3.20)
project(daisy_idle_monitor VERSION 1.0.0 LANGUAGES C)

# Keep the original implementation only for reproducible A/B memory benchmarks.
option(DAISY_IDLE_C_BASELINE "Build the original C idle monitor for comparison" OFF)
if(NOT DAISY_IDLE_C_BASELINE)
    find_program(CARGO cargo REQUIRED)
    set(RUST_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/../daisy_background")
    file(GLOB_RECURSE RUST_INPUTS CONFIGURE_DEPENDS "${RUST_SOURCE}/src/*.rs")
    set(RUST_TARGET "${CMAKE_CURRENT_BINARY_DIR}/cargo")
    add_custom_command(
        OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/libdaisy_idle.so" "${CMAKE_CURRENT_BINARY_DIR}/daisyos-idle-monitor"
        COMMAND ${CARGO} build --manifest-path "${RUST_SOURCE}/Cargo.toml" --target-dir "${RUST_TARGET}" --release --locked
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${RUST_TARGET}/release/libdaisy_idle.so" "${CMAKE_CURRENT_BINARY_DIR}/libdaisy_idle.so"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${RUST_TARGET}/release/daisyos-idle-monitor" "${CMAKE_CURRENT_BINARY_DIR}/daisyos-idle-monitor"
        DEPENDS ${RUST_INPUTS} "${RUST_SOURCE}/Cargo.toml" "${RUST_SOURCE}/Cargo.lock" "${RUST_SOURCE}/build.rs" "${RUST_SOURCE}/wayland_shim.c" "${CMAKE_CURRENT_SOURCE_DIR}/ext-idle-notify-v1.xml"
        VERBATIM
    )
    add_custom_target(daisy_idle ALL DEPENDS "${CMAKE_CURRENT_BINARY_DIR}/libdaisy_idle.so" "${CMAKE_CURRENT_BINARY_DIR}/daisyos-idle-monitor")
    return()
endif()

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
