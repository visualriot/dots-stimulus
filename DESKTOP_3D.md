# Desktop 3D stimulus

No VR packages or headset are needed. The player starts in the centre of a stationary 20 × 20 × 20 metre volume containing 1000 moving dots. Mouse look changes the view; keyboard movement changes the player's position. Neither action moves the swarm with the player.

## Run it

1. **Stop Play mode** in Unity before letting the scripts reload.
2. Return focus to Unity and wait for compilation. Use **Assets > Refresh** if necessary.
3. Open your existing `DotsStimulus` scene and press Play. The new **Desktop Preview** checkbox on `Stimulus Controller` defaults to on, including for the previously generated scene. At startup it selects 3D, disables the aperture and old tracked pose driver, adds desktop controls to the camera, sets a 75° vertical FOV, and centres the volume around the camera.
4. Alternatively, select **Tools > Dots Stimulus > Create desktop 3D scene** to create a separate scene already configured for desktop use. The old **Create stimulus scene** menu now creates the same desktop setup.
5. In the **Game** view select **Free Aspect** and reduce the **Scale** slider from 3.2× to **1× / Fit**. Click inside the Game view before using the controls. You can use its **Maximize On Play** option to fill the Editor window while playing. An Editor Game view is not an OS fullscreen application.

You should see white dots against black across the available view, without a grey frame. Only dots in front of the current viewing direction are drawn. Look around to see other parts of the volume. You are free to leave the finite cube; press C if you get lost.

## Controls

| Input | Action |
| --- | --- |
| Hold right mouse button and move mouse | Look around |
| W / S | Move forward / backward along the viewing direction |
| A / D | Strafe left / right |
| Q / E | Move down / up |
| Shift | Move faster |
| Release right mouse button, or Escape | Release the cursor |
| C | Return the camera to its starting position and orientation; swarm stays fixed |
| R | Restart the dots with fresh random positions/directions |
| Up / Down arrow | Change interaction radius or topological k, then restart |
| Space | Pause/resume the dots; camera movement remains available |
| F10 | Stop Play / quit the built application |

A and D are reserved for movement in desktop mode. They do not toggle the aperture or change dimension. The swarm starts in 3D on every Play. Set simulation parameters before Play; the simulation copies them at startup. Desktop camera sensitivity and movement speed are editable on its `Desktop Fly Camera` component (visible after Play in an older scene, or before Play in a newly generated scene).

## What matches MATLAB, and what changes

`SwarmSimulation.cs` is unchanged. It still uses the original neighbour rules, self inclusion, inclusive topological ties, synchronous velocity alignment, vector noise, position integration, and axis wrapping. The documented rare cancellation behavior remains unchanged too.

The desktop presentation selects MATLAB's **3D branch** (`Dimension_2D = false`) and hides the aperture (`use_aperture = false`). Local dot coordinates still have x/y in −10..10 and z in 50..70. Translating the whole rendered field by −60 m along z puts its centre at the player, giving world coordinates −10..10 on every axis for the default scene. This changes the observation position, not pairwise distances or swarm dynamics. The translation is applied once at startup; the cube does not follow the moving camera. Six-pixel white dots retain MATLAB's constant raster-size rule, so depth is most evident when moving sideways or looking around.

This is the MATLAB **simulation model in a surrounding 3D view**, not a reproduction of its fixed monitor image. Frame-based alignment/noise and different random generators still prevent claiming identical trajectories at different frame timings. VR and eye tracking remain deferred.

The original [port/reference guide](SETUP_AND_PORT.md) contains the detailed algorithm analysis. Its monitor/VR setup describes the older presentation; use this desktop guide for the current controls and setup. When adding VR later, disable desktop mode and remove/disable desktop camera controls before configuring a tracked camera.

## Validation

The 16 standalone simulation checks pass. In a temporary Unity 6000.4 project, Play Mode checks verified migration of the original scene into 3D, automatic desktop camera setup, disabled aperture/pose driver, camera centring, keyboard movement, mouse drag rotation, unchanged swarm placement, and camera reset. Forward- and backward-facing captures both rendered white dots across the black view and were visually inspected. The automated Unity checks are retained in `../verification/DesktopPreviewChecks.cs`; they run only when copied into a separate validation project's Editor folder.
