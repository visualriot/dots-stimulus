using System;
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
        [MenuItem("Tools/Dots Stimulus/Create desktop stimulus")]
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
            controls.ApplyCameraSettings();

            StimulusController controller = CreateStimulus(camera, StimulusPresentationMode.Desktop, true);
            string path = SaveUnique(scene, "DesktopStimulus.unity");
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Created " + path + ". Desktop starts in 2D filling the Game view. D switches to the immersive 3D preview; RMB or I/J/K/L rotates the simulated head; C resets it.");
        }

        [MenuItem("Tools/Dots Stimulus/Create VR stimulus")]
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
            camera.stereoTargetEye = stereo ? StereoTargetEyeMask.Both : StereoTargetEyeMask.None;
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
            renderer.fillDesktop2DViewport = true;
            renderer.centreDesktop3DOnViewer = true;
            renderer.centreVrVolumeOnViewer = true;
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
}
