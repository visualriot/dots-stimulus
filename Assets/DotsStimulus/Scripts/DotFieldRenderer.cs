using UnityEngine;
using UnityEngine.Rendering;

namespace DotsStimulus
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DotFieldRenderer : MonoBehaviour
    {
        public Shader dotShader;
        public Shader frameShader;
        [Min(1)] public float diameterPixels = 6;
        [Tooltip("Off: literal 6 render-target pixels. On: approximate angular size at the centre of view.")]
        public bool useAngularSize;
        [Min(.001f)] public float diameterDegrees = .3528f;
        [Tooltip("gluLookAt toward +Z maps MATLAB +X to screen left. Mirror only the rendering to match it.")]
        public bool mirrorMatlabX = true;
        public bool useAperture = true;
        [Min(.01f)] public float frameDistance = .715f;
        [Tooltip("VR adaptation: extend the frame beyond the headset view. MATLAB used the monitor edges.")]
        [Min(1)] public float frameOuterExtent = 10;

        private Mesh dots, frame;
        private Material dotsMaterial, frameMaterial;
        private MeshRenderer dotsRenderer;
        private GameObject frameObject;
        private Vector3[] vertices;
        private SwarmSettings layout;

        public void Initialize(SwarmSimulation simulation)
        {
            layout = simulation.Settings;
            if (!dotShader || !frameShader) throw new System.InvalidOperationException("Assign both shaders, or use Tools > Dots Stimulus > Create stimulus scene.");
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
            for (int i = 0; i < simulation.Positions.Length; i++)
            {
                var p = simulation.Positions[i];
                var centre = new Vector3((float)p.x * (mirrorMatlabX ? -1 : 1), (float)p.y, (float)p.z);
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
            dotsMaterial.SetFloat("_MinimumDepth", useAperture ? frameDistance : .01f);
            frameObject.SetActive(useAperture);
        }

        public void BuildFrame()
        {
            if (frame == null) return;
            float halfX = (float)(layout.width / 2 * frameDistance / (layout.startDepth + layout.depth));
            float halfY = (float)(layout.height / 2 * frameDistance / (layout.startDepth + layout.depth));
            float outer = Mathf.Max(frameOuterExtent, Mathf.Max(halfX, halfY) + 1);
            var v = new Vector3[16];
            var t = new int[24];
            Bar(v, t, 0, -outer, halfY, outer, outer);
            Bar(v, t, 1, -outer, -outer, outer, -halfY);
            Bar(v, t, 2, -outer, -halfY, -halfX, halfY);
            Bar(v, t, 3, halfX, -halfY, outer, halfY);
            frame.Clear(); frame.vertices = v; frame.triangles = t; frame.RecalculateBounds();
        }

        private void Bar(Vector3[] v, int[] t, int index, float x0, float y0, float x1, float y1)
        {
            int q = index * 4, a = index * 6;
            v[q] = new Vector3(x0, y0, frameDistance); v[q + 1] = new Vector3(x0, y1, frameDistance);
            v[q + 2] = new Vector3(x1, y1, frameDistance); v[q + 3] = new Vector3(x1, y0, frameDistance);
            t[a] = q; t[a + 1] = q + 1; t[a + 2] = q + 2;
            t[a + 3] = q; t[a + 4] = q + 2; t[a + 5] = q + 3;
        }

        private void OnValidate() => BuildFrame();
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
