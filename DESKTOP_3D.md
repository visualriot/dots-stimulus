# Desktop stimulus

Create it with **Tools > Dots Stimulus > Create desktop stimulus**. It starts with MATLAB's 2D simulation in a fixed orthographic camera, scaled across the complete Game-view viewport, and with the source aperture disabled. Mouse and I/J/K/L input do nothing in 2D. Press D to switch to a perspective immersive 3D desktop preview and enable rotation controls.

Desktop scenes stop the globally configured XR loader and disable URP XR rendering on their camera, so the Game window is monoscopic rather than an OpenXR `Left Eye` preview. In the Unity Editor, set the Game-view Scale control to Fit/1×; higher zoom values intentionally crop and pan the preview and do not represent the application viewport.

The camera uses the source display width (0.587 m) and viewing distance (0.715 m). At the 800×600 reference aspect this derives the source 34.2238° vertical FOV; by default the vertical FOV adapts to the active viewport aspect using MATLAB's `Screenheight = Screenwidth / aspect` relation. Its black, unprocessed image uses six-pixel dots by default. All values are editable in the Inspector.

`Fill Desktop 2D Viewport` remains a presentation-only switch on `Dot Field Renderer`; it never scales simulation positions used for neighbours or movement. The shared `Viewer Placement` control applies only in 3D: Centre surrounds the camera with the volume, while Outside places its nearest face or sphere surface at the editable `Outside Clearance` straight ahead.

Hold right mouse and drag, or use I/J/K/L, to rotate the simulated head. In 3D, the mouse wheel moves the camera closer or farther along its current viewing direction, while holding the middle mouse button and dragging moves it left/right/up/down in the current camera plane. `Scroll Zoom Metres Per Notch` and `Middle Drag Pan Metres Per Pixel` control these movement rates in the Inspector. C restores the initial position and orientation. The selected 3D volume is world-fixed and the dot field is never parented to the camera. Mouse movement is disabled in 2D and never applies to VR.

Desktop 3D supports an editable box or sphere. Dots follow the MATLAB alignment logic inside the core. In the editable buffer, `Magnetic Steering` curves them inward, while `Smooth Linear Bounce` takes them to the outer limit and back on the exact same line with a smooth stop and reversal. Neither mode wraps across 3D edges. Desktop 2D wrapping and presentation are unchanged.

Shared controls are D (2D/3D), A (aperture), R (restart), arrows (editable radius/K presets), Space (pause), and Escape (stop/quit). Desktop scenes intentionally contain no XR, calibration, or recording components.
