#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotsStimulus.EyeTracking;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
#if VIVE_SRANIPAL
using ViveSR.anipal.Eye;
#endif

namespace DotsStimulus.Editor.Tests
{
    public sealed class StimulusUnityTests
    {
        [Test]
        public void DesktopMenuCreatesSourceDefaults()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StimulusSceneBuilder.CreateDesktopStimulus();
            string path = EditorSceneManager.GetActiveScene().path;
            try
            {
                var stimulus = UnityEngine.Object.FindAnyObjectByType<StimulusController>();
                Assert.That(stimulus.presentationMode, Is.EqualTo(StimulusPresentationMode.Desktop));
                Assert.That(stimulus.settings.dimension2D, Is.True);
                Assert.That(stimulus.settings.count, Is.EqualTo(1000));
                Assert.That(stimulus.settings.metricRadius, Is.EqualTo(2));
                Assert.That(stimulus.settings.topologicalK, Is.EqualTo(2));
                var renderer = stimulus.GetComponent<DotFieldRenderer>();
                Assert.That(renderer.useAperture, Is.False);
                Assert.That(renderer.fillDesktop2DViewport, Is.True);
                Assert.That(renderer.viewerPlacement, Is.EqualTo(ViewerPlacement.Centre));
                Assert.That(renderer.outsideClearance, Is.EqualTo(10));
                Assert.That(stimulus.settings.volumeShape, Is.EqualTo(VolumeShape.Box));
                Assert.That(stimulus.settings.sphereRadius, Is.EqualTo(10));
                Assert.That(stimulus.settings.boundaryBuffer, Is.EqualTo(1));
                Assert.That(stimulus.settings.boundaryMode, Is.EqualTo(BoundaryMode.MagneticSteering));
                Assert.That(stimulus.viewer.GetUniversalAdditionalCameraData().allowXRRendering, Is.False);
                DesktopFlyCamera desktopCamera = stimulus.viewer.GetComponent<DesktopFlyCamera>();
                Assert.That(desktopCamera.DerivedVerticalFieldOfView, Is.EqualTo(34.2238f).Within(.01f));
                Assert.That(desktopCamera.scrollZoomMetresPerNotch, Is.EqualTo(1));
                Assert.That(desktopCamera.middleDragPanMetresPerPixel, Is.EqualTo(.01f));
                Assert.That(UnityEngine.Object.FindAnyObjectByType<EyeTrackingRecorder>(), Is.Null);
            }
            finally { DeleteGeneratedScene(path); }
        }

        [Test]
        public void VrMenuCreatesStereoTrackedWorldFixedRig()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StimulusSceneBuilder.CreateVrStimulus();
            string path = EditorSceneManager.GetActiveScene().path;
            try
            {
                var stimulus = UnityEngine.Object.FindAnyObjectByType<StimulusController>();
                var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                var recorder = UnityEngine.Object.FindAnyObjectByType<EyeTrackingRecorder>();
                Assert.That(stimulus.presentationMode, Is.EqualTo(StimulusPresentationMode.VirtualReality));
                Assert.That(stimulus.settings.dimension2D, Is.False);
                Assert.That(stimulus.GetComponent<DotFieldRenderer>().useAperture, Is.True);
                Assert.That(stimulus.GetComponent<DotFieldRenderer>().viewerPlacement, Is.EqualTo(ViewerPlacement.Centre));
                Assert.That(stimulus.GetComponent<DotFieldRenderer>().outsideClearance, Is.EqualTo(10));
                Assert.That(stimulus.settings.volumeShape, Is.EqualTo(VolumeShape.Box));
                Assert.That(stimulus.settings.sphereRadius, Is.EqualTo(10));
                Assert.That(stimulus.settings.boundaryBuffer, Is.EqualTo(1));
                Assert.That(stimulus.settings.boundaryMode, Is.EqualTo(BoundaryMode.MagneticSteering));
                Assert.That(origin, Is.Not.Null);
                Assert.That(origin.Camera.GetUniversalAdditionalCameraData().allowXRRendering, Is.True);
                Assert.That(origin.Camera.GetComponent<TrackedPoseDriver>(), Is.Not.Null);
                Assert.That(recorder, Is.Null);
                Assert.That(UnityEngine.Object.FindAnyObjectByType<SranipalSafeBootstrap>(), Is.Null);
                Assert.That(UnityEngine.Object.FindAnyObjectByType<SranipalCalibrationController>(), Is.Null);
                Assert.That(UnityEngine.Object.FindAnyObjectByType<OpenXREyeTrackingProvider>(), Is.Null);
                Assert.That(stimulus.transform.parent, Is.Null, "stimulus must not follow the tracked rig");
            }
            finally { DeleteGeneratedScene(path); }
        }

        [Test]
        public void RecordingRejectsUnavailableRequiredNativeProviderWithoutFiles()
        {
            var go = new GameObject("Recorder failure test");
            try
            {
                var recorder = go.AddComponent<EyeTrackingRecorder>();
                recorder.participantId = "UnityUnavailableProviderTest";
                recorder.nativeProviderOverride = new FakeNativeProvider(false);
                recorder.requireNative120Hz = true;
                recorder.StartRecording();
                Assert.That(recorder.State, Is.EqualTo(RecordingState.Error));
                Assert.That(Directory.Exists(Path.Combine(Application.dataPath, "_DATA", recorder.participantId)), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RecordingStreamsSchemasAndStopIsIdempotent()
        {
            string participant = "UnityRecorderSchema_" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Application.dataPath, "_DATA", participant);
            var go = new GameObject("Recorder schema test");
            try
            {
                var recorder = go.AddComponent<EyeTrackingRecorder>();
                recorder.participantId = participant;
                recorder.sessionId = "schema";
                recorder.nativeProviderOverride = new FakeNativeProvider(true);
                recorder.requireNative120Hz = true;
                recorder.StartRecording();
                Assert.That(recorder.State, Is.EqualTo(RecordingState.Recording));
                recorder.RecordEvent("test_event", "value");
                recorder.StopAndFlush("unity_editor_stop_button");
                recorder.StopRecording();
                Assert.That(recorder.State, Is.EqualTo(RecordingState.Saved));
                Assert.That(EyeTrackingRecorder.MonitorStatus.savedFiles, Has.Length.EqualTo(4));
                foreach (string file in EyeTrackingRecorder.MonitorStatus.savedFiles) Assert.That(File.Exists(file), Is.True, file);
                Assert.That(File.ReadAllText(EyeTrackingRecorder.MonitorStatus.savedFiles[0]), Does.Contain(EyeTrackingRecorder.MatlabSourceSha256));
                Assert.That(File.ReadAllText(EyeTrackingRecorder.MonitorStatus.savedFiles[0]), Does.Contain("requestedSranipalVersion"));
                Assert.That(File.ReadLines(EyeTrackingRecorder.MonitorStatus.savedFiles[1]).First(), Does.StartWith("host_stopwatch_ticks"));
                Assert.That(File.ReadLines(EyeTrackingRecorder.MonitorStatus.savedFiles[2]).First(), Does.Contain("simulation_step"));
                Assert.That(File.ReadAllText(EyeTrackingRecorder.MonitorStatus.savedFiles[3]), Does.Contain("test_event"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void TimestampCollisionReceivesNumericSuffix()
        {
            string directory = Path.Combine(Application.temporaryCachePath, "DotsCollision_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                const string timestamp = "20260929T142355.417Z";
                File.WriteAllText(Path.Combine(directory, timestamp + "_dots_metadata.json"), "reserved");
                Assert.That(EyeTrackingRecorder.ChooseUniquePrefix(directory, timestamp, "dots"), Is.EqualTo(timestamp + "_dots_1"));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void BlankParticipantIsSanitizedAndWarned()
        {
            Assert.That(EyeTrackingRecorder.Sanitize(" ", "UnknownParticipant"), Is.EqualTo("UnknownParticipant"));
            Assert.That(EyeTrackingRecorder.Sanitize("P/01", "UnknownParticipant"), Is.EqualTo("P_01"));
        }

        [Test]
        public void VrPresentationCenters3DAndMaps2DToPhysicalDisplay()
        {
            var go = new GameObject("VR presentation mapping test");
            try
            {
                var renderer = go.AddComponent<DotFieldRenderer>();
                renderer.presentationMode = StimulusPresentationMode.VirtualReality;
                renderer.mirrorMatlabX = false;
                var settings = new SwarmSettings { width = 20, height = 20, depth = 20, startDepth = 50, dimension2D = false };
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 50), settings).z, Is.EqualTo(-10).Within(1e-5));
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 60), settings).z, Is.EqualTo(0).Within(1e-5));
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 70), settings).z, Is.EqualTo(10).Within(1e-5));

                renderer.viewerPlacement = ViewerPlacement.Outside;
                renderer.outsideClearance = 10;
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 50), settings).z, Is.EqualTo(10).Within(1e-5), "box near face clearance");
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 70), settings).z, Is.EqualTo(30).Within(1e-5));

                settings.volumeShape = VolumeShape.Sphere;
                settings.sphereRadius = 7;
                renderer.outsideClearance = 4;
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 50), settings).z, Is.EqualTo(4).Within(1e-5), "sphere near surface clearance");
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 64), settings).z, Is.EqualTo(18).Within(1e-5));

                settings.dimension2D = true;
                Vector3 corner = renderer.MapPositionForPresentation(new DVector3(10, 10, 50), settings);
                Assert.That(corner.x, Is.EqualTo(renderer.vr2DDisplayWidth * .5f).Within(1e-5));
                Assert.That(corner.y, Is.EqualTo(renderer.vr2DDisplayHeight * .5f).Within(1e-5));
                Assert.That(corner.z, Is.EqualTo(renderer.vr2DDisplayDistance).Within(1e-5));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void DesktopPresentationFills2DViewportAndCenters3DWithoutChangingSimulationCoordinates()
        {
            var cameraObject = new GameObject("Desktop mapping camera", typeof(Camera));
            var rendererObject = new GameObject("Desktop presentation mapping test");
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.aspect = 4f / 3f;
                DesktopFlyCamera controls = DesktopFlyCamera.Configure(camera);
                controls.displayWidthMetres = .587f;
                controls.viewingDistanceMetres = .715f;

                var renderer = rendererObject.AddComponent<DotFieldRenderer>();
                renderer.presentationMode = StimulusPresentationMode.Desktop;
                renderer.mirrorMatlabX = false;
                renderer.fillDesktop2DViewport = true;
                renderer.viewerPlacement = ViewerPlacement.Centre;
                renderer.SetPresentationCamera(camera);

                var settings = new SwarmSettings { width = 20, height = 20, depth = 20, startDepth = 50, dimension2D = true };
                Vector3 corner = renderer.MapPositionForPresentation(new DVector3(10, 10, 50), settings);
                Assert.That(corner.x, Is.EqualTo(.587f * .5f).Within(1e-5));
                Assert.That(corner.y, Is.EqualTo((.587f / (4f / 3f)) * .5f).Within(1e-5));
                Assert.That(corner.z, Is.EqualTo(.715f).Within(1e-5));

                settings.dimension2D = false;
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 50), settings).z, Is.EqualTo(-10).Within(1e-5));
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 70), settings).z, Is.EqualTo(10).Within(1e-5));

                renderer.viewerPlacement = ViewerPlacement.Outside;
                renderer.outsideClearance = 10;
                Assert.That(renderer.MapPositionForPresentation(new DVector3(0, 0, 50), settings).z, Is.EqualTo(10).Within(1e-5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rendererObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void VrRecenterKeepsConfiguredOutsidePlacement()
        {
            var cameraObject = new GameObject("VR recenter camera", typeof(Camera));
            var stimulusObject = new GameObject("VR recenter stimulus");
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(5, 25, 0));
                var renderer = stimulusObject.AddComponent<DotFieldRenderer>();
                renderer.presentationMode = StimulusPresentationMode.VirtualReality;
                renderer.viewerPlacement = ViewerPlacement.Outside;
                renderer.outsideClearance = 10;
                var controller = stimulusObject.AddComponent<StimulusController>();
                controller.presentationMode = StimulusPresentationMode.VirtualReality;
                controller.viewer = camera;

                controller.Recenter();

                Assert.That(stimulusObject.transform.position, Is.EqualTo(camera.transform.position));
                Assert.That(Quaternion.Angle(stimulusObject.transform.rotation, camera.transform.rotation), Is.LessThan(1e-5));
                Assert.That(renderer.viewerPlacement, Is.EqualTo(ViewerPlacement.Outside));
                Assert.That(renderer.outsideClearance, Is.EqualTo(10));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stimulusObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DesktopCameraUsesFixedOrthographic2DAndPerspective3D()
        {
            var cameraObject = new GameObject("Desktop dimension camera", typeof(Camera));
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.aspect = 4f / 3f;
                DesktopFlyCamera controls = DesktopFlyCamera.Configure(camera);

                controls.SetTwoDimensional(true);
                Assert.That(controls.IsTwoDimensional, Is.True);
                Assert.That(camera.orthographic, Is.True);
                Assert.That(camera.orthographicSize, Is.EqualTo((.587f / (4f / 3f)) * .5f).Within(1e-5));

                controls.SetTwoDimensional(false);
                Assert.That(controls.IsTwoDimensional, Is.False);
                Assert.That(camera.orthographic, Is.False);
                Assert.That(camera.fieldOfView, Is.EqualTo(controls.FieldOfViewForAspect(4f / 3f)).Within(1e-5));
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void DesktopMouseWheelMovesOnlyIn3DAndResetRestoresHome()
        {
            var cameraObject = new GameObject("Desktop scroll camera", typeof(Camera));
            try
            {
                var controls = DesktopFlyCamera.Configure(cameraObject.GetComponent<Camera>());
                controls.scrollZoomMetresPerNotch = 1.5f;
                controls.SetTwoDimensional(false);
                Vector3 home = cameraObject.transform.position;

                controls.ApplyScrollZoom(1);
                Assert.That(cameraObject.transform.position.z, Is.EqualTo(home.z + 1.5f).Within(1e-5),
                    "Backends that report one unit per notch must produce visible movement");
                controls.ApplyScrollZoom(-1);
                Assert.That(cameraObject.transform.position, Is.EqualTo(home));

                controls.ApplyScrollZoom(120);
                Assert.That(cameraObject.transform.position.z, Is.EqualTo(home.z + 1.5f).Within(1e-5));
                controls.ApplyScrollZoom(-120);
                Assert.That(cameraObject.transform.position, Is.EqualTo(home));

                controls.SetTwoDimensional(true);
                controls.ApplyScrollZoom(120);
                Assert.That(cameraObject.transform.position, Is.EqualTo(home), "2D must ignore wheel movement");

                controls.SetTwoDimensional(false);
                controls.ApplyScrollZoom(120);
                controls.ResetView();
                Assert.That(cameraObject.transform.position, Is.EqualTo(home), "C/reset restores the initial position");
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void DesktopMiddleDragPansInCameraPlaneOnlyIn3D()
        {
            var cameraObject = new GameObject("Desktop pan camera", typeof(Camera));
            try
            {
                var controls = DesktopFlyCamera.Configure(cameraObject.GetComponent<Camera>());
                controls.middleDragPanMetresPerPixel = .02f;
                controls.SetTwoDimensional(false);
                cameraObject.transform.rotation = Quaternion.Euler(0, 90, 0);
                Vector3 home = cameraObject.transform.position;

                controls.ApplyMiddleDragPan(new Vector2(10, 5));
                Vector3 expected = home + cameraObject.transform.right * .2f + cameraObject.transform.up * .1f;
                Assert.That(Vector3.Distance(cameraObject.transform.position, expected), Is.LessThan(1e-5));

                controls.SetTwoDimensional(true);
                Vector3 position2D = cameraObject.transform.position;
                controls.ApplyMiddleDragPan(new Vector2(10, 5));
                Assert.That(cameraObject.transform.position, Is.EqualTo(position2D), "2D must ignore middle-drag panning");
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void SranipalNativePluginsAreDisabledInEditor()
        {
            foreach (string name in new[] { "libHTC_License.dll", "nanomsg.dll", "SRanipal.dll", "SRWorks_Log.dll", "ViveSR_Client.dll" })
            {
                var importer = AssetImporter.GetAtPath("Assets/ViveSR/Plugins/" + name) as PluginImporter;
                Assert.That(importer, Is.Not.Null, name);
                Assert.That(importer.GetCompatibleWithEditor(), Is.False, name + " must not load in Editor Play mode");
            }
        }

        private static void DeleteGeneratedScene(string path)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
        }

        private sealed class FakeNativeProvider : INativeEyeTrackingProvider
        {
            public FakeNativeProvider(bool available) => IsAvailable = available;
            public string ProviderName => "fake";
            public bool IsAvailable { get; }
            public bool IsCapturing { get; private set; }
            public string Status => IsAvailable ? "fake ready" : "fake unavailable";
            public long ReceivedCount => 0;
            public long DroppedCount => 0;
            public bool StartCapture() { IsCapturing = IsAvailable; return IsCapturing; }
            public void StopCapture() => IsCapturing = false;
            public bool TryDequeue(out NativeEyeSample sample) { sample = default; return false; }
        }
    }
}
#endif
