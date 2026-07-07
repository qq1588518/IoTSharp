cmake_minimum_required(VERSION 3.21)

project(SonnetDBVB6
    VERSION 0.1.0
    DESCRIPTION "SonnetDB Visual Basic 6 connector bridge"
    LANGUAGES C)

if(NOT WIN32)
    message(FATAL_ERROR "The VB6 connector bridge is Windows-only.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "The VB6 connector bridge must be built as Win32/x86 because VB6 is a 32-bit runtime.")
endif()

add_library(sonnetdb_vb6_native SHARED native/sonnetdb_vb6_bridge.c)
set_target_properties(sonnetdb_vb6_native PROPERTIES
    OUTPUT_NAME "SonnetDB.VB6.Native")

target_compile_definitions(sonnetdb_vb6_native PRIVATE
    UNICODE
    _UNICODE)

install(TARGETS sonnetdb_vb6_native
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
