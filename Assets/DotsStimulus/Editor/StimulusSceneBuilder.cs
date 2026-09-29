using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DotsStimulus.Editor
{
    public static class StimulusSceneBuilder
    {
        [MenuItem("Tools/Dots Stimulus/Create stimulus scene")]
        [MenuItem("Tools/Dots Stimulus/Create desktop 3D scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var origin = new GameObject("Desktop Player");
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(origin.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 140;
            camera.allowHDR = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            DesktopFlyCamera.Configure(camera);

            var stimulus = new GameObject("Stimulus (world fixed)");
            var renderer = stimulus.AddComponent<DotFieldRenderer>();
            renderer.useAperture = false;
            renderer.dotShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DotsStimulus/Shaders/StimulusDots.shader");
            renderer.frameShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DotsStimulus/Shaders/StimulusFrame.shader");
            var controller = stimulus.AddComponent<StimulusController>();
            controller.viewer = camera;
            controller.desktopPreview = true;
            controller.settings.dimension2D = false;
            stimulus.transform.position = new Vector3(0, 0, -60);
            Directory.CreateDirectory("Assets/DotsStimulus/Scenes");
            AssetDatabase.Refresh();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DotsStimulus/Scenes/Desktop3D.unity");
            EditorSceneManager.SaveScene(scene, path);
            Selection.activeGameObject = stimulus;
            Debug.Log("Created " + path + ". Press Play and focus the Game view. Hold right mouse to look; WASD to move; Q/E down/up; C return to centre. No VR setup needed.");
        }

        // Run only against a separate validation project, never the user's open Editor project.
        public static void ValidateImport()
        {
            CreateScene();
            foreach (string path in new[] { "Assets/DotsStimulus/Shaders/StimulusDots.shader", "Assets/DotsStimulus/Shaders/StimulusFrame.shader" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
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
