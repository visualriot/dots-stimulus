# Desktop stimulus

Create it with **Tools > Dots Stimulus > Create desktop stimulus**. It starts with MATLAB's 2D simulation in a fixed orthographic camera, scaled across the complete Game-view viewport, and with the source aperture disabled. Mouse and I/J/K/L input do nothing in 2D. Press D to switch to a perspective immersive 3D desktop preview and enable rotation controls.

Desktop scenes stop the globally configured XR loader and disable URP XR rendering on their camera, so the Game window is monoscopic rather than an OpenXR `Left Eye` preview. In the Unity Editor, set the Game-view Scale control to Fit/1×; higher zoom values intentionally crop and pan the preview and do not represent the application viewport.

The camera uses the source display width (0.587 m) and viewing distance (0.715 m). At the 800×600 reference aspect this derives the source 34.2238° vertical FOV; by default the vertical FOV adapts to the active viewport aspect using MATLAB's `Screenheight = Screenwidth / aspect` relation. Its black, unprocessed image uses six-pixel dots by default. All values are editable in the Inspector.

`Fill Desktop 2D Viewport` and `Centre Desktop 3D On Viewer` are presentation-only switches on `Dot Field Renderer`: they never scale the simulation positions used for neighbours, movement, or wrapping. Disable both and enable `Use Aperture` to inspect the literal MATLAB projection, which intentionally appears as a central square.

Hold right mouse and drag, or use I/J/K/L, to rotate the simulated head. C restores its initial orientation. Position never changes: this is a headset-view substitute, not free locomotion. The 3D presentation places the unchanged 20 m simulation volume around the camera, and the dot field is never parented to it.

Shared controls are D (2D/3D), A (aperture), R (restart), arrows (editable radius/K presets), Space (pause), and Escape (stop/quit). Desktop scenes intentionally contain no XR, calibration, or recording components.
