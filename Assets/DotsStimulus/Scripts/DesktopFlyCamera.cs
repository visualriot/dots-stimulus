using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace DotsStimulus
{
    /// <summary>Desktop head-look and free movement. Does not move or rotate the swarm.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DesktopFlyCamera : MonoBehaviour
    {
        [Min(.01f)] public float lookSensitivity = .12f;
        [Min(.01f)] public float movementSpeed = 2;
        [Min(1)] public float fastMultiplier = 3;
        private float yaw, pitch;
        private bool looking;
        private Vector3 homePosition;
        private Quaternion homeRotation;

        public static DesktopFlyCamera Configure(Camera camera)
        {
            // An old generated scene may still contain the VR pose driver.
            var trackedPose = camera.GetComponent<TrackedPoseDriver>();
            if (trackedPose) trackedPose.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.fieldOfView = 75;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 140;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            var controls = camera.GetComponent<DesktopFlyCamera>();
            if (!controls) controls = camera.gameObject.AddComponent<DesktopFlyCamera>();
            controls.enabled = true;
            controls.CaptureHome();
            return controls;
        }

        private void OnEnable() => CaptureHome();

        public void CaptureHome()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            ReadAngles();
        }

        public void ResetView()
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
            ReadAngles();
        }

        private void ReadAngles()
        {
            yaw = transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseMouse();
                return;
            }
            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    ReadAngles();
                    looking = true;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                if (mouse.rightButton.wasReleasedThisFrame) ReleaseMouse();
                if (looking)
                {
                    // Mouse delta is already displacement this frame; do not multiply by dt.
                    var delta = mouse.delta.ReadValue();
                    yaw += delta.x * lookSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89, 89);
                    transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                }
            }
            if (keyboard == null) return;
            float right = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float forward = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            float up = (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
            var movement = transform.right * right + transform.forward * forward + Vector3.up * up;
            movement = Vector3.ClampMagnitude(movement, 1);
            float speed = movementSpeed * (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? fastMultiplier : 1);
            transform.position += movement * (speed * Time.unscaledDeltaTime);
        }

        private void ReleaseMouse()
        {
            if (!looking) return;
            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable() => ReleaseMouse();
        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseMouse(); }
    }
}
