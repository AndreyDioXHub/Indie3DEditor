using System.Collections.Generic;
using Indie3DEditor.EditUV;
using Indie3DEditor.Retopology;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshSession
    {
        public static IndieEditMeshComponent Active { get; private set; }

        private static Object[] _previousSelection;
        private static bool _previousToolsHidden;

        public static void BeginEditing(IndieEditMeshComponent ep)
        {
            if (ep == null)
                return;

            if (IndieEditUVSession.Active != null)
                IndieEditUVSession.EndEditing(IndieEditUVSession.Active, true);

            if (IndieRetopologySession.Active != null)
                IndieRetopologySession.EndEditing(IndieRetopologySession.Active, true);

            if (Active != null)
                EndEditing(Active, true);

            var meshFilter = ep.GetMeshFilter();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Indie EditMesh: Object does not have a mesh to edit.");
                return;
            }

            IndieEditableMeshData imported;

            try
            {
                imported = IndieEditableMeshImporter.Import(meshFilter.sharedMesh);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Indie EditMesh: Cannot import mesh. {ex.Message}");
                return;
            }

            _previousSelection = Selection.objects;
            _previousToolsHidden = Tools.hidden;

            ep.SetOriginalMesh(meshFilter.sharedMesh);
            ep.SetData(imported);
            ep.isEditing = true;

            Active = ep;

            IndieEditMeshUndo.Clear();
            IndieEditMeshTransformTool.ResetDrag();
            IndieEditMeshSelection.ResetMarquee();
            IndieEditMeshCutTool.Deactivate();
            IndieEditMeshAttachTool.Deactivate();
            IndieEditMeshPointToPoint.Deactivate();

            RefreshMesh(ep);

            Selection.objects = new Object[]
            {
                ep.gameObject
            };

            Tools.hidden = true;

            // Modal edit: Ctrl+Z must not unwind Add Component / Selection via Unity Undo.
            UnityEditor.Undo.ClearAll();

            SceneView.RepaintAll();
        }

        public static void EndEditing(IndieEditMeshComponent ep, bool applyChanges)
        {
            if (ep == null)
                return;

            if (IndieEditMeshPreviewOp.IsActive && IndieEditMeshPreviewOp.Component == ep)
            {
                IndieEditMeshPreviewOp.Cancel();

                var vertexWindows = Resources.FindObjectsOfTypeAll<IndieEditMeshVertexSettingsWindow>();

                for (int i = 0; i < vertexWindows.Length; i++)
                {
                    if (vertexWindows[i] != null)
                        vertexWindows[i].Close();
                }

                var edgeWindows = Resources.FindObjectsOfTypeAll<IndieEditMeshEdgeSettingsWindow>();

                for (int i = 0; i < edgeWindows.Length; i++)
                {
                    if (edgeWindows[i] != null)
                        edgeWindows[i].Close();
                }

                var faceWindows = Resources.FindObjectsOfTypeAll<IndieEditMeshFaceSettingsWindow>();

                for (int i = 0; i < faceWindows.Length; i++)
                {
                    if (faceWindows[i] != null)
                        faceWindows[i].Close();
                }
            }

            IndieEditMeshEditProxy.Cancel(ep);

            var sgWindows = Resources.FindObjectsOfTypeAll<IndieEditMeshSelectBySGWindow>();

            for (int i = 0; i < sgWindows.Length; i++)
            {
                if (sgWindows[i] != null)
                    sgWindows[i].Close();
            }

            var meshFilter = ep.GetMeshFilter();

            if (meshFilter != null)
            {
                DestroyPreviewMesh(meshFilter, ep);

                if (applyChanges && ep.Data != null)
                {
                    Mesh previousMesh = ep.OriginalMesh;

                    var finalMesh = IndieEditableMeshBuilder.BuildMesh(ep.Data);
                    finalMesh.name = $"{ep.gameObject.name}_EditMesh";

                    string safeName = SanitizeAssetName(ep.gameObject.name);
                    var savedMesh = SaveMeshAsset(finalMesh, safeName);

                    meshFilter.sharedMesh = savedMesh != null ? savedMesh : finalMesh;

                    // Drop the prior Generated asset so the folder does not keep
                    // stacking Name_EditMesh / Name_EditMesh 1 / … on every exit.
                    if (previousMesh != null &&
                        previousMesh != meshFilter.sharedMesh &&
                        IsGeneratedMeshAsset(previousMesh))
                    {
                        DeleteMeshAsset(previousMesh);
                    }

                    EditorUtility.SetDirty(meshFilter);
                }
                else if (ep.OriginalMesh != null)
                {
                    meshFilter.sharedMesh = ep.OriginalMesh;
                }
            }

            ep.isEditing = false;
            ep.SetData(null);
            _previewMesh = null;
            _previewData = null;
            _hiddenTriangles = null;
            IndieEditMeshGeometryCache.Invalidate();

            if (Active == ep)
                Active = null;

            IndieEditMeshUndo.Clear();
            IndieEditMeshTransformTool.ResetDrag();
            IndieEditMeshSelection.ResetMarquee();
            IndieEditMeshCutTool.Deactivate();
            IndieEditMeshAttachTool.Deactivate();
            IndieEditMeshPointToPoint.Deactivate();
            IndieEditMeshTransformTypeInWindow.CloseIfOpen();

            Tools.hidden = _previousToolsHidden;
            Selection.objects = _previousSelection != null ? _previousSelection : new Object[0];

            IndieEditMeshViewFrame.RestoreFlySpeed();
            SceneView.RepaintAll();
        }

        public static void RefreshMesh(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return;

            IndieEditMeshEditProxy.Cancel(ep);

            var meshFilter = ep.GetMeshFilter();

            if (meshFilter == null)
                return;

            DestroyPreviewMesh(meshFilter, ep);
            IndieEditMeshGeometryCache.Invalidate();
            _hiddenTriangles = null;

            var mesh = IndieEditableMeshBuilder.BuildMesh(
                ep.Data,
                PreviewSourceVertices,
                PreviewFaceSlots,
                null,
                null
            );
            mesh.name = "Indie EditMesh Preview";
            mesh.hideFlags = HideFlags.HideAndDontSave;

            meshFilter.sharedMesh = mesh;
            _previewMesh = mesh;
            _previewData = ep.Data;
            _previewVertexCount = ep.Data.vertices.Count;
            _previewFaceCount = ep.Data.faces.Count;

            EditorUtility.SetDirty(ep);
        }

        /// <summary>
        /// Drag path: write moved positions into the live preview mesh. Normals and
        /// tangents stay stale until the next <see cref="RefreshMesh"/> (end of drag).
        /// Falls back to a full rebuild when topology no longer matches the preview.
        /// </summary>
        public static void RefreshMeshPositions(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return;

            IndieEditMeshEditProxy.Cancel(ep);

            var data = ep.Data;

            if (!IsPreviewCurrent(ep))
            {
                RefreshMesh(ep);
                return;
            }

            IndieEditMeshGeometryCache.Invalidate();

            PositionBuffer.Clear();

            for (int i = 0; i < PreviewSourceVertices.Count; i++)
                PositionBuffer.Add(data.vertices[PreviewSourceVertices[i]]);

            _previewMesh.SetVertices(PositionBuffer);
            _previewMesh.RecalculateBounds();
        }

        /// <summary>
        /// Drops the triangles of <paramref name="hiddenFaces"/> from the live preview mesh
        /// without rebuilding it. <see cref="RestoreHiddenFaces"/> puts them back;
        /// the next <see cref="RefreshMesh"/> rebuilds everything anyway.
        /// </summary>
        public static bool HideFaces(IndieEditMeshComponent ep, bool[] hiddenFaces)
        {
            if (ep == null || ep.Data == null || hiddenFaces == null)
                return false;

            RestoreHiddenFaces(ep);

            if (!IsPreviewCurrent(ep))
                RefreshMesh(ep);

            if (!IsPreviewCurrent(ep) || PreviewFaceSlots.Count != ep.Data.faces.Count)
                return false;

            int subMeshCount = _previewMesh.subMeshCount;
            var original = new int[subMeshCount][];
            var drop = new bool[subMeshCount][];

            for (int s = 0; s < subMeshCount; s++)
            {
                original[s] = _previewMesh.GetTriangles(s);
                drop[s] = new bool[original[s].Length / 3];
            }

            int count = Mathf.Min(hiddenFaces.Length, PreviewFaceSlots.Count);

            for (int i = 0; i < count; i++)
            {
                if (!hiddenFaces[i])
                    continue;

                var slot = PreviewFaceSlots[i];

                if (slot.x < 0 || slot.x >= subMeshCount || slot.y < 0 || slot.y >= drop[slot.x].Length)
                    continue;

                drop[slot.x][slot.y] = true;
            }

            var kept = new List<int>();

            for (int s = 0; s < subMeshCount; s++)
            {
                kept.Clear();

                for (int t = 0; t < drop[s].Length; t++)
                {
                    if (drop[s][t])
                        continue;

                    kept.Add(original[s][t * 3]);
                    kept.Add(original[s][t * 3 + 1]);
                    kept.Add(original[s][t * 3 + 2]);
                }

                _previewMesh.SetTriangles(kept, s, false);
            }

            _hiddenTriangles = original;
            return true;
        }

        public static void RestoreHiddenFaces(IndieEditMeshComponent ep)
        {
            if (_hiddenTriangles == null)
                return;

            var meshFilter = ep != null ? ep.GetMeshFilter() : null;

            if (_previewMesh != null &&
                meshFilter != null &&
                meshFilter.sharedMesh == _previewMesh &&
                _previewMesh.subMeshCount == _hiddenTriangles.Length)
            {
                for (int s = 0; s < _hiddenTriangles.Length; s++)
                    _previewMesh.SetTriangles(_hiddenTriangles[s], s, false);
            }

            _hiddenTriangles = null;
        }

        private static bool IsPreviewCurrent(IndieEditMeshComponent ep)
        {
            var data = ep.Data;
            var meshFilter = ep.GetMeshFilter();

            return meshFilter != null &&
                   _previewMesh != null &&
                   meshFilter.sharedMesh == _previewMesh &&
                   _previewData == data &&
                   _previewVertexCount == data.vertices.Count &&
                   _previewFaceCount == data.faces.Count &&
                   PreviewSourceVertices.Count == _previewMesh.vertexCount;
        }

        private static readonly List<int> PreviewSourceVertices = new List<int>();
        private static readonly List<Vector2Int> PreviewFaceSlots = new List<Vector2Int>();
        private static readonly List<Vector3> PositionBuffer = new List<Vector3>();
        private static int[][] _hiddenTriangles;

        private static Mesh _previewMesh;
        private static IndieEditableMeshData _previewData;
        private static int _previewVertexCount;
        private static int _previewFaceCount;

        private static void DestroyPreviewMesh(MeshFilter meshFilter, IndieEditMeshComponent ep)
        {
            if (meshFilter == null)
                return;

            var oldMesh = meshFilter.sharedMesh;

            if (oldMesh == null)
                return;

            if (oldMesh == ep.OriginalMesh)
                return;

            if ((oldMesh.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                if (oldMesh == _previewMesh)
                {
                    _previewMesh = null;
                    _previewData = null;
                }

                Object.DestroyImmediate(oldMesh);
            }
        }

        private const string GeneratedFolder = "Assets/Indie 3D Editor/Generated";

        private static Mesh SaveMeshAsset(Mesh mesh, string objectName)
        {
            if (mesh == null)
                return null;

            string rootFolder = "Assets/Indie 3D Editor";

            if (!AssetDatabase.IsValidFolder(rootFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Indie 3D Editor");
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.CreateFolder(rootFolder, "Generated");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedFolder}/{objectName}_EditMesh.asset"
            );

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

        private static void DeleteMeshAsset(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(path))
                return;

            AssetDatabase.DeleteAsset(path);
        }

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "EditMesh";

            foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidChar, '_');
            }

            return name;
        }
    }
}