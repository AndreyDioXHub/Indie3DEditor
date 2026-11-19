using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Indie3DEditor.CreatePrimitive
{
    public static class IndieCreatePrimitivePreview
    {
        public const string PreviewMeshName = "CreatePrimitive Preview";

        private const int PreviewVersion = 4;

        private static Material _fallbackMaterial;

        public static bool IsPreviewMesh(Mesh mesh)
        {
            if (mesh == null)
                return false;

            if (mesh.name != PreviewMeshName)
                return false;

            if ((mesh.hideFlags & HideFlags.HideAndDontSave) == 0)
                return false;

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(mesh)))
                return false;
#endif

            return true;
        }

        public static MeshFilter EnsureTargets(GameObject gameObject)
        {
            if (gameObject == null)
                return null;

            var filter = gameObject.GetComponent<MeshFilter>();

            if (filter == null)
                filter = gameObject.AddComponent<MeshFilter>();

            var renderer = gameObject.GetComponent<MeshRenderer>();

            if (renderer == null)
                renderer = gameObject.AddComponent<MeshRenderer>();

            EnsureMaterial(renderer);
            return filter;
        }

        internal static void Apply(IndieCreatePrimitiveComponent component)
        {
            if (component == null || component.IsCommitted)
                return;

            if (!component.gameObject.scene.IsValid())
                return;

            var filter = EnsureTargets(component.gameObject);

            if (filter == null)
                return;

            int hash = ParametersHash(component);
            var renderer = component.GetComponent<MeshRenderer>();
            EnsureMaterial(renderer);

            bool parametersChanged = component.AppliedHash != hash;

            if (!parametersChanged && IsPreviewMesh(filter.sharedMesh))
                return;

            Mesh previous = filter.sharedMesh;
            bool capturedBefore = component.CapturedOriginal;
            component.CaptureOriginalIfNeeded(previous);

            var mesh = IndieCreatePrimitiveMeshBuilder.Build(component);
            mesh.name = PreviewMeshName;
            mesh.hideFlags = HideFlags.HideAndDontSave;

            filter.sharedMesh = mesh;
            component.AppliedHash = hash;

            if (IsPreviewMesh(previous))
                DestroyMesh(previous);

#if UNITY_EDITOR
            if (!Application.isPlaying && (parametersChanged || !capturedBefore))
                UnityEditor.EditorUtility.SetDirty(component);

            UnityEditor.SceneView.RepaintAll();
#endif
        }

        internal static void RestoreOriginal(IndieCreatePrimitiveComponent component)
        {
            if (component == null || !component.CapturedOriginal)
                return;

            var filter = component.GetComponent<MeshFilter>();

            if (filter == null)
                return;

            Mesh current = filter.sharedMesh;
            filter.sharedMesh = component.OriginalMesh;

            if (IsPreviewMesh(current))
                DestroyMesh(current);
        }

        private static void EnsureMaterial(MeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial != null)
                return;

            Material material = null;
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;

            if (pipeline != null)
                material = pipeline.defaultMaterial;

            if (material == null)
                material = GetFallbackMaterial();

            if (material == null)
                return;

            renderer.sharedMaterial = material;
        }

        private static Material GetFallbackMaterial()
        {
            if (_fallbackMaterial != null)
                return _fallbackMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
                return null;

            _fallbackMaterial = new Material(shader);
            _fallbackMaterial.name = "CreatePrimitive Fallback";
            _fallbackMaterial.hideFlags = HideFlags.HideAndDontSave;
            return _fallbackMaterial;
        }

        private static int ParametersHash(IndieCreatePrimitiveComponent component)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + PreviewVersion;
                hash = hash * 31 + (int)component.type;
                hash = hash * 31 + component.width.GetHashCode();
                hash = hash * 31 + component.height.GetHashCode();
                hash = hash * 31 + component.depth.GetHashCode();
                hash = hash * 31 + component.widthSegments;
                hash = hash * 31 + component.heightSegments;
                hash = hash * 31 + component.depthSegments;
                hash = hash * 31 + component.radius.GetHashCode();
                hash = hash * 31 + component.sphereSegments;
                hash = hash * 31 + component.sphereRings;
                hash = hash * 31 + component.cylinderRadius.GetHashCode();
                hash = hash * 31 + component.cylinderHeight.GetHashCode();
                hash = hash * 31 + component.cylinderSides;
                return hash;
            }
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(mesh);
            else
                Object.DestroyImmediate(mesh);
        }
    }
}
