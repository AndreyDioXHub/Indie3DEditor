using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Indie3DEditor.CreatePrimitive
{
    public static class IndieCreatePrimitiveAssetStore
    {
        private const string RootFolder = "Assets/Indie 3D Editor";
        private const string GeneratedFolderName = "Generated";

        public static void Bake(IndieCreatePrimitiveComponent component)
        {
            if (component == null)
                return;

            var filter = IndieCreatePrimitivePreview.EnsureTargets(component.gameObject);

            if (filter == null)
                return;

            var mesh = IndieCreatePrimitiveMeshBuilder.Build(component);
            string typeName = component.type.ToString();
            string objectName = typeName + " Primitive";
            Mesh saved = Save(mesh, objectName, typeName);

            if (saved == null)
            {
                Object.DestroyImmediate(mesh);
                Debug.LogError("Indie 3D Editor: Failed to save the primitive mesh.");
                return;
            }

            Mesh previous = filter.sharedMesh;

            component.MarkCommitted();
            filter.sharedMesh = saved;
            component.gameObject.name = objectName;

            if (IndieCreatePrimitivePreview.IsPreviewMesh(previous))
                Object.DestroyImmediate(previous);

            var scene = component.gameObject.scene;

            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(component.gameObject);

            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);

            Object.DestroyImmediate(component);
            SceneView.RepaintAll();
        }

        private static Mesh Save(Mesh mesh, string objectName, string typeName)
        {
            if (mesh == null)
                return null;

            string generatedFolder = EnsureGeneratedFolder();

            if (string.IsNullOrEmpty(generatedFolder))
                return null;

            string assetName = SanitizeAssetName(objectName) + "_" + typeName;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{generatedFolder}/{assetName}.asset");

            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            mesh.hideFlags = HideFlags.None;

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static string EnsureGeneratedFolder()
        {
            string generatedFolder = RootFolder + "/" + GeneratedFolderName;

            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "Indie 3D Editor");

            if (!AssetDatabase.IsValidFolder(generatedFolder))
                AssetDatabase.CreateFolder(RootFolder, GeneratedFolderName);

            if (!AssetDatabase.IsValidFolder(generatedFolder))
                return null;

            return generatedFolder;
        }

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Primitive";

            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();

            for (int i = 0; i < invalidChars.Length; i++)
                name = name.Replace(invalidChars[i], '_');

            return name;
        }
    }
}
