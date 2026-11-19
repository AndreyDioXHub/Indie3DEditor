using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// GPU canvas: cached static base RT + dynamic neighborhood overlay.
    /// Full composite only when dirty; otherwise returns last final RT.
    /// </summary>
    public sealed class IndieEditUVCanvasGpu
    {
        private const string ShaderName = "Hidden/Indie3DEditor/EditUVCanvas";

        private static readonly Color BgColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color FaceColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color FaceOverTexture = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color FaceEdgeColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color SeamColor = new Color(0.15f, 0.95f, 0.25f, 0.85f);
        private static readonly Color WorkingSeamColor = new Color(0.2f, 0.85f, 1f, 0.95f);
        private static readonly Color VertColor = new Color(0.2f, 0.95f, 0.35f, 0.95f);
        private static readonly Color SelectColor = new Color(1f, 0.2f, 0.2f, 0.95f);
        private static readonly Color SelectFaceFill = new Color(1f, 0.2f, 0.2f, 0.28f);
        private static readonly Color OutsideUvDim = new Color(0f, 0f, 0f, 0.3f);
        private static readonly Color GridColor = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color UnitBorderColor = new Color(0.08f, 0.2f, 0.55f, 1f);
        private static readonly Color AxisUColor = new Color(0.9f, 0.25f, 0.25f, 1f);
        private static readonly Color AxisVColor = new Color(0.25f, 0.85f, 0.35f, 1f);

        private RenderTexture _rtBase;
        private RenderTexture _rtFinal;
        private Material _material;

        private Mesh _staticFaces;
        private Mesh _staticEdges;
        private Mesh _staticSeams;
        private Mesh _dynFaces;
        private Mesh _dynEdges;
        private Mesh _dynPoints;
        private Mesh _dynSelectFaces;
        private Mesh _dynSelectEdges;
        private Mesh _dynSelectPoints;

        private readonly IndieEditUVCanvasNeighborhood _neighborhood = new IndieEditUVCanvasNeighborhood();

        // Static layer keeps a fixed slot per face (3 tri verts, 6 edge verts);
        // faces in the dynamic neighborhood are parked off-canvas instead of removed.
        private static readonly Vector3 HiddenSlot = new Vector3(-1e5f, -1e5f, 0f);
        private readonly List<Vector3> _staticFaceSlots = new List<Vector3>();
        private readonly List<Vector3> _staticEdgeSlots = new List<Vector3>();
        private readonly HashSet<int> _slotHiddenFaces = new HashSet<int>();
        private readonly List<int> _slotScratch = new List<int>();

        private struct SeamSegment
        {
            public int V0;
            public int V1;
            public int BaseVertex;
        }

        private const float SeamOffsetEps = 0.0004f;
        private readonly List<Vector3> _seamVerts = new List<Vector3>();
        private readonly List<SeamSegment> _seamSegments = new List<SeamSegment>();
        private readonly Dictionary<int, List<int>> _seamSegmentsByVert = new Dictionary<int, List<int>>();

        private bool _geometryDirty = true;
        private bool _selectionDirty = true;
        private bool _seamsDirty = true;
        private bool _appearanceDirty = true;
        private bool _viewDirty = true;
        private bool _dynamicUvDirty = true;
        private bool _baseDirty = true;
        private bool _finalDirty = true;

        private IndieEditableMeshData _boundData;
        private bool _hasPreviewTexture;
        private Vector2 _lastPan;
        private float _lastZoom = -1f;
        private int _lastWidth;
        private int _lastHeight;

        public RenderTexture Texture => _rtFinal;

        public bool NeedsRedraw =>
            _geometryDirty ||
            _selectionDirty ||
            _seamsDirty ||
            _appearanceDirty ||
            _viewDirty ||
            _dynamicUvDirty ||
            _baseDirty ||
            _finalDirty ||
            _rtFinal == null;

        public void MarkGeometryDirty()
        {
            _geometryDirty = true;
            _selectionDirty = true;
            _seamsDirty = true;
            _dynamicUvDirty = true;
            _baseDirty = true;
            _finalDirty = true;
            _neighborhood.InvalidateAdjacency();
        }

        /// <summary>
        /// Base RT is redrawn only if the neighborhood partition actually changes (see Render).
        /// </summary>
        public void MarkSelectionDirty()
        {
            _selectionDirty = true;
            _finalDirty = true;
        }

        /// <summary>Seams live on the final overlay — base RT stays.</summary>
        public void MarkSeamsDirty()
        {
            _seamsDirty = true;
            _finalDirty = true;
        }

        public void MarkAppearanceDirty()
        {
            _appearanceDirty = true;
            _baseDirty = true;
            _finalDirty = true;
        }

        public void MarkViewDirty()
        {
            _viewDirty = true;
            _baseDirty = true;
            _finalDirty = true;
        }

        /// <summary>
        /// UV positions of dynamic neighborhood changed (gizmo drag).
        /// Falls back to full geometry dirty if affected verts leave neighborhood.
        /// </summary>
        public void MarkDynamicUvDirty(IndieEditableMeshData data, IEnumerable<int> affectedVerts)
        {
            if (data == null || affectedVerts == null)
            {
                MarkGeometryDirty();
                return;
            }

            // Pending neighborhood rebuild will pick up current UVs anyway.
            if (_geometryDirty || _selectionDirty)
            {
                _dynamicUvDirty = true;
                _finalDirty = true;
                return;
            }

            _neighborhood.EnsureAdjacency(data);

            if (!_neighborhood.ContainsAllVerts(affectedVerts))
            {
                MarkGeometryDirty();
                return;
            }

            _dynamicUvDirty = true;
            _finalDirty = true;
        }

        public void MarkDynamicUvDirty()
        {
            _dynamicUvDirty = true;
            _finalDirty = true;
        }

        public void Dispose()
        {
            DestroyRt(ref _rtBase);
            DestroyRt(ref _rtFinal);
            DestroyMesh(ref _staticFaces);
            DestroyMesh(ref _staticEdges);
            DestroyMesh(ref _staticSeams);
            DestroyMesh(ref _dynFaces);
            DestroyMesh(ref _dynEdges);
            DestroyMesh(ref _dynPoints);
            DestroyMesh(ref _dynSelectFaces);
            DestroyMesh(ref _dynSelectEdges);
            DestroyMesh(ref _dynSelectPoints);

            if (_material != null)
            {
                Object.DestroyImmediate(_material);
                _material = null;
            }

            _boundData = null;
            _neighborhood.InvalidateAdjacency();
        }

        public RenderTexture Render(
            IndieEditableMeshData data,
            IndieEditUVView view,
            Rect canvasRect,
            Vector2 mouseGui,
            bool hasPreviewTexture,
            Texture2D previewTexture,
            bool force = false
        )
        {
            if (data == null || view == null)
                return _rtFinal;

            int width = Mathf.Max(8, Mathf.CeilToInt(canvasRect.width));
            int height = Mathf.Max(8, Mathf.CeilToInt(canvasRect.height));

            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                MarkViewDirty();
            }

            if (view.Pan != _lastPan || !Mathf.Approximately(view.Zoom, _lastZoom))
            {
                _lastPan = view.Pan;
                _lastZoom = view.Zoom;
                MarkViewDirty();
            }

            EnsureTargets(width, height);
            EnsureMaterial();

            if (_material == null || _rtFinal == null)
                return null;

            if (_boundData != data)
            {
                _boundData = data;
                MarkGeometryDirty();
            }

            if (_hasPreviewTexture != hasPreviewTexture)
            {
                _hasPreviewTexture = hasPreviewTexture;
                MarkAppearanceDirty();
                _geometryDirty = true;
            }

            if (!force && !NeedsRedraw)
                return _rtFinal;

            bool rebuildSeams = _seamsDirty || _geometryDirty;

            if (_geometryDirty)
            {
                _neighborhood.EnsureAdjacency(data);
                _neighborhood.RebuildFromSelection(data);
                RebuildStaticAndDynamicMeshes(data, hasPreviewTexture);
                _geometryDirty = false;
                _selectionDirty = false;
                _dynamicUvDirty = false;
                _baseDirty = true;
                _finalDirty = true;
            }
            else if (_selectionDirty)
            {
                // UVs moved under the old neighborhood in the same frame: seam patch
                // by the new DynVerts could miss them.
                if (_dynamicUvDirty)
                    rebuildSeams = true;

                _neighborhood.EnsureAdjacency(data);
                _neighborhood.RebuildFromSelection(data);

                if (PatchStaticSlots(data))
                    _baseDirty = true;

                UpdateDynamicUvPositions(data);
                _selectionDirty = false;
                _dynamicUvDirty = false;
                _finalDirty = true;
            }
            else if (_dynamicUvDirty)
            {
                UpdateDynamicUvPositions(data);

                if (!rebuildSeams)
                    PatchSeamPositions(data);

                _dynamicUvDirty = false;
                _finalDirty = true;
            }

            if (rebuildSeams)
            {
                RebuildSeamsOverlay(data);
                _seamsDirty = false;
                _finalDirty = true;
            }

            if (_appearanceDirty)
            {
                _appearanceDirty = false;
                _baseDirty = true;
                _finalDirty = true;
            }

            if (_viewDirty)
            {
                _viewDirty = false;
                _baseDirty = true;
                _finalDirty = true;
            }

            ApplySharedUniforms(view, width, height, mouseGui, hasPreviewTexture, previewTexture);

            if (_baseDirty)
            {
                DrawBase(width, height);
                _baseDirty = false;
                _finalDirty = true;
            }

            if (_finalDirty)
            {
                DrawFinal(width, height, mouseGui, view);
                _finalDirty = false;
            }

            return _rtFinal;
        }

        private void DrawBase(int width, int height)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _rtBase;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);
            GL.Clear(true, true, BgColor);

            DrawFullscreenBackground(width, height);
            DrawMesh(_staticFaces, 1);
            DrawMesh(_staticEdges, 2);

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private void DrawFinal(int width, int height, Vector2 mouseGui, IndieEditUVView view)
        {
            var prev = RenderTexture.active;
            Graphics.Blit(_rtBase, _rtFinal);

            RenderTexture.active = _rtFinal;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);

            DrawMesh(_dynFaces, 1);
            DrawMesh(_dynEdges, 2);
            DrawMesh(_staticSeams, 2);
            DrawMesh(_dynSelectFaces, 1);
            DrawMesh(_dynSelectEdges, 2);

            Vector2 mouseUv = view.GuiToUv(mouseGui);
            _material.SetVector("_MouseUv", new Vector4(mouseUv.x, mouseUv.y, 0f, 0f));

            _material.SetFloat("_PointSize", 5f);
            DrawMesh(_dynPoints, 3);
            _material.SetFloat("_PointSize", 7f);
            DrawMesh(_dynSelectPoints, 3);

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private void ApplySharedUniforms(
            IndieEditUVView view,
            int width,
            int height,
            Vector2 mouseGui,
            bool hasPreviewTexture,
            Texture2D previewTexture
        )
        {
            IndieEditUVMaterialUtil.GetCheckerAppearance(out Color checkerA, out Color checkerB, out float density);

            _material.SetColor("_BgColor", BgColor);
            _material.SetColor("_CheckerA", checkerA);
            _material.SetColor("_CheckerB", checkerB);
            _material.SetFloat("_CheckerDensity", density);
            _material.SetFloat("_UseTexture", hasPreviewTexture && previewTexture != null ? 1f : 0f);
            _material.SetColor("_OutsideDim", OutsideUvDim);
            _material.SetColor("_GridColor", GridColor);
            _material.SetColor("_UnitBorderColor", UnitBorderColor);
            _material.SetColor("_AxisUColor", AxisUColor);
            _material.SetColor("_AxisVColor", AxisVColor);
            _material.SetVector("_Pan", new Vector4(view.Pan.x, view.Pan.y, 0f, 0f));
            _material.SetFloat("_Zoom", view.Zoom);
            _material.SetVector("_CanvasSize", new Vector4(width, height, 0f, 0f));
            _material.SetFloat("_HoverRadiusPx", 8f);
            _material.SetColor("_HoverBoost", new Color(1f, 1f, 0.35f, 0.45f));

            if (hasPreviewTexture && previewTexture != null)
                _material.SetTexture("_MainTex", previewTexture);
            else
                _material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        private void DrawFullscreenBackground(int width, int height)
        {
            if (!_material.SetPass(0))
                return;

            GL.Begin(GL.QUADS);
            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(width, 0f, 0f);
            GL.Vertex3(width, height, 0f);
            GL.Vertex3(0f, height, 0f);
            GL.End();
        }

        private void DrawMesh(Mesh mesh, int pass)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return;

            if (_material.SetPass(pass))
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        private void EnsureTargets(int width, int height)
        {
            if (_rtBase != null && _rtBase.width == width && _rtBase.height == height &&
                _rtFinal != null && _rtFinal.width == width && _rtFinal.height == height)
            {
                return;
            }

            DestroyRt(ref _rtBase);
            DestroyRt(ref _rtFinal);

            _rtBase = CreateRt(width, height, "EditUVCanvas.Base");
            _rtFinal = CreateRt(width, height, "EditUVCanvas.Final");
            _baseDirty = true;
            _finalDirty = true;
        }

        private static RenderTexture CreateRt(int width, int height, string name)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            rt.Create();
            return rt;
        }

        private void EnsureMaterial()
        {
            if (_material != null)
                return;

            Shader shader = Shader.Find(ShaderName);

            if (shader == null)
            {
                Debug.LogError("Indie EditUV: shader not found: " + ShaderName);
                return;
            }

            _material = new Material(shader)
            {
                name = "EditUVCanvas (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>
        /// Full rebuild (topology / data swap / appearance): every face gets a static slot,
        /// neighborhood faces are parked; dynamic layer is built from DynFaces only.
        /// </summary>
        private void RebuildStaticAndDynamicMeshes(IndieEditableMeshData data, bool hasPreviewTexture)
        {
            Color fill = hasPreviewTexture ? FaceOverTexture : FaceColor;
            int faceCount = data.faces.Count;

            _staticFaceSlots.Clear();
            _staticEdgeSlots.Clear();
            _slotHiddenFaces.Clear();

            var faceColors = new List<Color>(faceCount * 3);
            var faceTris = new List<int>(faceCount * 3);
            var edgeColors = new List<Color>(faceCount * 6);
            var edgeIndices = new List<int>(faceCount * 6);

            for (int i = 0; i < faceCount; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    _staticFaceSlots.Add(HiddenSlot);
                    faceColors.Add(fill);
                    faceTris.Add(i * 3 + k);
                }

                for (int k = 0; k < 6; k++)
                {
                    _staticEdgeSlots.Add(HiddenSlot);
                    edgeColors.Add(FaceEdgeColor);
                    edgeIndices.Add(i * 6 + k);
                }

                if (_neighborhood.DynFaces.Contains(i))
                    _slotHiddenFaces.Add(i);
                else
                    WriteStaticSlot(data, i, true);
            }

            _staticFaces = AssignMesh(
                _staticFaces,
                "EditUV.StaticFaces",
                _staticFaceSlots,
                faceColors,
                faceTris,
                MeshTopology.Triangles
            );
            _staticEdges = AssignMesh(
                _staticEdges,
                "EditUV.StaticEdges",
                _staticEdgeSlots,
                edgeColors,
                edgeIndices,
                MeshTopology.Lines
            );

            UpdateDynamicUvPositions(data);
        }

        /// <summary>
        /// Selection moved the neighborhood: park faces that entered DynFaces,
        /// restore faces that left it. Returns true if the static layer changed.
        /// </summary>
        private bool PatchStaticSlots(IndieEditableMeshData data)
        {
            if (_staticFaces == null || _staticFaceSlots.Count != data.faces.Count * 3)
            {
                RebuildStaticAndDynamicMeshes(data, _hasPreviewTexture);
                return true;
            }

            HashSet<int> dynFaces = _neighborhood.DynFaces;
            bool changed = false;

            _slotScratch.Clear();

            foreach (int f in _slotHiddenFaces)
            {
                if (!dynFaces.Contains(f))
                    _slotScratch.Add(f);
            }

            for (int i = 0; i < _slotScratch.Count; i++)
            {
                int f = _slotScratch[i];
                _slotHiddenFaces.Remove(f);
                WriteStaticSlot(data, f, true);
                changed = true;
            }

            foreach (int f in dynFaces)
            {
                if (f < 0 || f >= data.faces.Count || !_slotHiddenFaces.Add(f))
                    continue;

                WriteStaticSlot(data, f, false);
                changed = true;
            }

            if (!changed)
                return false;

            _staticFaces.SetVertices(_staticFaceSlots);
            _staticFaces.UploadMeshData(false);
            _staticEdges.SetVertices(_staticEdgeSlots);
            _staticEdges.UploadMeshData(false);
            return true;
        }

        private void WriteStaticSlot(IndieEditableMeshData data, int faceIndex, bool visible)
        {
            int f3 = faceIndex * 3;
            int f6 = faceIndex * 6;

            if (!visible || !TryGetFaceUvs(data, faceIndex, out Vector2 ua, out Vector2 ub, out Vector2 uc))
            {
                for (int k = 0; k < 3; k++)
                    _staticFaceSlots[f3 + k] = HiddenSlot;

                for (int k = 0; k < 6; k++)
                    _staticEdgeSlots[f6 + k] = HiddenSlot;

                return;
            }

            Vector3 a = new Vector3(ua.x, ua.y, 0f);
            Vector3 b = new Vector3(ub.x, ub.y, 0f);
            Vector3 c = new Vector3(uc.x, uc.y, 0f);

            _staticFaceSlots[f3] = a;
            _staticFaceSlots[f3 + 1] = b;
            _staticFaceSlots[f3 + 2] = c;

            _staticEdgeSlots[f6] = a;
            _staticEdgeSlots[f6 + 1] = b;
            _staticEdgeSlots[f6 + 2] = b;
            _staticEdgeSlots[f6 + 3] = c;
            _staticEdgeSlots[f6 + 4] = c;
            _staticEdgeSlots[f6 + 5] = a;
        }

        private static bool TryGetFaceUvs(
            IndieEditableMeshData data,
            int faceIndex,
            out Vector2 ua,
            out Vector2 ub,
            out Vector2 uc
        )
        {
            ua = ub = uc = default;

            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return false;

            var face = data.faces[faceIndex];
            int uvCount = data.uvs.Count;

            if (face.a < 0 || face.b < 0 || face.c < 0 ||
                face.a >= uvCount || face.b >= uvCount || face.c >= uvCount)
            {
                return false;
            }

            ua = data.uvs[face.a];
            ub = data.uvs[face.b];
            uc = data.uvs[face.c];
            return true;
        }

        private void RebuildDynamicPoints(IndieEditableMeshData data, HashSet<int> selectedVerts)
        {
            var pointVerts = new List<Vector3>();
            var pointCorners = new List<Vector2>();
            var pointColors = new List<Color>();
            var pointTris = new List<int>();

            var selPointVerts = new List<Vector3>();
            var selPointCorners = new List<Vector2>();
            var selPointColors = new List<Color>();
            var selPointTris = new List<int>();

            foreach (int v in _neighborhood.DynVerts)
            {
                if (v < 0 || v >= data.uvs.Count)
                    continue;

                if (selectedVerts.Contains(v))
                {
                    AppendPointQuad(selPointVerts, selPointCorners, selPointColors, selPointTris, data.uvs[v], SelectColor);
                }
                else
                {
                    AppendPointQuad(pointVerts, pointCorners, pointColors, pointTris, data.uvs[v], VertColor);
                }
            }

            // Selected verts always shown even if somehow outside dyn (shouldn't happen).
            for (int i = 0; i < data.selectedVertices.Count; i++)
            {
                int v = data.selectedVertices[i];

                if (v < 0 || v >= data.uvs.Count)
                    continue;

                if (!_neighborhood.DynVerts.Contains(v))
                    AppendPointQuad(selPointVerts, selPointCorners, selPointColors, selPointTris, data.uvs[v], SelectColor);
            }

            _dynPoints = AssignPointMesh(_dynPoints, "EditUV.DynPoints", pointVerts, pointCorners, pointColors, pointTris);
            _dynSelectPoints = AssignPointMesh(
                _dynSelectPoints,
                "EditUV.DynSelectPoints",
                selPointVerts,
                selPointCorners,
                selPointColors,
                selPointTris
            );
        }

        /// <summary>
        /// Dynamic layer (neighborhood fill/edges, selection, points) from current UVs.
        /// Cost is O(neighborhood), independent of mesh size.
        /// </summary>
        private void UpdateDynamicUvPositions(IndieEditableMeshData data)
        {
            HashSet<int> selectedVerts = ToSet(data.selectedVertices);
            HashSet<int> selectedFaces = ToSet(data.selectedFaces);
            bool hasPreview = _hasPreviewTexture;
            Color fill = hasPreview ? FaceOverTexture : FaceColor;

            var dynFaceVerts = new List<Vector3>();
            var dynFaceColors = new List<Color>();
            var dynFaceTris = new List<int>();
            var dynEdgeVerts = new List<Vector3>();
            var dynEdgeColors = new List<Color>();
            var dynEdgeIndices = new List<int>();
            var selFaceVerts = new List<Vector3>();
            var selFaceColors = new List<Color>();
            var selFaceTris = new List<int>();
            var selEdgeVerts = new List<Vector3>();
            var selEdgeColors = new List<Color>();
            var selEdgeIndices = new List<int>();

            foreach (int i in _neighborhood.DynFaces)
            {
                if (!TryGetFaceUvs(data, i, out Vector2 ua, out Vector2 ub, out Vector2 uc))
                    continue;

                if (selectedFaces.Contains(i))
                {
                    AppendTri(selFaceVerts, selFaceColors, selFaceTris, ua, ub, uc, SelectFaceFill);
                    AppendLine(selEdgeVerts, selEdgeColors, selEdgeIndices, ua, ub, SelectColor);
                    AppendLine(selEdgeVerts, selEdgeColors, selEdgeIndices, ub, uc, SelectColor);
                    AppendLine(selEdgeVerts, selEdgeColors, selEdgeIndices, uc, ua, SelectColor);
                }
                else
                {
                    AppendTri(dynFaceVerts, dynFaceColors, dynFaceTris, ua, ub, uc, fill);
                    AppendLine(dynEdgeVerts, dynEdgeColors, dynEdgeIndices, ua, ub, FaceEdgeColor);
                    AppendLine(dynEdgeVerts, dynEdgeColors, dynEdgeIndices, ub, uc, FaceEdgeColor);
                    AppendLine(dynEdgeVerts, dynEdgeColors, dynEdgeIndices, uc, ua, FaceEdgeColor);
                }
            }

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                int edgeIndex = data.selectedEdges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];

                if (edge.v0 >= data.uvs.Count || edge.v1 >= data.uvs.Count)
                    continue;

                AppendLine(
                    selEdgeVerts,
                    selEdgeColors,
                    selEdgeIndices,
                    data.uvs[edge.v0],
                    data.uvs[edge.v1],
                    SelectColor
                );
            }

            _dynFaces = AssignMesh(
                _dynFaces,
                "EditUV.DynFaces",
                dynFaceVerts,
                dynFaceColors,
                dynFaceTris,
                MeshTopology.Triangles
            );
            _dynEdges = AssignMesh(
                _dynEdges,
                "EditUV.DynEdges",
                dynEdgeVerts,
                dynEdgeColors,
                dynEdgeIndices,
                MeshTopology.Lines
            );
            _dynSelectFaces = AssignMesh(
                _dynSelectFaces,
                "EditUV.DynSelectFaces",
                selFaceVerts,
                selFaceColors,
                selFaceTris,
                MeshTopology.Triangles
            );
            _dynSelectEdges = AssignMesh(
                _dynSelectEdges,
                "EditUV.DynSelectEdges",
                selEdgeVerts,
                selEdgeColors,
                selEdgeIndices,
                MeshTopology.Lines
            );

            RebuildDynamicPoints(data, selectedVerts);
        }

        /// <summary>
        /// Full seam overlay (final RT). Only on seam / topology change;
        /// UV drag goes through <see cref="PatchSeamPositions"/>.
        /// </summary>
        private void RebuildSeamsOverlay(IndieEditableMeshData data)
        {
            _seamVerts.Clear();
            _seamSegments.Clear();
            _seamSegmentsByVert.Clear();

            var colors = new List<Color>();
            var indices = new List<int>();

            AppendSeamSet(data, IndieEditUVSeamState.BakedSeams, SeamColor, colors, indices);

            if (IndieEditUVSeamState.BakedSeams.Count == 0)
            {
                List<Vector2> fallback = IndieEditUVSeamUtil.CollectSeamUvEndpoints(data);

                if (fallback != null)
                {
                    for (int i = 0; i + 1 < fallback.Count; i += 2)
                        AppendSeamQuad(fallback[i], fallback[i + 1], SeamColor, colors, indices);
                }
            }

            AppendSeamSet(data, IndieEditUVSeamState.WorkingSeams, WorkingSeamColor, colors, indices);

            _staticSeams = AssignMesh(_staticSeams, "EditUV.StaticSeams", _seamVerts, colors, indices, MeshTopology.Lines);
        }

        /// <summary>Move seam segments touching neighborhood verts to current UVs.</summary>
        private void PatchSeamPositions(IndieEditableMeshData data)
        {
            if (_staticSeams == null || _seamSegments.Count == 0)
                return;

            bool changed = false;

            foreach (int v in _neighborhood.DynVerts)
            {
                if (!_seamSegmentsByVert.TryGetValue(v, out List<int> segments))
                    continue;

                for (int i = 0; i < segments.Count; i++)
                {
                    SeamSegment segment = _seamSegments[segments[i]];
                    WriteSeamQuad(segment.BaseVertex, data.uvs[segment.V0], data.uvs[segment.V1]);
                    changed = true;
                }
            }

            if (!changed)
                return;

            _staticSeams.SetVertices(_seamVerts);
            _staticSeams.UploadMeshData(false);
        }

        private void AppendSeamSet(
            IndieEditableMeshData data,
            IReadOnlyCollection<long> seams,
            Color color,
            List<Color> colors,
            List<int> indices
        )
        {
            if (seams == null)
                return;

            int uvCount = data.uvs.Count;

            foreach (long key in seams)
            {
                if (!IndieEditUVSeamState.TryGetEdgeEndpoints(data, key, out int v0, out int v1) ||
                    v0 >= uvCount ||
                    v1 >= uvCount)
                {
                    continue;
                }

                int segmentIndex = _seamSegments.Count;
                _seamSegments.Add(new SeamSegment { V0 = v0, V1 = v1, BaseVertex = _seamVerts.Count });
                AddSeamSegmentRef(v0, segmentIndex);
                AddSeamSegmentRef(v1, segmentIndex);
                AppendSeamQuad(data.uvs[v0], data.uvs[v1], color, colors, indices);
            }
        }

        private void AddSeamSegmentRef(int vertex, int segmentIndex)
        {
            if (!_seamSegmentsByVert.TryGetValue(vertex, out List<int> list))
            {
                list = new List<int>(2);
                _seamSegmentsByVert.Add(vertex, list);
            }

            list.Add(segmentIndex);
        }

        /// <summary>Seam line plus a 1-texel-ish offset copy so it reads thicker.</summary>
        private void AppendSeamQuad(Vector2 a, Vector2 b, Color color, List<Color> colors, List<int> indices)
        {
            int baseVertex = _seamVerts.Count;

            for (int k = 0; k < 4; k++)
            {
                _seamVerts.Add(Vector3.zero);
                colors.Add(color);
                indices.Add(baseVertex + k);
            }

            WriteSeamQuad(baseVertex, a, b);
        }

        private void WriteSeamQuad(int baseVertex, Vector2 a, Vector2 b)
        {
            _seamVerts[baseVertex] = new Vector3(a.x, a.y, 0f);
            _seamVerts[baseVertex + 1] = new Vector3(b.x, b.y, 0f);
            _seamVerts[baseVertex + 2] = new Vector3(a.x + SeamOffsetEps, a.y + SeamOffsetEps, 0f);
            _seamVerts[baseVertex + 3] = new Vector3(b.x + SeamOffsetEps, b.y + SeamOffsetEps, 0f);
        }

        private static HashSet<int> ToSet(List<int> list)
        {
            var set = new HashSet<int>();

            if (list == null)
                return set;

            for (int i = 0; i < list.Count; i++)
                set.Add(list[i]);

            return set;
        }

        private static void AppendTri(
            List<Vector3> verts,
            List<Color> colors,
            List<int> tris,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Color color
        )
        {
            int baseIndex = verts.Count;
            verts.Add(new Vector3(a.x, a.y, 0f));
            verts.Add(new Vector3(b.x, b.y, 0f));
            verts.Add(new Vector3(c.x, c.y, 0f));
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        private static void AppendLine(
            List<Vector3> verts,
            List<Color> colors,
            List<int> indices,
            Vector2 a,
            Vector2 b,
            Color color
        )
        {
            int i0 = verts.Count;
            verts.Add(new Vector3(a.x, a.y, 0f));
            verts.Add(new Vector3(b.x, b.y, 0f));
            colors.Add(color);
            colors.Add(color);
            indices.Add(i0);
            indices.Add(i0 + 1);
        }

        private static void AppendPointQuad(
            List<Vector3> verts,
            List<Vector2> corners,
            List<Color> colors,
            List<int> tris,
            Vector2 uv,
            Color color
        )
        {
            int baseIndex = verts.Count;
            Vector3 center = new Vector3(uv.x, uv.y, 0f);

            verts.Add(center);
            verts.Add(center);
            verts.Add(center);
            verts.Add(center);

            corners.Add(new Vector2(-1f, -1f));
            corners.Add(new Vector2(1f, -1f));
            corners.Add(new Vector2(1f, 1f));
            corners.Add(new Vector2(-1f, 1f));

            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);

            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
            tris.Add(baseIndex);
            tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 3);
        }

        private static Mesh AssignMesh(
            Mesh mesh,
            string name,
            List<Vector3> verts,
            List<Color> colors,
            List<int> indices,
            MeshTopology topology
        )
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = IndexFormat.UInt32
                };
            }

            mesh.Clear();

            if (verts.Count == 0)
            {
                mesh.UploadMeshData(false);
                return mesh;
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);

            if (topology == MeshTopology.Triangles)
                mesh.SetTriangles(indices, 0, false);
            else
                mesh.SetIndices(indices.ToArray(), topology, 0, false);

            mesh.UploadMeshData(false);
            return mesh;
        }

        private static Mesh AssignPointMesh(
            Mesh mesh,
            string name,
            List<Vector3> verts,
            List<Vector2> corners,
            List<Color> colors,
            List<int> tris
        )
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = IndexFormat.UInt32
                };
            }

            mesh.Clear();

            if (verts.Count == 0)
            {
                mesh.UploadMeshData(false);
                return mesh;
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void DestroyRt(ref RenderTexture rt)
        {
            if (rt == null)
                return;

            rt.Release();
            Object.DestroyImmediate(rt);
            rt = null;
        }

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null)
                return;

            Object.DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
