using System;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace DotsStimulus.Editor
{
    public static class StimulusSceneBuilder
    {
        [MenuItem("Tools/Dots Stimulus/Create New Stimulus/Desktop Stimulus", false, 0)]
        public static void CreateDesktopStimulus()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = CreateCamera("Desktop Observer", false);
            DesktopFlyCamera controls = DesktopFlyCamera.Configure(camera);
            controls.disableXrForDesktop = true;
            controls.displayWidthMetres = .587f;
            controls.viewingDistanceMetres = .715f;
            controls.referenceWidthPixels = 800;
            controls.referenceHeightPixels = 600;
            controls.adaptFieldOfViewToViewport = true;
            controls.scrollZoomMetresPerNotch = 1f;
            controls.middleDragPanMetresPerPixel = .01f;
            controls.ApplyCameraSettings();

            StimulusController controller = CreateStimulus(camera, StimulusPresentationMode.Desktop, true);
            string path = SaveUnique(scene, "DesktopStimulus.unity");
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Created " + path + ". Desktop starts in 2D filling the Game view. D switches to the immersive 3D preview; RMB or I/J/K/L rotates, the mouse wheel moves forward/back, middle-mouse drag pans, and C resets the simulated head.");
        }

        [MenuItem("Tools/Dots Stimulus/Create New Stimulus/VR Stimulus", false, 1)]
        public static void CreateVrStimulus()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureOpenXr();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var originObject = new GameObject("XR Origin");
            var offsetObject = new GameObject("Camera Offset");
            offsetObject.transform.SetParent(originObject.transform, false);
            Camera camera = CreateCamera("Main Camera", true);
            camera.transform.SetParent(offsetObject.transform, false);

            XROrigin origin = originObject.AddComponent<XROrigin>();
            origin.Origin = originObject;
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0;
            AddHeadPoseBindings(camera.gameObject);

            StimulusController stimulus = CreateStimulus(camera, StimulusPresentationMode.VirtualReality, false);

            string path = SaveUnique(scene, "VRStimulus.unity");
            Selection.activeGameObject = stimulus.gameObject;
            Debug.Log("Created " + path + ". VR starts in 3D with OpenXR head tracking. Eye tracking and recording are disabled.");
        }

        private static Camera CreateCamera(string parentName, bool stereo)
        {
            GameObject parent = stereo ? null : new GameObject(parentName);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            if (parent) cameraObject.transform.SetParent(parent.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 140f;
            camera.allowHDR = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.allowXRRendering = stereo;
            return camera;
        }

        private static StimulusController CreateStimulus(Camera camera, StimulusPresentationMode mode, bool dimension2D)
        {
            var stimulus = new GameObject("Stimulus (world fixed)");
            var renderer = stimulus.AddComponent<DotFieldRenderer>();
            renderer.presentationMode = mode;
            renderer.SetPresentationCamera(camera);
            renderer.useAperture = mode == StimulusPresentationMode.VirtualReality;
            renderer.frameDistance = .715f;
            renderer.frameOuterWidth = mode == StimulusPresentationMode.Desktop ? .587f : 10f;
            renderer.frameOuterHeight = mode == StimulusPresentationMode.Desktop ? .44025f : 10f;
            renderer.diameterPixels = 6;
            renderer.useAngularSize = false;
            renderer.useWorldSizeIn3D = true;
            renderer.diameterMetres = .06f;
            renderer.fillDesktop2DViewport = true;
            renderer.viewerPlacement = ViewerPlacement.Centre;
            renderer.outsideClearance = 10f;
            renderer.useApertureInImmersive3D = false;
            renderer.vr2DDisplayWidth = .587f;
            renderer.vr2DDisplayHeight = .44025f;
            renderer.vr2DDisplayDistance = .715f;
            renderer.vr2DFrameBorder = .04f;
            renderer.dotShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DotsStimulus/Shaders/StimulusDots.shader");
            renderer.frameShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DotsStimulus/Shaders/StimulusFrame.shader");
            var controller = stimulus.AddComponent<StimulusController>();
            controller.viewer = camera;
            controller.presentationMode = mode;
            controller.settings.dimension2D = dimension2D;
            controller.settings.count = 1000;
            controller.settings.metricRadius = 2;
            controller.settings.topologicalK = 2;
            controller.settings.speed = 3;
            controller.settings.width = controller.settings.height = controller.settings.depth = 20;
            controller.settings.startDepth = 50;
            controller.settings.volumeShape = VolumeShape.Box;
            controller.settings.sphereRadius = 10;
            controller.settings.boundaryBuffer = 1;
            controller.settings.boundaryMode = BoundaryMode.MagneticSteering;
            controller.settings.fadeBuffer = 2;
            controller.settings.fadeLifetimeMin = 1;
            controller.settings.fadeLifetimeMax = 2;
            controller.settings.fadeRespawnMode = FadeRespawnMode.Centre;
            controller.settings.wrapX = controller.settings.wrapY = controller.settings.wrapZ = true;
            return controller;
        }

        private static void AddHeadPoseBindings(GameObject cameraObject)
        {
            var driver = cameraObject.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.ignoreTrackingState = false;
            driver.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.PassThrough,
                "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.PassThrough,
                "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State", InputActionType.PassThrough,
                "<XRHMD>/trackingState", expectedControlType: "Integer"));
        }

        private static void ConfigureOpenXr()
        {
            XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (general != null)
            {
                general.InitManagerOnStart = true;
                EditorUtility.SetDirty(general);
            }
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                Debug.LogError("Windows OpenXR settings are missing. Configure OpenXR in XR Plug-in Management first.");
                return;
            }
            EyeGazeInteraction eyeGaze = settings.GetFeature<EyeGazeInteraction>();
            if (eyeGaze != null) { eyeGaze.enabled = false; EditorUtility.SetDirty(eyeGaze); }
            HTCViveControllerProfile vive = settings.GetFeature<HTCViveControllerProfile>();
            if (vive != null) { vive.enabled = true; EditorUtility.SetDirty(vive); }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static string SaveUnique(UnityEngine.SceneManagement.Scene scene, string fileName)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/" + fileName);
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        public static void ValidateImport()
        {
            CreateDesktopStimulus();
            foreach (string path in new[] { "Assets/DotsStimulus/Shaders/StimulusDots.shader", "Assets/DotsStimulus/Shaders/StimulusFrame.shader" })
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (!shader) throw new Exception("Shader not imported: " + path);
                var material = new Material(shader) { enableInstancing = true };
                ShaderUtil.CompilePass(material, 0, true);
                UnityEngine.Object.DestroyImmediate(material);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new Exception(path + ": " + message.message);
            }
            Debug.Log("DOTS_IMPORT_VALIDATION_PASSED");
        }
    }

    internal static class StimulusHelpText
    {
        public const string SharedControls =
            "PLAY MODE KEYS\n" +
            "D — switch between 2D and 3D (restarts the dots)\n" +
            "P — switch Centre/Outside placement in 3D (uses your current view)\n" +
            "A — show or hide the aperture frame\n" +
            "R — restart the dots\n" +
            "Up / Down — choose the next radius or neighbour-count preset (restarts)\n" +
            "Space — pause or continue\n" +
            "C — reset the desktop view or realign the VR stimulus\n" +
            "Escape — stop Play mode or quit the built application";

        public const string DesktopControls =
            "DESKTOP 3D MOVEMENT\n" +
            "Right-mouse drag or I / J / K / L — look around\n" +
            "Mouse wheel — move forward or backward\n" +
            "Middle-mouse drag — move sideways or up/down\n" +
            "These movement controls are intentionally disabled in 2D.";

        public const string VrControls =
            "VR VIEW\n" +
            "The headset controls the view. Desktop mouse movement is ignored.\n" +
            "C realigns the stimulus to the current headset pose. P also uses the current pose when changing placement.";

        public static void DrawQuickControls(bool virtualReality)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick controls", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(SharedControls, MessageType.Info);
            EditorGUILayout.HelpBox(virtualReality ? VrControls : DesktopControls, MessageType.None);
        }
    }

    public static class StimulusInstructionsMenu
    {
        private const string Root = "Tools/Dots Stimulus/Instructions/";

        [MenuItem(Root + "Quick Start", false, 100)]
        public static void QuickStart() => Show("Quick Start",
            "1. Create a Desktop or VR scene from Tools > Dots Stimulus > Create New Stimulus.\n\n" +
            "2. Select 'Stimulus (world fixed)' and set the dot, neighbour, shape, and boundary options in Stimulus Controller.\n\n" +
            "3. Set dot appearance and Centre/Outside placement in Dot Field Renderer.\n\n" +
            "4. Press Play. The quick-control list is shown at the bottom of the Inspectors.\n\n" +
            "5. Use a fixed seed before data collection if you need repeatable runs.");

        [MenuItem(Root + "Keyboard and Mouse Controls", false, 101)]
        public static void Controls() => Show("Keyboard and Mouse Controls",
            StimulusHelpText.SharedControls + "\n\n" + StimulusHelpText.DesktopControls + "\n\n" + StimulusHelpText.VrControls);

        [MenuItem(Root + "Desktop and VR", false, 102)]
        public static void DesktopAndVr() => Show("Desktop and VR",
            "DESKTOP\nStarts in 2D. Press D for a movable 3D preview. Right-mouse drag or I/J/K/L looks around; the wheel moves forward/back; middle-drag pans. Desktop contains no headset or eye-tracking setup.\n\n" +
            "VR\nStarts in immersive 3D and uses the tracked headset. The stimulus aligns once, then stays fixed in the room. C realigns it. Press D for a flat virtual 2D screen.\n\n" +
            "The dot movement rules are the same in Desktop and VR; only the way they are viewed changes.");

        [MenuItem(Root + "2D and 3D", false, 103)]
        public static void TwoAndThreeDimensions() => Show("2D and 3D",
            "2D\nDots move on one flat plane. X and Y wrapping can make a dot leaving one edge return at the opposite edge. Desktop fills the Game view; VR shows a virtual screen.\n\n" +
            "3D\nDots move inside a box or sphere. The selected boundary behaviour controls what happens when they reach the edge.\n\n" +
            "Pressing D switches dimension and restarts the dot positions. It does not change your saved Inspector settings.");

        [MenuItem(Root + "Viewer Placement", false, 104)]
        public static void Placement() => Show("Viewer Placement",
            "CENTRE\nThe box or sphere is centred around the viewer, so dots can surround them.\n\n" +
            "OUTSIDE\nThe whole volume is placed in front of the viewer. Outside Clearance sets the empty distance to its nearest surface.\n\n" +
            "Press P in 3D to switch placement. The stimulus uses the viewer's current position and facing direction, so Outside appears directly ahead. The dots are not restarted. P is ignored in 2D.");

        [MenuItem(Root + "Boundary Behaviours", false, 105)]
        public static void Boundaries() => Show("Boundary Behaviours (3D)",
            "MAGNETIC STEERING\nDots turn smoothly back toward the core while inside the boundary buffer.\n\n" +
            "SMOOTH LINEAR BOUNCE\nDots slow down, reverse, and return along the same straight line.\n\n" +
            "FADE AND RESPAWN\nAn exiting dot fades in the outer fade buffer. A replacement appears immediately at the centre or at a random point inside, so the core keeps the requested dot count. Fading copies do not influence the other dots.\n\n" +
            "Boundary settings affect 3D only. 2D uses its X/Y wrapping options.");

        [MenuItem(Root + "Dots, Neighbours, and Seeds", false, 106)]
        public static void DotsAndNeighbours() => Show("Dots, Neighbours, and Seeds",
            "DOT COUNT\nHow many active dots are simulated. A larger count also increases density and processing cost.\n\n" +
            "3D DOT SIZE\nWorld Size is on by default, so distant dots look smaller. Diameter Metres controls their physical size. 2D keeps screen-based sizing.\n\n" +
            "METRIC NEIGHBOURS\nEach dot follows dots within the chosen distance.\n\n" +
            "TOPOLOGICAL NEIGHBOURS\nEach dot follows its nearest K dots. K includes the dot itself.\n\n" +
            "SEEDS\nRandomize Seed gives a different run each time. Turn it off and choose a Seed to repeat the same Unity run.\n\n" +
            "Up/Down steps through the preset radius or K values and restarts the dots.");

        [MenuItem(Root + "Open Full README", false, 120)]
        public static void OpenReadme()
        {
            string path = Path.Combine(Application.dataPath, "DotsStimulus/README.md");
            if (!File.Exists(path))
            {
                Show("Full README", "README not found at Assets/DotsStimulus/README.md.");
                return;
            }
            Show("Full Researcher Guide", File.ReadAllText(path));
        }

        private static void Show(string title, string message) =>
            StimulusInstructionsWindow.Open(title, message);
    }

    public sealed class StimulusInstructionsWindow : EditorWindow
    {
        private string heading = "Dots Stimulus Instructions";
        private string instructions = "Choose a topic from Tools > Dots Stimulus > Instructions.";
        private Vector2 scrollPosition;

        public static void Open(string heading, string instructions)
        {
            StimulusInstructionsWindow window = GetWindow<StimulusInstructionsWindow>();
            window.titleContent = new GUIContent("Dots Stimulus Instructions");
            window.minSize = new Vector2(460, 320);
            window.heading = heading;
            window.instructions = instructions;
            window.scrollPosition = Vector2.zero;
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            var headingStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            var bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                wordWrap = true,
                richText = false
            };

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(heading, headingStyle);
            EditorGUILayout.Space(6);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField(instructions, bodyStyle);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(6);
        }
    }

    [CustomEditor(typeof(DesktopFlyCamera))]
    public sealed class DesktopFlyCameraEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            StimulusHelpText.DrawQuickControls(false);
        }
    }
}
