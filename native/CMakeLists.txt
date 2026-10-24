cmake_minimum_required(VERSION 3.20)
project(lualoom LANGUAGES C)

set(LUA_VERSION "54" CACHE STRING "Lua version: 53, 54, 55")

if(NOT DEFINED LUA_SRC)
    if(LUA_VERSION STREQUAL "53")
        set(LUA_SRC "${CMAKE_CURRENT_SOURCE_DIR}/../build/lua-5.3.6/src")
    elseif(LUA_VERSION STREQUAL "55")
        set(LUA_SRC "${CMAKE_CURRENT_SOURCE_DIR}/../build/lua-5.5.1/src")
    else()
        set(LUA_SRC "${CMAKE_CURRENT_SOURCE_DIR}/../build/lua-5.4.9/src")
    endif()
endif()

if(NOT EXISTS "${LUA_SRC}/lua.h")
    message(FATAL_ERROR "Lua source not found at ${LUA_SRC}. Run scripts/fetch-lua.ps1 -Version ${LUA_VERSION} first.")
endif()

# 用通配收集，避免每个 Lua 版本的源文件清单差异（如 5.3 的 lbitlib.c）
file(GLOB LUA_SOURCES "${LUA_SRC}/*.c")
list(REMOVE_ITEM LUA_SOURCES
    "${LUA_SRC}/lua.c"
    "${LUA_SRC}/luac.c"
    "${LUA_SRC}/onelua.c"
)

add_library(lualoom SHARED ${LUA_SOURCES})

target_compile_definitions(lualoom PRIVATE $<$<PLATFORM_ID:Windows>:LUA_BUILD_AS_DLL>)
target_include_directories(lualoom PUBLIC "${LUA_SRC}")

set_target_properties(lualoom PROPERTIES
    C_STANDARD 99
    POSITION_INDEPENDENT_CODE ON
    OUTPUT_NAME "lualoom"
)

if(UNIX AND NOT APPLE)
    target_link_libraries(lualoom PRIVATE m dl)
elseif(APPLE)
    target_link_libraries(lualoom PRIVATE m)
endif()
