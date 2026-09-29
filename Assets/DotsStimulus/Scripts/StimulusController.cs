using UnityEngine;
using UnityEngine.InputSystem;

namespace DotsStimulus
{
    [RequireComponent(typeof(DotFieldRenderer))]
    public sealed class StimulusController : MonoBehaviour
    {
        public SwarmSettings settings = new SwarmSettings { dimension2D = false };
        public Camera viewer;
        [Tooltip("Start in 3D, centre the volume around the camera, hide the aperture, and enable mouse/WASD controls. Disable for the original monitor/VR setup.")]
        public bool desktopPreview = true;
        public bool randomizeSeed = true;
        public int seed = 12345;
        public bool paused;
        public SwarmSimulation Simulation { get; private set; }
        private DotFieldRenderer display;
        private double previousTime;
        private bool firstFrame = true;
        private static readonly double[] Radii = { 0, .5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 1000 };
        private static readonly double[] Counts = { 2, 3, 5, 9, 17, 33 };

        private void Start()
        {
            if (!viewer) viewer = Camera.main;
            display = GetComponent<DotFieldRenderer>();
            if (desktopPreview)
            {
                if (!viewer) { Debug.LogError("Desktop preview needs a Viewer camera.", this); enabled = false; return; }
                settings.dimension2D = false;
                display.useAperture = false;
                DesktopFlyCamera.Configure(viewer);
                // Translate only the presentation. Local MATLAB coordinates remain z=50..70.
                // Place their centre at the camera, giving a world-fixed cube around the player.
                var localCentre = new Vector3(0, 0, (float)(settings.startDepth + settings.depth / 2));
                transform.position = viewer.transform.position - transform.TransformVector(localCentre);
            }
            int actualSeed = randomizeSeed ? System.Environment.TickCount : seed;
            Simulation = new SwarmSimulation(settings, actualSeed);
            display.Initialize(Simulation);
            previousTime = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"Dots stimulus: seed={actualSeed}, N={settings.count}, rule={settings.rule}, 3D={!settings.dimension2D}. " +
                (desktopPreview ? "Hold right mouse to look; WASD to move; Q/E down/up; Shift faster; C return to centre; R restart; Space pause; Escape release mouse; F10 quit." : "C repositions the field; D toggles dimension; A toggles aperture."));
        }

        private void Update()
        {
            if (Simulation == null) return;
            var keyboard = Keyboard.current;
            bool reset = false;
            if (keyboard != null)
            {
                if ((desktopPreview && keyboard.f10Key.wasPressedThisFrame) || (!desktopPreview && keyboard.escapeKey.wasPressedThisFrame)) { Quit(); return; }
                if (!desktopPreview && keyboard.dKey.wasPressedThisFrame)
                {
                    Simulation.Settings.dimension2D = !Simulation.Settings.dimension2D;
                    settings.dimension2D = Simulation.Settings.dimension2D;
                    reset = true;
                }
                else if (keyboard.rKey.wasPressedThisFrame) reset = true;
                else if (!desktopPreview && keyboard.aKey.wasPressedThisFrame) display.useAperture = !display.useAperture;
                else if (keyboard.upArrowKey.wasPressedThisFrame) { ChangeParameter(1); reset = true; }
                else if (keyboard.downArrowKey.wasPressedThisFrame) { ChangeParameter(-1); reset = true; }
                if (keyboard.cKey.wasPressedThisFrame) Recenter();
                if (keyboard.spaceKey.wasPressedThisFrame) paused = !paused;
            }
            if (reset) { Simulation.Reset(); firstFrame = true; }
            // Upload old state, then calculate next state: MATLAB draws before alignment/movement.
            display.Present(Simulation);
            double now = Time.realtimeSinceStartupAsDouble;
            double dt = now - previousTime;
            previousTime = now;
            if (paused || firstFrame) { firstFrame = false; return; }
            Simulation.Step(dt);
        }

        public void Recenter()
        {
            if (!viewer) return;
            if (desktopPreview)
            {
                var controls = viewer.GetComponent<DesktopFlyCamera>();
                if (controls) controls.ResetView();
                return;
            }
            // Move the stimulus coordinate system once, never parent it to the tracked camera.
            transform.SetPositionAndRotation(viewer.transform.position, viewer.transform.rotation);
        }

        private void ChangeParameter(int increment)
        {
            var s = Simulation.Settings;
            double[] list = s.rule == NeighbourRule.Metric ? Radii : Counts;
            double current = s.rule == NeighbourRule.Metric ? s.metricRadius : s.topologicalK;
            int closest = 0;
            for (int i = 1; i < list.Length; i++)
                if (System.Math.Abs(list[i] - current) < System.Math.Abs(list[closest] - current)) closest = i;
            double next = list[Mathf.Clamp(closest + increment, 0, list.Length - 1)];
            if (s.rule == NeighbourRule.Metric) settings.metricRadius = s.metricRadius = next;
            else settings.topologicalK = s.topologicalK = System.Math.Min((int)next, s.count);
            Debug.Log($"Dots stimulus: radius={s.metricRadius}, k={s.topologicalK}, 2D={s.dimension2D}");
        }

        private void OnApplicationFocus(bool hasFocus) { previousTime = Time.realtimeSinceStartupAsDouble; }
        private void OnEnable() { previousTime = Time.realtimeSinceStartupAsDouble; }
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
