using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// 3ds Max-style Cut (Vertex tab only): click vertex / edge / face points
    /// to create a connected cut path. RMB ends the current cut; RMB again exits.
    /// </summary>
    public static class IndieEditMeshCutTool
    {
        private enum HitKind
        {
            None,
            Vertex,
            Edge,
            Face
        }

        private struct HoverHit
        {
            public HitKind kind;
            public int index;
            public float edgeT;
            public Vector3 localPoint;
            public int vertexIndex;
        }

        private static bool _active;
        private static int _lastVertex = -1;
        private static bool _undoOpen;
        private static readonly List<int> _cutPath = new List<int>();
        private static HoverHit _hover;

        public static bool IsActive => _active;

        public static void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;
            ResetChain();
            _undoOpen = false;

            if (_active)
            {
                IndieEditMeshAttachTool.Deactivate();
                IndieEditMeshPointToPoint.Deactivate();
            }

            SceneView.RepaintAll();
        }

        public static void Deactivate()
        {
            SetActive(false);
        }

        public static void ResetChain()
        {
            _lastVertex = -1;
            _cutPath.Clear();
            _hover = default;
        }

        public static bool HandleSceneGUI(IndieEditMeshComponent ep, SceneView sceneView, Event evt)
        {
            if (!_active || ep == null || ep.Data == null || evt == null)
                return false;

            if (ep.subMode != IndieEditMeshSubMode.Vertex)
            {
                Deactivate();
                return false;
            }

            if (IndieEditMeshPreviewOp.IsActive)
                return false;

            UpdateHover(ep, sceneView, evt.mousePosition);

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (_lastVertex >= 0)
                {
                    ResetChain();
                    _undoOpen = false;
                }
                else
                {
                    Deactivate();
                }

                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && !evt.alt)
            {
                if (_lastVertex >= 0)
                {
                    ResetChain();
                    _undoOpen = false;
                }
                else
                {
                    Deactivate();
                }

                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.ContextClick && !evt.alt)
            {
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                if (_hover.kind != HitKind.None)
                    ApplyClick(ep, _hover);

                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                sceneView.Repaint();
                return false;
            }

            return false;
        }

        public static void Draw(IndieEditMeshComponent ep, SceneView sceneView)
        {
            if (!_active || ep == null || ep.Data == null)
                return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            if (_cutPath.Count >= 2)
            {
                Handles.color = new Color(1f, 0.85f, 0.15f, 0.95f);

                for (int i = 1; i < _cutPath.Count; i++)
                {
                    int a = _cutPath[i - 1];
                    int b = _cutPath[i];

                    if (a < 0 || a >= ep.Data.vertices.Count ||
                        b < 0 || b >= ep.Data.vertices.Count)
                    {
                        continue;
                    }

                    Handles.DrawLine(
                        ep.transform.TransformPoint(ep.Data.vertices[a]),
                        ep.transform.TransformPoint(ep.Data.vertices[b])
                    );
                }
            }

            if (_lastVertex >= 0 &&
                _lastVertex < ep.Data.vertices.Count &&
                _hover.kind != HitKind.None)
            {
                Handles.color = new Color(0.2f, 0.95f, 1f, 0.95f);
                Handles.DrawDottedLine(
                    ep.transform.TransformPoint(ep.Data.vertices[_lastVertex]),
                    ep.transform.TransformPoint(_hover.localPoint),
                    4f
                );
            }

            DrawHoverMarker(ep);

            Handles.BeginGUI();
            var rect = new Rect(12f, 12f, 280f, 40f);
            GUI.Label(
                rect,
                "Cut: click verts — splits crossed edges\nRMB = end cut / exit   Esc = cancel",
                EditorStyles.helpBox
            );
            Handles.EndGUI();
        }

        private static void DrawHoverMarker(IndieEditMeshComponent ep)
        {
            if (_hover.kind == HitKind.None)
                return;

            Vector3 world = ep.transform.TransformPoint(_hover.localPoint);
            float size = HandleUtility.GetHandleSize(world) * 0.06f;

            switch (_hover.kind)
            {
                case HitKind.Vertex:
                    Handles.color = new Color(1f, 0.9f, 0.2f, 1f);
                    Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
                    break;

                case HitKind.Edge:
                    Handles.color = new Color(0.2f, 1f, 0.55f, 1f);
                    Handles.DotHandleCap(0, world, Quaternion.identity, size * 0.85f, EventType.Repaint);
                    break;

                case HitKind.Face:
                    Handles.color = new Color(0.35f, 0.65f, 1f, 1f);
                    Handles.DotHandleCap(0, world, Quaternion.identity, size * 0.75f, EventType.Repaint);
                    break;
            }
        }

        private static void UpdateHover(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            _hover = default;

            int vertex = IndieEditMeshSelection.PickVertex(ep, sceneView, mousePosition);

            if (vertex >= 0)
            {
                _hover.kind = HitKind.Vertex;
                _hover.index = vertex;
                _hover.vertexIndex = vertex;
                _hover.localPoint = ep.Data.vertices[vertex];
                return;
            }

            int edge = IndieEditMeshSelection.PickEdge(ep, sceneView, mousePosition);

            if (edge >= 0)
            {
                float t = IndieEditMeshSelection.GetEdgeParam(ep, edge, mousePosition);
                var e = ep.Data.edges[edge];
                _hover.kind = HitKind.Edge;
                _hover.index = edge;
                _hover.edgeT = t;
                _hover.localPoint = Vector3.Lerp(
                    ep.Data.vertices[e.v0],
                    ep.Data.vertices[e.v1],
                    t
                );
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            if (IndieEditMeshSelection.RaycastFace(ep, ray, out int face, out Vector3 localPoint))
            {
                _hover.kind = HitKind.Face;
                _hover.index = face;
                _hover.localPoint = localPoint;
            }
        }

        private static void ApplyClick(IndieEditMeshComponent ep, HoverHit hit)
        {
            if (!_undoOpen)
            {
                IndieEditMeshUndo.Register(ep, "Cut");
                _undoOpen = true;
            }

            int vertex = MaterializeHit(ep.Data, hit);

            if (vertex < 0)
                return;

            if (_lastVertex >= 0 && _lastVertex != vertex)
            {
                var through = ConnectVertices(ep.Data, _lastVertex, vertex);

                for (int i = 0; i < through.Count; i++)
                {
                    int mid = through[i];

                    if (mid >= 0 && !_cutPath.Contains(mid))
                        _cutPath.Add(mid);
                }
            }

            _lastVertex = vertex;

            if (!_cutPath.Contains(vertex))
                _cutPath.Add(vertex);

            // Keep path verts selected for feedback.
            ep.Data.selectedVertices = new List<int>(_cutPath);
            ep.Data.selectedEdges.Clear();
            ep.Data.selectedFaces.Clear();

            ep.Data.RebuildEdges();
            IndieEditMeshSession.RefreshMesh(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
            EditorUtility.SetDirty(ep);
        }

        private static int MaterializeHit(IndieEditableMeshData data, HoverHit hit)
        {
            switch (hit.kind)
            {
                case HitKind.Vertex:
                    return hit.index;

                case HitKind.Edge:
                {
                    if (hit.index < 0 || hit.index >= data.edges.Count)
                        return -1;

                    var edge = data.edges[hit.index];

                    if (hit.edgeT <= 0.02f)
                        return edge.v0;

                    if (hit.edgeT >= 0.98f)
                        return edge.v1;

                    return SplitEdgeAndFaces(data, edge.v0, edge.v1, hit.edgeT);
                }

                case HitKind.Face:
                    return InsertVertexOnFace(data, hit.index, hit.localPoint);

                default:
                    return -1;
            }
        }

        private static int InsertVertexOnFace(
            IndieEditableMeshData data,
            int faceIndex,
            Vector3 localPoint
        )
        {
            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return -1;

            var face = data.faces[faceIndex];
            int v = IndieEditMeshMeshUtil.AddVertex(data, face.a, localPoint);

            if (data.uvs.Count > v &&
                face.a < data.uvs.Count &&
                face.b < data.uvs.Count &&
                face.c < data.uvs.Count)
            {
                Vector3 bary = Barycentric(
                    localPoint,
                    data.vertices[face.a],
                    data.vertices[face.b],
                    data.vertices[face.c]
                );
                data.uvs[v] =
                    data.uvs[face.a] * bary.x +
                    data.uvs[face.b] * bary.y +
                    data.uvs[face.c] * bary.z;
            }

            data.faces.RemoveAt(faceIndex);
            IndieEditMeshMeshUtil.AddTriangle(
                data.faces,
                face.a,
                face.b,
                v,
                face.materialId,
                face.smoothingGroups
            );
            IndieEditMeshMeshUtil.AddTriangle(
                data.faces,
                face.b,
                face.c,
                v,
                face.materialId,
                face.smoothingGroups
            );
            IndieEditMeshMeshUtil.AddTriangle(
                data.faces,
                face.c,
                face.a,
                v,
                face.materialId,
                face.smoothingGroups
            );

            RemapFaceSelectionAfterRemove(data, faceIndex);
            return v;
        }

        private static void RemapFaceSelectionAfterRemove(IndieEditableMeshData data, int removedFace)
        {
            for (int i = data.selectedFaces.Count - 1; i >= 0; i--)
            {
                int f = data.selectedFaces[i];

                if (f == removedFace)
                    data.selectedFaces.RemoveAt(i);
                else if (f > removedFace)
                    data.selectedFaces[i] = f - 1;
            }
        }

        private static int SplitEdgeAndFaces(IndieEditableMeshData data, int v0, int v1, float t)
        {
            int mid = IndieEditMeshMeshUtil.SplitEdgeAt(data, v0, v1, t, null);
            var newFaces = new List<IndieEditMeshFace>(data.faces.Count + 4);

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (!TrySplitFaceOnEdge(face, v0, v1, mid, out var f0, out var f1))
                {
                    newFaces.Add(face);
                    continue;
                }

                newFaces.Add(f0);
                newFaces.Add(f1);
            }

            data.faces = newFaces;
            return mid;
        }

        private static bool TrySplitFaceOnEdge(
            IndieEditMeshFace face,
            int v0,
            int v1,
            int mid,
            out IndieEditMeshFace f0,
            out IndieEditMeshFace f1
        )
        {
            f0 = default;
            f1 = default;

            if (MatchEdge(face.a, face.b, v0, v1))
            {
                f0 = MakeFace(face.a, mid, face.c, face);
                f1 = MakeFace(mid, face.b, face.c, face);
                return true;
            }

            if (MatchEdge(face.b, face.c, v0, v1))
            {
                f0 = MakeFace(face.b, mid, face.a, face);
                f1 = MakeFace(mid, face.c, face.a, face);
                return true;
            }

            if (MatchEdge(face.c, face.a, v0, v1))
            {
                f0 = MakeFace(face.c, mid, face.b, face);
                f1 = MakeFace(mid, face.a, face.b, face);
                return true;
            }

            return false;
        }

        private static bool MatchEdge(int a, int b, int v0, int v1)
        {
            return (a == v0 && b == v1) || (a == v1 && b == v0);
        }

        private static IndieEditMeshFace MakeFace(int a, int b, int c, IndieEditMeshFace source)
        {
            return new IndieEditMeshFace
            {
                a = a,
                b = b,
                c = c,
                materialId = source.materialId,
                smoothingGroups = source.smoothingGroups
            };
        }

        /// <summary>
        /// Cut A→B across existing faces: find every edge the segment crosses,
        /// insert verts at intersections (green dots), split those edges and
        /// rebuild touched triangles. No fan / overlapping subsurface faces.
        /// </summary>
        private static List<int> ConnectVertices(IndieEditableMeshData data, int a, int b)
        {
            var through = new List<int>();

            if (a == b || a < 0 || b < 0)
                return through;

            if (a >= data.vertices.Count || b >= data.vertices.Count)
                return through;

            if (VerticesShareEdge(data, a, b))
                return through;

            Vector3 pa = data.vertices[a];
            Vector3 pb = data.vertices[b];
            float abLen = Vector3.Distance(pa, pb);

            if (abLen < 1e-8f)
                return through;

            data.RebuildEdges();

            float tol = Mathf.Max(1e-5f, abLen * 1e-3f);
            var crossings = new List<(float t, int e0, int e1, float et)>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                // Skip the cut endpoints' own edges — hits there are t=0/1.
                if (edge.v0 == a || edge.v1 == a || edge.v0 == b || edge.v1 == b)
                    continue;

                Vector3 e0 = data.vertices[edge.v0];
                Vector3 e1 = data.vertices[edge.v1];

                if (!TryIntersectCutWithEdge(
                        pa,
                        pb,
                        e0,
                        e1,
                        tol,
                        out float tAb,
                        out float tEdge))
                {
                    continue;
                }

                crossings.Add((tAb, edge.v0, edge.v1, tEdge));
            }

            crossings.Sort((x, y) => x.t.CompareTo(y.t));

            var seen = new HashSet<long>();

            for (int i = 0; i < crossings.Count; i++)
            {
                var cross = crossings[i];
                long key = IndieEditMeshMeshUtil.EdgeKey(cross.e0, cross.e1);

                if (!seen.Add(key))
                    continue;

                int mid;

                if (cross.et <= 0.02f)
                    mid = cross.e0;
                else if (cross.et >= 0.98f)
                    mid = cross.e1;
                else
                    mid = SplitEdgeAndFaces(data, cross.e0, cross.e1, cross.et);

                if (mid >= 0 && mid != a && mid != b && !through.Contains(mid))
                    through.Add(mid);
            }

            data.RebuildEdges();
            return through;
        }

        /// <summary>
        /// True only when cut segment and edge properly cross in space
        /// (unclamped params in (0,1), distance ≤ tol). No false closest-point hits.
        /// </summary>
        private static bool TryIntersectCutWithEdge(
            Vector3 cut0,
            Vector3 cut1,
            Vector3 edge0,
            Vector3 edge1,
            float tol,
            out float tCut,
            out float tEdge
        )
        {
            tCut = 0f;
            tEdge = 0f;

            Vector3 da = cut1 - cut0;
            Vector3 db = edge1 - edge0;
            Vector3 r = cut0 - edge0;
            float aa = Vector3.Dot(da, da);
            float ab = Vector3.Dot(da, db);
            float bb = Vector3.Dot(db, db);
            float ar = Vector3.Dot(da, r);
            float br = Vector3.Dot(db, r);
            float denom = aa * bb - ab * ab;

            if (Mathf.Abs(denom) < 1e-14f)
                return false;

            float ta = (ab * br - bb * ar) / denom;
            float tb = (aa * br - ab * ar) / denom;

            if (ta <= 0.001f || ta >= 0.999f)
                return false;

            if (tb <= 0.001f || tb >= 0.999f)
                return false;

            Vector3 pa = cut0 + da * ta;
            Vector3 pb = edge0 + db * tb;

            if (Vector3.Distance(pa, pb) > tol)
                return false;

            tCut = ta;
            tEdge = tb;
            return true;
        }

        private static bool VerticesShareEdge(IndieEditableMeshData data, int a, int b)
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (IndieEditMeshMeshUtil.EdgeKey(face.a, face.b) == key ||
                    IndieEditMeshMeshUtil.EdgeKey(face.b, face.c) == key ||
                    IndieEditMeshMeshUtil.EdgeKey(face.c, face.a) == key)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;

            if (Mathf.Abs(denom) < 1e-12f)
                return new Vector3(1f, 0f, 0f);

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;
            return new Vector3(u, v, w);
        }
    }
}
