#include "SteeringTrainerEngineBridge.h"

#include "engine.h"
#include "simulator.h"
#include "units.h"
#include "vehicle.h"
#include "transmission.h"
#include "compiler.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <random>
#include <regex>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr double InitialRpm = 850.0;
constexpr double DefaultStepSeconds = 0.02;
constexpr int OutputSampleRate = 44100;
const std::filesystem::path EngineScripts[] = { "engines/atg-video-1/05_honda_vtec.mr", "engines/atg-video-2/03_2jz.mr", "engines/atg-video-2/07_gm_ls.mr", "engines/steering-trainer/hr15de.mr" };
thread_local std::string LastError;

struct EngineHandle {
    std::mutex mutex;
    Engine* engine = nullptr;
    Vehicle* vehicle = nullptr;
    Transmission* transmission = nullptr;
    Simulator* simulator = nullptr;
    double responseSeconds = 0.08;
    double closeResponseSeconds = 0.08;
    double nativeThrottleGamma = 2.0;
    double resetRpm = InitialRpm;
    double rotationDirection = -1.0;
    double pedalSensitivity = 1.0;
    double effectiveThrottle = 0.0;
    double previousRpm = InitialRpm;
    double heldRpm = 0.0;
    std::vector<std::int16_t> discardedAudio = std::vector<std::int16_t>(2048);
};

std::string readFile(const std::filesystem::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) throw std::runtime_error("Cannot read standard engine script: " + path.string());
    return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
}

void replaceOnce(std::string& text, const std::regex& pattern, const std::string& replacement, const char* description) {
    const std::string updated = std::regex_replace(text, pattern, replacement, std::regex_constants::format_first_only);
    if (updated == text) throw std::runtime_error(std::string("Could not adjust standard engine field: ") + description);
    text = updated;
}

std::filesystem::path prepareEngineScript(const std::filesystem::path& assets, int engineId, double flywheel, double friction) {
    auto source = readFile(assets / EngineScripts[engineId]);
    replaceOnce(source,
        std::regex(R"((friction_torque:\s*[0-9.]+\s*\*\s*units\.(?:lb_ft|Nm)))"),
        "$1 * " + std::to_string(friction), "friction torque");
    if (engineId == 0) replaceOnce(source,
        std::regex(R"((moment_of_inertia:\s*0\.22986844776863666\s*\*\s*0\.5))"),
        "$1 * " + std::to_string(flywheel), "Honda lumped rotational inertia");
    else replaceOnce(source,
        std::regex(R"(moment_of_inertia:\s*crank_moment\s*\+\s*flywheel_moment\s*\+\s*other_moment)"),
        "moment_of_inertia: crank_moment + flywheel_moment * " + std::to_string(flywheel) + " + other_moment", "flywheel inertia");

    const auto directory = std::filesystem::temp_directory_path() / "SteeringTrainerEngineSim";
    std::filesystem::create_directories(directory);
    const auto generated = directory / "standard_honda_vtec.mr";
    std::ofstream output(generated, std::ios::binary | std::ios::trunc);
    output << source;
    if (!output) throw std::runtime_error("Cannot write adjusted engine script");
    return generated;
}

void stepBlock(EngineHandle& handle, double pedal, double seconds) {
    const double shaped = std::pow(std::clamp(pedal, 0.0, 1.0), handle.pedalSensitivity);
    const double tau = shaped > handle.effectiveThrottle ? handle.responseSeconds : handle.closeResponseSeconds;
    const double alpha = tau <= 0.0 ? 1.0 : 1.0 - std::exp(-seconds / tau);
    handle.effectiveThrottle += (shaped - handle.effectiveThrottle) * alpha;
    // DirectThrottleLinkage applies the authored gamma (2, or 1.5 for HR15DE).
    // Compensate that curve so the requested
    // pedal curve reaches the engine unchanged.
    handle.engine->setSpeedControl(std::pow(handle.effectiveThrottle, 1.0 / handle.nativeThrottleGamma));

    handle.simulator->startFrame(seconds);
    // Let the solver update crank, rods and pistons consistently. A held
    // clutch uses the core dynamometer constraint, never a velocity overwrite.
    while (handle.simulator->simulateStep()) { }
    handle.simulator->endFrame();

    // The app does not play engine audio. Drain the upstream synthesizer to keep
    // its buffers bounded while retaining its normal PistonEngineSimulator path.
    handle.simulator->synthesizer().pumpAudioRendering();
    handle.simulator->readAudioOutput(static_cast<int>(handle.discardedAudio.size()), handle.discardedAudio.data());
}
}

extern "C" STEERING_ENGINE_API const char* steering_engine_last_error() {
    return LastError.c_str();
}

extern "C" STEERING_ENGINE_API void* steering_engine_create(const char* assetDirectory, int engineId, double flywheelMultiplier, double frictionMultiplier, double pedalSensitivity, double responseSeconds, double closeResponseSeconds) {
    try {
        LastError.clear();
        if (!assetDirectory || engineId < 0 || engineId > 3 || !std::isfinite(flywheelMultiplier) || flywheelMultiplier <= 0
            || !std::isfinite(frictionMultiplier) || frictionMultiplier < 0
            || !std::isfinite(pedalSensitivity) || pedalSensitivity <= 0
            || !std::isfinite(responseSeconds) || responseSeconds < 0
            || !std::isfinite(closeResponseSeconds) || closeResponseSeconds < 0) {
            throw std::runtime_error("Invalid engine model setting");
        }
        const std::filesystem::path assets = std::filesystem::absolute(assetDirectory);
        const auto engineScript = prepareEngineScript(assets, engineId, flywheelMultiplier, frictionMultiplier);
        const auto entryPath = std::filesystem::temp_directory_path() / "SteeringTrainerEngineSim" / "entry.mr";
        {
            std::ofstream entry(entryPath, std::ios::binary | std::ios::trunc);
            entry << "import \"" << engineScript.generic_string() << "\"\n\nmain()\n";
        }

        es_script::Compiler compiler;
        compiler.initialize(assets.string());
        if (!compiler.compile(entryPath.string())) {
            compiler.destroy();
            throw std::runtime_error("Open Engine Simulator could not compile its standard engine script; see error_log.log");
        }
        const auto output = compiler.execute();
        compiler.destroy();
        if (!output.engine || !output.vehicle || !output.transmission) {
            throw std::runtime_error("Standard engine script did not create a complete simulation");
        }

        auto handle = std::make_unique<EngineHandle>();
        handle->engine = output.engine;
        handle->vehicle = output.vehicle;
        handle->transmission = output.transmission;
        handle->responseSeconds = responseSeconds;
        handle->closeResponseSeconds = closeResponseSeconds;
        handle->nativeThrottleGamma = engineId == 3 ? 1.5 : 2.0;
        handle->resetRpm = engineId == 3 ? 800.0 : InitialRpm;
        handle->pedalSensitivity = pedalSensitivity;
        handle->simulator = handle->engine->createSimulator(handle->vehicle, handle->transmission, OutputSampleRate);
        handle->simulator->setSimulationFrequency(static_cast<int>(handle->engine->getSimulationFrequency()));
        handle->simulator->setSynthesizerLatencyCorrectionEnabled(false);
        handle->engine->getIgnitionModule()->m_enabled = true;
        handle->engine->setSpeedControl(0.0);
        handle->simulator->m_dyno.m_enabled = false;
        handle->simulator->m_starterMotor.m_enabled = true;

        // Stabilize the core model before publishing the first UI value.
        for (int i = 0; i < 300; ++i) {
            stepBlock(*handle, 0.0, DefaultStepSeconds);
            if (i >= 74 && handle->engine->getRpm() > 800) break;
        }
        handle->simulator->m_starterMotor.m_enabled = false;
        handle->effectiveThrottle = 0.0;
        handle->engine->setSpeedControl(0.0);
        for (int i = 0; i < 25; ++i) stepBlock(*handle, 0.0, DefaultStepSeconds);
        if (!std::isfinite(handle->engine->getRpm()) || handle->engine->getRpm() < 400) {
            steering_engine_destroy(handle.release());
            throw std::runtime_error("Engine could not sustain RPM after automatic starting");
        }
        handle->previousRpm = handle->engine->getRpm();
        // getRpm() is absolute. The core negates authored starter_speed, so
        // model ID / the script's starter sign cannot specify running direction.
        handle->rotationDirection = std::copysign(1.0, handle->engine->getOutputCrankshaft()->m_body.v_theta);
        return handle.release();
    }
    catch (const std::exception& error) {
        LastError = error.what();
        return nullptr;
    }
}

// Diagnostic only: hold a fixed speed on the core dynamometer, then return
// its steady mean reaction torque. The app's free-rev loop never calls this.
extern "C" STEERING_ENGINE_API int steering_engine_measure_torque(void* opaque, double rpm, double* torqueNm) {
    if (!opaque || !torqueNm || !std::isfinite(rpm) || rpm <= 0) return 0;
    auto& handle = *static_cast<EngineHandle*>(opaque);
    const std::lock_guard<std::mutex> lock(handle.mutex);
    auto& dyno = handle.simulator->m_dyno;
    dyno.m_enabled = true;
    dyno.m_hold = true;
    dyno.m_rotationSpeed = units::rpm(rpm);
    double total = 0.0;
    for (int i = 0; i < 200; ++i) {
        stepBlock(handle, 1.0, DefaultStepSeconds);
        if (i >= 100) total += handle.simulator->getFilteredDynoTorque();
    }
    *torqueNm = total / 100.0;
    dyno.m_enabled = false;
    dyno.m_hold = false;
    return std::isfinite(*torqueNm) ? 1 : 0;
}

extern "C" STEERING_ENGINE_API int steering_engine_step(void* opaque, double pedal, double elapsedSeconds, SteeringEngineSnapshot* snapshot) {
    try {
        if (!opaque || !snapshot || !std::isfinite(pedal) || !std::isfinite(elapsedSeconds) || elapsedSeconds <= 0) {
            throw std::runtime_error("Invalid simulation step");
        }
        auto& handle = *static_cast<EngineHandle*>(opaque);
        const std::lock_guard<std::mutex> lock(handle.mutex);
        const auto begin = std::chrono::steady_clock::now();
        double remaining = std::clamp(elapsedSeconds, 0.001, 0.1);
        int steps = 0;
        const double frequency = handle.simulator->getSimulationFrequency();
        while (remaining > 0.0) {
            const double block = std::min(remaining, std::max(1.0 / frequency, DefaultStepSeconds));
            stepBlock(handle, pedal, block);
            remaining -= block;
            ++steps;
        }
        const double rpm = handle.engine->getRpm();
        const double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - begin).count();
        snapshot->rpm = rpm;
        snapshot->pedal = std::clamp(pedal, 0.0, 1.0);
        snapshot->effectiveThrottle = handle.effectiveThrottle;
        snapshot->rpmPerSecond = (rpm - handle.previousRpm) / std::clamp(elapsedSeconds, 0.001, 0.1);
        snapshot->updateMilliseconds = elapsed * 1000.0;
        snapshot->simulationFrequency = static_cast<int>(frequency);
        snapshot->simulationSteps = handle.simulator->getFrameIterationCount();
        handle.previousRpm = rpm;
        return 1;
    }
    catch (const std::exception& error) {
        LastError = error.what();
        return 0;
    }
}

extern "C" STEERING_ENGINE_API int steering_engine_reset(void* opaque) {
    try {
        if (!opaque) return 0;
        auto& handle = *static_cast<EngineHandle*>(opaque);
        const std::lock_guard<std::mutex> lock(handle.mutex);
        handle.heldRpm = 0.0;
        handle.simulator->m_dyno.m_enabled = false;
        handle.simulator->m_dyno.m_hold = false;
        for (int i = 0; i < handle.engine->getCrankshaftCount(); ++i) {
            auto* shaft = handle.engine->getCrankshaft(i);
            shaft->m_body.v_theta = handle.rotationDirection * units::rpm(handle.resetRpm);
        }
        handle.effectiveThrottle = 0.0;
        handle.previousRpm = handle.resetRpm;
        handle.engine->setSpeedControl(0.0);
        return 1;
    }
    catch (const std::exception& error) { LastError = error.what(); return 0; }
}

extern "C" STEERING_ENGINE_API int steering_engine_prepare_rpm(void* opaque, double rpm) {
    try {
        if (!opaque || !std::isfinite(rpm) || rpm < 400.0 || rpm > 12000.0)
            throw std::runtime_error("Invalid preparation RPM");
        auto& handle = *static_cast<EngineHandle*>(opaque);
        const std::lock_guard<std::mutex> lock(handle.mutex);
        handle.heldRpm = rpm;
        handle.effectiveThrottle = 0.0;
        handle.engine->setSpeedControl(0.0);
        handle.engine->getIgnitionModule()->m_enabled = true;
        handle.simulator->m_dyno.m_rotationSpeed = units::rpm(rpm);
        handle.simulator->m_dyno.m_enabled = true;
        handle.simulator->m_dyno.m_hold = true;
        handle.simulator->m_starterMotor.m_enabled = false;
        // Accelerate the whole mechanism in its already-running direction;
        // settle cylinders and drivetrain outside scoring (maximum 2 seconds).
        for (int i = 0; i < 100; ++i) {
            stepBlock(handle, 0.0, DefaultStepSeconds);
            if (i >= 9 && std::abs(handle.engine->getRpm() - rpm) <= 10.0) break;
        }
        if (!std::isfinite(handle.engine->getRpm()) || std::abs(handle.engine->getRpm() - rpm) > 50.0)
            throw std::runtime_error("Core could not prepare the requested clutch RPM");
        handle.previousRpm = handle.engine->getRpm();
        return 1;
    }
    catch (const std::exception& error) { LastError = error.what(); return 0; }
}

extern "C" STEERING_ENGINE_API int steering_engine_release_clutch(void* opaque) {
    if (!opaque) { LastError = "Missing engine handle"; return 0; }
    auto& handle = *static_cast<EngineHandle*>(opaque);
    const std::lock_guard<std::mutex> lock(handle.mutex);
    handle.heldRpm = 0.0;
    handle.simulator->m_dyno.m_enabled = false;
    handle.simulator->m_dyno.m_hold = false;
    return 1;
}

extern "C" STEERING_ENGINE_API void steering_engine_destroy(void* opaque) {
    auto* handle = static_cast<EngineHandle*>(opaque);
    if (!handle) return;
    if (handle->simulator) { handle->simulator->releaseSimulation(); delete handle->simulator; }
    if (handle->engine) { handle->engine->destroy(); delete handle->engine; }
    delete handle->vehicle;
    delete handle->transmission;
    delete handle;
}
