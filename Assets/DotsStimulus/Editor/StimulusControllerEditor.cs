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
            Draw(settings, "width", "Width X (m)", "MATLAB L_x: total X size. Positions span -Width/2 to +Width/2. Source default: 20 m.");
            Draw(settings, "height", "Height Y (m)", "MATLAB L_y: total Y size. Positions span -Height/2 to +Height/2. Source default: 20 m.");
            Draw(settings, "depth", "Depth Z (m)", "MATLAB L_z: depth of the 3D region. Source default: 20 m. In 2D, Z remains fixed at Start Depth.");
            Draw(settings, "startDepth", "Start Depth Z (m)", "MATLAB z_start: near Z coordinate of the swarm. Source default: 50 m, so a 20 m 3D depth spans z=50 to 70 m. Centred immersive presentation translates this visually but does not change the simulation coordinates.");
            Draw(settings, "wrapX", "Wrap X", "MATLAB torus_x: crossing one X boundary makes the dot reappear at the opposite boundary without changing direction.");
            Draw(settings, "wrapY", "Wrap Y", "MATLAB torus_y: crossing one Y boundary makes the dot reappear at the opposite boundary without changing direction.");
            Draw(settings, "wrapZ", "Wrap Z (3D)", "MATLAB torus_z: wrap z between Start Depth and Start Depth + Depth. The current player default is on; the MATLAB comment says it was off in the experiment, which can let the aligned swarm drift out of view.");

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
            double volume = Math.Max(double.Epsilon, area * controller.settings.depth);
            EditorGUILayout.HelpBox($"Derived density: {controller.settings.count / area:F3} dots/m^2 in 2D; {controller.settings.count / volume:F3} dots/m^3 in 3D. Edit Dot Count or the simulation dimensions to change it.", MessageType.None);
            try { controller.settings.Validate(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
            if (Application.isPlaying && controller.Simulation != null)
                EditorGUILayout.HelpBox($"Step {controller.Simulation.StepCount}; last simulation delta {controller.LastStepDeltaSeconds:F6} s", MessageType.None);

            if (controller.presentationMode == StimulusPresentationMode.Desktop)
                EditorGUILayout.HelpBox("Desktop: 2D is a fixed orthographic full-viewport view with no mouse response. D restarts in immersive perspective 3D, where right-mouse drag or I/J/K/L rotates the simulated head. C resets the view.", MessageType.Info);
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
