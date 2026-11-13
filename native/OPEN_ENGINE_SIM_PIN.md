# Open Engine Simulator dependency

The native bridge in this folder uses the `zabayone/open-engine-sim` fork at the immutable commit below. The fork is based on `carlesonielfa/open-engine-sim` and was selected because it retains the reusable core and adds a headless integration path.

- Repository: https://github.com/zabayone/open-engine-sim
- Pinned commit: `9abd876e8521a39ad3b423bf03a4b2325d4f8338`
- Upstream comparison: https://github.com/carlesonielfa/open-engine-sim
- Upstream `carlesonielfa` master observed during selection: `1e226ee7bfbeb1d5012c7696aee82a57355df281`
- `simple-2d-constraint-solver` submodule: `e009f4ff1c9c4c5874e865e893cdb62e208fb2b3`
- `csv-io` submodule: `2112c55e1e831c8f7a1b91de23722b7926ad3d00`
- `piranha` submodule: `432f0b122bb1663b686c553c7e7269300afac3bc`

The upstream core is built without its SDL desktop host. Its `LICENSE` file is included next to the native assets and copied to the application output. Preserve that MIT notice when redistributing the app.

The default model is the authored Honda B18C5 VTEC I4 script (`assets/engines/atg-video-1/05_honda_vtec.mr`). Toyota 2JZ I6 (`assets/engines/atg-video-2/03_2jz.mr`) and GM LS V8 (`assets/engines/atg-video-2/07_gm_ls.mr`) are selectable. Selection regenerates and automatically starts the model; parameter changes regenerate on **Apply**. Flywheel and friction adjustments modify a generated copy, leaving vendored definitions intact. Honda authors a lumped rotational inertia, so its inertia multiplier scales that entire value. Pedal shaping and response delay are passed to the same simulator core. See `../ENGINE_MODEL_NOTES.md` for authoring information.

A custom estimated Nissan HR15DE model is stored outside the pinned dependency in `EngineModels/hr15de.mr` and packaged as `assets/engines/steering-trainer/hr15de.mr`. It derives port-flow and acoustic definitions from the MIT Honda script, retaining the original copyright notice and accompanying MIT license. It uses fixed cams, its own inertia split, and separate open/close response constants. Its geometry, assumed values and calibration are documented in `../HR15DE_MODEL.md`.
