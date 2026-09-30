using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DotsStimulus.EyeTracking
{
    [DefaultExecutionOrder(1000)]
    public sealed class EyeTrackingRecorder : MonoBehaviour
    {
        public const string MatlabSourceSha256 = "08352712BD443C0638BAEDDEF2F5BF1032FEACBD2FE663F313255E96B546EAF2";

        [Header("Identity")]
        [Tooltip("Folder name. Blank IDs are saved as UnknownParticipant and visibly warned about.")]
        public string participantId = "";
        public string sessionId = "dots";

        [Header("Sources")]
        public StimulusController stimulus;
        public Transform head;
        public SranipalNativeEyeProvider nativeProvider;
        public OpenXREyeTrackingProvider openXrProvider;
        public SranipalSafeBootstrap sranipalBootstrap;
        [Tooltip("Refuse F5 unless the true SRanipal callback is available. Disable only for portable OpenXR test recordings.")]
        public bool requireNative120Hz = true;

        [NonSerialized] public INativeEyeTrackingProvider nativeProviderOverride;

        [Header("Streaming")]
        [Tooltip("Seconds between writer flushes while recording.")]
        [Min(.05f)] public float flushIntervalSeconds = 1f;

        public bool IsRecording => state == RecordingState.Recording;
        public RecordingState State => state;
        public static RecordingStatusSnapshot MonitorStatus { get; private set; } = new RecordingStatusSnapshot();
        public static event Action MonitorStatusChanged;

        private RecordingState state = RecordingState.Idle;
        private StreamWriter nativeWriter, frameWriter, eventWriter;
        private FileStream metadataReservation;
        private string metadataPath, nativePath, framesPath, eventsPath, outputDirectory, prefix;
        private DateTime sessionStartUtc, sessionEndUtc;
        private long sessionStartStopwatch, nativeSamples, frameSamples, firstNativeTick, lastNativeTick;
        private double nextFlushTime;
        private bool stopInProgress;
        private readonly object stopLock = new object();

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private INativeEyeTrackingProvider NativeProvider => nativeProviderOverride ?? nativeProvider;

        private void Awake()
        {
            if (!stimulus) stimulus = FindAnyObjectByType<StimulusController>();
            if (!head && Camera.main) head = Camera.main.transform;
            if (!nativeProvider) nativeProvider = FindAnyObjectByType<SranipalNativeEyeProvider>();
            if (!openXrProvider) openXrProvider = FindAnyObjectByType<OpenXREyeTrackingProvider>();
            string message = sranipalBootstrap && !sranipalBootstrap.NativeStartupAllowed
                ? "Eye logging unavailable: " + sranipalBootstrap.Status
                : "Press F5 to begin recording only after participant consent.";
            Publish(RecordingState.Idle, message);
        }

        private void OnEnable()
        {
            if (stimulus) stimulus.ControlEvent += RecordEvent;
        }

        private void OnDisable()
        {
            StopAndFlush("component_disabled");
            if (stimulus) stimulus.ControlEvent -= RecordEvent;
        }

        private void OnDestroy() => StopAndFlush("component_destroyed");
        private void OnApplicationQuit() => StopAndFlush("application_quit");

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                if (IsRecording) StopRecording();
                else StartRecording();
            }
            if (!IsRecording) return;
            DrainNativeQueue();
            WriteFrame();
            if (Time.realtimeSinceStartupAsDouble >= nextFlushTime)
            {
                FlushWriters();
                nextFlushTime = Time.realtimeSinceStartupAsDouble + flushIntervalSeconds;
            }
            PublishProgress();
        }

        /// <summary>Starts a new, collision-safe recording session. Safe for a future trial manager to call.</summary>
        public void StartRecording()
        {
            if (IsRecording) return;
            if (NativeProvider == null && requireNative120Hz)
            {
                Fail("SRanipal native provider component is missing; recording did not start.");
                return;
            }
            if (requireNative120Hz && !NativeProvider.IsAvailable)
            {
                Fail("True 120 Hz SRanipal data is unavailable; recording did not start. " + NativeProvider.Status);
                return;
            }
            if (NativeProvider != null && NativeProvider.IsAvailable && !NativeProvider.StartCapture())
            {
                Fail("SRanipal callback registration failed; recording did not start. " + NativeProvider.Status);
                return;
            }

            try
            {
                sessionStartUtc = DateTime.UtcNow;
                sessionStartStopwatch = Stopwatch.GetTimestamp();
                string participant = Sanitize(participantId, "UnknownParticipant");
                string session = Sanitize(sessionId, "dots");
                outputDirectory = Path.Combine(Application.dataPath, "_DATA", participant);
                Directory.CreateDirectory(outputDirectory);
                string timestamp = sessionStartUtc.ToString("yyyyMMdd'T'HHmmss.fff'Z'", Invariant);
                prefix = ReserveUniquePrefix(timestamp, session);
                OpenWriters();
                nativeSamples = frameSamples = firstNativeTick = lastNativeTick = 0;
                nextFlushTime = Time.realtimeSinceStartupAsDouble + flushIntervalSeconds;
                state = RecordingState.Recording;
                WriteEvent("recording_started", string.IsNullOrWhiteSpace(participantId) ? "participant_id_blank" : "f5_or_api");
                PublishProgress();
                UnityEngine.Debug.Log("Eye recording STARTED: " + prefix + " -> " + outputDirectory, this);
            }
            catch (Exception exception)
            {
                NativeProvider?.StopCapture();
                CloseWriters();
                Fail("Could not start recording: " + exception.Message);
            }
        }

        /// <summary>Stops, drains, flushes and closes the current session. Idempotent.</summary>
        public void StopRecording() => StopAndFlush("f5_or_api");

        public void StopAndFlush() => StopAndFlush("api");

        public void StopAndFlush(string reason)
        {
            lock (stopLock)
            {
                if (!IsRecording || stopInProgress) return;
                stopInProgress = true;
            }
            try
            {
                NativeProvider?.StopCapture();
                DrainNativeQueue();
                WriteEvent("recording_stopped", reason);
                sessionEndUtc = DateTime.UtcNow;
                FlushWriters();
                WriteMetadata(reason);
                CloseWriters();
                state = RecordingState.Saved;
                string[] files = { metadataPath, nativePath, framesPath, eventsPath };
                Publish(RecordingState.Saved, "Saved and closed: " + string.Join(", ", files.Select(Path.GetFileName)), files);
                UnityEngine.Debug.Log("Eye recording SAVED: " + string.Join(", ", files), this);
            }
            catch (Exception exception)
            {
                CloseWriters();
                Fail("Recording finalization error: " + exception.Message);
            }
            finally
            {
                stopInProgress = false;
            }
        }

        public void RecordEvent(string eventName, string detail)
        {
            if (IsRecording) WriteEvent(eventName, detail);
        }

        private string ReserveUniquePrefix(string timestamp, string session)
        {
            string candidate = ChooseUniquePrefix(outputDirectory, timestamp, session);
            metadataPath = Path.Combine(outputDirectory, candidate + "_metadata.json");
            nativePath = Path.Combine(outputDirectory, candidate + "_eye_native.csv");
            framesPath = Path.Combine(outputDirectory, candidate + "_frames.csv");
            eventsPath = Path.Combine(outputDirectory, candidate + "_events.csv");
            metadataReservation = new FileStream(metadataPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            return candidate;
        }

        public static string ChooseUniquePrefix(string directory, string timestamp, string session)
        {
            for (int collision = 0; collision < 10000; collision++)
            {
                string candidate = timestamp + "_" + session + (collision == 0 ? "" : "_" + collision);
                string[] suffixes = { "_metadata.json", "_eye_native.csv", "_frames.csv", "_events.csv" };
                if (!suffixes.Any(suffix => File.Exists(Path.Combine(directory, candidate + suffix)))) return candidate;
            }
            throw new IOException("Could not allocate a unique timestamped filename after 10,000 attempts.");
        }

        private void OpenWriters()
        {
            nativeWriter = NewWriter(nativePath);
            frameWriter = NewWriter(framesPath);
            eventWriter = NewWriter(eventsPath);
            nativeWriter.WriteLine("host_stopwatch_ticks,host_utc_ticks,sranipal_timestamp,left_validity,right_validity,combined_validity,left_origin_mm_x,left_origin_mm_y,left_origin_mm_z,left_direction_x,left_direction_y,left_direction_z,right_origin_mm_x,right_origin_mm_y,right_origin_mm_z,right_direction_x,right_direction_y,right_direction_z,combined_origin_mm_x,combined_origin_mm_y,combined_origin_mm_z,combined_direction_x,combined_direction_y,combined_direction_z,left_openness,right_openness,combined_openness,left_pupil_mm,right_pupil_mm,combined_pupil_mm,left_pupil_x,left_pupil_y,right_pupil_x,right_pupil_y,combined_pupil_x,combined_pupil_y,convergence_distance_mm,convergence_valid");
            frameWriter.WriteLine("host_stopwatch_ticks,utc_iso8601,unity_realtime_seconds,unity_frame,head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw,openxr_gaze_valid,gaze_origin_x,gaze_origin_y,gaze_origin_z,gaze_direction_x,gaze_direction_y,gaze_direction_z,gaze_tracking_state,gaze_source,simulation_seed,simulation_step,simulation_delta_seconds,paused,dimension,rule,metric_radius_m,topological_k,topological_radius_cap_m,speed_mps,noise,width_m,height_m,depth_m,start_depth_m,wrap_x,wrap_y,wrap_z,aperture_enabled");
            eventWriter.WriteLine("host_stopwatch_ticks,utc_iso8601,unity_realtime_seconds,unity_frame,event,detail");
        }

        private static StreamWriter NewWriter(string path) => new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Utf8NoBom, 65536);

        private void DrainNativeQueue()
        {
            if (NativeProvider == null || nativeWriter == null) return;
            while (NativeProvider.TryDequeue(out NativeEyeSample s))
            {
                if (firstNativeTick == 0) firstNativeTick = s.hostStopwatchTicks;
                lastNativeTick = s.hostStopwatchTicks;
                nativeSamples++;
                nativeWriter.WriteLine(string.Join(",", new[]
                {
                    I(s.hostStopwatchTicks), I(s.hostUtcTicks), I(s.sranipalTimestamp), I(s.leftValidity), I(s.rightValidity), I(s.combinedValidity),
                    F(s.leftOriginX), F(s.leftOriginY), F(s.leftOriginZ), F(s.leftDirectionX), F(s.leftDirectionY), F(s.leftDirectionZ),
                    F(s.rightOriginX), F(s.rightOriginY), F(s.rightOriginZ), F(s.rightDirectionX), F(s.rightDirectionY), F(s.rightDirectionZ),
                    F(s.combinedOriginX), F(s.combinedOriginY), F(s.combinedOriginZ), F(s.combinedDirectionX), F(s.combinedDirectionY), F(s.combinedDirectionZ),
                    F(s.leftOpenness), F(s.rightOpenness), F(s.combinedOpenness), F(s.leftPupilMm), F(s.rightPupilMm), F(s.combinedPupilMm),
                    F(s.leftPupilX), F(s.leftPupilY), F(s.rightPupilX), F(s.rightPupilY), F(s.combinedPupilX), F(s.combinedPupilY),
                    F(s.convergenceDistanceMm), s.convergenceValid ? "1" : "0"
                }));
            }
        }

        private void WriteFrame()
        {
            if (frameWriter == null) return;
            FrameGazeSample gaze = default;
            bool hasGaze = openXrProvider != null && openXrProvider.TryGetSample(out gaze);
            Vector3 hp = head ? head.position : Vector3.zero;
            Quaternion hq = head ? head.rotation : Quaternion.identity;
            SwarmSettings s = stimulus != null && stimulus.Simulation != null ? stimulus.Simulation.Settings : stimulus?.settings;
            long step = stimulus?.Simulation?.StepCount ?? -1;
            frameSamples++;
            frameWriter.WriteLine(string.Join(",", new[]
            {
                I(Stopwatch.GetTimestamp()), Csv(DateTime.UtcNow.ToString("O", Invariant)), F(Time.realtimeSinceStartupAsDouble), I(Time.frameCount),
                F(hp.x), F(hp.y), F(hp.z), F(hq.x), F(hq.y), F(hq.z), F(hq.w),
                hasGaze && gaze.valid ? "1" : "0", F(gaze.originWorld.x), F(gaze.originWorld.y), F(gaze.originWorld.z),
                F(gaze.directionWorld.x), F(gaze.directionWorld.y), F(gaze.directionWorld.z), I((int)gaze.trackingState), Csv(gaze.source ?? "none"),
                I(stimulus ? stimulus.ActualSeed : 0), I(step), F(stimulus ? stimulus.LastStepDeltaSeconds : 0), stimulus && stimulus.paused ? "1" : "0",
                Csv(s != null && s.dimension2D ? "2D" : "3D"), Csv(s?.rule.ToString() ?? "unknown"), F(s?.metricRadius ?? 0), I(s?.topologicalK ?? 0),
                F(s?.topologicalRadiusCap ?? 0), F(s?.speed ?? 0), F(s?.noise ?? 0), F(s?.width ?? 0), F(s?.height ?? 0), F(s?.depth ?? 0), F(s?.startDepth ?? 0),
                B(s != null && s.wrapX), B(s != null && s.wrapY), B(s != null && s.wrapZ), B(stimulus && stimulus.Display && stimulus.Display.useAperture)
            }));
        }

        private void WriteEvent(string eventName, string detail)
        {
            eventWriter?.WriteLine(string.Join(",", I(Stopwatch.GetTimestamp()), Csv(DateTime.UtcNow.ToString("O", Invariant)),
                F(Time.realtimeSinceStartupAsDouble), I(Time.frameCount), Csv(eventName), Csv(detail)));
        }

        private void WriteMetadata(string stopReason)
        {
            if (metadataReservation == null) return;
            DotFieldRenderer renderer = stimulus ? stimulus.Display : null;
            if (!renderer && stimulus) renderer = stimulus.GetComponent<DotFieldRenderer>();
            DesktopFlyCamera desktop = head ? head.GetComponent<DesktopFlyCamera>() : null;
            SranipalCalibrationController calibration = GetComponent<SranipalCalibrationController>();
            SranipalEnvironmentReport environment = SranipalEnvironmentValidator.Evaluate();
            Metadata metadata = new Metadata
            {
                schemaVersion = 1,
                participantId = Sanitize(participantId, "UnknownParticipant"),
                participantIdWasBlank = string.IsNullOrWhiteSpace(participantId),
                sessionId = Sanitize(sessionId, "dots"),
                sessionPrefix = prefix,
                startedUtc = sessionStartUtc.ToString("O", Invariant),
                endedUtc = sessionEndUtc.ToString("O", Invariant),
                stopReason = stopReason,
                unityVersion = Application.unityVersion,
                applicationVersion = Application.version,
                matlabSourceSha256 = MatlabSourceSha256,
                requestedSranipalVersion = SranipalNativeEyeProvider.RequestedSdkVersion,
                importedSranipalNativeDllVersion = SranipalNativeEyeProvider.DetectNativeDllVersion(),
                installedSranipalRuntimeVersion = environment.installedRuntimeVersion,
                activeOpenXrRuntime = environment.activeOpenXrRuntime,
                nativeProvider = NativeProvider?.Status ?? "missing",
                openXrProvider = openXrProvider?.Status ?? "missing",
                nativeSamples = nativeSamples,
                frameSamples = frameSamples,
                queueDrops = NativeProvider?.DroppedCount ?? 0,
                measuredNativeHz = MeasuredNativeHz(),
                actualSeed = stimulus ? stimulus.ActualSeed : 0,
                settings = stimulus != null && stimulus.Simulation != null ? stimulus.Simulation.Settings.Copy() : stimulus?.settings?.Copy(),
                presentationMode = stimulus ? stimulus.presentationMode.ToString() : "unknown",
                apertureEnabled = renderer && renderer.useAperture,
                dotDiameterPixels = renderer ? renderer.diameterPixels : 0,
                useAngularDotSize = renderer && renderer.useAngularSize,
                dotDiameterDegrees = renderer ? renderer.diameterDegrees : 0,
                mirrorMatlabX = renderer && renderer.mirrorMatlabX,
                frameDistanceMetres = renderer ? renderer.frameDistance : 0,
                frameOuterWidthMetres = renderer ? renderer.frameOuterWidth : 0,
                frameOuterHeightMetres = renderer ? renderer.frameOuterHeight : 0,
                cameraVerticalFovDegrees = head && head.TryGetComponent(out Camera camera) ? camera.fieldOfView : 0,
                nearClipMetres = head && head.TryGetComponent(out camera) ? camera.nearClipPlane : 0,
                farClipMetres = head && head.TryGetComponent(out camera) ? camera.farClipPlane : 0,
                desktopDisplayWidthMetres = desktop ? desktop.displayWidthMetres : 0,
                desktopViewingDistanceMetres = desktop ? desktop.viewingDistanceMetres : 0,
                desktopReferenceWidthPixels = desktop ? desktop.referenceWidthPixels : 0,
                desktopReferenceHeightPixels = desktop ? desktop.referenceHeightPixels : 0,
                calibrateOnStart = calibration && calibration.calibrateOnStart,
                requireNative120Hz = requireNative120Hz,
                nativeQueueCapacity = nativeProvider ? nativeProvider.queueCapacity : 0,
                flushIntervalSeconds = flushIntervalSeconds,
                consentReminder = "Explicit participant consent is required before F5 because eye data is sensitive."
            };
            string json = JsonUtility.ToJson(metadata, true);
            using (var writer = new StreamWriter(metadataReservation, Utf8NoBom, 8192, true)) writer.Write(json);
            metadataReservation.Flush(true);
            metadataReservation.Dispose();
            metadataReservation = null;
        }

        private void FlushWriters()
        {
            nativeWriter?.Flush(); frameWriter?.Flush(); eventWriter?.Flush(); metadataReservation?.Flush();
        }

        private void CloseWriters()
        {
            nativeWriter?.Dispose(); frameWriter?.Dispose(); eventWriter?.Dispose(); metadataReservation?.Dispose();
            nativeWriter = null; frameWriter = null; eventWriter = null; metadataReservation = null;
        }

        private void PublishProgress()
        {
            double elapsed = (Stopwatch.GetTimestamp() - sessionStartStopwatch) / (double)Stopwatch.Frequency;
            MonitorStatus = new RecordingStatusSnapshot
            {
                state = RecordingState.Recording,
                participantId = Sanitize(participantId, "UnknownParticipant"), sessionId = Sanitize(sessionId, "dots"),
                message = string.IsNullOrWhiteSpace(participantId) ? "RECORDING — WARNING: blank ID saved as UnknownParticipant" : "RECORDING",
                outputPath = outputDirectory, elapsedSeconds = elapsed, nativeSamples = nativeSamples, frameSamples = frameSamples,
                queueDrops = NativeProvider?.DroppedCount ?? 0, measuredNativeHz = MeasuredNativeHz()
            };
            MonitorStatusChanged?.Invoke();
        }

        private void Publish(RecordingState newState, string message, string[] files = null)
        {
            state = newState;
            MonitorStatus = new RecordingStatusSnapshot
            {
                state = newState, participantId = Sanitize(participantId, "UnknownParticipant"), sessionId = Sanitize(sessionId, "dots"),
                message = message, outputPath = outputDirectory ?? "", savedFiles = files ?? new string[0],
                nativeSamples = nativeSamples, frameSamples = frameSamples, queueDrops = NativeProvider?.DroppedCount ?? 0,
                measuredNativeHz = MeasuredNativeHz(), elapsedSeconds = sessionStartStopwatch == 0 ? 0 : (Stopwatch.GetTimestamp() - sessionStartStopwatch) / (double)Stopwatch.Frequency
            };
            MonitorStatusChanged?.Invoke();
        }

        private void Fail(string message)
        {
            Publish(RecordingState.Error, message);
            UnityEngine.Debug.LogError(message, this);
        }

        private double MeasuredNativeHz()
        {
            if (nativeSamples < 2 || lastNativeTick <= firstNativeTick) return 0;
            return (nativeSamples - 1) * Stopwatch.Frequency / (double)(lastNativeTick - firstNativeTick);
        }

        public static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = new string(value.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim('.', ' ');
            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string F(double value) => value.ToString("R", Invariant);
        private static string I(long value) => value.ToString(Invariant);
        private static string I(ulong value) => value.ToString(Invariant);
        private static string B(bool value) => value ? "1" : "0";

        [Serializable]
        private sealed class Metadata
        {
            public int schemaVersion;
            public string participantId, sessionId, sessionPrefix, startedUtc, endedUtc, stopReason;
            public bool participantIdWasBlank;
            public string unityVersion, applicationVersion, matlabSourceSha256;
            public string requestedSranipalVersion, importedSranipalNativeDllVersion, installedSranipalRuntimeVersion, activeOpenXrRuntime;
            public string nativeProvider, openXrProvider, presentationMode, consentReminder;
            public long nativeSamples, frameSamples, queueDrops;
            public double measuredNativeHz;
            public int actualSeed;
            public SwarmSettings settings;
            public bool apertureEnabled, useAngularDotSize, mirrorMatlabX, calibrateOnStart, requireNative120Hz;
            public float dotDiameterPixels, dotDiameterDegrees, frameDistanceMetres, frameOuterWidthMetres, frameOuterHeightMetres;
            public float cameraVerticalFovDegrees, nearClipMetres, farClipMetres;
            public float desktopDisplayWidthMetres, desktopViewingDistanceMetres, flushIntervalSeconds;
            public int desktopReferenceWidthPixels, desktopReferenceHeightPixels, nativeQueueCapacity;
        }
    }
}
