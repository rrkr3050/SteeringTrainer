#include "SteeringTrainerEngineBridge.h"

#include <algorithm>
#include <cmath>
#include <iostream>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    for (int engineId = 0; engineId < 4; ++engineId) {
    std::cout << "engine_id=" << engineId << '\n';
    void* engine = steering_engine_create(argv[1], engineId, 1.0, 1.0, engineId == 3 ? 1.5 : 1.0, engineId == 3 ? 0.04 : 0.08, engineId == 3 ? 0.06 : 0.08);
    if (!engine) {
        std::cerr << steering_engine_last_error() << '\n';
        return 1;
    }
    SteeringEngineSnapshot snapshot{};
    double baselineRpm = 0.0;
    double peakRpm = 0.0;
    for (int i = 0; i < 15; ++i) {
        const double pedal = i < 3 ? 0.0 : (i < 10 ? 0.85 : 0.0);
        if (!steering_engine_step(engine, pedal, 0.1, &snapshot)) {
            std::cerr << steering_engine_last_error() << '\n';
            steering_engine_destroy(engine);
            return 1;
        }
        if (i == 2) baselineRpm = snapshot.rpm;
        if (pedal > 0) peakRpm = std::max(peakRpm, snapshot.rpm);
        std::cout << "input=" << pedal << " rpm=" << snapshot.rpm
                  << " steps=" << snapshot.simulationSteps
                  << " update_ms=" << snapshot.updateMilliseconds << '\n';
    }
    if (engineId == 3) {
        for (double rpm : {3600.0, 6000.0}) {
            double torque = 0;
            if (!steering_engine_measure_torque(engine, rpm, &torque)) return 1;
            std::cout << "dyno_rpm=" << rpm << " torque_nm=" << torque << " power_kw=" << torque * rpm * 0.000104719755 << '\n';
        }
    }
    steering_engine_destroy(engine);

    if (!(peakRpm > baselineRpm + 300.0)) {
        std::cerr << "Pedal input did not raise simulated RPM\n";
        return 1;
    }

    // Exercise the settings that are applied by regenerating the scripted model.
    engine = steering_engine_create(argv[1], engineId, 0.7, 1.4, 1.3, 0.16, 0.16);
    if (!engine) {
        std::cerr << steering_engine_last_error() << '\n';
        return 1;
    }
    if (!steering_engine_step(engine, 0.75, 0.1, &snapshot) || !std::isfinite(snapshot.rpm) || snapshot.rpm <= 0) {
        std::cerr << "Adjusted engine model did not produce a valid RPM\n";
        steering_engine_destroy(engine);
        return 1;
    }
    std::cout << "adjusted_model_rpm=" << snapshot.rpm << " update_ms=" << snapshot.updateMilliseconds << '\n';
    steering_engine_destroy(engine);
    }
    return 0;
}
