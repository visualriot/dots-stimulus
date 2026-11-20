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
            EditorGUILayout.LabelField("Dot Simulation", EditorStyles.boldLabel);
            Draw(settings, "count", "Dot Count / Density", "Number of active dots. More dots increase density and processing cost. Default: 1000.");
            Draw(settings, "dimension2D", "Two Dimensional", "On: dots move on one flat X/Y plane. Off: dots move through the selected 3D box or sphere.");
            SerializedProperty rule = settings.FindPropertyRelative("rule");
            EditorGUILayout.PropertyField(rule, new GUIContent("Neighbour Rule", "Metric follows dots within a distance. Topological follows the nearest K dots."));
            bool topological = rule.enumValueIndex == (int)NeighbourRule.Topological;
            using (new EditorGUI.DisabledScope(topological))
                Draw(settings, "metricRadius", "Metric Radius (m)", "Metric mode only. Each dot follows dots within this distance. Default: 2 m.");
            using (new EditorGUI.DisabledScope(!topological))
            {
                Draw(settings, "topologicalK", "Topological K", "Topological mode only. Number of nearest dots to follow, including the dot itself. K=2 normally means itself plus one other dot.");
                Draw(settings, "topologicalRadiusCap", "Topological Radius Cap (m)", "Topological mode only. Ignore selected neighbours farther away than this. Default 1000 m is effectively unlimited.");
            }
            Draw(settings, "speed", "Speed (m/s)", "Movement speed of every active dot. Default: 3 metres per second.");
            Draw(settings, "noise", "Directional Noise", "Adds random variation to movement direction. Zero turns noise off. Larger values produce less regular movement.");
            Draw(settings, "width", "Width X (m)", "Horizontal size of the 2D plane or 3D box, in metres. Default: 20 m.");
            Draw(settings, "height", "Height Y (m)", "Vertical size of the 2D plane or 3D box, in metres. Default: 20 m.");
            Draw(settings, "wrapX", "Wrap X (2D)", "2D only. A dot leaving the left or right edge returns at the opposite edge.");
            Draw(settings, "wrapY", "Wrap Y (2D)", "2D only. A dot leaving the top or bottom edge returns at the opposite edge.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3D Shape and Boundary", EditorStyles.boldLabel);
            SerializedProperty volumeShape = settings.FindPropertyRelative("volumeShape");
            EditorGUILayout.PropertyField(volumeShape, new GUIContent("Volume Shape", "3D only. Choose a box or sphere for the active dot area."));
            bool sphere = volumeShape.enumValueIndex == (int)VolumeShape.Sphere;
            using (new EditorGUI.DisabledScope(sphere))
                Draw(settings, "depth", "Box Depth Z (m)", "3D Box only. Front-to-back size of the box. Default: 20 m.");
            using (new EditorGUI.DisabledScope(!sphere))
                Draw(settings, "sphereRadius", "Sphere Radius (m)", "3D Sphere only. Distance from the sphere centre to its active edge. Default: 10 m.");
            Draw(settings, "startDepth", "Start Depth Z (m)", "Simulation-space position of the 2D plane or nearest 3D edge. Most studies can keep the default of 50 m.");
            SerializedProperty boundaryMode = settings.FindPropertyRelative("boundaryMode");
            EditorGUILayout.PropertyField(boundaryMode, new GUIContent("Buffer Behaviour", "3D only. Choose whether dots turn back, bounce back, or fade while a replacement appears inside."));
            bool fading = boundaryMode.enumValueIndex == (int)BoundaryMode.FadeAndRespawn;
            if (fading)
            {
                Draw(settings, "fadeBuffer", "Fade Buffer (m)", "Space outside the active box or sphere where outgoing copies slow down and fade. Default: 2 m.");
                Draw(settings, "fadeLifetimeMin", "Minimum Fade Lifetime (s)", "Shortest time an outgoing copy can take to fade. Default: 1 second.");
                Draw(settings, "fadeLifetimeMax", "Maximum Fade Lifetime (s)", "Longest time an outgoing copy can take to fade. Default: 2 seconds.");
                Draw(settings, "fadeRespawnMode", "Respawn Location", "Centre puts replacements at the shape centre. Random Within Core spreads them through the active box or sphere.");
            }
            else
            {
                Draw(settings, "boundaryBuffer", "Boundary Buffer (m)", "Space outside the active shape used for turning or bouncing. Default: 1 m.");
            }
            using (new EditorGUI.DisabledScope(true))
                Draw(settings, "wrapZ", "Legacy Wrap Z (inactive)", "Kept only so older scenes still load correctly. Current 3D modes never wrap in Z.");

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
            double selectedBuffer = controller.settings.boundaryMode == BoundaryMode.FadeAndRespawn
                ? controller.settings.fadeBuffer
                : controller.settings.boundaryBuffer;
            string outer = controller.settings.volumeShape == VolumeShape.Sphere
                ? $"sphere core radius {controller.settings.sphereRadius:F3} m; outer radius {controller.settings.sphereRadius + selectedBuffer:F3} m"
                : $"box core {controller.settings.width:F3} x {controller.settings.height:F3} x {controller.settings.depth:F3} m; outer {controller.settings.width + 2 * selectedBuffer:F3} x {controller.settings.height + 2 * selectedBuffer:F3} x {controller.settings.depth + 2 * selectedBuffer:F3} m";
            EditorGUILayout.HelpBox($"Derived density: {controller.settings.count / area:F3} dots/m^2 in 2D; {controller.settings.count / volume:F3} dots/m^3 in the 3D core. Selected volume: {outer}.", MessageType.None);
            try { controller.settings.Validate(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
            if (Application.isPlaying && controller.Simulation != null)
                EditorGUILayout.HelpBox($"Step {controller.Simulation.StepCount}; active core dots {controller.Simulation.Positions.Length}; fading dots {controller.Simulation.FadingDots.Count}; last simulation delta {controller.LastStepDeltaSeconds:F6} s", MessageType.None);

            if (controller.presentationMode == StimulusPresentationMode.Desktop)
                EditorGUILayout.HelpBox("Desktop starts as a fixed 2D view. Press D for a movable 3D preview. The dot movement rules are unchanged.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("VR starts in immersive 3D using the tracked headset. Press D for a flat virtual 2D screen. C realigns without restarting the dots.", MessageType.Info);

            EditorGUILayout.HelpBox("Inspector values are copied into the simulation when Play begins. Save the scene after changing study settings.", MessageType.None);
            StimulusHelpText.DrawQuickControls(vr);
        }

        private static void Draw(SerializedProperty root, string child, string label, string tooltip)
        {
            EditorGUILayout.PropertyField(root.FindPropertyRelative(child), new GUIContent(label, tooltip));
        }
    }
}
