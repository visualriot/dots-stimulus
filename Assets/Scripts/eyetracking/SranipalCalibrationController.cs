using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
#if VIVE_SRANIPAL
using ViveSR.anipal.Eye;
#endif

namespace DotsStimulus.EyeTracking
{
    public sealed class SranipalCalibrationController : MonoBehaviour
    {
        [Tooltip("Launch SRanipal calibration once the framework is ready. Disable for environment-only tests.")]
        public bool calibrateOnStart = true;
        [Tooltip("Maximum seconds to wait for SRanipal before reporting calibration unavailable.")]
        [Min(0)] public float frameworkWaitSeconds = 15f;
        public EyeTrackingRecorder recorder;
        public SranipalSafeBootstrap bootstrap;

        private void Start()
        {
            if (calibrateOnStart) StartCoroutine(CalibrateWhenReady("startup"));
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                StartCoroutine(CalibrateWhenReady("manual_f3"));
        }

        public void Calibrate() => StartCoroutine(CalibrateWhenReady("api"));

        private IEnumerator CalibrateWhenReady(string reason)
        {
            if (bootstrap && !bootstrap.NativeStartupAllowed)
            {
                Debug.LogWarning("Calibration unavailable because SRanipal native startup was blocked: " + bootstrap.Status, this);
                recorder?.RecordEvent("calibration_unavailable", reason + ":preflight_blocked");
                yield break;
            }
            double deadline = Time.realtimeSinceStartupAsDouble + frameworkWaitSeconds;
#if VIVE_SRANIPAL
            while (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING &&
                   Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            if (SRanipal_Eye_Framework.Status == SRanipal_Eye_Framework.FrameworkStatus.WORKING)
            {
                recorder?.RecordEvent("calibration_started", reason);
                try
                {
                    bool launched = SRanipal_Eye.LaunchEyeCalibration();
                    recorder?.RecordEvent("calibration_finished", launched ? "launched" : "launch_failed");
                    if (!launched) Debug.LogWarning("SRanipal rejected the calibration launch request.", this);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError("SRanipal calibration launch failed: " + exception.Message, this);
                    recorder?.RecordEvent("calibration_error", exception.GetType().Name);
                }
            }
            else
            {
                Debug.LogWarning("SRanipal did not become ready; calibration was not launched.", this);
                recorder?.RecordEvent("calibration_unavailable", reason);
            }
#else
            yield return null;
            Debug.LogWarning("Calibration unavailable: import SRanipal 1.3.2.0 and define VIVE_SRANIPAL.", this);
            recorder?.RecordEvent("calibration_unavailable", reason + ":sdk_absent");
#endif
        }
    }
}
