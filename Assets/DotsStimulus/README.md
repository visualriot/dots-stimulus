# Dots Stimulus — Researcher Guide

This Unity project presents moving dots in 2D or 3D for desktop and VR experiments. The same dot movement rules are used in both presentation types; Desktop and VR only change how the participant views the stimulus.

## Create a new experiment scene

In Unity, choose one of:

- **Tools > Dots Stimulus > Create New Stimulus > Desktop Stimulus**
- **Tools > Dots Stimulus > Create New Stimulus > VR Stimulus**

The command asks before closing an unsaved scene, creates the required camera and stimulus objects, saves a new scene, and selects `Stimulus (world fixed)` so its settings are visible.

For short help inside Unity, use **Tools > Dots Stimulus > Instructions**.

## Quick workflow

1. Create a Desktop or VR scene.
2. Select `Stimulus (world fixed)`.
3. Configure movement and boundaries in **Stimulus Controller**.
4. Configure dot appearance and viewer placement in **Dot Field Renderer**.
5. Press Play and use the controls below.
6. Before collecting data, decide whether each run should be random or repeatable.

Inspector values are copied into the active simulation when Play begins. Changing a value during Play does not normally change the running simulation unless a listed keyboard control does so.

## Play-mode controls

| Input | Action |
|---|---|
| `D` | Switch between 2D and 3D and restart the dots |
| `P` | In 3D, switch Centre/Outside placement using the current viewer pose; dots are not restarted |
| `A` | Show or hide the aperture frame |
| `R` | Restart the dot positions and directions |
| `Up` / `Down` | Select the next radius or neighbour-count preset and restart |
| `Space` | Pause or continue |
| `C` | Reset the desktop view or realign the VR stimulus |
| `Escape` | Stop Play mode in the Editor or quit a built application |

Desktop movement works in 3D only:

| Input | Action |
|---|---|
| Right-mouse drag | Look around |
| `I` / `J` / `K` / `L` | Look up / left / down / right |
| Mouse wheel | Move forward or backward |
| Middle-mouse drag | Move sideways or up/down |

VR uses the tracked headset for viewing. Desktop mouse movement is ignored in VR.

## Desktop and VR

### Desktop

Desktop starts in 2D and fills the Game view. Press `D` for a perspective 3D preview with mouse and keyboard movement. Press `C` to return the desktop camera to its starting position and direction.

The Desktop scene intentionally contains no XR, calibration, eye-tracking, or recording components.

### VR

VR starts in immersive 3D. Once a valid headset pose is available, the stimulus aligns to it and then stays fixed in the room. Press `C` to align it again without restarting the dots.

Press `D` to show the 2D stimulus on a virtual screen in front of the current headset pose. The screen width, height, distance, and border are editable in **Dot Field Renderer**.

## 2D and 3D

### 2D

Dots move on a flat X/Y plane. **Wrap X** and **Wrap Y** decide whether a dot leaving one side returns at the opposite side. The 3D shape and boundary settings do not affect 2D.

### 3D

Dots move inside either:

- **Box** — uses Width, Height, and Box Depth.
- **Sphere** — uses Sphere Radius.

The core is the main box or sphere where active dots interact. A boundary or fade buffer exists outside it. The selected **Buffer Behaviour** determines what happens when a dot crosses the core edge.

Pressing `D` changes dimension and restarts the dots.

## Viewer placement

Viewer Placement affects 3D only:

- **Centre** places the box or sphere around the viewer.
- **Outside** places the whole box or sphere directly in front of the viewer. **Outside Clearance** is the empty distance from the viewer to the nearest surface.

Press `P` to switch placement. The stimulus first uses the viewer's current position and facing direction, so Outside appears directly ahead. This does not restart the dots. The stimulus remains fixed after placement until it is placed or aligned again.

## Boundary behaviours

### Magnetic Steering

Dots entering the boundary buffer turn smoothly back toward the core. A thicker buffer gives them more space to turn.

### Smooth Linear Bounce

Dots entering the boundary buffer slow down, stop at the outer limit, then return along the same straight line.

### Fade And Respawn

When a dot exits the core:

1. A visual copy continues into the fade buffer, slows down, and fades away.
2. A new active dot appears immediately inside the core.
3. The fading copy does not affect neighbour movement.

This keeps the requested active dot count inside the core. **Respawn Location** can be:

- **Centre** — every replacement begins at the shape centre.
- **Random Within Core** — replacements are spread randomly through the box or sphere.

The default Fade Buffer is 2 m and the default randomized lifetime is 1–2 seconds.

## Dot movement settings

- **Dot Count / Density** — number of active simulated dots. More dots increase density and processing cost.
- **Neighbour Rule: Metric** — a dot follows every dot within Metric Radius.
- **Neighbour Rule: Topological** — a dot follows the nearest K dots. K includes the dot itself.
- **Topological Radius Cap** — ignores selected neighbours beyond this distance.
- **Speed** — movement speed in metres per second.
- **Directional Noise** — adds random variation to direction. Zero disables it.
- **Width / Height / Depth / Radius** — size of the movement area in metres.
- **Start Depth** — the shape's forward position in simulation coordinates. Most studies can keep the default.

The Inspector shows the derived dot density and the selected core and outer dimensions.

## Dot appearance

- **Use World Size In 3D** gives every 3D dot a fixed physical diameter, so distant dots naturally look smaller. This is on by default.
- **Diameter Metres** sets that 3D physical diameter. The default is 0.06 m (6 cm).
- **Diameter Pixels** keeps the rendered dot size fixed in pixels.
- **Use Angular Size** keeps approximately the same apparent size when display resolution changes.
- **Mirror MATLAB X** mirrors only the displayed horizontal position; it does not change movement or neighbour distances.
- **Aperture** shows a dark frame that hides the outer edges of the source-style view. Press `A` to toggle it.

Pixel and angular sizing are used for 2D. If **Use World Size In 3D** is turned off, 3D also uses the selected pixel/angular sizing and dots remain the same apparent size at every distance.

Shader fields are assigned automatically and normally should not be changed.

## Random and repeatable runs

- **Randomize Seed on** gives a different run whenever Play starts.
- **Randomize Seed off** uses the chosen Seed so the same Unity setup repeats the same run.
- **Actual Seed** shows the seed used by the current run.

Use a fixed seed when exact repeatability matters. Record the seed and all Inspector settings with experimental data.

## Before collecting data

- Confirm Desktop or VR and the starting 2D/3D mode.
- Confirm Centre or Outside placement and its clearance.
- Confirm box/sphere size, dot count, neighbour rule, and boundary behaviour.
- Confirm random or fixed seed behavior.
- Run a short test and verify that the participant can see the intended volume.
- Save the Unity scene after changing Inspector values.

## Additional technical notes

See [SETUP_AND_PORT.md](../../SETUP_AND_PORT.md) for implementation and source-port details and [DESKTOP_3D.md](../../DESKTOP_3D.md) for more detail about the desktop camera.
