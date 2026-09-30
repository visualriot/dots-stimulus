using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
#if VIVE_SRANIPAL
using ViveSR.anipal.Eye;
#endif

namespace DotsStimulus.EyeTracking
{
    /// <summary>
    /// Bounded, thread-safe SRanipal v1 callback bridge. Define VIVE_SRANIPAL only after importing that SDK.
    /// No Unity API is called from the native callback thread.
    /// </summary>
    public sealed class SranipalNativeEyeProvider : MonoBehaviour, INativeEyeTrackingProvider
    {
        [Tooltip("Maximum unwritten callback samples. A full queue drops new samples and reports the loss.")]
        [Min(128)] public int queueCapacity = 8192;

        public const string RequestedSdkVersion = SranipalEnvironmentValidator.RequestedSdkVersion;

        public string ProviderName => "SRanipal native callback (v1 API)";
        public bool IsCapturing => Volatile.Read(ref captureState) == 1;
        public long ReceivedCount => Interlocked.Read(ref receivedCount);
        public long DroppedCount => Interlocked.Read(ref droppedCount);

        public static string DetectNativeDllVersion()
        {
            string[] candidates =
            {
                Path.Combine(Application.dataPath, "ViveSR", "Plugins", "SRanipal.dll"),
                Path.Combine(Application.dataPath, "Plugins", "x86_64", "SRanipal.dll"),
                Path.Combine(Application.dataPath, "Plugins", "SRanipal.dll")
            };
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                try { return FileVersionInfo.GetVersionInfo(candidate).FileVersion ?? "unknown"; }
                catch { return "unreadable"; }
            }
            return "not found";
        }

#if VIVE_SRANIPAL
        public bool IsAvailable => SRanipal_Eye_Framework.Status == SRanipal_Eye_Framework.FrameworkStatus.WORKING;
        public string Status => !string.IsNullOrEmpty(lastError) ? lastError :
            (IsAvailable ? (IsCapturing ? "SRanipal native callback active" : "SRanipal ready") : "SRanipal framework is not WORKING");
#else
        public bool IsAvailable => false;
        public string Status => "SRanipal SDK absent. Import 1.3.2.0 and define VIVE_SRANIPAL.";
#endif

        private readonly ConcurrentQueue<NativeEyeSample> samples = new ConcurrentQueue<NativeEyeSample>();
        private long receivedCount;
        private long droppedCount;
        private int queuedCount;
        private int captureState;
#if VIVE_SRANIPAL
        private string lastError;
#endif

        public bool StartCapture()
        {
            if (IsCapturing) return true;
            while (samples.TryDequeue(out _)) { }
            Interlocked.Exchange(ref receivedCount, 0);
            Interlocked.Exchange(ref droppedCount, 0);
            Interlocked.Exchange(ref queuedCount, 0);
#if VIVE_SRANIPAL
            lastError = null;
            if (!IsAvailable || SRanipal_Eye_Framework.Instance == null) return false;
            SRanipal_Eye_Framework.Instance.EnableEyeDataCallback = true;
            active = this;
            callback = EyeCallback;
            callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            try
            {
                int result = SRanipal_Eye.WrapperRegisterEyeDataCallback(callbackPointer);
                if (result != (int)ViveSR.Error.WORK)
                {
                    lastError = "SRanipal callback registration returned error " + result;
                    ClearCallbackReferences();
                    return false;
                }
            }
            catch (Exception exception)
            {
                lastError = "SRanipal callback registration exception: " + exception.GetType().Name + ": " + exception.Message;
                ClearCallbackReferences();
                return false;
            }
            Volatile.Write(ref captureState, 1);
            return true;
#else
            return false;
#endif
        }

        public void StopCapture()
        {
#if VIVE_SRANIPAL
            bool wasCapturing = Interlocked.Exchange(ref captureState, 0) == 1;
            if (wasCapturing && callbackPointer != IntPtr.Zero)
            {
                try
                {
                    int result = SRanipal_Eye.WrapperUnRegisterEyeDataCallback(callbackPointer);
                    if (result != (int)ViveSR.Error.WORK)
                        lastError = "SRanipal callback unregister returned error " + result;
                }
                catch (Exception exception)
                {
                    lastError = "SRanipal callback unregister exception: " + exception.GetType().Name + ": " + exception.Message;
                }
            }
            ClearCallbackReferences();
#else
            Volatile.Write(ref captureState, 0);
#endif
        }

        public bool TryDequeue(out NativeEyeSample sample)
        {
            if (!samples.TryDequeue(out sample)) return false;
            Interlocked.Decrement(ref queuedCount);
            return true;
        }

        private void Enqueue(NativeEyeSample sample)
        {
            Interlocked.Increment(ref receivedCount);
            if (Interlocked.Increment(ref queuedCount) > Math.Max(128, queueCapacity))
            {
                Interlocked.Decrement(ref queuedCount);
                Interlocked.Increment(ref droppedCount);
                return;
            }
            samples.Enqueue(sample);
        }

        private void OnDisable() => StopCapture();
        private void OnDestroy() => StopCapture();
        private void OnApplicationQuit() => StopCapture();

#if VIVE_SRANIPAL
        private static SranipalNativeEyeProvider active;
        private static SRanipal_Eye.CallbackBasic callback;
        private static IntPtr callbackPointer;

        private void ClearCallbackReferences()
        {
            if (ReferenceEquals(active, this)) active = null;
            callback = null;
            callbackPointer = IntPtr.Zero;
        }

        private static void EyeCallback(ref EyeData eyeData)
        {
            SranipalNativeEyeProvider target = active;
            if (ReferenceEquals(target, null) || !target.IsCapturing) return;
            var left = eyeData.verbose_data.left;
            var right = eyeData.verbose_data.right;
            var combined = eyeData.verbose_data.combined;
            NativeEyeSample s = new NativeEyeSample
            {
                hostStopwatchTicks = Stopwatch.GetTimestamp(), hostUtcTicks = DateTime.UtcNow.Ticks,
                sranipalTimestamp = eyeData.timestamp,
                leftValidity = (ulong)left.eye_data_validata_bit_mask,
                rightValidity = (ulong)right.eye_data_validata_bit_mask,
                combinedValidity = (ulong)combined.eye_data.eye_data_validata_bit_mask,
                leftOriginX = left.gaze_origin_mm.x, leftOriginY = left.gaze_origin_mm.y, leftOriginZ = left.gaze_origin_mm.z,
                leftDirectionX = left.gaze_direction_normalized.x, leftDirectionY = left.gaze_direction_normalized.y, leftDirectionZ = left.gaze_direction_normalized.z,
                rightOriginX = right.gaze_origin_mm.x, rightOriginY = right.gaze_origin_mm.y, rightOriginZ = right.gaze_origin_mm.z,
                rightDirectionX = right.gaze_direction_normalized.x, rightDirectionY = right.gaze_direction_normalized.y, rightDirectionZ = right.gaze_direction_normalized.z,
                combinedOriginX = combined.eye_data.gaze_origin_mm.x, combinedOriginY = combined.eye_data.gaze_origin_mm.y, combinedOriginZ = combined.eye_data.gaze_origin_mm.z,
                combinedDirectionX = combined.eye_data.gaze_direction_normalized.x, combinedDirectionY = combined.eye_data.gaze_direction_normalized.y, combinedDirectionZ = combined.eye_data.gaze_direction_normalized.z,
                leftOpenness = left.eye_openness, rightOpenness = right.eye_openness, combinedOpenness = combined.eye_data.eye_openness,
                leftPupilMm = left.pupil_diameter_mm, rightPupilMm = right.pupil_diameter_mm, combinedPupilMm = combined.eye_data.pupil_diameter_mm,
                leftPupilX = left.pupil_position_in_sensor_area.x, leftPupilY = left.pupil_position_in_sensor_area.y,
                rightPupilX = right.pupil_position_in_sensor_area.x, rightPupilY = right.pupil_position_in_sensor_area.y,
                combinedPupilX = combined.eye_data.pupil_position_in_sensor_area.x, combinedPupilY = combined.eye_data.pupil_position_in_sensor_area.y,
                convergenceDistanceMm = combined.convergence_distance_mm,
                convergenceValid = combined.convergence_distance_validity
            };
            target.Enqueue(s);
        }
#endif
    }
}
