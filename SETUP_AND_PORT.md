# MATLAB-faithful Unity/OpenXR port

## Authority and model

The only behavioural authority is `../info/PLS_Stimulus_Base.m`, guarded by SHA-256
`08352712BD443C0638BAEDDEF2F5BF1032FEACBD2FE663F313255E96B546EAF2`.
`SwarmSimulation.cs` preserves double precision, self inclusion, inclusive metric/topological ties, synchronous old-state velocity updates, ordinary (non-toroidal) neighbour distance, vector noise, wrapping, and the source's rare 3D cancellation-speed defect.

Defaults are 1,000 dots, source 2D, metric radius 2 m, topological K 2 (the same `sim_param` when changing rule), cap 1,000 m, speed 3 m/s, a 20 m cube at z=50–70 m, wrapping on all axes, a source aperture plane at 0.715 m, and six-pixel dots. Desktop intentionally defaults to 2D; VR intentionally defaults to 3D. Startup never changes the configured dimension.

All active researcher values are Inspector fields. The `Stimulus Controller` custom Inspector labels simulation units and validates the settings; `Dot Field Renderer`, `Desktop Fly Camera`, and camera expose presentation and clipping values.

## Scene creation

- **Tools > Dots Stimulus > Create desktop stimulus** creates a rotation-only desktop observer with no XR or eye components.
- **Tools > Dots Stimulus > Create VR stimulus** enables Windows OpenXR initialization and the HTC Vive Controller profile, disables Eye Gaze Interaction, then creates an `XR Origin`, camera offset, stereo camera, AudioListener, and Input System head-pose bindings. It adds no locomotion, controller interactors, eye tracking, calibration, or recording components.

The VR stimulus aligns its coordinate origin to the first valid tracked head pose once, then remains world-fixed. In 3D, the unchanged MATLAB z=50–70 m coordinates are presentation-translated to -10–+10 m, placing the original 20 m cube around the aligned head without changing positions used by the simulation, neighbour distances, movement, or wrapping. C realigns it without restarting the swarm. The headset owns its stereo projections; the desktop-only source FOV is not forced in VR.

Desktop starts with the aperture disabled and maps the unchanged 2D simulation across the complete active viewport using a fixed orthographic camera. Mouse and I/J/K/L rotation are disabled in 2D. Its display geometry keeps the MATLAB 0.587 m width and derives height from the current viewport aspect; at 800×600 this is the source 0.44025 m height. Pressing D switches to a perspective camera and enables rotation-only mouse/keyboard control. In desktop 3D, the unchanged z=50–70 m coordinates are presentation-translated to -10–+10 m so rotation previews the immersive headset view. Returning to 2D restores the initial camera orientation. `Fill Desktop 2D Viewport` and `Centre Desktop 3D On Viewer` can be disabled, and the aperture enabled, to compare against the literal source projection (the intentionally small central square). These are rendering transforms only and do not alter simulation positions or distances.

Immersive VR 3D hides the desktop aperture by default so dots surround the viewer. Pressing D switches to a physical virtual display in front of the viewer and realigns it to the current head pose. Its default width 0.587 m, height 0.44025 m, and distance 0.715 m reproduce the MATLAB display geometry (about 44.7° horizontal by 34.2° vertical), while its size, distance, and border are editable on `Dot Field Renderer`.

Dot count and width/height/depth are editable density controls. The custom Inspector shows derived 2D dots/m² and 3D dots/m³. Metric radius, topological K and radius cap, speed, noise, wrapping, presets, and every other MATLAB simulation value remain editable with source defaults.

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
