using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace DotsStimulus
{
    public enum ViewerPlacement { Centre, Outside }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DotFieldRenderer : MonoBehaviour
    {
        [Header("Presentation mode")]
        [Tooltip("Assigned by Stimulus Controller. This changes only how positions are displayed: Desktop uses a full-viewport 2D view or simulated-head 3D; VR uses immersive 3D or a virtual 2D display.")]
        public StimulusPresentationMode presentationMode = StimulusPresentationMode.Desktop;

        [Header("Dots")]
        [Tooltip("Shader that draws the white circular dots. Assigned automatically; researchers normally should not change it.")]
        public Shader dotShader;
        [Tooltip("Shader that draws the dark-grey aperture frame. Assigned automatically; researchers normally should not change it.")]
        public Shader frameShader;
        [Tooltip("Dot diameter in render pixels. MATLAB uses glPointSize(6), so the source default is 6 pixels.")]
        [Min(1)] public float diameterPixels = 6;
        [Tooltip("Off: use Diameter Pixels exactly, matching MATLAB. On: keep approximately the same angular diameter when resolution or headset rendering scale changes.")]
        public bool useAngularSize;
        [Tooltip("Dot angular diameter in degrees when Use Angular Size is on. 0.3528 degrees corresponds to 6 pixels on the MATLAB 800x600, 0.587 m-wide display viewed from 0.715 m.")]
        [Min(.001f)] public float diameterDegrees = .3528f;
        [Tooltip("Mirror only the displayed X coordinate to reproduce MATLAB/OpenGL's camera orientation. Simulation positions and neighbour distances are never mirrored.")]
        public bool mirrorMatlabX = true;

        [Header("Desktop presentation")]
        [Tooltip("Desktop only. Scale the 2D plane onto the complete Game-view viewport, like a 2D application. This affects display only; the simulation remains a 20x20 m plane by default.")]
        public bool fillDesktop2DViewport = true;
        [HideInInspector] public bool centreDesktop3DOnViewer = true;

        [Header("3D viewer placement")]
        [Tooltip("3D only. Centre places the viewer at the volume centre. Outside places the volume straight ahead of the viewer.")]
        public ViewerPlacement viewerPlacement = ViewerPlacement.Centre;
        [Tooltip("3D Outside mode only. Empty distance in metres from the viewer to the nearest box face or sphere surface.")]
        [Min(0f)] public float outsideClearance = 10f;
        [SerializeField, HideInInspector] private int threeDPlacementSerializationVersion = 1;

        [Header("MATLAB desktop aperture")]
        [Tooltip("MATLAB use_aperture: show or hide the dark-grey window frame that masks the box edges. A toggles it at runtime. Desktop full-viewport 2D starts with it off.")]
        public bool useAperture = true;
        [Tooltip("MATLAB z_frame: distance from the eye/camera to the aperture plane, in metres. The source places it at the physical monitor distance, 0.715 m.")]
        [Min(.01f)] public float frameDistance = .715f;
        [Tooltip("Outer half-width covered by the aperture bars. MATLAB uses Screenwidth_m (0.587 m) as an intentionally oversized buffer so the frame reaches beyond the monitor edges.")]
        [FormerlySerializedAs("frameOuterExtent")]
        [Min(.01f)] public float frameOuterWidth = .587f;
        [Tooltip("Outer half-height covered by the aperture bars. MATLAB uses Screenheight_m = Screenwidth_m / aspect, giving 0.44025 m at 800x600, as an oversized buffer.")]
        [Min(.01f)] public float frameOuterHeight = .44025f;

        [Header("VR immersive 3D")]
        [HideInInspector] public bool centreVrVolumeOnViewer = true;
        [Tooltip("Show the MATLAB monitor-style aperture while dots surround the viewer. Normally off because it blocks immersion; it does not change the swarm simulation.")]
        public bool useApertureInImmersive3D;

        [Header("VR 2D display (metres)")]
        [Tooltip("Width of the virtual screen shown after pressing D in VR. MATLAB Screenwidth_m default: 0.587 m (58.7 cm).")]
        [Min(.05f)] public float vr2DDisplayWidth = .587f;
        [Tooltip("Height of the virtual VR screen. MATLAB derives Screenheight_m from width/aspect; at 800x600 the default is 0.44025 m (44.025 cm).")]
        [Min(.05f)] public float vr2DDisplayHeight = .44025f;
        [Tooltip("Eye-to-screen distance for VR 2D, in metres. MATLAB distance_to_screen_m default: 0.715 m (71.5 cm).")]
        [Min(.05f)] public float vr2DDisplayDistance = .715f;
        [Tooltip("Width of the dark-grey border outside the VR 2D display, in metres. This is a VR presentation setting, not a neighbour or simulation boundary.")]
        [Min(0f)] public float vr2DFrameBorder = .04f;

        private Mesh dots, frame;
        private Material dotsMaterial, frameMaterial;
        private MeshRenderer dotsRenderer;
        private GameObject frameObject;
        private Vector3[] vertices;
        private SwarmSettings layout;
        private bool lastDimension2D;
        private Camera presentationCamera;

        public void SetPresentationCamera(Camera camera) => presentationCamera = camera;

        private void Awake() => MigrateThreeDPlacement();

        public void Initialize(SwarmSimulation simulation)
        {
            layout = simulation.Settings;
            lastDimension2D = layout.dimension2D;
            if (!dotShader || !frameShader) throw new System.InvalidOperationException("Assign both shaders, or create a scene from Tools > Dots Stimulus.");
            dots = new Mesh { name = "Dot centres and billboard corners" };
            dots.MarkDynamic();
            vertices = new Vector3[layout.count * 4];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[layout.count * 6];
            for (int i = 0; i < layout.count; i++)
            {
                int q = i * 4, t = i * 6;
                uv[q] = new Vector2(-1, -1); uv[q + 1] = new Vector2(-1, 1);
                uv[q + 2] = new Vector2(1, 1); uv[q + 3] = new Vector2(1, -1);
                triangles[t] = q; triangles[t + 1] = q + 1; triangles[t + 2] = q + 2;
                triangles[t + 3] = q; triangles[t + 4] = q + 2; triangles[t + 5] = q + 3;
            }
            dots.vertices = vertices; dots.uv = uv; dots.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = dots;
            dotsMaterial = new Material(dotShader) { name = "White stimulus dots", enableInstancing = true };
            dotsRenderer = GetComponent<MeshRenderer>();
            dotsRenderer.sharedMaterial = dotsMaterial;
            dotsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dotsRenderer.receiveShadows = false;
            frameObject = new GameObject("Aperture (generated)", typeof(MeshFilter), typeof(MeshRenderer));
            frameObject.transform.SetParent(transform, false);
            frame = new Mesh { name = "Four aperture bars" };
            frameObject.GetComponent<MeshFilter>().sharedMesh = frame;
            frameMaterial = new Material(frameShader) { name = "Grey aperture", enableInstancing = true };
            var frameRenderer = frameObject.GetComponent<MeshRenderer>();
            frameRenderer.sharedMaterial = frameMaterial;
            frameRenderer.shadowCastingMode = ShadowCastingMode.Off;
            frameRenderer.receiveShadows = false;
            BuildFrame();
            Present(simulation);
        }

        public void Present(SwarmSimulation simulation)
        {
            if (lastDimension2D != simulation.Settings.dimension2D)
            {
                lastDimension2D = simulation.Settings.dimension2D;
                BuildFrame();
            }
            for (int i = 0; i < simulation.Positions.Length; i++)
            {
                var p = simulation.Positions[i];
                Vector3 centre = MapPositionForPresentation(p, simulation.Settings);
                for (int j = 0; j < 4; j++) vertices[i * 4 + j] = centre;
            }
            dots.vertices = vertices;
            dots.RecalculateBounds();
            var bounds = dots.bounds;
            bounds.Expand(2f); // Include billboard extent around centre-only mesh vertices.
            dots.bounds = bounds;
            dotsMaterial.SetFloat("_DiameterPixels", diameterPixels);
            dotsMaterial.SetFloat("_DiameterDegrees", diameterDegrees);
            dotsMaterial.SetFloat("_UseAngularSize", useAngularSize ? 1 : 0);
            bool immersive3D = IsImmersive3D(simulation.Settings);
            bool visibleAperture = useAperture && (!immersive3D || useApertureInImmersive3D);
            bool mapped2D = simulation.Settings.dimension2D &&
                (presentationMode == StimulusPresentationMode.VirtualReality || fillDesktop2DViewport);
            float mapped2DDepth = presentationMode == StimulusPresentationMode.VirtualReality
                ? vr2DDisplayDistance
                : DesktopDisplayGeometry().distance;
            dotsMaterial.SetFloat("_MinimumDepth", immersive3D ? -1000000f :
                (mapped2D ? mapped2DDepth - .001f : (useAperture ? frameDistance : .01f)));
            frameObject.SetActive(visibleAperture);
        }

        public Vector3 MapPositionForPresentation(DVector3 position, SwarmSettings settings)
        {
            float x = (float)position.x * (mirrorMatlabX ? -1f : 1f);
            float y = (float)position.y;
            if (presentationMode == StimulusPresentationMode.Desktop)
            {
                if (settings.dimension2D && fillDesktop2DViewport)
                {
                    var geometry = DesktopDisplayGeometry();
                    float displayX = x / (float)settings.width * geometry.width;
                    float displayY = y / (float)settings.height * geometry.height;
                    return new Vector3(displayX, displayY, geometry.distance);
                }

                return MapThreeDimensionalPosition(x, y, position, settings);
            }

            if (settings.dimension2D)
            {
                float displayX = x / (float)settings.width * vr2DDisplayWidth;
                float displayY = y / (float)settings.height * vr2DDisplayHeight;
                return new Vector3(displayX, displayY, vr2DDisplayDistance);
            }

            return MapThreeDimensionalPosition(x, y, position, settings);
        }

        private Vector3 MapThreeDimensionalPosition(float x, float y, DVector3 position, SwarmSettings settings)
        {
            double centreZ = settings.volumeShape == VolumeShape.Sphere
                ? settings.startDepth + settings.sphereRadius
                : settings.startDepth + settings.depth * .5;
            double z = position.z - centreZ;
            if (viewerPlacement == ViewerPlacement.Outside)
            {
                double forwardExtent = settings.volumeShape == VolumeShape.Sphere
                    ? settings.sphereRadius
                    : settings.depth * .5;
                z += forwardExtent + outsideClearance;
            }
            return new Vector3(x, y, (float)z);
        }

        private bool IsImmersive3D(SwarmSettings settings)
        {
            return !settings.dimension2D;
        }

        private (float width, float height, float distance) DesktopDisplayGeometry()
        {
            const float fallbackWidth = .587f;
            const float fallbackDistance = .715f;
            const float fallbackAspect = 800f / 600f;
            float width = fallbackWidth;
            float distance = fallbackDistance;
            float aspect = fallbackAspect;

            if (presentationCamera)
            {
                if (presentationCamera.TryGetComponent(out DesktopFlyCamera controls))
                {
                    width = controls.displayWidthMetres;
                    distance = controls.viewingDistanceMetres;
                    aspect = controls.adaptFieldOfViewToViewport
                        ? presentationCamera.aspect
                        : controls.referenceWidthPixels / (float)Mathf.Max(1, controls.referenceHeightPixels);
                }
                else
                {
                    aspect = presentationCamera.aspect;
                }
            }

            aspect = Mathf.Max(.01f, aspect);
            return (width, width / aspect, distance);
        }

        public void BuildFrame()
        {
            if (frame == null) return;
            bool vr2D = presentationMode == StimulusPresentationMode.VirtualReality && layout.dimension2D;
            float distance = vr2D ? vr2DDisplayDistance : frameDistance;
            float halfX = vr2D ? vr2DDisplayWidth * .5f : (float)(layout.width / 2 * frameDistance / (layout.startDepth + layout.depth));
            float halfY = vr2D ? vr2DDisplayHeight * .5f : (float)(layout.height / 2 * frameDistance / (layout.startDepth + layout.depth));
            float outerX = vr2D ? halfX + vr2DFrameBorder : Mathf.Max(frameOuterWidth, halfX + .001f);
            float outerY = vr2D ? halfY + vr2DFrameBorder : Mathf.Max(frameOuterHeight, halfY + .001f);
            var v = new Vector3[16];
            var t = new int[24];
            Bar(v, t, 0, -outerX, halfY, outerX, outerY, distance);
            Bar(v, t, 1, -outerX, -outerY, outerX, -halfY, distance);
            Bar(v, t, 2, -outerX, -halfY, -halfX, halfY, distance);
            Bar(v, t, 3, halfX, -halfY, outerX, halfY, distance);
            frame.Clear(); frame.vertices = v; frame.triangles = t; frame.RecalculateBounds();
        }

        private static void Bar(Vector3[] v, int[] t, int index, float x0, float y0, float x1, float y1, float distance)
        {
            int q = index * 4, a = index * 6;
            v[q] = new Vector3(x0, y0, distance); v[q + 1] = new Vector3(x0, y1, distance);
            v[q + 2] = new Vector3(x1, y1, distance); v[q + 3] = new Vector3(x1, y0, distance);
            t[a] = q; t[a + 1] = q + 1; t[a + 2] = q + 2;
            t[a + 3] = q; t[a + 4] = q + 2; t[a + 5] = q + 3;
        }

        private void OnValidate()
        {
            MigrateThreeDPlacement();
            frameDistance = Mathf.Max(.01f, frameDistance);
            frameOuterWidth = Mathf.Max(.01f, frameOuterWidth);
            frameOuterHeight = Mathf.Max(.01f, frameOuterHeight);
            vr2DDisplayWidth = Mathf.Max(.05f, vr2DDisplayWidth);
            vr2DDisplayHeight = Mathf.Max(.05f, vr2DDisplayHeight);
            vr2DDisplayDistance = Mathf.Max(.05f, vr2DDisplayDistance);
            vr2DFrameBorder = Mathf.Max(0, vr2DFrameBorder);
            outsideClearance = Mathf.Max(0, outsideClearance);
            BuildFrame();
        }
        private void MigrateThreeDPlacement()
        {
            if (threeDPlacementSerializationVersion >= 1) return;
            viewerPlacement = ViewerPlacement.Centre;
            outsideClearance = 10f;
            threeDPlacementSerializationVersion = 1;
        }
        private void OnDisable()
        {
            if (dotsRenderer) dotsRenderer.enabled = false;
            if (frameObject) frameObject.SetActive(false);
        }
        private void OnEnable() { if (dotsRenderer) dotsRenderer.enabled = true; }
        private void OnDestroy()
        {
            if (dots) Destroy(dots);
            if (frame) Destroy(frame);
            if (dotsMaterial) Destroy(dotsMaterial);
            if (frameMaterial) Destroy(frameMaterial);
            if (frameObject) Destroy(frameObject);
        }
    }
}
