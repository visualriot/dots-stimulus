using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace DotsStimulus
{
    public enum StimulusPresentationMode { Desktop, VirtualReality }

    [RequireComponent(typeof(DotFieldRenderer))]
    public sealed class StimulusController : MonoBehaviour
    {
        [Header("MATLAB simulation (metres and seconds)")]
        [Tooltip("Copied into the active simulation when Play begins. Runtime controls keep applicable values synchronized.")]
        public SwarmSettings settings = new SwarmSettings();

        [Header("Presentation")]
        [Tooltip("Selects the presentation rig only. Desktop uses a fixed orthographic 2D view or mouse-look 3D preview; Virtual Reality uses the tracked OpenXR headset. The swarm algorithm is identical.")]
        public StimulusPresentationMode presentationMode = StimulusPresentationMode.Desktop;
        [Tooltip("Camera used to display the stimulus. Assigned automatically by the scene creator: the desktop camera or the tracked XR headset camera.")]
        public Camera viewer;
        [Tooltip("VR only. Wait for a valid tracked head pose, align the stimulus to it once, then leave the stimulus world-fixed. C performs a manual realignment.")]
        public bool alignToFirstValidHeadPose = true;

        [Header("Randomization")]
        [Tooltip("On: choose a new seed whenever Play starts, matching MATLAB rng('shuffle'). Off: use Seed below for repeatable Unity runs.")]
        public bool randomizeSeed = true;
        [Tooltip("Seed used only when Randomize Seed is off. The same Unity seed repeats a Unity run, but MATLAB uses a different random-number generator.")]
        public int seed = 12345;
        [Tooltip("Start the simulation paused. Space toggles pause without regenerating the dots.")]
        public bool paused;

        [Header("Arrow-key presets")]
        [Tooltip("MATLAB possible_radius values stepped through by Up/Down when Neighbour Rule is Metric. Values are interaction radii in metres.")]
        public double[] metricRadiusPresets = { 0, .5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 1000 };
        [Tooltip("MATLAB possible_num_n values stepped through by Up/Down when Neighbour Rule is Topological. K includes the dot itself; ties can include more than K dots.")]
        public int[] topologicalKPresets = { 2, 3, 5, 9, 17, 33 };

        public SwarmSimulation Simulation { get; private set; }
        [SerializeField, Tooltip("Actual seed used by the active simulation; assigned when Play begins.")]
        private int actualSeed;
        public int ActualSeed => actualSeed;
        public double LastStepDeltaSeconds { get; private set; }
        public bool HasAlignedToHeadPose { get; private set; }
        public DotFieldRenderer Display => display;
        public event Action<string, string> ControlEvent;

        private DotFieldRenderer display;
        private double previousTime;
        private bool firstFrame = true;

        private void Start()
        {
            if (!viewer) viewer = Camera.main;
            display = GetComponent<DotFieldRenderer>();
            if (!viewer)
            {
                Debug.LogError("Dots stimulus requires a Viewer camera.", this);
                enabled = false;
                return;
            }

            if (presentationMode == StimulusPresentationMode.Desktop)
                DesktopFlyCamera.Configure(viewer).SetTwoDimensional(settings.dimension2D);

            actualSeed = randomizeSeed ? Environment.TickCount : seed;
            Simulation = new SwarmSimulation(settings, actualSeed);
            display.presentationMode = presentationMode;
            display.SetPresentationCamera(viewer);
            display.Initialize(Simulation);
            previousTime = Time.realtimeSinceStartupAsDouble;
            Emit("started", $"seed={actualSeed};mode={presentationMode};dimension={(settings.dimension2D ? "2D" : "3D")}");
            Debug.Log($"Dots stimulus: seed={actualSeed}, N={settings.count}, rule={settings.rule}, dimension={(settings.dimension2D ? "2D" : "3D")}. " +
                "D 2D/3D; A aperture; R restart; arrows parameter; Space pause; C reset/realign view; Escape stop.");
        }

        private void Update()
        {
            if (Simulation == null) return;
            if (presentationMode == StimulusPresentationMode.VirtualReality && alignToFirstValidHeadPose && !HasAlignedToHeadPose)
                TryAlignToTrackedHead();

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { Emit("quit", "escape"); Quit(); return; }
                if (keyboard.dKey.wasPressedThisFrame)
                {
                    settings.dimension2D = !settings.dimension2D;
                    Simulation.Settings.dimension2D = settings.dimension2D;
                    Restart("dimension", settings.dimension2D ? "2D" : "3D");
                    if (presentationMode == StimulusPresentationMode.VirtualReality)
                    {
                        Recenter();
                    }
                    else
                    {
                        var controls = viewer.GetComponent<DesktopFlyCamera>();
                        if (controls) controls.SetTwoDimensional(settings.dimension2D);
                    }
                }
                else if (keyboard.rKey.wasPressedThisFrame) Restart("restart", "keyboard");
                else if (keyboard.aKey.wasPressedThisFrame)
                {
                    display.useAperture = !display.useAperture;
                    Emit("aperture", display.useAperture ? "enabled" : "disabled");
                }
                else if (keyboard.upArrowKey.wasPressedThisFrame) { ChangeParameter(1); Restart("parameter", "up"); }
                else if (keyboard.downArrowKey.wasPressedThisFrame) { ChangeParameter(-1); Restart("parameter", "down"); }

                if (keyboard.cKey.wasPressedThisFrame) Recenter();
                if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    paused = !paused;
                    Emit("pause", paused ? "paused" : "resumed");
                }
            }

            display.Present(Simulation);
            double now = Time.realtimeSinceStartupAsDouble;
            double dt = now - previousTime;
            previousTime = now;
            LastStepDeltaSeconds = 0;
            if (paused || firstFrame) { firstFrame = false; return; }
            LastStepDeltaSeconds = dt;
            Simulation.Step(dt);
        }

        public void Restart(string eventName = "restart", string detail = "api")
        {
            if (Simulation == null) return;
            Simulation.Reset();
            firstFrame = true;
            LastStepDeltaSeconds = 0;
            Emit(eventName, detail);
        }

        public void Recenter()
        {
            if (!viewer) return;
            if (presentationMode == StimulusPresentationMode.Desktop)
            {
                var controls = viewer.GetComponent<DesktopFlyCamera>();
                if (controls) controls.ResetView();
                Emit("desktop_view_reset", "initial_pose");
                return;
            }

            transform.SetPositionAndRotation(viewer.transform.position, viewer.transform.rotation);
            HasAlignedToHeadPose = true;
            Emit("vr_stimulus_aligned", "manual");
        }

        private void TryAlignToTrackedHead()
        {
            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked) return;
            transform.SetPositionAndRotation(viewer.transform.position, viewer.transform.rotation);
            HasAlignedToHeadPose = true;
            Emit("vr_stimulus_aligned", "first_valid_head_pose");
        }

        private void ChangeParameter(int increment)
        {
            if (settings.rule == NeighbourRule.Metric)
            {
                if (metricRadiusPresets == null || metricRadiusPresets.Length == 0) return;
                int closest = Closest(metricRadiusPresets, settings.metricRadius);
                settings.metricRadius = metricRadiusPresets[Mathf.Clamp(closest + increment, 0, metricRadiusPresets.Length - 1)];
                Simulation.Settings.metricRadius = settings.metricRadius;
            }
            else
            {
                if (topologicalKPresets == null || topologicalKPresets.Length == 0) return;
                int closest = Closest(topologicalKPresets, settings.topologicalK);
                settings.topologicalK = Math.Min(settings.count, Math.Max(1, topologicalKPresets[Mathf.Clamp(closest + increment, 0, topologicalKPresets.Length - 1)]));
                Simulation.Settings.topologicalK = settings.topologicalK;
            }
        }

        private static int Closest(double[] values, double value)
        {
            int closest = 0;
            for (int i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - value) < Math.Abs(values[closest] - value)) closest = i;
            return closest;
        }

        private static int Closest(int[] values, int value)
        {
            int closest = 0;
            for (int i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - value) < Math.Abs(values[closest] - value)) closest = i;
            return closest;
        }

        public void Emit(string eventName, string detail) => ControlEvent?.Invoke(eventName, detail);

        private void OnApplicationFocus(bool hasFocus)
        {
            previousTime = Time.realtimeSinceStartupAsDouble;
            if (Simulation != null) Emit("application_focus", hasFocus ? "gained" : "lost");
        }

        private void OnEnable() => previousTime = Time.realtimeSinceStartupAsDouble;

        private void OnValidate()
        {
            if (metricRadiusPresets == null || metricRadiusPresets.Length == 0) metricRadiusPresets = new[] { 2.0 };
            if (topologicalKPresets == null || topologicalKPresets.Length == 0) topologicalKPresets = new[] { 2 };
            for (int i = 0; i < metricRadiusPresets.Length; i++) metricRadiusPresets[i] = Math.Max(0, metricRadiusPresets[i]);
            for (int i = 0; i < topologicalKPresets.Length; i++) topologicalKPresets[i] = Math.Max(1, topologicalKPresets[i]);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
