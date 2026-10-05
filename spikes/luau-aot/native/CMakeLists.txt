cmake_minimum_required(VERSION 3.24)

# Static CRT (/MT), the same one a NativeAOT executable links. OFF exists only
# to show what a /MD build does to the AOT link.
option(SPIKE_STATIC_CRT "Build with the static MSVC runtime" ON)
if(SPIKE_STATIC_CRT)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project(spectra_luau_spike LANGUAGES CXX C)

set(LUAU_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../.luau/src" CACHE PATH "Luau checkout")
set(SPIKE_UNWIND "longjmp" CACHE STRING "How Luau raises errors: longjmp or cxx")
option(SPIKE_SHARED "Build one shared library instead of static libraries" OFF)

if(NOT EXISTS "${LUAU_SOURCE_DIR}/VM/include/lua.h")
    message(FATAL_ERROR "Luau sources not found at '${LUAU_SOURCE_DIR}'. Run build-luau.ps1, it fetches them.")
endif()

set(LUAU_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUAU_STATIC_CRT ${SPIKE_STATIC_CRT} CACHE BOOL "" FORCE)
# LUAU_EXTERN_C would also force longjmp. The definitions are set below instead
# so the cxx variant gets extern "C" names too.
set(LUAU_EXTERN_C OFF CACHE BOOL "" FORCE)
add_subdirectory("${LUAU_SOURCE_DIR}" luau EXCLUDE_FROM_ALL)

set(SPIKE_EXPORT "")
if(SPIKE_SHARED)
    if(MSVC)
        set(SPIKE_EXPORT " __declspec(dllexport)")
    else()
        set(SPIKE_EXPORT " __attribute__((visibility(\"default\")))")
    endif()
endif()

target_compile_definitions(Luau.VM PUBLIC "LUA_API=extern \"C\"${SPIKE_EXPORT}")
target_compile_definitions(Luau.Compiler PUBLIC "LUACODE_API=extern \"C\"${SPIKE_EXPORT}")
target_compile_definitions(Luau.CodeGen PUBLIC "LUACODEGEN_API=extern \"C\"${SPIKE_EXPORT}")

if(SPIKE_UNWIND STREQUAL "longjmp")
    target_compile_definitions(Luau.VM PUBLIC LUA_USE_LONGJMP=1)
elseif(NOT SPIKE_UNWIND STREQUAL "cxx")
    message(FATAL_ERROR "SPIKE_UNWIND must be longjmp or cxx")
endif()

if(SPIKE_SHARED)
    add_library(luau SHARED spectra_luau.cpp spectra_luau_compile.cpp)
    target_compile_definitions(luau PRIVATE "SL_API=extern \"C\"${SPIKE_EXPORT}")
    # Whole archive: the exports are the API, nothing in the shim references most of them.
    target_link_libraries(luau PRIVATE
        "$<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.VM>"
        "$<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.Compiler>"
        "$<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.CodeGen>")
    if(NOT MSVC)
        target_link_options(luau PRIVATE -Wl,--gc-sections)
    endif()
else()
    add_library(spectra_luau_shim STATIC spectra_luau.cpp)
    target_link_libraries(spectra_luau_shim PUBLIC Luau.VM)
    add_library(spectra_luau_compile_shim STATIC spectra_luau_compile.cpp)
    target_link_libraries(spectra_luau_compile_shim PUBLIC Luau.Compiler)
    # Names the libraries the csproj links, so they all get built.
    add_custom_target(spike_static ALL DEPENDS
        spectra_luau_shim spectra_luau_compile_shim
        Luau.VM Luau.Compiler Luau.Bytecode Luau.Ast Luau.CodeGen Luau.Common)
endif()
