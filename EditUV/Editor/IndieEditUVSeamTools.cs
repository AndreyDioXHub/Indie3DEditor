using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Scene input + preview for Edit Seams and Point-to-Point Seams.
    /// </summary>
    public static class IndieEditUVSeamTools
    {
        private const float PickPixelDistance = 14f;
        private const float MarqueePixelThreshold = 6f;

        private static readonly Color PreviewCommitted = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color PreviewHover = new Color(0.3f, 0.9f, 1f, 0.9f);

        private static bool _mouseDown;
        private static bool _marqueeActive;
        private static Vector2 _marqueeStart;
        private static Vector2 _marqueeCurrent;
        private static bool _ctrlRemove;

        private static int _p2pAnchorVertex = -1;
        private static List<long> _p2pCommittedPath = new List<long>();
        private static List<long> _p2pPreviewPath = new List<long>();
        private static bool _swallowRightClick;
        private static bool _rightClickMoved;

        /// <summary>
        /// Right click leaves Point-to-Point Seams and eats the Scene View menu.
        /// Working seams stay. Alt+RMB stays with the camera.
        /// </summary>
        public static bool TryConsumeRightClick(Event evt)
        {
            if (evt == null || evt.alt)
                return false;

            bool pointToPoint = IndieEditUVSeamState.Mode == IndieEditUVSeamMode.PointToPoint;

            if (!pointToPoint && !_swallowRightClick)
                return false;

            bool rmb =
                evt.button == 1 &&
                (evt.type == EventType.MouseDown ||
                 evt.type == EventType.MouseDrag ||
                 evt.type == EventType.MouseUp);
            bool context = evt.type == EventType.ContextClick;

            if (!rmb && !context)
                return false;

            if (evt.type == EventType.MouseDown)
            {
                _swallowRightClick = true;
                _rightClickMoved = false;
                ExitPointToPoint();
            }
            else if (evt.type == EventType.MouseDrag)
            {
                _rightClickMoved = true;
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (_rightClickMoved)
                    _swallowRightClick = false;
            }
            else if (context)
            {
                _swallowRightClick = false;
                ExitPointToPoint();
            }

            evt.Use();
            return true;
        }

        private static void ExitPointToPoint()
        {
            if (IndieEditUVSeamState.Mode != IndieEditUVSeamMode.PointToPoint)
                return;

            IndieEditUVSeamState.SetMode(IndieEditUVSeamMode.PointToPoint);
        }

        public static void ResetToolTransient()
        {
            _mouseDown = false;
            _marqueeActive = false;
            _p2pAnchorVertex = -1;
            _p2pCommittedPath.Clear();
            _p2pPreviewPath.Clear();
        }

        public static bool HandleSceneGui(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Event evt,
            int blockControl = -1
        )
        {
            if (uv == null || uv.Data == null || evt == null)
                return false;

            if (!IndieEditUVSeamState.HasActiveSeamTool)
                return false;

            if (blockControl < 0)
                blockControl = GUIUtility.GetControlID(FocusType.Passive);

            if (IndieEditUVSeamState.Mode == IndieEditUVSeamMode.EditSeams)
                return HandleEditSeams(uv, sceneView, evt, blockControl);

            if (IndieEditUVSeamState.Mode == IndieEditUVSeamMode.PointToPoint)
                return HandlePointToPoint(uv, sceneView, evt, blockControl);

            return false;
        }

        public static void DrawOverlay(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            DrawP2PPreview(uv);
            DrawMarquee();
        }

        private static bool HandleEditSeams(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Event evt,
            int blockControl
        )
        {
            if (evt.alt)
                return false;

            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                (HandleUtility.nearestControl == blockControl || GUIUtility.hotControl == 0))
            {
                GUIUtility.hotControl = blockControl;
                _mouseDown = true;
                _marqueeActive = false;
                _marqueeStart = evt.mousePosition;
                _marqueeCurrent = evt.mousePosition;
                _ctrlRemove = evt.control || evt.command;
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseDrag &&
                evt.button == 0 &&
                GUIUtility.hotControl == blockControl)
            {
                _marqueeCurrent = evt.mousePosition;

                if (!_marqueeActive &&
                    Vector2.Distance(_marqueeStart, _marqueeCurrent) >= MarqueePixelThreshold)
                {
                    _marqueeActive = true;
                }

                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp &&
                evt.button == 0 &&
                GUIUtility.hotControl == blockControl)
            {
                _marqueeCurrent = evt.mousePosition;

                if (_marqueeActive)
                    ApplyMarquee(uv, sceneView, NormalizeRect(_marqueeStart, _marqueeCurrent), _ctrlRemove);
                else
                    ApplyClick(uv, sceneView, evt.mousePosition, _ctrlRemove);

                GUIUtility.hotControl = 0;
                _mouseDown = false;
                _marqueeActive = false;
                evt.Use();
                IndieEditUVNotify.SelectionOrMeshChanged();
                return true;
            }

            return false;
        }

        private static bool HandlePointToPoint(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Event evt,
            int blockControl
        )
        {
            if (evt.alt)
                return false;

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                UpdateP2PPreview(uv, sceneView, evt.mousePosition);
                sceneView.Repaint();
                return false;
            }

            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                (HandleUtility.nearestControl == blockControl || GUIUtility.hotControl == 0))
            {
                GUIUtility.hotControl = blockControl;
                int vertex = PickNearestVertexOnEdge(uv, sceneView, evt.mousePosition);

                if (vertex < 0)
                {
                    evt.Use();
                    return true;
                }

                if (_p2pAnchorVertex < 0)
                {
                    _p2pAnchorVertex = vertex;
                    _p2pCommittedPath.Clear();
                    _p2pPreviewPath.Clear();
                }
                else
                {
                    List<long> path = FindP2PPath(uv, sceneView, vertex);

                    if (path != null && path.Count > 0)
                    {
                        IndieEditUVSeamState.AddWorkingMany(path);
                        _p2pCommittedPath.AddRange(path);
                    }

                    _p2pAnchorVertex = vertex;
                    _p2pPreviewPath.Clear();
                }

                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp &&
                evt.button == 0 &&
                GUIUtility.hotControl == blockControl)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
                return true;
            }

            return false;
        }

        private static void ApplyClick(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            bool remove
        )
        {
            int edgeIndex = PickEdgeIndex(uv, sceneView, mousePosition);

            if (edgeIndex < 0)
                return;

            long key = IndieEditUVSeamState.EdgeKeyFromData(uv.Data, edgeIndex);

            if (remove)
                IndieEditUVSeamState.RemoveWorking(key);
            else
                IndieEditUVSeamState.AddWorking(key);
        }

        private static void ApplyMarquee(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Rect rect,
            bool remove
        )
        {
            var data = uv.Data;
            Camera camera = sceneView != null ? sceneView.camera : null;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                Vector3 w0 = uv.transform.TransformPoint(data.vertices[edge.v0]);
                Vector3 w1 = uv.transform.TransformPoint(data.vertices[edge.v1]);

                if (camera != null)
                {
                    if (camera.WorldToScreenPoint(w0).z <= 0f ||
                        camera.WorldToScreenPoint(w1).z <= 0f)
                    {
                        continue;
                    }
                }

                Vector2 g0 = HandleUtility.WorldToGUIPoint(w0);
                Vector2 g1 = HandleUtility.WorldToGUIPoint(w1);

                if (!SegmentIntersectsRect(g0, g1, rect) &&
                    !rect.Contains(g0) &&
                    !rect.Contains(g1))
                {
                    continue;
                }

                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (remove)
                    IndieEditUVSeamState.RemoveWorking(key);
                else
                    IndieEditUVSeamState.AddWorking(key);
            }
        }

        private static void UpdateP2PPreview(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            _p2pPreviewPath.Clear();

            if (_p2pAnchorVertex < 0)
                return;

            int hover = PickNearestVertexOnEdge(uv, sceneView, mousePosition);

            if (hover < 0 || hover == _p2pAnchorVertex)
                return;

            List<long> path = FindP2PPath(uv, sceneView, hover);

            if (path != null)
                _p2pPreviewPath.AddRange(path);
        }

        private static List<long> FindP2PPath(IndieEditUVComponent uv, SceneView sceneView, int toVertex)
        {
            Camera camera = IndieEditUVSelection.IgnoreBackFaces && sceneView != null ? sceneView.camera : null;
            return IndieEditUVSeamOps.FindShortestEdgePath(uv.Data, _p2pAnchorVertex, toVertex, uv.transform, camera);
        }

        private static void DrawP2PPreview(IndieEditUVComponent uv)
        {
            if (IndieEditUVSeamState.Mode != IndieEditUVSeamMode.PointToPoint)
                return;

            var data = uv.Data;
            var transform = uv.transform;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            if (_p2pCommittedPath.Count > 0)
            {
                Handles.color = PreviewCommitted;

                for (int i = 0; i < _p2pCommittedPath.Count; i++)
                    DrawEdgeKey(data, transform, _p2pCommittedPath[i], 4f);
            }

            if (_p2pPreviewPath.Count > 0)
            {
                Handles.color = PreviewHover;

                for (int i = 0; i < _p2pPreviewPath.Count; i++)
                    DrawEdgeKey(data, transform, _p2pPreviewPath[i], 3.5f);
            }

            if (_p2pAnchorVertex >= 0 &&
                _p2pAnchorVertex < data.vertices.Count)
            {
                Vector3 world = transform.TransformPoint(data.vertices[_p2pAnchorVertex]);
                float size = HandleUtility.GetHandleSize(world) * 0.1f;
                Handles.color = PreviewCommitted;
                Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
            }
        }

        private static void DrawEdgeKey(
            IndieEditableMeshData data,
            Transform transform,
            long key,
            float thickness
        )
        {
            if (!IndieEditUVSeamState.TryGetEdgeEndpoints(data, key, out int v0, out int v1))
                return;

            Handles.DrawLine(
                transform.TransformPoint(data.vertices[v0]),
                transform.TransformPoint(data.vertices[v1]),
                thickness
            );
        }

        private static void DrawMarquee()
        {
            if (!_marqueeActive)
                return;

            Rect rect = NormalizeRect(_marqueeStart, _marqueeCurrent);
            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.85f, 1f, 0.12f));
            Color border = new Color(0.25f, 0.9f, 1f, 0.95f);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), border);
            Handles.EndGUI();
        }

        private static int PickEdgeIndex(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            var data = uv.Data;
            Camera camera = sceneView != null ? sceneView.camera : null;
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                Vector3 w0 = uv.transform.TransformPoint(data.vertices[edge.v0]);
                Vector3 w1 = uv.transform.TransformPoint(data.vertices[edge.v1]);

                if (camera != null)
                {
                    if (camera.WorldToScreenPoint(w0).z <= 0f ||
                        camera.WorldToScreenPoint(w1).z <= 0f)
                    {
                        continue;
                    }
                }

                float distance = DistancePointSegment(
                    mousePosition,
                    HandleUtility.WorldToGUIPoint(w0),
                    HandleUtility.WorldToGUIPoint(w1)
                );

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int PickNearestVertexOnEdge(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            if (IndieEditUVSelection.IgnoreBackFaces)
                return IndieEditUVSelection.PickFrontEdgeVertex(uv, sceneView, mousePosition);

            int edgeIndex = PickEdgeIndex(uv, sceneView, mousePosition);

            if (edgeIndex < 0)
                return -1;

            var data = uv.Data;
            var edge = data.edges[edgeIndex];
            Vector2 g0 = HandleUtility.WorldToGUIPoint(
                uv.transform.TransformPoint(data.vertices[edge.v0])
            );
            Vector2 g1 = HandleUtility.WorldToGUIPoint(
                uv.transform.TransformPoint(data.vertices[edge.v1])
            );

            float d0 = Vector2.Distance(mousePosition, g0);
            float d1 = Vector2.Distance(mousePosition, g1);
            return d0 <= d1 ? edge.v0 : edge.v1;
        }

        private static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;

            if (lenSq < 1e-8f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b))
                return true;

            Vector2 tl = new Vector2(rect.xMin, rect.yMin);
            Vector2 tr = new Vector2(rect.xMax, rect.yMin);
            Vector2 bl = new Vector2(rect.xMin, rect.yMax);
            Vector2 br = new Vector2(rect.xMax, rect.yMax);

            return SegmentsIntersect(a, b, tl, tr) ||
                   SegmentsIntersect(a, b, tr, br) ||
                   SegmentsIntersect(a, b, br, bl) ||
                   SegmentsIntersect(a, b, bl, tl);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a);
            float d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c);
            float d4 = Cross(d - c, b - c);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static Rect NormalizeRect(Vector2 a, Vector2 b)
        {
            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float xMax = Mathf.Max(a.x, b.x);
            float yMax = Mathf.Max(a.y, b.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
