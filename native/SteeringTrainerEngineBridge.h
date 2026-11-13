#pragma once

#ifdef _WIN32
#ifdef STEERING_ENGINE_BRIDGE_EXPORTS
#define STEERING_ENGINE_API __declspec(dllexport)
#else
#define STEERING_ENGINE_API __declspec(dllimport)
#endif
#else
#define STEERING_ENGINE_API
#endif

extern "C" {
struct SteeringEngineSnapshot {
    double rpm;
    double pedal;
    double effectiveThrottle;
    double rpmPerSecond;
    double updateMilliseconds;
    int simulationFrequency;
    int simulationSteps;
};

STEERING_ENGINE_API void* steering_engine_create(const char* assetDirectory, int engineId, double flywheelMultiplier, double frictionMultiplier, double pedalSensitivity, double responseSeconds, double closeResponseSeconds);
STEERING_ENGINE_API int steering_engine_measure_torque(void* handle, double rpm, double* torqueNm);
STEERING_ENGINE_API int steering_engine_step(void* handle, double pedal, double elapsedSeconds, SteeringEngineSnapshot* snapshot);
STEERING_ENGINE_API int steering_engine_reset(void* handle);
STEERING_ENGINE_API int steering_engine_prepare_rpm(void* handle, double rpm);
STEERING_ENGINE_API int steering_engine_release_clutch(void* handle);
STEERING_ENGINE_API void steering_engine_destroy(void* handle);
STEERING_ENGINE_API const char* steering_engine_last_error();
}
