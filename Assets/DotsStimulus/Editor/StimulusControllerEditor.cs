using System;
using UnityEditor;
using UnityEngine;

namespace DotsStimulus.Editor
{
    [CustomEditor(typeof(StimulusController))]
    public sealed class StimulusControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty settings = serializedObject.FindProperty("settings");
            EditorGUILayout.LabelField("MATLAB Simulation", EditorStyles.boldLabel);
            Draw(settings, "count", "Dot Count / Density", "MATLAB N: total number of simulated dots. Source default: 1000. Density also depends on Width, Height and, in 3D, Depth.");
            Draw(settings, "dimension2D", "Two Dimensional", "MATLAB Dimension_2D. On: every dot stays in the plane at Start Depth and moves only in X/Y. Off: positions and directions use the full 3D volume.");
            SerializedProperty rule = settings.FindPropertyRelative("rule");
            EditorGUILayout.PropertyField(rule, new GUIContent("Neighbour Rule", "MATLAB algo_type. Metric selects every dot inside radius r. Topological selects the nearest K entries, including the dot itself."));
            bool topological = rule.enumValueIndex == (int)NeighbourRule.Topological;
            using (new EditorGUI.DisabledScope(topological))
                Draw(settings, "metricRadius", "Metric Radius (m)", "MATLAB sim_param in metric mode: every dot at Euclidean distance <= this radius is a neighbour. Self is included. Source default: 2 m.");
            using (new EditorGUI.DisabledScope(!topological))
            {
                Draw(settings, "topologicalK", "Topological K", "MATLAB sim_param in topological mode. K includes the dot itself, so K=2 normally means self plus the nearest other dot. All ties at the Kth distance are included.");
                Draw(settings, "topologicalRadiusCap", "Topological Radius Cap (m)", "MATLAB int_r_topo: additional maximum distance for topological neighbours. Source default 1000 m effectively covers the whole room.");
            }
            Draw(settings, "speed", "Speed (m/s)", "MATLAB v: constant movement speed. Neighbour vectors change direction only; the result is normalized back to this speed. Source default: 3 m/s.");
            Draw(settings, "noise", "Directional Noise (vector amplitude)", "MATLAB eta: random vector-component amplitude, NOT an angle in radians. Applied in X/Y for 2D and X/Y/Z for 3D, then normalized. 0 disables noise.");
            Draw(settings, "width", "Width X (m)", "MATLAB L_x. Used by 2D and by the 3D Box shape. Positions span -Width/2 to +Width/2. Source default: 20 m.");
            Draw(settings, "height", "Height Y (m)", "MATLAB L_y. Used by 2D and by the 3D Box shape. Positions span -Height/2 to +Height/2. Source default: 20 m.");
            Draw(settings, "wrapX", "Wrap X (2D)", "MATLAB torus_x. Retained unchanged for 2D; 3D uses the magnetic boundary instead of wrapping.");
            Draw(settings, "wrapY", "Wrap Y (2D)", "MATLAB torus_y. Retained unchanged for 2D; 3D uses the magnetic boundary instead of wrapping.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3D Study Extensions", EditorStyles.boldLabel);
            SerializedProperty volumeShape = settings.FindPropertyRelative("volumeShape");
            EditorGUILayout.PropertyField(volumeShape, new GUIContent("Volume Shape", "3D only. Choose the original box or a spherical core volume."));
            bool sphere = volumeShape.enumValueIndex == (int)VolumeShape.Sphere;
            using (new EditorGUI.DisabledScope(sphere))
                Draw(settings, "depth", "Box Depth Z (m)", "3D Box only. MATLAB L_z; source default 20 m.");
            using (new EditorGUI.DisabledScope(!sphere))
                Draw(settings, "sphereRadius", "Sphere Radius (m)", "3D Sphere only. Core radius before the magnetic buffer; default 10 m.");
            Draw(settings, "startDepth", "Start Depth Z (m)", "Near Z edge of either 3D shape and the fixed Z plane in 2D. With the defaults, both shapes span z=50 to 70 m.");
            Draw(settings, "boundaryBuffer", "Boundary Buffer (m)", "3D only. Thickness outside the core where the selected buffer behaviour is applied. Default 1 m.");
            Draw(settings, "boundaryMode", "Buffer Behaviour", "3D only. Magnetic Steering curves progressively inward. Smooth Linear Bounce follows the incoming line to the outer limit, slows smoothly, and returns on that exact line before MATLAB steering resumes.");
            using (new EditorGUI.DisabledScope(true))
                Draw(settings, "wrapZ", "Legacy Wrap Z (inactive)", "Serialized for compatibility only. All 3D shapes now use the magnetic boundary and never wrap.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Presentation", EditorStyles.boldLabel);
            SerializedProperty presentation = serializedObject.FindProperty("presentationMode");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                EditorGUILayout.PropertyField(presentation);
            bool vr = presentation.enumValueIndex == (int)StimulusPresentationMode.VirtualReality;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewer"));
            using (new EditorGUI.DisabledScope(!vr))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("alignToFirstValidHeadPose"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Randomization", EditorStyles.boldLabel);
            SerializedProperty randomizeSeed = serializedObject.FindProperty("randomizeSeed");
            EditorGUILayout.PropertyField(randomizeSeed);
            using (new EditorGUI.DisabledScope(randomizeSeed.boolValue))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("paused"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Arrow-key Presets", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(topological))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("metricRadiusPresets"));
            using (new EditorGUI.DisabledScope(!topological))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("topologicalKPresets"));

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("actualSeed"), new GUIContent("Actual Seed", "Seed used by the active simulation."));
            serializedObject.ApplyModifiedProperties();

            StimulusController controller = (StimulusController)target;
            if (!Application.isPlaying && controller.TryGetComponent(out DotFieldRenderer dotRenderer) &&
                dotRenderer.presentationMode != controller.presentationMode)
            {
                Undo.RecordObject(dotRenderer, "Change stimulus presentation mode");
                dotRenderer.presentationMode = controller.presentationMode;
                EditorUtility.SetDirty(dotRenderer);
            }
            double area = Math.Max(double.Epsilon, controller.settings.width * controller.settings.height);
            double volume = controller.settings.volumeShape == VolumeShape.Sphere
                ? 4.0 / 3.0 * Math.PI * Math.Pow(controller.settings.sphereRadius, 3)
                : area * controller.settings.depth;
            volume = Math.Max(double.Epsilon, volume);
            string outer = controller.settings.volumeShape == VolumeShape.Sphere
                ? $"sphere core radius {controller.settings.sphereRadius:F3} m; outer radius {controller.settings.sphereRadius + controller.settings.boundaryBuffer:F3} m"
                : $"box core {controller.settings.width:F3} x {controller.settings.height:F3} x {controller.settings.depth:F3} m; outer {controller.settings.width + 2 * controller.settings.boundaryBuffer:F3} x {controller.settings.height + 2 * controller.settings.boundaryBuffer:F3} x {controller.settings.depth + 2 * controller.settings.boundaryBuffer:F3} m";
            EditorGUILayout.HelpBox($"Derived density: {controller.settings.count / area:F3} dots/m^2 in 2D; {controller.settings.count / volume:F3} dots/m^3 in the 3D core. Selected volume: {outer}.", MessageType.None);
            try { controller.settings.Validate(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
            if (Application.isPlaying && controller.Simulation != null)
                EditorGUILayout.HelpBox($"Step {controller.Simulation.StepCount}; last simulation delta {controller.LastStepDeltaSeconds:F6} s", MessageType.None);

            if (controller.presentationMode == StimulusPresentationMode.Desktop)
                EditorGUILayout.HelpBox("Desktop: 2D is a fixed orthographic full-viewport view with no mouse response. D restarts in immersive perspective 3D, where right-mouse drag or I/J/K/L rotates the simulated head and the mouse wheel moves it forward/back. C resets position and rotation.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("VR: 3D surrounds the initially aligned tracked head. D restarts in 2D on a virtual screen in front of the current head pose. C realigns the presentation without resetting the swarm.", MessageType.Info);

            EditorGUILayout.HelpBox("Controls: D = 2D/3D and restart; A = aperture; R = restart; Up/Down = radius or K preset and restart; Space = pause; C = reset/realign view; Escape = stop.", MessageType.None);
            EditorGUILayout.HelpBox("Inspector values are copied into the MATLAB-faithful simulation when Play begins. Runtime controls keep the applicable displayed values synchronized.", MessageType.Info);
        }

        private static void Draw(SerializedProperty root, string child, string label, string tooltip)
        {
            EditorGUILayout.PropertyField(root.FindPropertyRelative(child), new GUIContent(label, tooltip));
        }
    }
}
