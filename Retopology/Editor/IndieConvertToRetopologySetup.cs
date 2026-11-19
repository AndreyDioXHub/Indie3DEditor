using System;
using System.IO;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    [InitializeOnLoad]
    public static class IndieConvertToRetopologySetup
    {
        private const string GeneratedFolder = "Assets/Indie 3D Editor/Generated";

        static IndieConvertToRetopologySetup()
        {
            ObjectFactory.componentWasAdded -= OnComponentAdded;
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        public static IndieRetopologyMeshData CopyTriangles(IndieEditableMeshData source)
        {
            var data = new IndieRetopologyMeshData();

            if (source == null || source.vertices == null)
                return data;

            int vertexCount = source.vertices.Count;

            if (vertexCount > 0)
                data.vertices.AddRange(source.vertices);

            if (source.faces == null)
                return data;

            for (int i = 0; i < source.faces.Count; i++)
            {
                IndieEditMeshFace face = source.faces[i];

                if (face.a < 0 || face.b < 0 || face.c < 0 ||
                    face.a >= vertexCount || face.b >= vertexCount || face.c >= vertexCount)
                    continue;

                data.faces.Add(new IndieRetopologyFace
                {
                    a = face.a,
                    b = face.b,
                    c = face.c
                });
            }

            return data;
        }

        public static bool IsEditPolyGeneratedMesh(Mesh mesh)
        {
            if (mesh == null)
                return false;

            string path = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');

            if (!path.StartsWith(GeneratedFolder + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return false;

            string file = Path.GetFileNameWithoutExtension(path);
            return HasGeneratedSuffix(file, "_EditMesh") || HasGeneratedSuffix(file, "_EditPoly");
        }

        private static bool HasGeneratedSuffix(string file, string suffix)
        {
            int marker = file.LastIndexOf(suffix, StringComparison.Ordinal);

            if (marker < 0)
                return false;

            int after = marker + suffix.Length;
            return after == file.Length || file[after] == ' ';
        }

        private static void OnComponentAdded(Component component)
        {
            var converter = component as IndieConvertToRetopology;

            if (converter == null)
                return;

            EditorApplication.delayCall += () => Convert(converter);
        }

        private static void Convert(IndieConvertToRetopology converter)
        {
            if (converter == null)
                return;

            GameObject host = converter.gameObject;
            UnityEngine.Object.DestroyImmediate(converter);

            if (host == null || host.GetComponent<IndieRetopologyComponent>() != null)
                return;

            var editPoly = host.GetComponent<IndieEditMeshComponent>();
            var filter = host.GetComponent<MeshFilter>();
            bool editingPoly = editPoly != null && editPoly.isEditing;
            IndieEditableMeshData captured = null;
            Mesh sourceMesh = filter != null ? filter.sharedMesh : null;

            if (editingPoly)
                captured = editPoly.Data;
            else if (sourceMesh != null)
            {
                try
                {
                    captured = IndieEditableMeshImporter.Import(sourceMesh);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Indie Retopology: Cannot convert mesh. {ex.Message}");
                    return;
                }
            }

            if (captured == null || captured.vertices == null || captured.vertices.Count == 0)
            {
                Debug.LogError("Indie Retopology: Object does not have a mesh.");
                return;
            }

            if (editingPoly)
            {
                IndieEditMeshSession.EndEditing(editPoly, true);

                if (filter != null)
                    sourceMesh = filter.sharedMesh;

                UnityEngine.Object.DestroyImmediate(editPoly);
            }

            var retopo = host.AddComponent<IndieRetopologyComponent>();
            retopo.MarkInitialized();
            retopo.SetTarget(null);
            retopo.SetData(CopyTriangles(captured));

            if (IsEditPolyGeneratedMesh(sourceMesh))
                retopo.SetOutputMesh(null);

            EditorUtility.SetDirty(retopo);
        }
    }
}
