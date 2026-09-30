using System.IO;
using DotsStimulus.EyeTracking;
using UnityEditor;
using UnityEngine;

namespace DotsStimulus.Editor
{
    public sealed class RecordingMonitorWindow : EditorWindow
    {
        [MenuItem("Window/Dots Stimulus Recording Monitor")]
        public static void Open() => GetWindow<RecordingMonitorWindow>("Eye Recording");

        public static void OpenIfRecorderPresent()
        {
            if (Object.FindAnyObjectByType<EyeTrackingRecorder>()) Open();
        }

        private void OnEnable()
        {
            EyeTrackingRecorder.MonitorStatusChanged += Repaint;
            EditorApplication.update += Repaint;
            minSize = new Vector2(430, 300);
        }

        private void OnDisable()
        {
            EyeTrackingRecorder.MonitorStatusChanged -= Repaint;
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            RecordingStatusSnapshot s = EyeTrackingRecorder.MonitorStatus;
            Color color = s.state switch
            {
                RecordingState.Recording => new Color(.85f, .08f, .08f),
                RecordingState.Saved => new Color(.1f, .6f, .2f),
                RecordingState.Error => new Color(.9f, .35f, .05f),
                _ => new Color(.45f, .45f, .45f)
            };
            GUIStyle stateStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            Color old = GUI.color;
            GUI.color = color;
            GUILayout.Space(8);
            GUILayout.Label(s.state.ToString().ToUpperInvariant(), stateStyle, GUILayout.Height(45));
            if (s.state == RecordingState.Recording)
                GUILayout.Label("●  DATA IS BEING RECORDED", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
            GUI.color = old;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Participant", s.participantId);
            EditorGUILayout.LabelField("Session", s.sessionId);
            EditorGUILayout.LabelField("Elapsed", s.elapsedSeconds.ToString("F1") + " s");
            EditorGUILayout.LabelField("Native samples", s.nativeSamples.ToString());
            EditorGUILayout.LabelField("Measured native rate", s.measuredNativeHz > 0 ? s.measuredNativeHz.ToString("F2") + " Hz" : "—");
            EditorGUILayout.LabelField("Frame samples", s.frameSamples.ToString());
            EditorGUILayout.LabelField("Queue drops", s.queueDrops.ToString());
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(s.message, s.state == RecordingState.Error ? MessageType.Error :
                (s.participantId == "UnknownParticipant" || s.message.Contains("unavailable") ? MessageType.Warning : MessageType.Info));
            EditorGUILayout.LabelField("Output", s.outputPath, EditorStyles.wordWrappedLabel);
            foreach (string file in s.savedFiles)
                EditorGUILayout.LabelField("Saved", Path.GetFileName(file));
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("Eye data is sensitive. Obtain explicit participant consent before pressing F5.", MessageType.Warning);
        }
    }
}
