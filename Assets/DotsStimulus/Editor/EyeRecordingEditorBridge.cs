using DotsStimulus.EyeTracking;
using UnityEditor;
using UnityEngine;

namespace DotsStimulus.Editor
{
    /// <summary>Finalizes an active session before scene objects are disabled by the Editor Stop button.</summary>
    [InitializeOnLoad]
    public static class EyeRecordingEditorBridge
    {
        static EyeRecordingEditorBridge() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ValidateVrSetupBeforePlay();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                foreach (EyeTrackingRecorder recorder in Object.FindObjectsByType<EyeTrackingRecorder>())
                    recorder.StopAndFlush("unity_editor_stop_button");
            }
            else if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += RecordingMonitorWindow.OpenIfRecorderPresent;
            }
        }

        private static void ValidateVrSetupBeforePlay()
        {
            bool hasVrStimulus = false;
            foreach (StimulusController stimulus in Object.FindObjectsByType<StimulusController>())
            {
                if (stimulus.presentationMode == StimulusPresentationMode.VirtualReality) { hasVrStimulus = true; break; }
            }
            if (!hasVrStimulus) return;

            if (Object.FindAnyObjectByType<SranipalSafeBootstrap>() || Object.FindAnyObjectByType<EyeTrackingRecorder>())
            {
                CancelPlay("This scene still contains the previous eye-tracking setup. Create a fresh scene with Tools > Dots Stimulus > Create New Stimulus > VR Stimulus. The new VR scene contains no eye tracking.");
            }
        }

        private static void CancelPlay(string message)
        {
            EditorApplication.isPlaying = false;
            Debug.LogError("VR PLAY BLOCKED: " + message);
            EditorApplication.delayCall += () => EditorUtility.DisplayDialog("VR setup is unsafe", message, "OK");
        }
    }
}
