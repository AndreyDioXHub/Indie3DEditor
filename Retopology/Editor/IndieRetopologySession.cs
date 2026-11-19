using System.Collections.Generic;
using System.IO;
using Indie3DEditor.EditMesh;
using Indie3DEditor.EditUV;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologySession
    {
        private const string GeneratedFolder = "Assets/Indie 3D Editor/Generated";

        public static IndieRetopologyComponent Active { get; private set; }

        private static string FlatMaterialPath =>
            Indie3DEditorPackagePaths.Material("Retopology/Materials/RetopoFlat.mat");

        private static IndieRetopologyComponent _pendingExit;
        private static bool _pendingApply;
        private static bool _pendingRecover;
        private static bool _editorStateCaptured;
        private static bool _previousToolsHidden;
        private static Object[] _previousSelection;
        private static Mesh _previewMesh;
        private static Material[] _savedMaterials;
        private static IndieRetopologyComponent _materialOwner;
        private static bool _rendererStateSaved;
        private static ShadowCastingMode _savedShadows;
        private static bool _savedReceiveShadows;
        private static LightProbeUsage _savedLightProbes;

        public static Material FlatMaterial =>
            AssetDatabase.LoadAssetAtPath<Material>(FlatMaterialPath);

        public static void BeginEditing(IndieRetopologyComponent retopo)
        {
            if (retopo == null)
                return;

            if (IndieEditUVSession.Active != null)
                IndieEditUVSession.EndEditing(IndieEditUVSession.Active, true);

            if (IndieEditMeshSession.Active != null)
                IndieEditMeshSession.EndEditing(IndieEditMeshSession.Active, true);

            if (Active != null && Active != retopo)
                EndEditing(Active, true);

            if (Active == retopo && retopo.isEditing)
                return;

            retopo.EnsureData();

            var meshFilter = retopo.GetMeshFilter();
            Mesh current = meshFilter != null ? meshFilter.sharedMesh : null;

            if (IsWritableGeneratedMesh(retopo, current) &&
                !IndieConvertToRetopologySetup.IsEditPolyGeneratedMesh(current))
                retopo.SetOutputMesh(current);

            AdoptFilterMesh(retopo, current);

            retopo.isEditing = true;
            Active = retopo;

            IndieRetopologyUndo.Clear();
            IndieRetopologyDrag.Reset();
            IndieRetopologyPick.ClearHover();
            ApplyFlatMaterial(retopo);
            RefreshMesh(retopo);
            CaptureEditorState();

            Selection.objects = new Object[]
            {
                retopo.gameObject
            };

            Tools.hidden = true;
            UnityEditor.Undo.ClearAll();
            EditorUtility.SetDirty(retopo);
            SceneView.RepaintAll();
        }

        public static void EndEditing(IndieRetopologyComponent retopo, bool applyChanges)
        {
            if (retopo == null)
                return;

            if (IndieRetopologyCreatePolygon.IsActive)
                IndieRetopologyCreatePolygon.Cancel();
            else if (IndieRetopologyDrag.IsDragging)
                IndieRetopologyDrag.HandleMouseUp(retopo);
            else
                IndieRetopologyDrag.Reset();

            IndieRetopologyManager.ClearEditPointer();
            IndieRetopologyConnectWindow.Dismiss();
            IndieRetopologyAttach.Deactivate();

            bool wasActive = Active == retopo;
            retopo.isEditing = false;

            if (wasActive)
                Active = null;

            IndieRetopologyPick.ClearHover();
            IndieRetopologyPick.InvalidateCache();
            IndieRetopologyUndo.Clear();

            if (wasActive)
            {
                IndieEditMeshViewFrame.RestoreFlySpeed();
                RestoreEditorState();
            }

            RestoreFlatMaterial(retopo);
            EditorUtility.SetDirty(retopo);

            if (applyChanges)
                Bake(retopo);
            else
                DiscardPreview(retopo);

            SceneView.RepaintAll();
        }

        public static void RequestEndEditing(IndieRetopologyComponent retopo, bool applyChanges)
        {
            if (retopo == null)
                return;

            _pendingExit = retopo;
            _pendingApply = applyChanges;
            _pendingRecover = false;
            EditorApplication.update -= ApplyPendingExit;
            EditorApplication.update += ApplyPendingExit;
        }

        public static void RequestRecover(IndieRetopologyComponent retopo)
        {
            if (retopo == null)
                return;

            _pendingExit = retopo;
            _pendingRecover = true;
            EditorApplication.update -= ApplyPendingExit;
            EditorApplication.update += ApplyPendingExit;
        }

        private static void ApplyPendingExit()
        {
            EditorApplication.update -= ApplyPendingExit;
            IndieRetopologyComponent retopo = _pendingExit;
            bool applyChanges = _pendingApply;
            bool recover = _pendingRecover;
            _pendingExit = null;
            _pendingRecover = false;

            if (retopo == null)
                return;

            if (recover)
                Recover(retopo);
            else
                EndEditing(retopo, applyChanges);
        }

        public static void Recover(IndieRetopologyComponent retopo)
        {
            if (retopo == null)
                return;

            retopo.EnsureData();

            if (IndieRetopologyCreatePolygon.IsActive)
                IndieRetopologyCreatePolygon.Cancel();
            else
                IndieRetopologyDrag.Reset();

            IndieRetopologyManager.ClearEditPointer();
            retopo.isEditing = false;

            bool recoverWasActive = Active == retopo;

            if (recoverWasActive)
                Active = null;

            Bake(retopo);
            IndieRetopologyUndo.Clear();
            IndieRetopologyPick.ClearHover();
            IndieEditMeshViewFrame.RestoreFlySpeed();
            IndieRetopologyConnectWindow.Dismiss();
            IndieRetopologyAttach.Deactivate();
            RestoreFlatMaterial(retopo);

            if (recoverWasActive || Active == null)
                RestoreEditorState();

            EditorUtility.SetDirty(retopo);
            SceneView.RepaintAll();
        }

        private static void CaptureEditorState()
        {
            if (_editorStateCaptured)
                return;

            _previousToolsHidden = Tools.hidden;
            _previousSelection = Selection.objects;
            _editorStateCaptured = true;
        }

        private static void RestoreEditorState()
        {
            if (!_editorStateCaptured)
                return;

            _editorStateCaptured = false;
            Tools.hidden = _previousToolsHidden;
            Selection.objects = _previousSelection != null ? _previousSelection : new Object[0];
            _previousSelection = null;
        }

        private static void AdoptFilterMesh(IndieRetopologyComponent retopo, Mesh current)
        {
            if (retopo == null || current == null || current == _previewMesh || current == retopo.TargetMesh)
                return;

            retopo.EnsureData();

            if (retopo.Data.faces != null && retopo.Data.faces.Count > 0)
                return;

            IndieEditableMeshData imported;

            try
            {
                imported = IndieEditableMeshImporter.Import(current);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Indie Retopology: Cannot convert mesh. {ex.Message}");
                return;
            }

            if (imported.vertices == null || imported.vertices.Count == 0)
                return;

            retopo.SetData(IndieConvertToRetopologySetup.CopyTriangles(imported));
        }

        public static void RefreshMesh(IndieRetopologyComponent retopo)
        {
            if (retopo == null)
                return;

            retopo.EnsureData();

            var meshFilter = retopo.GetMeshFilter();

            if (meshFilter == null)
                return;

            EnsurePreviewMesh();
            IndieRetopologyMeshBuilder.FillPreview(_previewMesh, retopo.Data);
            _previewMesh.name = IndieRetopologyMeshBuilder.PreviewMeshName;

            if (meshFilter.sharedMesh != _previewMesh)
                meshFilter.sharedMesh = _previewMesh;
        }

        public static void EnsurePreview(IndieRetopologyComponent retopo)
        {
            if (!IsPreviewShown(retopo))
                RefreshMesh(retopo);
        }

        public static void RefreshPreviewPositions(IndieRetopologyComponent retopo, List<int> faces)
        {
            if (!IsPreviewShown(retopo) ||
                !IndieRetopologyMeshBuilder.WritePreviewPositions(_previewMesh, retopo.Data, faces))
            {
                RefreshMesh(retopo);
            }
        }

        private static bool IsPreviewShown(IndieRetopologyComponent retopo)
        {
            if (retopo == null || _previewMesh == null)
                return false;

            var meshFilter = retopo.GetMeshFilter();

            return meshFilter != null &&
                meshFilter.sharedMesh == _previewMesh &&
                IndieRetopologyMeshBuilder.IsPreviewCurrent(_previewMesh, retopo.Data);
        }

        private static void Bake(IndieRetopologyComponent retopo)
        {
            var meshFilter = retopo.GetMeshFilter();

            if (meshFilter == null)
                return;

            retopo.EnsureData();

            Mesh onFilter = meshFilter.sharedMesh;

            if (onFilter == _previewMesh)
                onFilter = null;

            ReleasePreview(meshFilter);

            string safeName = SanitizeAssetName(retopo.gameObject.name);
            Mesh saved = SaveMeshAsset(retopo, safeName, onFilter);

            meshFilter.sharedMesh = null;
            meshFilter.sharedMesh = saved;

            if (saved != null)
                retopo.SetOutputMesh(saved);

            EditorUtility.SetDirty(meshFilter);
        }

        private static void DiscardPreview(IndieRetopologyComponent retopo)
        {
            var meshFilter = retopo.GetMeshFilter();
            ReleasePreview(meshFilter);

            if (meshFilter != null && retopo.OutputMesh != null)
                meshFilter.sharedMesh = retopo.OutputMesh;
        }

        private static void ApplyFlatMaterial(IndieRetopologyComponent retopo)
        {
            if (retopo == null || _materialOwner == retopo)
                return;

            RestoreFlatMaterial(_materialOwner);

            var renderer = retopo.GetComponent<MeshRenderer>();
            Material flat = FlatMaterial;

            if (renderer == null || flat == null)
                return;

            _savedMaterials = renderer.sharedMaterials;
            _materialOwner = retopo;
            _savedShadows = renderer.shadowCastingMode;
            _savedReceiveShadows = renderer.receiveShadows;
            _savedLightProbes = renderer.lightProbeUsage;
            _rendererStateSaved = true;
            renderer.sharedMaterial = flat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        private static void RestoreFlatMaterial(IndieRetopologyComponent retopo)
        {
            if (retopo == null || _materialOwner != retopo)
                return;

            var renderer = retopo.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                if (_savedMaterials != null)
                    renderer.sharedMaterials = _savedMaterials;

                if (_rendererStateSaved)
                {
                    renderer.shadowCastingMode = _savedShadows;
                    renderer.receiveShadows = _savedReceiveShadows;
                    renderer.lightProbeUsage = _savedLightProbes;
                }
            }

            _savedMaterials = null;
            _materialOwner = null;
            _rendererStateSaved = false;
        }

        private static void EnsurePreviewMesh()
        {
            if (_previewMesh != null)
                return;

            _previewMesh = new Mesh
            {
                name = IndieRetopologyMeshBuilder.PreviewMeshName,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void ReleasePreview(MeshFilter meshFilter)
        {
            if (_previewMesh == null)
                return;

            if (meshFilter != null && meshFilter.sharedMesh == _previewMesh)
                meshFilter.sharedMesh = null;

            Object.DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        private static Mesh SaveMeshAsset(IndieRetopologyComponent retopo, string objectName, Mesh filterMesh)
        {
            Mesh existing = FindMeshToOverwrite(retopo, objectName, filterMesh);

            if (existing != null)
            {
                string keptName = existing.name;
                IndieRetopologyMeshBuilder.Fill(existing, retopo.Data);
                existing.name = keptName;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            const string rootFolder = "Assets/Indie 3D Editor";

            if (!AssetDatabase.IsValidFolder(rootFolder))
                AssetDatabase.CreateFolder("Assets", "Indie 3D Editor");

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder(rootFolder, "Generated");

            Mesh mesh = IndieRetopologyMeshBuilder.Build(retopo.Data);
            mesh.name = objectName + "_Retopo";
            string path = $"{GeneratedFolder}/{objectName}_Retopo.asset";
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static Mesh FindMeshToOverwrite(IndieRetopologyComponent retopo, string objectName, Mesh filterMesh)
        {
            if (IsWritableGeneratedMesh(retopo, retopo.OutputMesh))
                return retopo.OutputMesh;

            if (IsWritableGeneratedMesh(retopo, filterMesh))
                return filterMesh;

            string path = $"{GeneratedFolder}/{objectName}_Retopo.asset";
            Mesh atPath = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            return IsWritableGeneratedMesh(retopo, atPath) ? atPath : null;
        }

        private static bool IsWritableGeneratedMesh(IndieRetopologyComponent retopo, Mesh mesh)
        {
            if (mesh == null || (retopo != null && mesh == retopo.TargetMesh))
                return false;

            string path = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');

            if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                return false;

            return path.StartsWith(GeneratedFolder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Retopo";

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                name = name.Replace(invalidChar, '_');

            return name;
        }
    }
}
