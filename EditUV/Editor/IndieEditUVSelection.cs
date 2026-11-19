using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVSelection
    {
        private const float PickPixelDistance = 14f;
        private const float MarqueePixelThreshold = 6f;
        private const float UvVertPickPixels = 8f;
        private const float UvEdgePickPixels = 6f;

        private static bool _mouseDown;
        private static bool _marqueeActive;
        private static Vector2 _marqueeStart;
        private static Vector2 _marqueeCurrent;
        private static bool _marqueeModifiers;

        public static bool IgnoreBackFaces = true;

        public static bool IsMarqueeActive => _marqueeActive;

        public static Rect GetMarqueeGuiRect()
        {
            return NormalizeRect(_marqueeStart, _marqueeCurrent);
        }

        public static void ResetMarquee()
        {
            _mouseDown = false;
            _marqueeActive = false;
            _marqueeStart = Vector2.zero;
            _marqueeCurrent = Vector2.zero;
            _marqueeModifiers = false;
        }

        public static void SelectAll(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            var data = uv.Data;
            data.ClearSelection();

            switch (uv.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    for (int i = 0; i < data.vertices.Count; i++)
                        data.selectedVertices.Add(i);
                    break;

                case IndieEditMeshSubMode.Edge:
                    for (int i = 0; i < data.edges.Count; i++)
                        data.selectedEdges.Add(i);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    for (int i = 0; i < data.faces.Count; i++)
                        data.selectedFaces.Add(i);
                    break;
            }

            IndieEditUVNotify.SelectionChanged();
        }

        public static void HandleMouseDown(IndieEditUVComponent uv, Vector2 mousePosition, Event evt)
        {
            if (uv == null || uv.Data == null || evt.button != 0)
                return;

            _mouseDown = true;
            _marqueeActive = false;
            _marqueeStart = mousePosition;
            _marqueeCurrent = mousePosition;
            _marqueeModifiers = evt.shift || evt.control || evt.command;
        }

        public static void HandleMouseDrag(IndieEditUVComponent uv, Vector2 mousePosition, Event evt)
        {
            if (!_mouseDown || evt.button != 0)
                return;

            _marqueeCurrent = mousePosition;

            if (!_marqueeActive &&
                Vector2.Distance(_marqueeStart, _marqueeCurrent) >= MarqueePixelThreshold)
            {
                _marqueeActive = true;
            }
        }

        public static void HandleMouseUp(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            Event evt
        )
        {
            if (!_mouseDown || evt.button != 0)
                return;

            _marqueeCurrent = mousePosition;

            if (_marqueeActive)
                ApplyMarqueeSelection(uv, sceneView, GetMarqueeGuiRect(), _marqueeModifiers);
            else
                HandleSceneClick(uv, sceneView, mousePosition, evt);

            ResetMarquee();
            EditorUtility.SetDirty(uv);
            IndieEditUVNotify.SelectionChanged();
        }

        public static void HandleSceneClick(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            Event evt
        )
        {
            if (uv == null || uv.Data == null)
                return;

            bool modifier = evt.shift || evt.control || evt.command;

            switch (uv.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    HandleVertexClick(uv, sceneView, mousePosition, modifier);
                    break;

                case IndieEditMeshSubMode.Edge:
                    HandleEdgeClick(uv, sceneView, mousePosition, modifier);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    HandlePolygonClick(uv, sceneView, mousePosition, modifier);
                    break;
            }
        }

        public static bool HandleUvCanvasClick(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Vector2 mouseGui,
            bool modifier
        )
        {
            if (uv == null || uv.Data == null || view == null)
                return false;

            switch (uv.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    return HandleUvVertexClick(uv, view, mouseGui, modifier);

                case IndieEditMeshSubMode.Edge:
                    return HandleUvEdgeClick(uv, view, mouseGui, modifier);

                case IndieEditMeshSubMode.Polygon:
                    return HandleUvPolygonClick(uv, view, mouseGui, modifier);
            }

            return false;
        }

        public static void ApplyUvMarquee(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Rect guiRect,
            bool modifier
        )
        {
            if (uv == null || uv.Data == null || view == null)
                return;

            var data = uv.Data;
            var hits = new List<int>();

            switch (uv.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    for (int i = 0; i < data.uvs.Count; i++)
                    {
                        if (guiRect.Contains(view.UvToGui(data.uvs[i])))
                            hits.Add(i);
                    }

                    ApplyVertexHits(data, hits, modifier);
                    break;

                case IndieEditMeshSubMode.Edge:
                    for (int i = 0; i < data.edges.Count; i++)
                    {
                        var edge = data.edges[i];

                        if (edge.v0 >= data.uvs.Count || edge.v1 >= data.uvs.Count)
                            continue;

                        Vector2 a = view.UvToGui(data.uvs[edge.v0]);
                        Vector2 b = view.UvToGui(data.uvs[edge.v1]);

                        if (guiRect.Contains(a) || guiRect.Contains(b) ||
                            SegmentIntersectsRect(a, b, guiRect))
                        {
                            hits.Add(i);
                        }
                    }

                    ApplyEdgeHits(data, hits, modifier);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    for (int i = 0; i < data.faces.Count; i++)
                    {
                        var face = data.faces[i];

                        if (face.a >= data.uvs.Count ||
                            face.b >= data.uvs.Count ||
                            face.c >= data.uvs.Count)
                        {
                            continue;
                        }

                        Vector2 a = view.UvToGui(data.uvs[face.a]);
                        Vector2 b = view.UvToGui(data.uvs[face.b]);
                        Vector2 c = view.UvToGui(data.uvs[face.c]);
                        Vector2 centroid = (a + b + c) / 3f;

                        if (guiRect.Contains(centroid) ||
                            guiRect.Contains(a) ||
                            guiRect.Contains(b) ||
                            guiRect.Contains(c))
                        {
                            hits.Add(i);
                        }
                    }

                    ApplyFaceHits(data, hits, modifier);
                    break;
            }

            EditorUtility.SetDirty(uv);
            IndieEditUVNotify.SelectionChanged();
        }

        private static void HandleVertexClick(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier
        )
        {
            int index = PickVertexIndex(uv, sceneView, mousePosition);

            if (index >= 0)
            {
                if (modifier)
                    uv.Data.ToggleVertexSelection(index);
                else
                    uv.Data.SetVertexSelection(index);
            }
            else if (!modifier)
            {
                uv.Data.ClearSelection();
            }
        }

        private static void HandleEdgeClick(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier
        )
        {
            int index = PickEdgeIndex(uv, sceneView, mousePosition);

            if (index >= 0)
            {
                if (modifier)
                    uv.Data.ToggleEdgeSelection(index);
                else
                    uv.Data.SetEdgeSelection(index);
            }
            else if (!modifier)
            {
                uv.Data.ClearSelection();
            }
        }

        private static void HandlePolygonClick(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier
        )
        {
            if (TryPickFrontFace(uv, sceneView, mousePosition, out int faceIndex))
            {
                if (modifier)
                    uv.Data.ToggleFaceSelection(faceIndex);
                else
                    uv.Data.SetFaceSelection(faceIndex);
            }
            else if (!modifier)
            {
                uv.Data.ClearSelection();
            }
        }

        private static bool HandleUvVertexClick(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Vector2 mouseGui,
            bool modifier
        )
        {
            var data = uv.Data;
            float best = UvVertPickPixels;
            int bestIndex = -1;

            for (int i = 0; i < data.uvs.Count; i++)
            {
                float d = Vector2.Distance(mouseGui, view.UvToGui(data.uvs[i]));

                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                if (modifier)
                    data.ToggleVertexSelection(bestIndex);
                else
                    data.SetVertexSelection(bestIndex);

                return true;
            }

            if (!modifier)
                data.ClearSelection();

            return bestIndex >= 0 || !modifier;
        }

        private static bool HandleUvEdgeClick(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Vector2 mouseGui,
            bool modifier
        )
        {
            var data = uv.Data;
            float best = UvEdgePickPixels;
            int bestIndex = -1;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (edge.v0 >= data.uvs.Count || edge.v1 >= data.uvs.Count)
                    continue;

                float d = DistancePointSegment(
                    mouseGui,
                    view.UvToGui(data.uvs[edge.v0]),
                    view.UvToGui(data.uvs[edge.v1])
                );

                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                if (modifier)
                    data.ToggleEdgeSelection(bestIndex);
                else
                    data.SetEdgeSelection(bestIndex);

                return true;
            }

            if (!modifier)
                data.ClearSelection();

            return bestIndex >= 0 || !modifier;
        }

        private static bool HandleUvPolygonClick(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Vector2 mouseGui,
            bool modifier
        )
        {
            var data = uv.Data;
            Vector2 mouseUv = view.GuiToUv(mouseGui);
            int bestIndex = -1;
            float bestArea = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a >= data.uvs.Count ||
                    face.b >= data.uvs.Count ||
                    face.c >= data.uvs.Count)
                {
                    continue;
                }

                Vector2 a = data.uvs[face.a];
                Vector2 b = data.uvs[face.b];
                Vector2 c = data.uvs[face.c];

                if (!IndieEditMeshPickMath.PointInTriangle(mouseUv, a, b, c))
                    continue;

                float area = Mathf.Abs(Cross(b - a, c - a));

                if (area < bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                if (modifier)
                    data.ToggleFaceSelection(bestIndex);
                else
                    data.SetFaceSelection(bestIndex);

                return true;
            }

            if (!modifier)
                data.ClearSelection();

            return bestIndex >= 0 || !modifier;
        }

        private static void ApplyMarqueeSelection(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Rect rect,
            bool modifier
        )
        {
            var data = uv.Data;
            var hits = new List<int>();
            Camera camera = sceneView.camera;

            switch (uv.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    for (int i = 0; i < data.vertices.Count; i++)
                    {
                        Vector3 world = uv.transform.TransformPoint(data.vertices[i]);

                        if (camera != null && camera.WorldToScreenPoint(world).z <= 0f)
                            continue;

                        if (rect.Contains(HandleUtility.WorldToGUIPoint(world)))
                            hits.Add(i);
                    }

                    ApplyVertexHits(data, hits, modifier);
                    break;

                case IndieEditMeshSubMode.Edge:
                    for (int i = 0; i < data.edges.Count; i++)
                    {
                        var edge = data.edges[i];
                        Vector3 w0 = uv.transform.TransformPoint(data.vertices[edge.v0]);
                        Vector3 w1 = uv.transform.TransformPoint(data.vertices[edge.v1]);
                        Vector2 g0 = HandleUtility.WorldToGUIPoint(w0);
                        Vector2 g1 = HandleUtility.WorldToGUIPoint(w1);

                        if (rect.Contains(g0) || rect.Contains(g1) ||
                            SegmentIntersectsRect(g0, g1, rect))
                        {
                            hits.Add(i);
                        }
                    }

                    ApplyEdgeHits(data, hits, modifier);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    for (int i = 0; i < data.faces.Count; i++)
                    {
                        var face = data.faces[i];
                        Vector3 wa = uv.transform.TransformPoint(data.vertices[face.a]);
                        Vector3 wb = uv.transform.TransformPoint(data.vertices[face.b]);
                        Vector3 wc = uv.transform.TransformPoint(data.vertices[face.c]);

                        if (IgnoreBackFaces &&
                            !IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
                        {
                            continue;
                        }

                        Vector2 ga = HandleUtility.WorldToGUIPoint(wa);
                        Vector2 gb = HandleUtility.WorldToGUIPoint(wb);
                        Vector2 gc = HandleUtility.WorldToGUIPoint(wc);
                        Vector2 centroid = (ga + gb + gc) / 3f;

                        if (rect.Contains(centroid) ||
                            rect.Contains(ga) ||
                            rect.Contains(gb) ||
                            rect.Contains(gc))
                        {
                            hits.Add(i);
                        }
                    }

                    ApplyFaceHits(data, hits, modifier);
                    break;
            }
        }

        private static void ApplyVertexHits(IndieEditableMeshData data, List<int> hits, bool modifier)
        {
            if (!modifier)
                data.ClearSelection();

            for (int i = 0; i < hits.Count; i++)
            {
                if (modifier)
                {
                    if (!data.selectedVertices.Contains(hits[i]))
                        data.selectedVertices.Add(hits[i]);
                }
                else
                {
                    data.selectedVertices.Add(hits[i]);
                }
            }

            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        private static void ApplyEdgeHits(IndieEditableMeshData data, List<int> hits, bool modifier)
        {
            if (!modifier)
                data.ClearSelection();

            for (int i = 0; i < hits.Count; i++)
            {
                if (modifier)
                {
                    if (!data.selectedEdges.Contains(hits[i]))
                        data.selectedEdges.Add(hits[i]);
                }
                else
                {
                    data.selectedEdges.Add(hits[i]);
                }
            }

            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
        }

        private static void ApplyFaceHits(IndieEditableMeshData data, List<int> hits, bool modifier)
        {
            if (!modifier)
                data.ClearSelection();

            for (int i = 0; i < hits.Count; i++)
            {
                if (modifier)
                {
                    if (!data.selectedFaces.Contains(hits[i]))
                        data.selectedFaces.Add(hits[i]);
                }
                else
                {
                    data.selectedFaces.Add(hits[i]);
                }
            }

            data.selectedVertices.Clear();
            data.selectedEdges.Clear();
        }

        public static bool TryPickFrontFace(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            out int faceIndex
        )
        {
            Camera camera = sceneView != null ? sceneView.camera : Camera.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            return RaycastFace(uv, ray, camera, out faceIndex) ||
                   PickFaceScreenSpace(uv, camera, mousePosition, out faceIndex);
        }

        /// <summary>
        /// Ignore Back Faces: only corners of the nearest front face under the cursor,
        /// so verts/edges behind the surface cannot win on GUI distance.
        /// </summary>
        private static int PickVertexOnFrontFace(IndieEditUVComponent uv, SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryPickFrontFace(uv, sceneView, mousePosition, out int faceIndex))
                return -1;

            var data = uv.Data;
            var face = data.faces[faceIndex];
            int[] corners = { face.a, face.b, face.c };
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 gui = HandleUtility.WorldToGUIPoint(uv.transform.TransformPoint(data.vertices[corners[i]]));
                float distance = Vector2.Distance(mousePosition, gui);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = corners[i];
                }
            }

            return bestIndex;
        }

        private static int PickEdgeOnFrontFace(IndieEditUVComponent uv, SceneView sceneView, Vector2 mousePosition)
        {
            return TryPickFrontEdge(uv, sceneView, mousePosition, out int v0, out int v1)
                ? FindEdgeIndex(uv.Data, v0, v1)
                : -1;
        }

        /// <summary>
        /// Endpoint of the front-face edge under the cursor nearest in GUI;
        /// equal GUI distance goes to the endpoint closer to the camera.
        /// </summary>
        public static int PickFrontEdgeVertex(IndieEditUVComponent uv, SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryPickFrontEdge(uv, sceneView, mousePosition, out int v0, out int v1))
                return -1;

            Vector3 w0 = uv.transform.TransformPoint(uv.Data.vertices[v0]);
            Vector3 w1 = uv.transform.TransformPoint(uv.Data.vertices[v1]);
            float d0 = Vector2.Distance(mousePosition, HandleUtility.WorldToGUIPoint(w0));
            float d1 = Vector2.Distance(mousePosition, HandleUtility.WorldToGUIPoint(w1));

            if (!Mathf.Approximately(d0, d1))
                return d0 < d1 ? v0 : v1;

            Camera camera = sceneView != null ? sceneView.camera : Camera.current;

            if (camera == null)
                return v0;

            Vector3 eye = camera.transform.position;
            return (w0 - eye).sqrMagnitude <= (w1 - eye).sqrMagnitude ? v0 : v1;
        }

        private static bool TryPickFrontEdge(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition,
            out int bestV0,
            out int bestV1
        )
        {
            bestV0 = -1;
            bestV1 = -1;

            if (!TryPickFrontFace(uv, sceneView, mousePosition, out int faceIndex))
                return false;

            var data = uv.Data;
            var face = data.faces[faceIndex];
            int[] corners = { face.a, face.b, face.c };
            float bestDistance = PickPixelDistance;

            for (int i = 0; i < corners.Length; i++)
            {
                int v0 = corners[i];
                int v1 = corners[(i + 1) % corners.Length];
                float distance = DistancePointSegment(
                    mousePosition,
                    HandleUtility.WorldToGUIPoint(uv.transform.TransformPoint(data.vertices[v0])),
                    HandleUtility.WorldToGUIPoint(uv.transform.TransformPoint(data.vertices[v1]))
                );

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestV0 = v0;
                    bestV1 = v1;
                }
            }

            return bestV0 >= 0;
        }

        private static int FindEdgeIndex(IndieEditableMeshData data, int v0, int v1)
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1) == key)
                    return i;
            }

            return -1;
        }

        private static int PickVertexIndex(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            if (IgnoreBackFaces)
                return PickVertexOnFrontFace(uv, sceneView, mousePosition);

            var data = uv.Data;
            Camera camera = sceneView.camera;
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < data.vertices.Count; i++)
            {
                Vector3 world = uv.transform.TransformPoint(data.vertices[i]);

                if (camera != null && camera.WorldToScreenPoint(world).z <= 0f)
                    continue;

                float distance = Vector2.Distance(
                    mousePosition,
                    HandleUtility.WorldToGUIPoint(world)
                );

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int PickEdgeIndex(
            IndieEditUVComponent uv,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            if (IgnoreBackFaces)
                return PickEdgeOnFrontFace(uv, sceneView, mousePosition);

            var data = uv.Data;
            Camera camera = sceneView.camera;
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

        private static bool RaycastFace(IndieEditUVComponent uv, Ray worldRay, Camera camera, out int faceIndex)
        {
            faceIndex = -1;

            if (uv == null || uv.Data == null)
                return false;

            var data = uv.Data;
            var transform = uv.transform;
            Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = transform.InverseTransformDirection(worldRay.direction);

            if (direction.sqrMagnitude > 1e-8f)
                direction.Normalize();

            float best = float.PositiveInfinity;
            bool cullBack = IgnoreBackFaces && camera != null;

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!TryGetFaceVertices(data, i, out Vector3 v0, out Vector3 v1, out Vector3 v2))
                    continue;

                if (cullBack &&
                    !IndieEditMeshPickMath.IsFrontFacingTriangle(
                        camera,
                        transform.TransformPoint(v0),
                        transform.TransformPoint(v1),
                        transform.TransformPoint(v2)))
                {
                    continue;
                }

                if (IndieEditMeshPickMath.RayTriangle(origin, direction, v0, v1, v2, out float t) && t < best)
                {
                    best = t;
                    faceIndex = i;
                }
            }

            return faceIndex >= 0;
        }

        /// <summary>
        /// Fallback when the ray misses (grazing angles, sub-pixel slivers):
        /// nearest front face whose GUI projection contains the mouse.
        /// </summary>
        private static bool PickFaceScreenSpace(IndieEditUVComponent uv, Camera camera, Vector2 mousePosition, out int faceIndex)
        {
            faceIndex = -1;

            if (uv == null || uv.Data == null || camera == null)
                return false;

            var data = uv.Data;
            var transform = uv.transform;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!TryGetFaceVertices(data, i, out Vector3 v0, out Vector3 v1, out Vector3 v2))
                    continue;

                Vector3 wa = transform.TransformPoint(v0);
                Vector3 wb = transform.TransformPoint(v1);
                Vector3 wc = transform.TransformPoint(v2);

                if (IgnoreBackFaces && !IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
                    continue;

                float depth = camera.WorldToScreenPoint((wa + wb + wc) * (1f / 3f)).z;

                if (depth <= 0f || depth >= bestDepth)
                    continue;

                if (camera.WorldToScreenPoint(wa).z <= 0f ||
                    camera.WorldToScreenPoint(wb).z <= 0f ||
                    camera.WorldToScreenPoint(wc).z <= 0f)
                {
                    continue;
                }

                if (!IndieEditMeshPickMath.PointInTriangle(
                        mousePosition,
                        HandleUtility.WorldToGUIPoint(wa),
                        HandleUtility.WorldToGUIPoint(wb),
                        HandleUtility.WorldToGUIPoint(wc)))
                {
                    continue;
                }

                bestDepth = depth;
                faceIndex = i;
            }

            return faceIndex >= 0;
        }

        private static bool TryGetFaceVertices(
            IndieEditableMeshData data,
            int faceIndex,
            out Vector3 v0,
            out Vector3 v1,
            out Vector3 v2
        )
        {
            var face = data.faces[faceIndex];
            int count = data.vertices.Count;

            if (face.a < 0 || face.b < 0 || face.c < 0 ||
                face.a >= count || face.b >= count || face.c >= count)
            {
                v0 = v1 = v2 = default;
                return false;
            }

            v0 = data.vertices[face.a];
            v1 = data.vertices[face.b];
            v2 = data.vertices[face.c];
            return true;
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

        private static Rect NormalizeRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y)
            );
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b))
                return true;

            Vector2 r0 = new Vector2(rect.xMin, rect.yMin);
            Vector2 r1 = new Vector2(rect.xMax, rect.yMin);
            Vector2 r2 = new Vector2(rect.xMax, rect.yMax);
            Vector2 r3 = new Vector2(rect.xMin, rect.yMax);

            return SegmentsIntersect(a, b, r0, r1) ||
                   SegmentsIntersect(a, b, r1, r2) ||
                   SegmentsIntersect(a, b, r2, r3) ||
                   SegmentsIntersect(a, b, r3, r0);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a);
            float d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c);
            float d4 = Cross(d - c, b - c);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        private static float Cross(Vector2 a, Vector2 b) => IndieEditMeshPickMath.Cross(a, b);
    }
}
