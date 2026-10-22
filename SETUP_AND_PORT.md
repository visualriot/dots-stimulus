# MATLAB-faithful Unity/OpenXR port

## Authority and model

The only behavioural authority is `../info/PLS_Stimulus_Base.m`, guarded by SHA-256
`08352712BD443C0638BAEDDEF2F5BF1032FEACBD2FE663F313255E96B546EAF2`.
`SwarmSimulation.cs` preserves double precision, self inclusion, inclusive metric/topological ties, synchronous old-state velocity updates, ordinary (non-toroidal) neighbour distance, vector noise, the unchanged 2D wrapping path, and the source's rare 3D cancellation-speed defect. The researcher-requested 3D boundary extension is applied only after the MATLAB alignment/noise direction has been calculated.

Defaults are 1,000 dots, source 2D, metric radius 2 m, topological K 2 (the same `sim_param` when changing rule), cap 1,000 m, speed 3 m/s, a 20 m box at z=50–70 m, a source aperture plane at 0.715 m, and six-pixel dots. Desktop intentionally defaults to 2D; VR intentionally defaults to 3D. Startup never changes the configured dimension. In 3D, researchers can select that box or a 10 m-radius sphere and both use a 1 m magnetic boundary buffer by default; 2D retains the source wrapping behavior.

All active researcher values are Inspector fields. The `Stimulus Controller` custom Inspector labels simulation units and validates the settings; `Dot Field Renderer`, `Desktop Fly Camera`, and camera expose presentation and clipping values.

## Scene creation

- **Tools > Dots Stimulus > Create desktop stimulus** creates a desktop observer with 3D rotation, mouse-wheel forward/back movement, and middle-mouse left/right/up/down panning, but no XR or eye components.
- **Tools > Dots Stimulus > Create VR stimulus** enables Windows OpenXR initialization and the HTC Vive Controller profile, disables Eye Gaze Interaction, then creates an `XR Origin`, camera offset, stereo camera, AudioListener, and Input System head-pose bindings. It adds no locomotion, controller interactors, eye tracking, calibration, or recording components.

The VR stimulus aligns its coordinate origin to the first valid tracked head pose once, then remains world-fixed. In 3D, `Viewer Placement` can put the selected volume around the aligned head or straight ahead of it. Outside mode measures its editable clearance from the head to the nearest box face or sphere surface; the default is 10 m. This is a rigid presentation translation and never moves the tracked camera rig. C realigns it without restarting the swarm. The headset owns its stereo projections; the desktop-only source FOV is not forced in VR.

Desktop starts with the aperture disabled and maps the unchanged 2D simulation across the complete active viewport using a fixed orthographic camera. All mouse movement is disabled in 2D. Its display geometry keeps the MATLAB 0.587 m width and derives height from the current viewport aspect; at 800×600 this is the source 0.44025 m height. Pressing D switches to a perspective camera and enables mouse/keyboard rotation, mouse-wheel movement along the viewing direction, and middle-mouse drag panning in the current camera plane. Desktop 3D uses the same Centre/Outside placement as VR. Returning to 2D restores the initial camera position and orientation. `Fill Desktop 2D Viewport` remains a 2D-only rendering transform and does not alter simulation positions or distances.

Immersive VR 3D hides the desktop aperture by default so dots surround the viewer. Pressing D switches to a physical virtual display in front of the viewer and realigns it to the current head pose. Its default width 0.587 m, height 0.44025 m, and distance 0.715 m reproduce the MATLAB display geometry (about 44.7° horizontal by 34.2° vertical), while its size, distance, and border are editable on `Dot Field Renderer`.

Dot count and width/height are editable 2D density controls. The 3D box uses width/height/depth, while the sphere uses its independent radius. The custom Inspector shows derived 2D dots/m², shape-correct 3D dots/m³, and core/outer dimensions. Metric radius, topological K and radius cap, speed, noise, 2D wrapping, presets, and every other applicable MATLAB simulation value remain editable with source defaults.

Inside either 3D core, movement follows the MATLAB direction unchanged. `Buffer Behaviour` selects one of two 3D responses. `Magnetic Steering` uses a smooth penetration-based blend to turn the dot inward at configured speed. `Smooth Linear Bounce` locks the incoming line, decelerates smoothly to the outer box/sphere limit, then retraces that exact line to the core before releasing the dot back to MATLAB steering. It does not curve along the boundary. Both modes have an outer projection safeguard for unusually long frames. The buffer is invisible and uses no colliders or Rigidbody physics. The legacy 3D wrap flags remain serialized for scene compatibility but are inactive.

## Eye tracking temporarily disabled

The SRanipal source and recorder scripts are retained for later investigation, but they are not part of newly generated VR scenes. `VIVE_SRANIPAL` is removed, all five SRanipal native plug-ins are disabled for Editor, Eye Gaze Interaction is disabled, and the scene contains no framework, provider, calibration, recorder, or recording monitor object. F3 and F5 therefore perform no eye-tracking action and no participant files are created.

This isolation is intentional because the combined SRanipal/OpenXR scene caused native process termination on the target machine. Eye tracking must not be re-enabled until it is investigated separately from stimulus rendering and head tracking.

SteamVR must remain the active system OpenXR runtime and must recognize the Vive Pro Eye, but `SR_Runtime` is not required for this no-eye-tracking phase.

## Verification

Run the standalone checks from the repository root:

```powershell
dotnet run --project verification/SwarmChecks.csproj --configuration Release
```

They fail immediately if the MATLAB SHA changes. `verification/MatlabReferenceCases.m` is an R2024b batch oracle with explicit states, injected noise, and fallback angle for metric/topological selection, ties, cancellation, 2D projection, movement, wrapping, and update order. Review the MATLAB source before regenerating its CSV.

Hardware acceptance for the current phase requires stereo rendering, head tracking, tracked/world-fixed alignment, 2D/3D switching, and N=1000 metric/topological profiling at 90 Hz. Eye calibration, gaze samples, and recording are explicitly out of scope until the native crash is resolved independently.
