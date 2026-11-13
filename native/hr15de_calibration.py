"""Measure the authored model through the exact native bridge used by the app."""
import argparse
import ctypes as c
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('app_directory', type=Path)
parser.add_argument('--engine', type=int, default=3)
parser.add_argument('--flywheel', type=float, default=1.0)
parser.add_argument('--idle-seconds', type=int, default=10)
parser.add_argument('--check-response', action='store_true')
args = parser.parse_args()
lib = c.CDLL(str(args.app_directory / 'SteeringTrainer.EngineSim.dll'))
lib.steering_engine_create.argtypes = [c.c_char_p, c.c_int] + [c.c_double] * 5
lib.steering_engine_create.restype = c.c_void_p
lib.steering_engine_last_error.restype = c.c_char_p

class Snapshot(c.Structure):
    _fields_ = [(name, c.c_double) for name in ('rpm', 'pedal', 'effective', 'rpm_rate', 'wall_ms')] + [('frequency', c.c_int), ('steps', c.c_int)]

lib.steering_engine_step.argtypes = [c.c_void_p, c.c_double, c.c_double, c.POINTER(Snapshot)]
lib.steering_engine_measure_torque.argtypes = [c.c_void_p, c.c_double, c.POINTER(c.c_double)]
lib.steering_engine_destroy.argtypes = [c.c_void_p]
lib.steering_engine_reset.argtypes = [c.c_void_p]
engine = lib.steering_engine_create(str(args.app_directory / 'EngineSimAssets').encode('mbcs'), args.engine, args.flywheel, 1, 1.5, .04, .06)
if not engine:
    raise RuntimeError(lib.steering_engine_last_error().decode('mbcs'))
try:
    sample = Snapshot()
    for i in range(args.idle_seconds * 10):
        assert lib.steering_engine_step(engine, 0, .1, c.byref(sample))
        if (i + 1) % 10 == 0:
            print(f'idle_t={(i+1)/10:.1f}s rpm={sample.rpm:.1f}', flush=True)
    for target in (3600, 6000):
        torque = c.c_double()
        assert lib.steering_engine_measure_torque(engine, target, c.byref(torque))
        assert lib.steering_engine_step(engine, 1, .001, c.byref(sample))
        print(f'target={target} actual={sample.rpm:.1f} torque={torque.value:.2f} Nm power={torque.value*target*.000104719755:.2f} kW', flush=True)
    if args.check_response:
        assert lib.steering_engine_reset(engine)
        for _ in range(30):
            assert lib.steering_engine_step(engine, 0, .1, c.byref(sample))
        baseline = sample.rpm
        for _ in range(10):
            assert lib.steering_engine_step(engine, .85, .1, c.byref(sample))
        peak = sample.rpm
        for _ in range(20):
            assert lib.steering_engine_step(engine, 0, .1, c.byref(sample))
        print(f'reset_idle={baseline:.1f} blip_peak={peak:.1f} returned={sample.rpm:.1f}', flush=True)
        assert 600 < baseline < 1050, 'Reset did not return to running idle'
        assert peak > baseline + 300, 'Throttle did not raise RPM'
        assert sample.rpm < peak, 'RPM did not fall after throttle release'
finally:
    lib.steering_engine_destroy(engine)
