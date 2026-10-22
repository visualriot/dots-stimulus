using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace DotsStimulus
{
    /// <summary>Fixed orthographic 2D view or rotatable, mouse-wheel-dolly 3D substitute for a tracked head.</summary>
    [DefaultExecutionOrder(-10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class DesktopFlyCamera : MonoBehaviour
    {
        [Header("Desktop head simulation (3D only)")]
        [Tooltip("Stop the globally configured XR loader in desktop scenes and force this camera to render a normal monoscopic Game view.")]
        public bool disableXrForDesktop = true;
        [Tooltip("Degrees per mouse-delta unit while the right mouse button is held in 3D.")]
        [Min(.001f)] public float mouseSensitivity = .12f;
        [Tooltip("I/J/K/L pitch/yaw speed in degrees per second.")]
        [Min(.01f)] public float keyboardRotationSpeed = 60f;
        [Tooltip("Maximum upward or downward simulated-head rotation in 3D, in degrees. This prevents the desktop camera from flipping over.")]
        [Range(1, 89.9f)] public float maximumPitch = 89f;
        [Tooltip("Desktop 3D only. Metres moved along the current viewing direction for one mouse-wheel notch. Wheel up moves closer; wheel down moves farther away.")]
        [Min(.001f)] public float scrollZoomMetresPerNotch = 1f;
        [Tooltip("Desktop 3D only. Metres moved sideways or vertically for each pixel of middle-mouse drag.")]
        [Min(.0001f)] public float middleDragPanMetresPerPixel = .01f;

        [Header("MATLAB display geometry")]
        [Tooltip("Physical monitor width in metres (587 mm in the MATLAB source).")]
        [Min(.01f)] public float displayWidthMetres = .587f;
        [Tooltip("Eye-to-display distance in metres (715 mm in the MATLAB source).")]
        [Min(.01f)] public float viewingDistanceMetres = .715f;
        [Tooltip("MATLAB reference window width in pixels. The active source window is 800x600; this is used when viewport adaptation is off.")]
        [Min(1)] public int referenceWidthPixels = 800;
        [Tooltip("MATLAB reference window height in pixels. The active source window is 800x600; this is used when viewport adaptation is off.")]
        [Min(1)] public int referenceHeightPixels = 600;
        [Tooltip("Derive display height and vertical FOV from the active Game-view aspect, like MATLAB's Screenheight = Screenwidth / aspect. Disable for a fixed 800x600 reference projection.")]
        public bool adaptFieldOfViewToViewport = true;
        [Tooltip("Nearest distance rendered by the desktop camera, in metres. MATLAB z_near is 0.01 m.")]
        [Min(.001f)] public float nearClipMetres = .01f;
        [Tooltip("Farthest distance rendered by the desktop camera, in metres. MATLAB uses total_depth x 2 = 140 m with the default z=50-70 m volume.")]
        [Min(.1f)] public float farClipMetres = 140f;

        private float yaw;
        private float pitch;
        private bool looking;
        private bool panning;
        private bool cursorCaptured;
        private bool twoDimensional;
        private Vector3 homePosition;
        private Quaternion homeRotation;

        public bool IsTwoDimensional => twoDimensional;

        public float DerivedVerticalFieldOfView
        {
            get
            {
                float aspect = referenceWidthPixels / (float)Mathf.Max(1, referenceHeightPixels);
                return FieldOfViewForAspect(aspect);
            }
        }

        public float FieldOfViewForAspect(float aspect)
        {
            float displayHeight = DisplayHeightForAspect(aspect);
            return 2f * Mathf.Atan(displayHeight / (2f * viewingDistanceMetres)) * Mathf.Rad2Deg;
        }

        public float DisplayHeightForAspect(float aspect) => displayWidthMetres / Mathf.Max(.01f, aspect);

        public static DesktopFlyCamera Configure(Camera camera)
        {
            var trackedPose = camera.GetComponent<TrackedPoseDriver>();
            if (trackedPose) trackedPose.enabled = false;
            var controls = camera.GetComponent<DesktopFlyCamera>();
            if (!controls) controls = camera.gameObject.AddComponent<DesktopFlyCamera>();
            controls.ApplyCameraSettings();
            controls.CaptureHome();
            return controls;
        }

        public void ApplyCameraSettings()
        {
            var camera = GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.rect = new Rect(0, 0, 1, 1);
            ApplyProjection(camera);
            camera.nearClipPlane = nearClipMetres;
            camera.farClipPlane = farClipMetres;
            camera.allowHDR = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
        }

        private void Awake()
        {
            EnsureScrollZoomDefault();
            if (disableXrForDesktop) StopXrSubsystems();
            ApplyCameraSettings();
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

        public void SetTwoDimensional(bool enabled)
        {
            twoDimensional = enabled;
            ReleaseMouse();
            if (twoDimensional) ResetView();
            ApplyProjection(GetComponent<Camera>());
        }

        private void ReadAngles()
        {
            yaw = transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
        }

        private void Update()
        {
            if (twoDimensional)
            {
                ReleaseMouse();
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                ApplyScrollZoom(mouse.scroll.ReadValue().y);
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    ReadAngles();
                    looking = true;
                    CaptureMouse();
                }
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    panning = true;
                    CaptureMouse();
                }
                if (mouse.rightButton.wasReleasedThisFrame)
                {
                    looking = false;
                    ReleaseMouseIfIdle();
                }
                if (mouse.middleButton.wasReleasedThisFrame)
                {
                    panning = false;
                    ReleaseMouseIfIdle();
                }

                Vector2 delta = mouse.delta.ReadValue();
                if (looking)
                {
                    yaw += delta.x * mouseSensitivity;
                    pitch -= delta.y * mouseSensitivity;
                }
                if (panning) ApplyMiddleDragPan(delta);
            }

            if (keyboard != null)
            {
                float seconds = Time.unscaledDeltaTime;
                yaw += ((keyboard.lKey.isPressed ? 1f : 0f) - (keyboard.jKey.isPressed ? 1f : 0f)) * keyboardRotationSpeed * seconds;
                pitch += ((keyboard.iKey.isPressed ? 1f : 0f) - (keyboard.kKey.isPressed ? 1f : 0f)) * keyboardRotationSpeed * seconds;
            }

            pitch = Mathf.Clamp(pitch, -maximumPitch, maximumPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        public void ApplyScrollZoom(float scrollDeltaY)
        {
            if (twoDimensional || Mathf.Approximately(scrollDeltaY, 0)) return;

            // Input System backends are not consistent here: a physical wheel notch is
            // commonly reported as 120 on Windows, but some mice/drivers report 1.
            // Treat small values as already-normalised notches so scrolling never becomes
            // an imperceptible 1/120th of the configured distance.
            const float standardWindowsWheelDelta = 120f;
            float notches = Mathf.Abs(scrollDeltaY) >= 10f
                ? scrollDeltaY / standardWindowsWheelDelta
                : scrollDeltaY;
            transform.position += transform.forward * (notches * scrollZoomMetresPerNotch);
        }

        public void ApplyMiddleDragPan(Vector2 mouseDelta)
        {
            if (twoDimensional || mouseDelta.sqrMagnitude <= Mathf.Epsilon) return;
            transform.position += (transform.right * mouseDelta.x + transform.up * mouseDelta.y) * middleDragPanMetresPerPixel;
        }

        private void LateUpdate()
        {
            var camera = GetComponent<Camera>();
            float aspect = EffectiveAspect(camera);
            if (twoDimensional)
            {
                float requiredSize = DisplayHeightForAspect(aspect) * .5f;
                if (!Mathf.Approximately(camera.orthographicSize, requiredSize)) camera.orthographicSize = requiredSize;
            }
            else
            {
                float requiredFov = FieldOfViewForAspect(aspect);
                if (!Mathf.Approximately(camera.fieldOfView, requiredFov)) camera.fieldOfView = requiredFov;
            }
        }

        private void ApplyProjection(Camera camera)
        {
            float aspect = EffectiveAspect(camera);
            camera.orthographic = twoDimensional;
            camera.orthographicSize = DisplayHeightForAspect(aspect) * .5f;
            camera.fieldOfView = FieldOfViewForAspect(aspect);
        }

        private float EffectiveAspect(Camera camera) => adaptFieldOfViewToViewport
            ? camera.aspect
            : referenceWidthPixels / (float)Mathf.Max(1, referenceHeightPixels);

        private static void StopXrSubsystems()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || manager.activeLoader == null) return;
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }

        private void ReleaseMouse()
        {
            looking = false;
            panning = false;
            ReleaseCapturedMouse();
        }

        private void CaptureMouse()
        {
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ReleaseMouseIfIdle()
        {
            if (!looking && !panning) ReleaseCapturedMouse();
        }

        private void ReleaseCapturedMouse()
        {
            if (!cursorCaptured) return;
            cursorCaptured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnValidate()
        {
            EnsureScrollZoomDefault();
            if (TryGetComponent(out Camera camera)) ApplyCameraSettings();
        }

        private void EnsureScrollZoomDefault()
        {
            scrollZoomMetresPerNotch = scrollZoomMetresPerNotch <= 0 ? 1f : Mathf.Max(.001f, scrollZoomMetresPerNotch);
            middleDragPanMetresPerPixel = middleDragPanMetresPerPixel <= 0 ? .01f : Mathf.Max(.0001f, middleDragPanMetresPerPixel);
        }

        private void OnDisable() => ReleaseMouse();
        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseMouse(); }
    }
}
