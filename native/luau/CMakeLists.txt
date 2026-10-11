cmake_minimum_required(VERSION 3.24)

# build-luau.ps1 drives this file. build-luau.sh compiles the same sources with
# the compiler directly, so an option that changes here changes there too.

# /MT, the C runtime a NativeAOT executable links. With /MD the AOT link fails
# with LNK2038.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project(spectra_luau LANGUAGES CXX C)

set(LUAU_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../external/luau" CACHE PATH "The Luau submodule")
option(SPECTRA_LUAU_SHARED "Build the shared library instead of the static one" OFF)

if(NOT EXISTS "${LUAU_SOURCE_DIR}/VM/include/lua.h")
    message(FATAL_ERROR "Luau sources are missing at '${LUAU_SOURCE_DIR}'. Run: git submodule update --init --recursive")
endif()

set(LUAU_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUAU_STATIC_CRT ON CACHE BOOL "" FORCE)
# LUAU_EXTERN_C would set the C names and longjmp, and no export. The
# definitions below set all three.
set(LUAU_EXTERN_C OFF CACHE BOOL "" FORCE)
add_subdirectory("${LUAU_SOURCE_DIR}" luau EXCLUDE_FROM_ALL)

set(EXPORT "")
if(SPECTRA_LUAU_SHARED)
    set(EXPORT " __declspec(dllexport)")
endif()

# Errors by longjmp, vectors of three floats. The shim refuses to compile
# against anything else.
target_compile_definitions(Luau.VM PUBLIC "LUA_API=extern \"C\"${EXPORT}" LUA_USE_LONGJMP=1 LUA_VECTOR_SIZE=3)
target_compile_definitions(Luau.Compiler PUBLIC "LUACODE_API=extern \"C\"${EXPORT}")

# No code generator: Luau.CodeGen stays out.
set(LUAU_PARTS Luau.VM Luau.Compiler Luau.Bytecode Luau.Ast Luau.Common)

set(LUAU_OBJECTS "")
foreach(part ${LUAU_PARTS})
    list(APPEND LUAU_OBJECTS "$<TARGET_OBJECTS:${part}>")
endforeach()

# One library holds the shim and all of Luau, so a link names one file.
if(SPECTRA_LUAU_SHARED)
    add_library(spectra_luau SHARED spectra_luau.cpp spectra_luau_compile.cpp ${LUAU_OBJECTS})
else()
    add_library(spectra_luau STATIC spectra_luau.cpp spectra_luau_compile.cpp ${LUAU_OBJECTS})
endif()

add_dependencies(spectra_luau ${LUAU_PARTS})
target_compile_features(spectra_luau PRIVATE cxx_std_17)
target_compile_definitions(spectra_luau PRIVATE "SL_API=extern \"C\"${EXPORT}")

# For the include paths and the definitions above. The objects are already in.
foreach(part ${LUAU_PARTS})
    target_include_directories(spectra_luau PRIVATE "$<TARGET_PROPERTY:${part},INTERFACE_INCLUDE_DIRECTORIES>")
    target_compile_definitions(spectra_luau PRIVATE "$<TARGET_PROPERTY:${part},INTERFACE_COMPILE_DEFINITIONS>")
endforeach()
