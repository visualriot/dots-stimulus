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
        [Tooltip("Dot movement, neighbour, shape, and boundary settings. These values are copied into the simulation when Play begins.")]
        public SwarmSettings settings = new SwarmSettings();

        [Header("Presentation")]
        [Tooltip("Choose Desktop for a screen and mouse view, or Virtual Reality for a tracked headset. Dot movement is the same in both.")]
        public StimulusPresentationMode presentationMode = StimulusPresentationMode.Desktop;
        [Tooltip("Camera through which the participant sees the dots. The scene-creation menu assigns this automatically.")]
        public Camera viewer;
        [Tooltip("VR only. Align the stimulus to the first tracked headset pose, then leave it fixed in the room. Press C to align it again.")]
        public bool alignToFirstValidHeadPose = true;
        [SerializeField, HideInInspector] private int threeDExtensionSerializationVersion = 2;

        [Header("Randomization")]
        [Tooltip("On gives a different run each time Play starts. Off uses the Seed below for a repeatable Unity run.")]
        public bool randomizeSeed = true;
        [Tooltip("Number used to repeat the same Unity run when Randomize Seed is off.")]
        public int seed = 12345;
        [Tooltip("Start with movement paused. Press Space to pause or continue without regenerating the dots.")]
        public bool paused;

        [Header("Arrow-key presets")]
        [Tooltip("Metric Radius values selected by the Up and Down keys. Values are in metres.")]
        public double[] metricRadiusPresets = { 0, .5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 1000 };
        [Tooltip("Topological K values selected by the Up and Down keys. K includes the dot itself.")]
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
            MigrateThreeDExtensionSettings();
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
                "D 2D/3D; P viewer placement; A aperture; R restart; arrows parameter; Space pause; C reset/realign view; Escape stop.");
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
                else if (keyboard.pKey.wasPressedThisFrame) ToggleViewerPlacement();
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

        public void ToggleViewerPlacement()
        {
            if (Simulation != null && Simulation.Settings.dimension2D) return;
            DotFieldRenderer targetDisplay = display ? display : GetComponent<DotFieldRenderer>();
            if (!targetDisplay) return;
            if (viewer)
            {
                transform.SetPositionAndRotation(viewer.transform.position, viewer.transform.rotation);
                if (presentationMode == StimulusPresentationMode.VirtualReality) HasAlignedToHeadPose = true;
            }
            targetDisplay.viewerPlacement = targetDisplay.viewerPlacement == ViewerPlacement.Centre
                ? ViewerPlacement.Outside
                : ViewerPlacement.Centre;
            Emit("viewer_placement", targetDisplay.viewerPlacement == ViewerPlacement.Centre ? "centre" : "outside");
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
            MigrateThreeDExtensionSettings();
            if (metricRadiusPresets == null || metricRadiusPresets.Length == 0) metricRadiusPresets = new[] { 2.0 };
            if (topologicalKPresets == null || topologicalKPresets.Length == 0) topologicalKPresets = new[] { 2 };
            for (int i = 0; i < metricRadiusPresets.Length; i++) metricRadiusPresets[i] = Math.Max(0, metricRadiusPresets[i]);
            for (int i = 0; i < topologicalKPresets.Length; i++) topologicalKPresets[i] = Math.Max(1, topologicalKPresets[i]);
        }

        private void MigrateThreeDExtensionSettings()
        {
            if (settings == null) settings = new SwarmSettings();
            if (threeDExtensionSerializationVersion < 1)
            {
                settings.volumeShape = VolumeShape.Box;
                settings.sphereRadius = 10;
                settings.boundaryBuffer = 1;
                settings.boundaryMode = BoundaryMode.MagneticSteering;
            }
            if (threeDExtensionSerializationVersion < 2)
            {
                settings.fadeBuffer = 2;
                settings.fadeLifetimeMin = 1;
                settings.fadeLifetimeMax = 2;
                settings.fadeRespawnMode = FadeRespawnMode.Centre;
            }
            threeDExtensionSerializationVersion = 2;
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
