using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using Indie3DEditor.Retopology;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVSession
    {
        public static IndieEditUVComponent Active { get; private set; }

        private static Object[] _previousSelection;
        private static bool _previousToolsHidden;
        private static Tool _previousTool;

        public static void BeginEditing(IndieEditUVComponent uv)
        {
            if (uv == null)
                return;

            if (IndieEditMeshSession.Active != null)
                IndieEditMeshSession.EndEditing(IndieEditMeshSession.Active, true);

            if (IndieRetopologySession.Active != null)
                IndieRetopologySession.EndEditing(IndieRetopologySession.Active, true);

            if (Active != null)
                EndEditing(Active, true);

            var meshFilter = uv.GetMeshFilter();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Indie EditUV: Object does not have a mesh to edit.");
                return;
            }

            IndieEditableMeshData imported;

            try
            {
                imported = IndieEditableMeshImporter.Import(meshFilter.sharedMesh);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Indie EditUV: Cannot import mesh. {ex.Message}");
                return;
            }

            IndieEditMeshMeshUtil.EnsureUvChannel(imported);

            _previousSelection = Selection.objects;
            _previousToolsHidden = Tools.hidden;
            _previousTool = Tools.current;

            uv.SetOriginalMesh(meshFilter.sharedMesh);
            uv.SetData(imported);
            uv.isEditing = true;

            Active = uv;

            IndieEditUVUndo.Clear();
            IndieEditUVGizmo.Reset();
            IndieEditUVSelection.ResetMarquee();
            IndieEditUVSeamState.ClearSession();
            IndieEditUVSeamState.RebuildBaked(imported);
            IndieEditUVPelt.EndSession();
            IndieEditUVMaterialUtil.ClearSession();
            IndieEditUVMaterialUtil.ApplyPreview(uv);
            RefreshMesh(uv);
            IndieEditUVSceneView.InvalidateCache(uv);

            Selection.objects = new Object[]
            {
                uv.gameObject
            };

            Tools.hidden = true;
            // View tool shows the hand cursor and steals LMB for pan — keep None while editing.
            Tools.current = Tool.None;
            UnityEditor.Undo.ClearAll();

            IndieEditUVSettingsWindow.Open(uv);
            SceneView.RepaintAll();
        }

        public static void EndEditing(IndieEditUVComponent uv, bool applyChanges)
        {
            if (uv == null)
                return;

            IndieEditUVSettingsWindow.CloseIfOpen();
            IndieEditUVPeltWindow.CloseIfOpen();
            IndieEditUVRelaxWindow.CloseIfOpen();
            IndieEditUVRelax.Stop();
            IndieEditUVPelt.EndSession();
            IndieEditUVSeamState.ClearSession();

            var meshFilter = uv.GetMeshFilter();

            if (meshFilter != null)
            {
                DestroyPreviewMesh(meshFilter, uv);

                if (applyChanges && uv.Data != null)
                {
                    Mesh previousMesh = uv.OriginalMesh;

                    var finalMesh = IndieEditableMeshBuilder.BuildMesh(uv.Data);
                    string safeName = SanitizeAssetName(uv.gameObject.name);
                    var savedMesh = SaveMeshAsset(finalMesh, safeName, previousMesh);

                    meshFilter.sharedMesh = savedMesh != null ? savedMesh : finalMesh;

                    EditorUtility.SetDirty(meshFilter);
                }
                else if (uv.OriginalMesh != null)
                {
                    meshFilter.sharedMesh = uv.OriginalMesh;
                }
            }

            IndieEditUVMaterialUtil.RestoreMaterials(uv);

            uv.isEditing = false;
            uv.SetData(null);
            uv.SetOriginalMesh(null);

            if (Active == uv)
                Active = null;

            IndieEditUVUndo.Clear();
            IndieEditUVGizmo.Reset();
            IndieEditUVSelection.ResetMarquee();
            IndieEditUVSceneView.InvalidateCache(uv);

            Tools.hidden = _previousToolsHidden;
            Tools.current = _previousTool;
            Selection.objects = _previousSelection != null ? _previousSelection : new Object[0];

            IndieEditMeshViewFrame.RestoreFlySpeed();
            SceneView.RepaintAll();
        }

        public static void RefreshMesh(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            var meshFilter = uv.GetMeshFilter();

            if (meshFilter == null)
                return;

            DestroyPreviewMesh(meshFilter, uv);

            var mesh = IndieEditableMeshBuilder.BuildMesh(uv.Data, PreviewSourceVertices);
            mesh.name = "Indie EditUV Preview";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _previewMesh = mesh;

            meshFilter.sharedMesh = mesh;
            IndieEditUVSceneView.InvalidateCache(uv);

            EditorUtility.SetDirty(uv);
        }

        // Preview vertex -> data vertex (smoothing groups split data verts per normal).
        private static readonly List<int> PreviewSourceVertices = new List<int>();
        private static readonly List<Vector2> PreviewUvBuffer = new List<Vector2>();
        private static Mesh _previewMesh;

        /// <summary>
        /// UV-only change (gizmo / type-in drag): rewrite UV0 of the preview mesh in place.
        /// Falls back to <see cref="RefreshMesh"/> when the preview was not built by RefreshMesh
        /// or the data no longer matches its vertex map.
        /// </summary>
        public static void RefreshUvsOnly(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            var data = uv.Data;
            var meshFilter = uv.GetMeshFilter();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            int uvCount = data.uvs.Count;

            if (mesh == null ||
                mesh != _previewMesh ||
                uvCount != data.vertices.Count ||
                mesh.vertexCount != PreviewSourceVertices.Count)
            {
                RefreshMesh(uv);
                return;
            }

            PreviewUvBuffer.Clear();

            for (int i = 0; i < PreviewSourceVertices.Count; i++)
            {
                int source = PreviewSourceVertices[i];

                if (source < 0 || source >= uvCount)
                {
                    RefreshMesh(uv);
                    return;
                }

                PreviewUvBuffer.Add(data.uvs[source]);
            }

            mesh.SetUVs(0, PreviewUvBuffer);
        }

        private static void DestroyPreviewMesh(MeshFilter meshFilter, IndieEditUVComponent uv)
        {
            if (meshFilter == null)
                return;

            var oldMesh = meshFilter.sharedMesh;

            if (oldMesh == null)
                return;

            if (oldMesh == uv.OriginalMesh)
                return;

            if ((oldMesh.hideFlags & HideFlags.HideAndDontSave) != 0)
                Object.DestroyImmediate(oldMesh);
        }

        private const string GeneratedFolder = "Assets/Indie 3D Editor/Generated";

        /// <summary>
        /// A mesh already in Generated is overwritten in place so Edit UV after
        /// Edit Poly does not add a second asset. Anything else gets a new file.
        /// </summary>
        private static Mesh SaveMeshAsset(Mesh mesh, string objectName, Mesh existing)
        {
            if (mesh == null)
                return null;

            if (IsGeneratedMeshAsset(existing))
            {
                string keptName = existing.name;
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = keptName;
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            string rootFolder = "Assets/Indie 3D Editor";

            if (!AssetDatabase.IsValidFolder(rootFolder))
                AssetDatabase.CreateFolder("Assets", "Indie 3D Editor");

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder(rootFolder, "Generated");

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedFolder}/{objectName}_EditUV.asset"
            );

            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static bool IsGeneratedMeshAsset(Mesh mesh)
        {
            if (mesh == null)
                return false;

            string path = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');
            return path.StartsWith(GeneratedFolder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "EditUVMesh";

            foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(invalidChar, '_');

            return name;
        }
    }
}
