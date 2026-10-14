cmake_minimum_required(VERSION 3.21)

project(daisy_screenshot_helper VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(Qt6 REQUIRED COMPONENTS Core DBus Gui)

add_executable(daisy_screenshot_helper
    daisy_screenshot_helper.cpp)

set_target_properties(daisy_screenshot_helper PROPERTIES
    OUTPUT_NAME "daisy-screenshot-helper")

target_link_libraries(daisy_screenshot_helper
    PRIVATE
        Qt6::Core
        Qt6::DBus
        Qt6::Gui)

include(GNUInstallDirs)
install(TARGETS daisy_screenshot_helper
    RUNTIME DESTINATION "${CMAKE_INSTALL_LIBEXECDIR}/daisyos"
    PERMISSIONS OWNER_READ OWNER_WRITE OWNER_EXECUTE GROUP_READ GROUP_EXECUTE WORLD_READ WORLD_EXECUTE)
