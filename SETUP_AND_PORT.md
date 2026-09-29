# MATLAB stimulus → Unity / Vive Pro Eye

> **Current setup: desktop 3D first.** Follow [DESKTOP_3D.md](DESKTOP_3D.md) for mouse/WASD controls and a dot volume surrounding the player. The scene builder now creates that setup, and existing scenes enable it through the new Desktop Preview option. The monitor/VR setup and A/D/C key descriptions below are retained as reference for the earlier presentation; they do not describe the current desktop mode. VR setup is deferred.

The source of truth is [`../info/PLS_Stimulus_Base.m`](../info/PLS_Stimulus_Base.m). The port lives in `Assets/DotsStimulus`. It implements the alignment stimulus, a stereo-capable dot renderer, a physical aperture, keyboard controls, and a scene builder. The field stays fixed in the world, as requested. Eye tracking is not implemented.

Project inspected: Unity **6000.4.10f1**, Universal Render Pipeline **17.4.0**, Input System **1.19.0**. XR Plug-in Management and OpenXR were not installed in the original project. Follow the setup below to install and enable them through Unity.

## 1. How the MATLAB code is refactored

| MATLAB responsibility | Unity implementation | Reason |
| --- | --- | --- |
| Parameters, `pos`, `vel`, initialization | [`SwarmSimulation.cs`](Assets/DotsStimulus/Scripts/SwarmSimulation.cs) | Pure C# using doubles; independent of camera, input, and headset SDK |
| Distances, neighbour selection, alignment, cancellation, noise, movement, wrapping | `SwarmSimulation.Step(seconds)` | One synchronous update of the whole swarm; no Rigidbody or per-dot GameObjects |
| Main loop, keyboard, restart and timing | [`StimulusController.cs`](Assets/DotsStimulus/Scripts/StimulusController.cs) | Unity calls `Update`; do not port the blocking MATLAB `while` loop literally |
| `glDrawArrays(GL.POINTS)` and `glPointSize(6)` | [`DotFieldRenderer.cs`](Assets/DotsStimulus/Scripts/DotFieldRenderer.cs), [`StimulusDots.shader`](Assets/DotsStimulus/Shaders/StimulusDots.shader) | One mesh with four vertices per dot; shader makes round billboards in each eye |
| Four aperture bars | `DotFieldRenderer.BuildFrame`, [`StimulusFrame.shader`](Assets/DotsStimulus/Shaders/StimulusFrame.shader) | An unlit frame at 0.715 m; rendered over the dots, matching the source's disabled depth test |
| Psychtoolbox window and fixed `gluLookAt` | Tracked Unity camera and OpenXR | The headset supplies head pose, stereo views, and projection |
| Scene wiring | [`StimulusSceneBuilder.cs`](Assets/DotsStimulus/Editor/StimulusSceneBuilder.cs) | Creates the camera, pose bindings, stimulus, and shader references from a menu |

The simulation's step order is:

1. Measure ordinary 3D Euclidean distances using the old positions.
2. Select each dot's neighbours, including itself.
3. Sum their **old velocities**, then normalize the sum.
4. Apply the shared cancellation fallback where necessary.
5. Add vector noise, then normalize if noise is enabled.
6. Multiply by speed; in 2D remove the z velocity and restore planar speed.
7. Advance position by velocity × elapsed seconds.
8. Wrap enabled position axes; force z to 50 in 2D.

All neighbour sums are calculated before any velocity is overwritten. Updating each dot's live velocity immediately would change the model because later dots would see partially updated neighbours.

### Exact source semantics to retain

- **Metric:** `distance <= radius`, including the diagonal/self. Radius zero usually means only self, but coincident dots also count.
- **Topological:** MATLAB's `sort_distances(k,:)` includes self. `k=3` normally means self plus two other dots. Every tie at the kth distance is included, so the actual count can exceed k. Apply `int_r_topo` as an additional radius cap. The port finds the kth distance by partitioning instead of sorting the entire row; the threshold rule is unchanged.
- **Wrapping:** positions wrap, but distances do **not** use a toroidal minimum-image distance. Points on opposite sides of the box are not made neighbours by wrapping.
- **Noise:** `eta * (uniform(0,1) - 0.5)` is added to each applicable direction component. Eta is a vector amplitude, not an angle. Noise is applied once per simulation step, without multiplying by delta time.
- **2D:** the dots occupy a plane at z = 50 m, not the aperture plane at 0.715 m.
- **Restart:** D, R, and arrow keys reinitialize positions and velocities. A toggles only the aperture.
- **Precision:** simulation arrays use doubles. C# `double.Epsilon` is not MATLAB `eps`; the port explicitly uses `2.2204460492503131e-16`.
- **Randomness:** distributions are equivalent, but .NET and MATLAB use different random streams and draw ordering. Matching seed numbers does not reproduce MATLAB trajectories. For sample-by-sample comparison, export identical starting arrays, noise vectors, fallback random angles, and delta times from MATLAB.

### A source bug deliberately preserved

In the cancellation block, MATLAB sets `ax` from the **velocity**, whose length is normally 3, despite the comment calling it a unit vector. It normalizes `p1`, but `p2 = cross(ax,p1)` therefore has length 3. The resulting random combination is not generally unit length. In **3D with eta = 0**, affected dots can temporarily exceed the configured speed. All cancelled dots also share a direction derived from the first cancelled dot, even if they belong to unrelated groups.

This port preserves both behaviors. In 2D, the later planar normalization restores speed. If you decide to fix the 3D model, normalize `axis` before building the perpendicular basis and normalize the resulting shared direction. Change the reference MATLAB and cancellation test together; that is a model revision, not just a language translation.

## 2. First run: desktop preview

1. Open **Unity Hub** and open the inner `Dots-Stimulus` folder, which contains `Assets`, `Packages`, and `ProjectSettings`. Use **6000.4.10f1**.
2. Return focus to Unity and wait for compilation. If the menu below is missing, use **Assets > Refresh** and check **Window > General > Console** for red errors.
3. Select **Tools > Dots Stimulus > Create stimulus scene**. If prompted about your current scene, save it as appropriate. The tool creates a new scene under `Assets/DotsStimulus/Scenes`. Repeating the command creates a uniquely named scene rather than overwriting one.
4. The Hierarchy should contain:

   ```text
   Tracking Origin                    position 0,0,0; rotation 0,0,0; scale 1,1,1
     Main Camera                      tracked head camera
   Stimulus (world fixed)              sibling of Tracking Origin, not a camera child
   ```

5. Select **Stimulus (world fixed)**. Its `Stimulus Controller` has all simulation settings. Its `Dot Field Renderer` has dot size and aperture settings. Both shader fields should already be assigned.
6. Open the **Game** view and choose a fixed **800 × 600** resolution for comparison with the source. The builder sets the desktop camera's vertical FOV to **34.2238°**, near clip to **0.01 m**, far clip to **140 m**, and background to black. Post-processing is off.
7. Press **Play**, then click the Game view to give it keyboard focus. You should see white dots through a central square opening in a grey frame. Press **A** to see the full swarm and **D** to switch between 2D and 3D.
8. Stop Play before changing `SwarmSettings` in the Inspector. The simulation copies these settings at startup; arbitrary Inspector edits do not update that running copy. Keyboard changes do update it.

The builder uses a minimal tracked-camera rig because this task does not need controller interaction or locomotion. An `XROrigin` component and XR Interaction Toolkit are not required by these scripts. The camera uses the Input System's Tracked Pose Driver, with inline actions that the driver enables itself. Unity documents this component as the bridge from tracked device input to a GameObject transform. [Tracked input devices](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/TrackedInputDevices.html)

If you prefer an existing XR Origin rig later, assign its camera to `Stimulus Controller > Viewer` and remove the generated camera rig. Keep one active camera and one active rig. Keep the stimulus outside the tracked camera hierarchy. [Unity XR Origin](https://docs.unity3d.com/6000.4/Documentation/Manual/xr-origin.html)

## 3. Enable Vive Pro Eye VR

Use **Windows PC VR → OpenXR → SteamVR → Vive Pro Eye** for headset display and head tracking. Unity's OpenXR provider supports devices with compatible runtimes, including Vive and SteamVR. [Unity XR package support](https://docs.unity.com/en-us/engine/6000.3/manual/xr/support/support-packages)

1. Connect the Vive Pro Eye and its base stations. Start **SteamVR** and complete its headset/room setup. Confirm that the headset tracks correctly in SteamVR before testing Unity.
2. In SteamVR settings, find **Current OpenXR Runtime** and set it to **SteamVR** if necessary. Depending on SteamVR version, look under **OpenXR**, or show Advanced Settings and check **Developer**. HTC documents the runtime selection under Developer. This wired Vive Pro Eye setup does not need the streaming steps on that support page. [HTC runtime setting](https://www.vive.com/ca/support/vs/category_howto/trouble-with-openxr-titles.html)
3. In Unity, open **File > Build Profiles**. Select **Windows** and switch to that platform if needed. Choose **Intel 64-bit / x86_64** for the build. If Windows support is missing, install it for this Editor version through Unity Hub.
4. Open **Window > Package Management > Package Manager**. In the **Unity Registry**, install **XR Plug-in Management** (`com.unity.xr.management`). Use the compatible released version Unity offers for this Editor.
5. Open **Edit > Project Settings > XR Plug-in Management**. Select the **PC, Mac & Linux Standalone / Windows** tab and enable **OpenXR**. Let Unity install the OpenXR package. Keep **Initialize XR on Startup** enabled. Installing the package alone does not enable its loader.
6. Under **XR Plug-in Management > OpenXR**, select **Single Pass Instanced** where offered (newer versions may label it **Single Pass Instanced / Multiview**). Select **SteamVR** as the Play Mode OpenXR Runtime if listed; otherwise use the system default after step 2.
7. In **Project Settings > Player > Other Settings**, use **Direct3D11** for the initial Windows setup. If needed, disable Auto Graphics API and put Direct3D11 first. Keep **Active Input Handling = Input System Package (New)**, which this project already uses. Restart the Editor if requested.
8. An HTC Vive controller interaction profile can be added if you later use the wands. This port uses operator keyboard controls and does not need controller bindings. Do not enable eye tracking features yet.
9. Open **XR Plug-in Management > Project Validation**. Address any errors for the Windows/OpenXR target, then let Unity recompile. [OpenXR configuration and validation entry point](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/project-configuration.html)
10. Open the generated stimulus scene. Confirm its Main Camera still has **Tracked Pose Driver (Input System)**, with **Rotation And Position**, **Update And Before Render**, and **Ignore Tracking State** unchecked. The builder supplies these actions:

    | Action | Binding | Expected type |
    | --- | --- | --- |
    | Head position | `<XRHMD>/centerEyePosition` | Vector3 |
    | Head rotation | `<XRHMD>/centerEyeRotation` | Quaternion |
    | Head tracking state | `<XRHMD>/trackingState` | Integer |

11. Keep the camera's **Target Eye = Both**. Leave headset FOV/projection to OpenXR; the 34.2238° camera setting is only for the desktop preview. Do not override either eye's projection matrix.
12. Start SteamVR, put on the headset, then press Unity **Play**. Give the Game view keyboard focus. Look straight ahead from the intended observation position and have the operator press **C**. This places the stimulus origin at the current head position and aligns its axes with the current view **once**. The field then stays fixed as the head moves.
13. Test **A**, **D**, **R**, and the arrow keys. To build, stop Play, open **File > Build Profiles**, add the generated scene to the active scene list, and make it the first enabled scene. Disable `SampleScene` for this build. Build a Windows executable and launch it with SteamVR running.

No SteamVR Unity Asset Store plugin, SRanipal integration, or eye tracking SDK is needed for this initial head-tracked display path. Installing eye tracking later is a separate task; the hardware name alone does not guarantee that a particular runtime exposes gaze data.

## 4. Rendering and URP settings

The scene builder disables camera post-processing, HDR, dynamic resolution, and occlusion culling. The source project still has its original shared URP assets; the builder does not silently change them.

For the first comparison, select the **PC** quality level in **Edit > Project Settings > Quality** and check which pipeline asset it uses. In this project that is `Assets/Settings/PC_RPAsset.asset`. Keep **Render Scale = 1**. In `PC_Renderer.asset`, disable the **Screen Space Ambient Occlusion** renderer feature for this simple unlit scene. Keep bloom, tone mapping, temporal antialiasing, motion blur, and depth of field off. These effects can change dot size, intensity, or persistence. Start without MSAA to match the baseline; if you later enable it, document and compare its visual effect.

Both custom shaders include Unity's stereo instancing macros so each eye uses its own transform. A geometry shader and separate dot GameObjects are unnecessary. The headset test must still verify both eyes; a desktop preview cannot establish correct stereo output on your runtime. [Unity custom shaders for stereo instancing](https://docs.unity3d.com/6000.0/Documentation/Manual/SinglePassInstancing.html)

### Dot size is an explicit experimental choice

Default: **Use Angular Size = off; Diameter Pixels = 6**. This reproduces the source's fixed raster-size rule, using pixels of the current eye render target. Six headset render pixels do not have the same visual angle as six pixels in the original desktop window. SteamVR supersampling and URP render scale change that relationship.

If the goal is approximately the original central visual angle, enable **Use Angular Size** and use **Diameter Degrees = 0.3528**. This value is derived from the source's 800-pixel-wide window, nominal 0.587 m display width, and 0.715 m viewing distance:

```text
diameter = 2 * atan((6 / 800 * 0.587 / 2) / 0.715) ≈ 0.3528 degrees
```

That calculation inherits the source's assumption that its 800-pixel window corresponds to the entire 0.587 m monitor width. A physically smaller desktop window would have a different actual angle. The shader's angular option is a central-view approximation, not a calibrated uniform retinal angle across the entire lens field. Decide which definition your experiment needs before collecting data.

### Aperture and world scale

Use **1 Unity unit = 1 metre**, and leave the stimulus and tracking origin scales at `(1,1,1)`.

- Dot volume: x/y from −10 to +10 m; depth from 50 to 70 m in 3D.
- In 2D, z is exactly 50 m.
- Frame plane: z = 0.715 m relative to the stimulus origin.
- Opening half-width/height: `10 * 0.715 / 70 = 0.102142857 m`.
- Full opening: **0.204285714 × 0.204285714 m**, about **16.2602°** from the origin.
- The opening uses the **back wall at 70 m**, including in 2D, exactly as the source does.
- The frame extends to ±10 m in x/y in this port to cover the wider headset view. MATLAB only needed to cover its monitor. This extension is an intentional rendering adaptation.

The aperture is a real world-fixed plane. Each eye sees it from its own position, so binocular occlusion differs from the monoscopic monitor. Moving the head changes which dots appear through it. Walking through or around the near frame no longer reproduces the original observation geometry; press C from the intended observation point to reposition the setup. The recenter key includes current head pitch and roll, so look level when pressing it.

Unity white and grey values are not calibrated physical luminance. Display transfer, headset optics, supersampling, antialiasing, stereo, and gamma/color-space differences mean this is not a pixel-identical or luminance-validated Psychtoolbox replacement.

## 5. Parameters and controls

| Source | Inspector field | Default |
| --- | --- | --- |
| `N` | Count | 1000 |
| `Dimension_2D` | Dimension 2D | true |
| `algo_type` | Rule | Metric |
| `sim_param` in metric mode | Metric Radius | 2 m |
| `sim_param` in topological mode | Topological K | 3, including self |
| `int_r_topo` | Topological Radius Cap | 1000 m |
| `v` | Speed | 3 m/s |
| `eta` | Noise | 0 |
| `L_x`, `L_y`, `L_z` | Width, Height, Depth | 20, 20, 20 m |
| `z_start` | Start Depth | 50 m |
| `torus_x/y/z` | Wrap X/Y/Z | all true |
| `use_aperture` | Renderer: Use Aperture | true |
| `glPointSize(6)` | Renderer: Diameter Pixels | 6 |

The original defaults use metric `sim_param = 2`; the separate topological field defaults to 3 for convenience. To reproduce switching the original `algo_type` to topological while leaving `sim_param = 2`, set **Topological K = 2** explicitly. The source fixes N at 1000; the port exposes Count for diagnostics. Keep 1000 for matching trials. It rejects invalid k values and nonpositive speed/dimensions rather than allowing undefined values.

| Key | Action |
| --- | --- |
| D | Toggle 2D/3D and initialize a fresh swarm |
| R | Initialize a fresh swarm with current settings |
| A | Toggle aperture without restarting |
| ↑ / ↓ | Step radius or k through the source's lists, then restart |
| C | Place the world-fixed stimulus in front of the current head pose; does not restart the swarm |
| Space | Pause/resume simulation; head tracking continues |
| Escape | Stop Play in the Editor / quit the built application |

Radius list: `0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 1000`. Topological list: `2, 3, 5, 9, 17, 33`.

For repeatable Unity runs, disable **Randomize Seed** and set **Seed** before Play. The actual initial seed is printed in the Console. R continues the current random stream; stopping and starting Play with the same fixed seed begins the same sequence. Matching trajectories also requires identical step timings and input actions.

## 6. Timing, performance, and validation

The original performs one alignment update after every `Screen('Flip')`, then moves using elapsed real time. Therefore **speed is time-based, but alignment and noise are frame-based**. Running the same parameters at different frame rates changes the dynamics. Do not move this port into Unity's default 50 Hz `FixedUpdate` and assume it remains equivalent.

The controller uploads the current state for drawing and then calculates the next state once per Unity `Update`, using `Time.realtimeSinceStartupAsDouble` for elapsed seconds. It does not scale elapsed time with `Time.timeScale` or clamp long frames. It resets its clock on focus changes, and adds an initial/reset display frame. Unity does not expose the same blocking `Screen('Flip')` contract here: the calculation still occurs before that Unity frame is actually presented. This preserves update ordering approximately, not exact presentation latency. Compositor-reprojected frames do not create additional simulation steps.

For an experiment that needs frame-accurate equivalence, choose and record an explicit update frequency, compare against MATLAB at that frequency, log simulation and presentation timing, and validate dropped/reprojected frames on the target headset. Changing to a fixed simulation tick can improve reproducibility, but is a deliberate model/timing decision. This prototype does not yet include trial scheduling, response collection, or a timing/gaze logger.

The reference implementation is O(N²) for distances, uses reusable arrays, and allocates no per-dot objects or per-step neighbour matrices. Topological selection partitions one scratch row rather than sorting every row. It still needs profiling in a Windows build on the actual PC. If it misses the headset's frame budget, move the same two-phase update into Burst/Jobs or compute, retaining inclusive ties, self, ordinary distances, and shared fallback semantics. A spatial index is useful for small metric radii, but radius 1000 is effectively global and needs separate consideration.

Standalone checks are in [`../verification`](../verification). With the installed .NET 10 SDK, run from the repository root:

```powershell
dotnet run --project verification/SwarmChecks.csproj --configuration Release
```

These checks cover initialization, metric boundaries, self inclusion, synchronous alignment, topological ties and caps, comparison with an independent full-sort reference, wrapping, movement, cancellation behavior, noise, seeds, and invalid k. They do not run MATLAB itself or validate headset output.

Validation performed on 2026-09-22: **all 16 standalone checks passed**. In a separate temporary copy, **Unity 6000.4.10f1 compiled the C# scripts, generated the scene, imported both shaders, explicitly compiled their material passes, and exited with code 0**. The local .NET benchmark measured about 2.28 ms per metric step and 8.15 ms per topological step for 1000 dots; these are not Unity/headset timings. No headset, stereo presentation, luminance calibration, or sustained VR performance has been verified.

Before a headset session, check:

1. Metric radius 0 gives independent straight trajectories between wraps; 1000 produces global alignment.
2. D restarts into a depth volume; D again returns every point to the 50 m plane.
3. A reveals/hides the aperture without resetting movement.
4. Both eyes render dots and the aperture, with no pink materials or missing eye.
5. After C, turning the head leaves the field in the world; moving sideways changes the view through the aperture.
6. Unity Profiler and SteamVR frame timing show the application meeting the actual headset refresh budget. A standalone .NET benchmark is not evidence of VR performance.

## 7. Common setup problems

| Symptom | Check |
| --- | --- |
| No Tools > Dots Stimulus menu | Wait for script compilation; focus Unity, refresh Assets, inspect the Console |
| Desktop works but headset is black | SteamVR tracking first; Windows OpenXR loader enabled; Initialize XR on Startup; active runtime |
| View renders but does not track the head | Tracked Pose Driver and its three bindings; no duplicate camera; current input tracking state |
| Only grey frame or apparently empty view | Press C while looking forward; press A to inspect full swarm; far clip must exceed 70; ensure scale is 1 |
| Keyboard does nothing | Focus the Game view or built application; use the New Input System |
| Pink materials | URP active; shader fields assigned; inspect Console shader errors |
| One eye missing or incorrect dot size | Check single-pass instancing/runtime settings and both eye outputs; compare Multi Pass diagnostically |
| Stutter or changing collective motion | Profile simulation CPU time, actual frame rate, and compositor reprojection; check noise and update frequency |
| Built app opens SampleScene | Fix active Build Profile scene list and put the generated stimulus scene first |

## 8. Eye tracking later

Keep gaze acquisition as a separate component. It should report timestamped gaze origin/direction and validity in world coordinates, independently of `SwarmSimulation`. Transform gaze into stimulus-local coordinates when testing which part of the field was viewed; account for the renderer's `Mirror Matlab X` option when mapping back to simulation coordinates. Associate gaze samples with the **displayed** stimulus step, not simply the already-advanced simulation arrays. Verify the Vive Pro Eye SDK/runtime route and calibration at that stage. No gaze acquisition or gaze-driven behavior is present in this port.
