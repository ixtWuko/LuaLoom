cmake_minimum_required(VERSION 3.20)
project(lua54 LANGUAGES C)

if(NOT DEFINED LUA_SRC)
    set(LUA_SRC "${CMAKE_CURRENT_SOURCE_DIR}/../build/lua-5.4.9/src")
endif()

if(NOT EXISTS "${LUA_SRC}/lua.h")
    message(FATAL_ERROR "Lua source not found at ${LUA_SRC}. Run scripts/fetch-lua.ps1 first.")
endif()

set(LUA_SOURCES
    "${LUA_SRC}/lapi.c"
    "${LUA_SRC}/lauxlib.c"
    "${LUA_SRC}/lbaselib.c"
    "${LUA_SRC}/lcode.c"
    "${LUA_SRC}/lcorolib.c"
    "${LUA_SRC}/lctype.c"
    "${LUA_SRC}/ldblib.c"
    "${LUA_SRC}/ldebug.c"
    "${LUA_SRC}/ldo.c"
    "${LUA_SRC}/ldump.c"
    "${LUA_SRC}/lfunc.c"
    "${LUA_SRC}/lgc.c"
    "${LUA_SRC}/linit.c"
    "${LUA_SRC}/liolib.c"
    "${LUA_SRC}/llex.c"
    "${LUA_SRC}/lmathlib.c"
    "${LUA_SRC}/lmem.c"
    "${LUA_SRC}/loadlib.c"
    "${LUA_SRC}/lobject.c"
    "${LUA_SRC}/lopcodes.c"
    "${LUA_SRC}/loslib.c"
    "${LUA_SRC}/lparser.c"
    "${LUA_SRC}/lstate.c"
    "${LUA_SRC}/lstring.c"
    "${LUA_SRC}/lstrlib.c"
    "${LUA_SRC}/ltable.c"
    "${LUA_SRC}/ltablib.c"
    "${LUA_SRC}/ltm.c"
    "${LUA_SRC}/lundump.c"
    "${LUA_SRC}/lutf8lib.c"
    "${LUA_SRC}/lvm.c"
    "${LUA_SRC}/lzio.c"
)

add_library(lua54 SHARED ${LUA_SOURCES})

target_compile_definitions(lua54 PRIVATE LUA_BUILD_AS_DLL)
target_include_directories(lua54 PUBLIC "${LUA_SRC}")

set_target_properties(lua54 PROPERTIES
    C_STANDARD 99
    OUTPUT_NAME "lua54"
)
