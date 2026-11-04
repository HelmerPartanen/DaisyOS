cmake_minimum_required(VERSION 3.21)

project(daisy_window_decoration VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_AUTOMOC ON)

find_package(Qt6 REQUIRED COMPONENTS Core Gui)
find_package(KF6CoreAddons REQUIRED)
find_package(KF6Config REQUIRED)
find_package(KDecoration3 REQUIRED)

include(CheckCXXSourceCompiles)
set(CMAKE_REQUIRED_LIBRARIES KDecoration3::KDecoration Qt6::Core)
check_cxx_source_compiles([=[
    #include <KDecoration3/Decoration>
    int main() {
        const auto style = KDecoration3::Style::Shadow;
        return style == KDecoration3::Style::Shadow ? 0 : 1;
    }
]=] DAISY_KDECORATION_HAS_SHADOW_STYLE)
unset(CMAKE_REQUIRED_LIBRARIES)

add_library(daisy_window_decoration MODULE
    daisydecoration.cpp
    daisydecoration.h
    daisyframestyle.cpp
    daisyframestyle.h
    daisydecoration.json)

set_target_properties(daisy_window_decoration PROPERTIES
    OUTPUT_NAME "org.daisyos.decoration"
    PREFIX "")

target_link_libraries(daisy_window_decoration
    PRIVATE
        Qt6::Core
        Qt6::Gui
        KF6::CoreAddons
        KF6::ConfigCore
        KDecoration3::KDecoration)

if(DAISY_KDECORATION_HAS_SHADOW_STYLE)
    target_compile_definitions(daisy_window_decoration PRIVATE DAISY_KDECORATION_HAS_SHADOW_STYLE=1)
endif()

# KDecoration plugins live beside the system decorations, for example
# /usr/lib/qt6/plugins/org.kde.kdecoration3 on Arch Linux.
include(GNUInstallDirs)
set(DAISY_KDECORATION_PLUGIN_DIR
    "${CMAKE_INSTALL_LIBDIR}/qt6/plugins/org.kde.kdecoration3"
    CACHE PATH "KDecoration3 plugin directory")
install(TARGETS daisy_window_decoration
    LIBRARY DESTINATION "${DAISY_KDECORATION_PLUGIN_DIR}")

option(DAISY_BUILD_DECORATION_TESTS "Build titlebar pixel clipping regression checks" OFF)
if(DAISY_BUILD_DECORATION_TESTS)
    enable_testing()
    add_executable(titlebar_clip_test tests/titlebar_clip_test.cpp daisyframestyle.cpp)
    target_include_directories(titlebar_clip_test PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
    target_link_libraries(titlebar_clip_test PRIVATE Qt6::Core Qt6::Gui KDecoration3::KDecoration)
    add_test(NAME titlebar_clip COMMAND titlebar_clip_test)
    set_tests_properties(titlebar_clip PROPERTIES ENVIRONMENT "QT_QPA_PLATFORM=offscreen")
endif()
