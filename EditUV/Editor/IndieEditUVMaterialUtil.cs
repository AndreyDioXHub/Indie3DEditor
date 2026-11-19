using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVMaterialUtil
    {
        private static string CheckerMaterialPath =>
            Indie3DEditorPackagePaths.Material("EditUV/Materials/UVChecker.mat");
        private static string FlatMaterialPath =>
            Indie3DEditorPackagePaths.Material("EditUV/Materials/UVFlat.mat");

        private static Texture2D _previewTexture;
        private static Material _runtimeChecker;
        private static Material _runtimeFlat;
        private static Material _sessionFlatInstance;

        public static Texture2D PreviewTexture => _previewTexture;

        public static void ClearSession()
        {
            _previewTexture = null;
            DestroySessionFlatInstance();
        }

        public static void SetPreviewTexture(IndieEditUVComponent uv, Texture2D texture)
        {
            _previewTexture = texture;
            ApplyPreview(uv, preserveOriginals: true);
        }

        public static void ApplyPreview(IndieEditUVComponent uv)
        {
            ApplyPreview(uv, preserveOriginals: false);
        }

        public static void ApplyChecker(IndieEditUVComponent uv)
        {
            _previewTexture = null;
            ApplyPreview(uv, preserveOriginals: false);
        }

        public static void RestoreMaterials(IndieEditUVComponent uv)
        {
            if (uv == null)
                return;

            var renderer = uv.GetMeshRenderer();

            if (renderer == null)
                return;

            Material[] originals = uv.OriginalMaterials;

            if (originals != null)
            {
                renderer.sharedMaterials = originals;
                EditorUtility.SetDirty(renderer);
            }

            uv.SetOriginalMaterials(null);
            ClearSession();
        }

        public static void GetCheckerAppearance(out Color colorA, out Color colorB, out float density)
        {
            colorA = new Color(0.85f, 0.85f, 0.85f, 1f);
            colorB = new Color(0.2f, 0.2f, 0.2f, 1f);
            density = 8f;

            Material checker = LoadCheckerAsset();

            if (checker == null)
                return;

            if (checker.HasProperty("_ColorA"))
                colorA = checker.GetColor("_ColorA");

            if (checker.HasProperty("_ColorB"))
                colorB = checker.GetColor("_ColorB");

            if (checker.HasProperty("_Density"))
                density = Mathf.Max(checker.GetFloat("_Density"), 0.0001f);
        }

        private static void ApplyPreview(IndieEditUVComponent uv, bool preserveOriginals)
        {
            if (uv == null)
                return;

            var renderer = uv.GetMeshRenderer();

            if (renderer == null)
                return;

            if (!preserveOriginals || uv.OriginalMaterials == null)
            {
                Material[] originals = renderer.sharedMaterials;
                var copy = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                    copy[i] = originals[i];

                uv.SetOriginalMaterials(copy);
            }

            Material preview = _previewTexture != null
                ? GetFlatMaterial(_previewTexture)
                : GetCheckerMaterial();

            if (preview == null)
            {
                Debug.LogError(
                    _previewTexture != null
                        ? "Indie EditUV: UVFlat material/shader not found."
                        : "Indie EditUV: UVChecker material not found at " + CheckerMaterialPath
                );
                return;
            }

            int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
            var assigned = new Material[slotCount];

            for (int i = 0; i < slotCount; i++)
                assigned[i] = preview;

            renderer.sharedMaterials = assigned;
            EditorUtility.SetDirty(renderer);
        }

        private static Material GetCheckerMaterial()
        {
            Material asset = LoadCheckerAsset();

            if (asset != null)
                return asset;

            Shader shader = Shader.Find("Indie3DEditor/UVChecker");

            if (shader == null)
                return null;

            if (_runtimeChecker == null)
            {
                _runtimeChecker = new Material(shader)
                {
                    name = "UVChecker (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _runtimeChecker.SetColor("_ColorA", new Color(0.85f, 0.85f, 0.85f, 1f));
                _runtimeChecker.SetColor("_ColorB", new Color(0.2f, 0.2f, 0.2f, 1f));
                _runtimeChecker.SetFloat("_Density", 8f);
            }

            return _runtimeChecker;
        }

        private static Material LoadCheckerAsset()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(CheckerMaterialPath);
        }

        private static Material GetFlatMaterial(Texture2D texture)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(FlatMaterialPath);

            if (source == null)
            {
                Shader shader = Shader.Find("Indie3DEditor/UVFlat");

                if (shader == null)
                    return null;

                if (_runtimeFlat == null)
                {
                    _runtimeFlat = new Material(shader)
                    {
                        name = "UVFlat (Runtime)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                source = _runtimeFlat;
            }

            if (_sessionFlatInstance == null)
            {
                _sessionFlatInstance = new Material(source)
                {
                    name = "UVFlat (Session)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (_sessionFlatInstance.HasProperty("_BaseMap"))
                _sessionFlatInstance.SetTexture("_BaseMap", texture);
            else
                _sessionFlatInstance.mainTexture = texture;

            if (_sessionFlatInstance.HasProperty("_BaseColor"))
                _sessionFlatInstance.SetColor("_BaseColor", Color.white);

            return _sessionFlatInstance;
        }

        private static void DestroySessionFlatInstance()
        {
            if (_sessionFlatInstance == null)
                return;

            Object.DestroyImmediate(_sessionFlatInstance);
            _sessionFlatInstance = null;
        }
    }
}
