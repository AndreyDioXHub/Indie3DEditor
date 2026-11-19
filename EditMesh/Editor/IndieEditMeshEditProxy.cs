using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Live polygon edit on a small piece of the mesh. The selection region is drawn
    /// by a hidden child renderer; its triangles are dropped from the main preview
    /// mesh, which is not rebuilt until <see cref="Commit"/>. ep.Data stays untouched
    /// while the proxy is active.
    /// </summary>
    public static class IndieEditMeshEditProxy
    {
        private const string ProxyObjectName = "Indie EditMesh Proxy";
        private const string GhostShaderName = "Hidden/Indie3DEditor/EditMeshProxyGhost";

        /// <summary>Up to this many region faces a drag frame rebuilds normals too.</summary>
        private const int FullRefreshFaceLimit = 4096;

        private static IndieEditMeshComponent _component;
        private static IndieEditableMeshData _full;
        private static IndieEditMeshFaceRegion _region;
        private static IndieEditableMeshData _live;
        private static bool _useSmoothingGroups;

        private static GameObject _object;
        private static MeshFilter _objectFilter;
        private static MeshRenderer _objectRenderer;
        private static Material[] _sourceMaterials;
        private static Mesh _mesh;
        private static int _meshVertexCount;
        private static int _meshFaceCount;
        private static Material _ghostMaterial;

        private static readonly List<int> SourceVertices = new List<int>();
        private static readonly List<Vector3> PositionBuffer = new List<Vector3>();

        public static bool IsActive => _component != null;

        public static bool IsActiveFor(IndieEditMeshComponent ep)
        {
            return ep != null && _component == ep;
        }

        /// <summary>Data being edited: the region while active, otherwise ep.Data.</summary>
        public static IndieEditableMeshData DataFor(IndieEditMeshComponent ep)
        {
            if (ep == null)
                return null;

            return IsActiveFor(ep) && _live != null ? _live : ep.Data;
        }

        public static bool Begin(IndieEditMeshComponent ep)
        {
            return Begin(ep, IndieEditMeshFaceRegionSpan.SharedVertex);
        }

        public static bool Begin(IndieEditMeshComponent ep, IndieEditMeshFaceRegionSpan span)
        {
            Cancel();

            if (ep == null || ep.Data == null || IndieEditMeshSession.Active != ep)
                return false;

            var renderer = ep.GetComponent<MeshRenderer>();

            if (renderer == null || ep.GetMeshFilter() == null)
                return false;

            var region = IndieEditMeshFaceRegions.Extract(ep.Data, span);

            if (region == null)
                return false;

            if (!IndieEditMeshSession.HideFaces(ep, region.FullLiveFaces))
                return false;

            _component = ep;
            _full = ep.Data;
            _region = region;
            _live = region.Data.Clone();
            _useSmoothingGroups = IndieEditableMeshBuilder.HasAnySmoothingGroup(ep.Data);

            CreateObject(ep, renderer);
            RefreshMesh();
            return true;
        }

        /// <summary>Fresh copy of the region as it was at <see cref="Begin"/>.</summary>
        public static IndieEditableMeshData ResetLive()
        {
            if (!IsActive)
                return null;

            _live = _region.Data.Clone();
            return _live;
        }

        /// <summary>Fresh copy of the region with <paramref name="op"/> applied the way the region expects.</summary>
        public static void RunOnLive(System.Action<IndieEditableMeshData> op)
        {
            if (!IsActive)
                return;

            IndieEditMeshFaceRegions.Run(_region, ResetLive(), op);
        }

        public static void RefreshMesh()
        {
            if (!IsActive || _live == null)
                return;

            var mesh = IndieEditableMeshBuilder.BuildMesh(
                _live,
                SourceVertices,
                null,
                _region.ContextFaces,
                _useSmoothingGroups
            );
            mesh.name = ProxyObjectName;
            mesh.hideFlags = HideFlags.HideAndDontSave;

            if (_mesh != null)
                Object.DestroyImmediate(_mesh);

            _mesh = mesh;
            _meshVertexCount = _live.vertices.Count;
            _meshFaceCount = _live.faces.Count;

            if (_objectFilter != null)
                _objectFilter.sharedMesh = mesh;

            // Extra materials would draw the last submesh again.
            if (_objectRenderer != null && _sourceMaterials != null)
            {
                int count = Mathf.Min(_sourceMaterials.Length, mesh.subMeshCount);

                if (_objectRenderer.sharedMaterials.Length != count)
                {
                    var materials = new Material[count];
                    System.Array.Copy(_sourceMaterials, materials, count);
                    _objectRenderer.sharedMaterials = materials;
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>Drag path: positions only on large regions, full region rebuild on small ones.</summary>
        public static void RefreshPositions()
        {
            if (!IsActive || _live == null)
                return;

            if (_mesh == null ||
                _live.faces.Count <= FullRefreshFaceLimit ||
                _meshVertexCount != _live.vertices.Count ||
                _meshFaceCount != _live.faces.Count ||
                SourceVertices.Count != _mesh.vertexCount)
            {
                RefreshMesh();
                return;
            }

            PositionBuffer.Clear();

            for (int i = 0; i < SourceVertices.Count; i++)
                PositionBuffer.Add(_live.vertices[SourceVertices[i]]);

            _mesh.SetVertices(PositionBuffer);
            _mesh.RecalculateBounds();
        }

        /// <summary>Merges the region into ep.Data and rebuilds the full mesh once.</summary>
        public static bool Commit()
        {
            if (!IsActive)
                return false;

            var ep = _component;
            var region = _region;
            var live = _live;
            bool sameData = ep.Data == _full;

            Teardown(restoreFaces: !sameData);

            if (!sameData || live == null)
                return false;

            IndieEditMeshFaceRegions.Merge(region, live, ep.Data);
            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
            return true;
        }

        /// <summary>Drops the region and shows the untouched mesh again, no rebuild.</summary>
        public static void Cancel()
        {
            if (!IsActive)
                return;

            Teardown(restoreFaces: true);
            SceneView.RepaintAll();
        }

        public static void Cancel(IndieEditMeshComponent ep)
        {
            if (IsActiveFor(ep))
                Cancel();
        }

        /// <summary>
        /// Region parts hidden under the rest of the mesh, as a dim tint on top.
        /// Call on Repaint inside the Scene view.
        /// </summary>
        public static void DrawGhost(IndieEditMeshComponent ep)
        {
            if (!IsActiveFor(ep) || _mesh == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (!EnsureGhostMaterial() || !_ghostMaterial.SetPass(0))
                return;

            Matrix4x4 matrix = ep.transform.localToWorldMatrix;

            for (int s = 0; s < _mesh.subMeshCount; s++)
                Graphics.DrawMeshNow(_mesh, matrix, s);
        }

        private static void CreateObject(IndieEditMeshComponent ep, MeshRenderer source)
        {
            DestroyStaleObjects(ep);

            _object = new GameObject(ProxyObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = ep.gameObject.layer
            };

            // Child of the edited object: same transform, and the selection outline
            // covers the region like the rest of the mesh.
            _object.transform.SetParent(ep.transform, false);
            _objectFilter = _object.AddComponent<MeshFilter>();

            _sourceMaterials = source.sharedMaterials;
            _objectRenderer = _object.AddComponent<MeshRenderer>();
            _objectRenderer.sharedMaterials = _sourceMaterials;
            _objectRenderer.shadowCastingMode = source.shadowCastingMode;
            _objectRenderer.receiveShadows = source.receiveShadows;

            SceneVisibilityManager.instance.DisablePicking(_object, false);
        }

        private static void DestroyStaleObjects(IndieEditMeshComponent ep)
        {
            var transform = ep.transform;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;

                if (child.name == ProxyObjectName && (child.hideFlags & HideFlags.DontSave) != 0)
                    Object.DestroyImmediate(child);
            }
        }

        private static void Teardown(bool restoreFaces)
        {
            if (restoreFaces && _component != null)
                IndieEditMeshSession.RestoreHiddenFaces(_component);

            if (_object != null)
                Object.DestroyImmediate(_object);

            if (_mesh != null)
                Object.DestroyImmediate(_mesh);

            _object = null;
            _objectFilter = null;
            _objectRenderer = null;
            _sourceMaterials = null;
            _mesh = null;
            _component = null;
            _full = null;
            _region = null;
            _live = null;
            _meshVertexCount = 0;
            _meshFaceCount = 0;
            SourceVertices.Clear();
        }

        private static bool EnsureGhostMaterial()
        {
            if (_ghostMaterial != null)
                return true;

            var shader = Shader.Find(GhostShaderName);

            if (shader == null)
                return false;

            _ghostMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
