# Additional clean files
cmake_minimum_required(VERSION 3.16)

if("${CONFIG}" STREQUAL "" OR "${CONFIG}" STREQUAL "Release")
  file(REMOVE_RECURSE
  "CMakeFiles/daisy_window_decoration_autogen.dir/AutogenUsed.txt"
  "CMakeFiles/daisy_window_decoration_autogen.dir/ParseCache.txt"
  "daisy_window_decoration_autogen"
  )
endif()
