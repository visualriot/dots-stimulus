using UnityEditor;
using UnityEngine;

namespace DotsStimulus.Editor
{
    [CustomEditor(typeof(DotFieldRenderer))]
    public sealed class DotFieldRendererEditor : UnityEditor.Editor
    {
        private const float MatlabWidthMetres = .587f;
        private const float MatlabHeightMetres = .44025f;
        private const float MatlabWidthPixels = 800f;
        private const float MatlabHeightPixels = 600f;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var renderer = (DotFieldRenderer)target;
            StimulusController controller = renderer.GetComponent<StimulusController>();
            SerializedProperty presentation = serializedObject.FindProperty("presentationMode");

            EditorGUILayout.LabelField("Presentation", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                EditorGUILayout.PropertyField(presentation, new GUIContent("Presentation Mode", "Desktop or VR presentation. This value is synchronized with Stimulus Controller."));
            bool presentationChanged = EditorGUI.EndChangeCheck();

            StimulusPresentationMode mode = (StimulusPresentationMode)presentation.enumValueIndex;
            bool vr = mode == StimulusPresentationMode.VirtualReality;
            EditorGUILayout.HelpBox(vr
                ? "VR settings are active. Desktop-only presentation controls are shown greyed out."
                : "Desktop settings are active. VR-only presentation controls are shown greyed out.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dots (shared)", EditorStyles.boldLabel);
            SerializedProperty angular = serializedObject.FindProperty("useAngularSize");
            EditorGUILayout.PropertyField(angular);
            using (new EditorGUI.DisabledScope(angular.boolValue))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("diameterPixels"));
            using (new EditorGUI.DisabledScope(!angular.boolValue))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("diameterDegrees"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mirrorMatlabX"));
            EditorGUILayout.HelpBox("Angular size changes dot diameter only. It never changes dot count, spatial density, neighbours, or movement.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Desktop presentation", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(vr))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fillDesktop2DViewport"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3D viewer placement", EditorStyles.boldLabel);
            SerializedProperty viewerPlacement = serializedObject.FindProperty("viewerPlacement");
            EditorGUILayout.PropertyField(viewerPlacement);
            using (new EditorGUI.DisabledScope(viewerPlacement.enumValueIndex != (int)ViewerPlacement.Outside))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("outsideClearance"));
            EditorGUILayout.HelpBox("Shared by desktop 3D and immersive VR 3D. Outside Clearance is measured from the viewer to the nearest volume surface. Neither setting affects either 2D presentation.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MATLAB aperture", EditorStyles.boldLabel);
            SerializedProperty useAperture = serializedObject.FindProperty("useAperture");
            EditorGUILayout.PropertyField(useAperture);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VR immersive 3D", EditorStyles.boldLabel);
            SerializedProperty immersiveAperture = serializedObject.FindProperty("useApertureInImmersive3D");
            using (new EditorGUI.DisabledScope(!vr))
            {
                EditorGUILayout.PropertyField(immersiveAperture);
            }

            bool sourceApertureGeometryActive = useAperture.boolValue && (!vr || immersiveAperture.boolValue);
            using (new EditorGUI.DisabledScope(!sourceApertureGeometryActive))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("frameDistance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("frameOuterWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("frameOuterHeight"));
            }
            if (vr)
                EditorGUILayout.HelpBox("Frame Distance/Outer Width/Outer Height are normally desktop-only. In VR they become relevant only if both Use Aperture and Use Aperture In Immersive 3D are enabled.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VR 2D display", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!vr))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("vr2DDisplayWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("vr2DDisplayHeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("vr2DDisplayDistance"));
                using (new EditorGUI.DisabledScope(!useAperture.boolValue))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("vr2DFrameBorder"));
            }

            if (vr)
            {
                float width = Mathf.Max(.0001f, serializedObject.FindProperty("vr2DDisplayWidth").floatValue);
                float height = Mathf.Max(.0001f, serializedObject.FindProperty("vr2DDisplayHeight").floatValue);
                float distance = Mathf.Max(.0001f, serializedObject.FindProperty("vr2DDisplayDistance").floatValue);
                float horizontalDegrees = 2f * Mathf.Atan(width / (2f * distance)) * Mathf.Rad2Deg;
                float verticalDegrees = 2f * Mathf.Atan(height / (2f * distance)) * Mathf.Rad2Deg;
                float equivalentWidthPixels = width / (MatlabWidthMetres / MatlabWidthPixels);
                float equivalentHeightPixels = height / (MatlabHeightMetres / MatlabHeightPixels);
                EditorGUILayout.HelpBox(
                    $"Derived view: {horizontalDegrees:F2} x {verticalDegrees:F2} degrees. " +
                    $"At the MATLAB physical pixel pitch this is approximately {equivalentWidthPixels:F0} x {equivalentHeightPixels:F0} reference pixels. " +
                    "Metres remain authoritative in VR because headset pixels do not have a fixed world size.", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering references (normally unchanged)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dotShader"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("frameShader"));

            serializedObject.ApplyModifiedProperties();

            if (presentationChanged && controller && !Application.isPlaying && controller.presentationMode != mode)
            {
                Undo.RecordObject(controller, "Change stimulus presentation mode");
                controller.presentationMode = mode;
                EditorUtility.SetDirty(controller);
            }
        }
    }
}
