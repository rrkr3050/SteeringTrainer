cmake_minimum_required(VERSION 3.24)

project(steering_trainer_engine_bridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>" CACHE STRING "Use the static MSVC runtime for a portable app bridge")
set(BUILD_TESTING OFF CACHE BOOL "Disable upstream test downloads" FORCE)

if(NOT DEFINED FLEX_EXECUTABLE)
    find_program(FLEX_EXECUTABLE NAMES flex win_flex REQUIRED)
endif()
if(NOT DEFINED BISON_EXECUTABLE)
    find_program(BISON_EXECUTABLE NAMES bison win_bison REQUIRED)
endif()
if(NOT DEFINED FLEX_INCLUDE_DIR)
    find_path(FLEX_INCLUDE_DIR FlexLexer.h REQUIRED)
endif()

set(ENGINE_SIM_BUILD_SCRIPTING ON CACHE BOOL "Build the pinned Open Engine Simulator scripting layer" FORCE)
set(ENGINE_SIM_BUILD_DESKTOP OFF CACHE BOOL "No upstream desktop host" FORCE)
set(ENGINE_SIM_BUILD_HEADLESS OFF CACHE BOOL "The bridge owns its simulation loop" FORCE)
set(ENGINE_SIM_FETCH_SDL3 OFF CACHE BOOL "No SDL dependency" FORCE)
add_subdirectory(open-engine-sim)

add_library(steering_engine_bridge SHARED SteeringTrainerEngineBridge.cpp)
target_link_libraries(steering_engine_bridge PRIVATE engine-sim::scripting)
target_include_directories(steering_engine_bridge PRIVATE open-engine-sim/include open-engine-sim/scripting/include)
target_compile_definitions(steering_engine_bridge PRIVATE STEERING_ENGINE_BRIDGE_EXPORTS)
set_target_properties(steering_engine_bridge PROPERTIES OUTPUT_NAME "SteeringTrainer.EngineSim")

add_executable(engine_bridge_smoke engine_bridge_smoke.cpp)
target_link_libraries(engine_bridge_smoke PRIVATE steering_engine_bridge)
