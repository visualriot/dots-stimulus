using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace DotsStimulus.EyeTracking
{
    /// <summary>Portable, frame-rate combined gaze from the OpenXR Eye Gaze Interaction profile.</summary>
    public sealed class OpenXREyeTrackingProvider : MonoBehaviour, IEyeTrackingProvider
    {
        [Tooltip("Transform from OpenXR tracking space to world space, normally the XR Origin.")]
        public Transform trackingOrigin;

        public string ProviderName => "OpenXR frame gaze";
        public bool IsAvailable => FindDevice().isValid;
        public bool IsCapturing => isActiveAndEnabled;
        public string Status => IsAvailable ? "OpenXR eye-gaze device available" : "No OpenXR eye-gaze device";

        private readonly List<InputDevice> devices = new List<InputDevice>(2);
        private InputDevice device;

        public bool TryGetSample(out FrameGazeSample sample)
        {
            sample = default;
            InputDevice current = FindDevice();
            if (!current.isValid) return false;
            bool hasPosition = current.TryGetFeatureValue(EyeTrackingUsages.gazePosition, out Vector3 localOrigin);
            bool hasRotation = current.TryGetFeatureValue(EyeTrackingUsages.gazeRotation, out Quaternion localRotation);
            current.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
            current.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState);
            bool valid = hasPosition && hasRotation && tracked;
            Transform origin = trackingOrigin;
            sample.valid = valid;
            sample.originWorld = origin ? origin.TransformPoint(localOrigin) : localOrigin;
            Vector3 localDirection = localRotation * Vector3.forward;
            sample.directionWorld = origin ? origin.TransformDirection(localDirection).normalized : localDirection.normalized;
            sample.trackingState = trackingState;
            sample.source = "OpenXR";
            return true;
        }

        private InputDevice FindDevice()
        {
            if (device.isValid) return device;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, devices);
            if (devices.Count > 0) device = devices[0];
            return device;
        }
    }
}
