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
        [Tooltip("How the participant views the dots. This is kept in sync with Stimulus Controller.")]
        public StimulusPresentationMode presentationMode = StimulusPresentationMode.Desktop;

        [Header("Dots")]
        [Tooltip("Draws the white dots. Assigned automatically; normally leave this unchanged.")]
        public Shader dotShader;
        [Tooltip("Draws the dark aperture frame. Assigned automatically; normally leave this unchanged.")]
        public Shader frameShader;
        [Tooltip("Width of each dot in screen pixels when Use Angular Size is off. Default: 6 pixels.")]
        [Min(1)] public float diameterPixels = 6;
        [Tooltip("Keep approximately the same apparent dot size when display resolution or headset render scale changes.")]
        public bool useAngularSize;
        [Tooltip("Apparent dot width in degrees when Use Angular Size is on. Default: 0.3528 degrees.")]
        [Min(.001f)] public float diameterDegrees = .3528f;
        [Tooltip("Flip the displayed image left-to-right to match the source experiment. This does not change movement or neighbour distances.")]
        public bool mirrorMatlabX = true;
        [Tooltip("3D only. Give dots a fixed size in metres so distant dots look smaller. Turn off only to keep the old constant on-screen size in 3D.")]
        public bool useWorldSizeIn3D = true;
        [Tooltip("3D dot diameter in metres when Use World Size In 3D is on. Default: 0.06 m (6 cm).")]
        [Min(.001f)] public float diameterMetres = .06f;

        [Header("Desktop presentation")]
        [Tooltip("Desktop 2D only. Fill the complete Game view. This changes only the display, not dot movement or distances.")]
        public bool fillDesktop2DViewport = true;
        [HideInInspector] public bool centreDesktop3DOnViewer = true;

        [Header("3D viewer placement")]
        [Tooltip("3D only. Centre surrounds the viewer with the volume. Outside puts the whole volume directly in front.")]
        public ViewerPlacement viewerPlacement = ViewerPlacement.Centre;
        [Tooltip("Outside mode only. Empty distance in metres from the viewer to the nearest surface of the box or sphere.")]
        [Min(0f)] public float outsideClearance = 10f;
        [SerializeField, HideInInspector] private int threeDPlacementSerializationVersion = 2;

        [Header("MATLAB desktop aperture")]
        [Tooltip("Show a dark frame around the stimulus. Press A during Play to show or hide it.")]
        public bool useAperture = true;
        [Tooltip("Distance in metres from the viewer to the aperture frame. Default: 0.715 m.")]
        [Min(.01f)] public float frameDistance = .715f;
        [Tooltip("How far the aperture frame extends horizontally from its centre. Normally leave this at the default.")]
        [FormerlySerializedAs("frameOuterExtent")]
        [Min(.01f)] public float frameOuterWidth = .587f;
        [Tooltip("How far the aperture frame extends vertically from its centre. Normally leave this at the default.")]
        [Min(.01f)] public float frameOuterHeight = .44025f;

        [Header("VR immersive 3D")]
        [HideInInspector] public bool centreVrVolumeOnViewer = true;
        [Tooltip("VR 3D only. Show the aperture frame while dots surround the viewer. Normally leave this off so it does not block the view.")]
        public bool useApertureInImmersive3D;

        [Header("VR 2D display (metres)")]
        [Tooltip("Width in metres of the virtual screen used for VR 2D. Default: 0.587 m.")]
        [Min(.05f)] public float vr2DDisplayWidth = .587f;
        [Tooltip("Height in metres of the virtual screen used for VR 2D. Default: 0.44025 m.")]
        [Min(.05f)] public float vr2DDisplayHeight = .44025f;
        [Tooltip("Distance in metres from the headset to the virtual VR 2D screen. Default: 0.715 m.")]
        [Min(.05f)] public float vr2DDisplayDistance = .715f;
        [Tooltip("Width in metres of the dark border around the VR 2D screen. This does not affect dot movement boundaries.")]
        [Min(0f)] public float vr2DFrameBorder = .04f;

        private Mesh dots, fadingDots, frame;
        private Material dotsMaterial, frameMaterial;
        private MeshRenderer dotsRenderer, fadingDotsRenderer;
        private GameObject fadingDotsObject, frameObject;
        private Vector3[] vertices;
        private Vector3[] fadingVertices;
        private Color32[] fadingColours;
        private int fadingCapacity;
        private SwarmSettings layout;
        private bool lastDimension2D;
        private Camera presentationCamera;

        public void SetPresentationCamera(Camera camera) => presentationCamera = camera;

        private void Awake() => MigrateThreeDPlacement();

        public void Initialize(SwarmSimulation simulation)
        {
            layout = simulation.Settings;
            lastDimension2D = layout.dimension2D;
            if (!dotShader || !frameShader) throw new System.InvalidOperationException("Assign both shaders, or create a scene from Tools > Dots Stimulus > Create New Stimulus.");
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
            var opaque = new Color32[vertices.Length];
            for (int i = 0; i < opaque.Length; i++) opaque[i] = new Color32(255, 255, 255, 255);
            dots.vertices = vertices; dots.uv = uv; dots.colors32 = opaque; dots.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = dots;
            dotsMaterial = new Material(dotShader) { name = "White stimulus dots", enableInstancing = true };
            dotsRenderer = GetComponent<MeshRenderer>();
            dotsRenderer.sharedMaterial = dotsMaterial;
            dotsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dotsRenderer.receiveShadows = false;
            fadingDotsObject = new GameObject("Fading dots (generated)", typeof(MeshFilter), typeof(MeshRenderer));
            fadingDotsObject.transform.SetParent(transform, false);
            fadingDots = new Mesh { name = "Fading dot centres and billboard corners", indexFormat = IndexFormat.UInt32 };
            fadingDots.MarkDynamic();
            fadingDotsObject.GetComponent<MeshFilter>().sharedMesh = fadingDots;
            fadingDotsRenderer = fadingDotsObject.GetComponent<MeshRenderer>();
            fadingDotsRenderer.sharedMaterial = dotsMaterial;
            fadingDotsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fadingDotsRenderer.receiveShadows = false;
            fadingDotsRenderer.enabled = false;
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
            bounds.Expand(Mathf.Max(2f, diameterMetres * 2f)); // Include billboard extent around centre-only mesh vertices.
            dots.bounds = bounds;
            PresentFadingDots(simulation);
            dotsMaterial.SetFloat("_DiameterPixels", diameterPixels);
            dotsMaterial.SetFloat("_DiameterDegrees", diameterDegrees);
            dotsMaterial.SetFloat("_UseAngularSize", useAngularSize ? 1 : 0);
            dotsMaterial.SetFloat("_DiameterMetres", diameterMetres);
            dotsMaterial.SetFloat("_UseWorldSize", UsesWorldSize(simulation.Settings) ? 1 : 0);
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

        private void PresentFadingDots(SwarmSimulation simulation)
        {
            int count = simulation.FadingDots.Count;
            if (count == 0)
            {
                fadingDotsRenderer.enabled = false;
                return;
            }

            EnsureFadingCapacity(count);
            for (int i = 0; i < fadingColours.Length; i++) fadingColours[i] = new Color32(255, 255, 255, 0);
            for (int i = 0; i < count; i++)
            {
                SwarmSimulation.FadingDot fading = simulation.FadingDots[i];
                Vector3 centre = MapPositionForPresentation(fading.Position, simulation.Settings);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((float)fading.Opacity) * 255);
                var colour = new Color32(255, 255, 255, alpha);
                int q = i * 4;
                for (int j = 0; j < 4; j++)
                {
                    fadingVertices[q + j] = centre;
                    fadingColours[q + j] = colour;
                }
            }
            fadingDots.vertices = fadingVertices;
            fadingDots.colors32 = fadingColours;
            fadingDots.RecalculateBounds();
            var bounds = fadingDots.bounds;
            bounds.Expand(Mathf.Max(2f, diameterMetres * 2f));
            fadingDots.bounds = bounds;
            fadingDotsRenderer.enabled = true;
        }

        private void EnsureFadingCapacity(int required)
        {
            if (required <= fadingCapacity) return;
            fadingCapacity = Mathf.NextPowerOfTwo(Mathf.Max(16, required));
            fadingVertices = new Vector3[fadingCapacity * 4];
            fadingColours = new Color32[fadingVertices.Length];
            var uv = new Vector2[fadingVertices.Length];
            var triangles = new int[fadingCapacity * 6];
            for (int i = 0; i < fadingCapacity; i++)
            {
                int q = i * 4, t = i * 6;
                uv[q] = new Vector2(-1, -1); uv[q + 1] = new Vector2(-1, 1);
                uv[q + 2] = new Vector2(1, 1); uv[q + 3] = new Vector2(1, -1);
                triangles[t] = q; triangles[t + 1] = q + 1; triangles[t + 2] = q + 2;
                triangles[t + 3] = q; triangles[t + 4] = q + 2; triangles[t + 5] = q + 3;
            }
            fadingDots.Clear();
            fadingDots.vertices = fadingVertices;
            fadingDots.uv = uv;
            fadingDots.colors32 = fadingColours;
            fadingDots.triangles = triangles;
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

        public bool UsesWorldSize(SwarmSettings settings) => !settings.dimension2D && useWorldSizeIn3D;

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
            diameterMetres = Mathf.Max(.001f, diameterMetres);
            BuildFrame();
        }
        private void MigrateThreeDPlacement()
        {
            if (threeDPlacementSerializationVersion < 1)
            {
                viewerPlacement = ViewerPlacement.Centre;
                outsideClearance = 10f;
            }
            if (threeDPlacementSerializationVersion < 2)
            {
                useWorldSizeIn3D = true;
                diameterMetres = .06f;
            }
            threeDPlacementSerializationVersion = 2;
        }
        private void OnDisable()
        {
            if (dotsRenderer) dotsRenderer.enabled = false;
            if (fadingDotsRenderer) fadingDotsRenderer.enabled = false;
            if (frameObject) frameObject.SetActive(false);
        }
        private void OnEnable() { if (dotsRenderer) dotsRenderer.enabled = true; }
        private void OnDestroy()
        {
            if (dots) Destroy(dots);
            if (fadingDots) Destroy(fadingDots);
            if (frame) Destroy(frame);
            if (dotsMaterial) Destroy(dotsMaterial);
            if (frameMaterial) Destroy(frameMaterial);
            if (frameObject) Destroy(frameObject);
            if (fadingDotsObject) Destroy(fadingDotsObject);
        }
    }
}
