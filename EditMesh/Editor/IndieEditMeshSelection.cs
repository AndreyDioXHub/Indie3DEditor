using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshSelection
    {
        private const float PickPixelDistance = 14f;
        private const float MarqueePixelThreshold = 6f;
        private const float OcclusionBarycentricSlack = 1e-4f;
        private const float OcclusionDepthEpsilon = 1e-4f;

        private static bool _mouseDown;
        private static bool _marqueeActive;
        private static Vector2 _marqueeStart;
        private static Vector2 _marqueeCurrent;
        private static bool _marqueeModifiers;

        /// <summary>
        /// When true (default), picks stay on the front surface: the nearest
        /// camera-facing face under the cursor, then only its verts and edges.
        /// </summary>
        public static bool IgnoreBackFaces = true;

        /// <summary>
        /// When true, selection and transforms pull in other verts that share
        /// the same position (UV/hard-edge stacks) without welding.
        /// </summary>
        public static bool LinkCoincident = true;

        private static readonly List<int> CoincidentScratch = new List<int>(16);

        public static bool IsMarqueeActive => _marqueeActive;

        public static Rect GetMarqueeGuiRect()
        {
            return NormalizeRect(_marqueeStart, _marqueeCurrent);
        }

        public static void SelectAll(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return;

            var data = ep.Data;
            data.ClearSelection();

            switch (ep.subMode)
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

            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void ResetMarquee()
        {
            _mouseDown = false;
            _marqueeActive = false;
            _marqueeStart = Vector2.zero;
            _marqueeCurrent = Vector2.zero;
            _marqueeModifiers = false;
        }

        public static void HandleMouseDown(IndieEditMeshComponent ep, Vector2 mousePosition, Event evt)
        {
            if (ep == null || ep.Data == null || evt.button != 0)
                return;

            _mouseDown = true;
            _marqueeActive = false;
            _marqueeStart = mousePosition;
            _marqueeCurrent = mousePosition;
            _marqueeModifiers = evt.shift || evt.control || evt.command;
        }

        public static void HandleMouseDrag(IndieEditMeshComponent ep, Vector2 mousePosition, Event evt)
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
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            Event evt
        )
        {
            if (!_mouseDown || evt.button != 0)
                return;

            _marqueeCurrent = mousePosition;

            if (_marqueeActive)
            {
                ApplyMarqueeSelection(ep, sceneView, GetMarqueeGuiRect(), _marqueeModifiers);
            }
            else
            {
                HandleSceneClick(ep, sceneView, mousePosition, evt);
            }

            ResetMarquee();
            EditorUtility.SetDirty(ep);
            sceneView.Repaint();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void HandleSceneClick(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            Event evt)
        {
            if (ep == null || ep.Data == null)
                return;

            bool modifier = evt.shift || evt.control || evt.command;

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    HandleVertexClick(ep, sceneView, mousePosition, modifier);
                    break;

                case IndieEditMeshSubMode.Edge:
                    HandleEdgeClick(ep, sceneView, mousePosition, modifier);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    HandlePolygonClick(ep, sceneView, mousePosition, modifier);
                    break;
            }

            EditorUtility.SetDirty(ep);
            sceneView.Repaint();
        }

        private static void HandleVertexClick(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier)
        {
            int index = PickVertexIndex(ep, sceneView, mousePosition);

            if (index >= 0)
            {
                if (LinkCoincident)
                    ApplyLinkedVertexClick(ep.Data, index, modifier);
                else if (modifier)
                    ep.Data.ToggleVertexSelection(index);
                else
                    ep.Data.SetVertexSelection(index);
            }
            else if (!modifier)
            {
                ep.Data.ClearSelection();
            }
        }

        private static void ApplyLinkedVertexClick(IndieEditableMeshData data, int index, bool modifier)
        {
            IndieEditMeshMeshUtil.CollectCoincidentVertices(data, index, CoincidentScratch);

            if (modifier)
            {
                bool anySelected = false;

                for (int i = 0; i < CoincidentScratch.Count; i++)
                {
                    if (data.selectedVertices.Contains(CoincidentScratch[i]))
                    {
                        anySelected = true;
                        break;
                    }
                }

                if (anySelected)
                {
                    for (int i = 0; i < CoincidentScratch.Count; i++)
                        data.selectedVertices.Remove(CoincidentScratch[i]);
                }
                else
                {
                    for (int i = 0; i < CoincidentScratch.Count; i++)
                    {
                        int v = CoincidentScratch[i];

                        if (!data.selectedVertices.Contains(v))
                            data.selectedVertices.Add(v);
                    }
                }
            }
            else
            {
                data.selectedVertices.Clear();

                for (int i = 0; i < CoincidentScratch.Count; i++)
                    data.selectedVertices.Add(CoincidentScratch[i]);
            }

            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        private static void HandleEdgeClick(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier)
        {
            int index = PickEdgeIndex(ep, sceneView, mousePosition);

            if (index >= 0)
            {
                if (modifier)
                    ep.Data.ToggleEdgeSelection(index);
                else
                    ep.Data.SetEdgeSelection(index);
            }
            else if (!modifier)
            {
                ep.Data.ClearSelection();
            }
        }

        private static void HandlePolygonClick(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            bool modifier)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            if (RaycastFace(ep, ray, out int faceIndex))
            {
                if (modifier)
                    ep.Data.ToggleFaceSelection(faceIndex);
                else
                    ep.Data.SetFaceSelection(faceIndex);
            }
            else if (!modifier)
            {
                ep.Data.ClearSelection();
            }
        }

        private static void ApplyMarqueeSelection(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Rect rect,
            bool modifier
        )
        {
            if (ep == null || ep.Data == null)
                return;

            if (rect.width < 1f && rect.height < 1f)
                return;

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    ApplyMarqueeVertices(ep, sceneView, rect, modifier);
                    break;

                case IndieEditMeshSubMode.Edge:
                    ApplyMarqueeEdges(ep, sceneView, rect, modifier);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    ApplyMarqueeFaces(ep, sceneView, rect, modifier);
                    break;
            }
        }

        private static void ApplyMarqueeVertices(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Rect rect,
            bool modifier
        )
        {
            var data = ep.Data;
            var hits = new List<int>();
            Camera camera = sceneView.camera;
            BuildFrontFacingMasks(ep, camera, out _, out var frontVerts);

            for (int i = 0; i < data.vertices.Count; i++)
            {
                if (frontVerts != null && !frontVerts.Contains(i))
                    continue;

                Vector3 world = ep.transform.TransformPoint(data.vertices[i]);

                if (camera != null && camera.WorldToScreenPoint(world).z <= 0f)
                    continue;

                Vector2 gui = HandleUtility.WorldToGUIPoint(world);

                if (!rect.Contains(gui))
                    continue;

                if (frontVerts != null && !IsVertexOnNearestFrontFace(ep, camera, i))
                    continue;

                hits.Add(i);
            }

            ApplyIndexListToVertexSelection(data, hits, modifier);
        }

        private static void ApplyMarqueeEdges(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Rect rect,
            bool modifier
        )
        {
            var data = ep.Data;
            var hits = new List<int>();
            Camera camera = sceneView.camera;
            BuildFrontFacingMasks(ep, camera, out var frontEdges, out _);

            for (int i = 0; i < data.edges.Count; i++)
            {
                if (frontEdges != null && !frontEdges.Contains(i))
                    continue;

                var edge = data.edges[i];
                Vector3 world0 = ep.transform.TransformPoint(data.vertices[edge.v0]);
                Vector3 world1 = ep.transform.TransformPoint(data.vertices[edge.v1]);

                if (camera != null)
                {
                    if (camera.WorldToScreenPoint(world0).z <= 0f ||
                        camera.WorldToScreenPoint(world1).z <= 0f)
                    {
                        continue;
                    }
                }

                Vector2 gui0 = HandleUtility.WorldToGUIPoint(world0);
                Vector2 gui1 = HandleUtility.WorldToGUIPoint(world1);
                Vector2 mid = (gui0 + gui1) * 0.5f;

                if (rect.Contains(gui0) || rect.Contains(gui1) || rect.Contains(mid) ||
                    SegmentIntersectsRect(gui0, gui1, rect))
                {
                    if (frontEdges != null && !IsEdgeOnNearestFrontFace(ep, camera, i))
                        continue;

                    hits.Add(i);
                }
            }

            if (!modifier)
                data.ClearSelection();

            foreach (int index in hits)
            {
                if (modifier)
                {
                    if (!data.selectedEdges.Contains(index))
                        data.selectedEdges.Add(index);
                }
                else
                {
                    data.selectedEdges.Add(index);
                }
            }

            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
        }

        private static void ApplyMarqueeFaces(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Rect rect,
            bool modifier
        )
        {
            var data = ep.Data;
            var hits = new List<int>();

            Camera camera = sceneView.camera;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                Vector3 worldA = ep.transform.TransformPoint(data.vertices[face.a]);
                Vector3 worldB = ep.transform.TransformPoint(data.vertices[face.b]);
                Vector3 worldC = ep.transform.TransformPoint(data.vertices[face.c]);

                if (IgnoreBackFaces &&
                    !IsFrontFacingTriangle(camera, worldA, worldB, worldC))
                {
                    continue;
                }

                Vector2 guiA = HandleUtility.WorldToGUIPoint(worldA);
                Vector2 guiB = HandleUtility.WorldToGUIPoint(worldB);
                Vector2 guiC = HandleUtility.WorldToGUIPoint(worldC);
                Vector2 centroid = (guiA + guiB + guiC) / 3f;

                if (rect.Contains(centroid) ||
                    rect.Contains(guiA) ||
                    rect.Contains(guiB) ||
                    rect.Contains(guiC))
                {
                    hits.Add(i);
                }
            }

            if (!modifier)
                data.ClearSelection();

            foreach (int index in hits)
            {
                if (modifier)
                {
                    if (!data.selectedFaces.Contains(index))
                        data.selectedFaces.Add(index);
                }
                else
                {
                    data.selectedFaces.Add(index);
                }
            }

            data.selectedVertices.Clear();
            data.selectedEdges.Clear();
        }

        private static void ApplyIndexListToVertexSelection(
            IndieEditableMeshData data,
            List<int> hits,
            bool modifier
        )
        {
            if (LinkCoincident && hits.Count > 0)
            {
                var expanded = new HashSet<int>(hits);
                IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(data, expanded);
                hits = new List<int>(expanded);
            }

            if (!modifier)
                data.ClearSelection();

            foreach (int index in hits)
            {
                if (modifier)
                {
                    if (!data.selectedVertices.Contains(index))
                        data.selectedVertices.Add(index);
                }
                else
                {
                    data.selectedVertices.Add(index);
                }
            }

            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        private static int PickVertexIndex(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition)
        {
            var data = ep.Data;

            if (data == null)
                return -1;

            if (IgnoreBackFaces)
                return PickVertexOnFrontFace(ep, sceneView, mousePosition);

            Camera camera = sceneView.camera;
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < data.vertices.Count; i++)
            {
                Vector3 worldPosition = ep.transform.TransformPoint(data.vertices[i]);
                Vector3 screenPosition = camera.WorldToScreenPoint(worldPosition);

                if (screenPosition.z <= 0f)
                    continue;

                Vector2 guiPosition = HandleUtility.WorldToGUIPoint(worldPosition);
                float distance = Vector2.Distance(mousePosition, guiPosition);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int PickEdgeIndex(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition)
        {
            var data = ep.Data;

            if (data == null)
                return -1;

            if (IgnoreBackFaces)
                return PickEdgeOnFrontFace(ep, sceneView, mousePosition);

            Camera camera = sceneView.camera;
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                Vector3 world0 = ep.transform.TransformPoint(data.vertices[edge.v0]);
                Vector3 world1 = ep.transform.TransformPoint(data.vertices[edge.v1]);

                Vector3 screen0 = camera.WorldToScreenPoint(world0);
                Vector3 screen1 = camera.WorldToScreenPoint(world1);

                if (screen0.z <= 0f || screen1.z <= 0f)
                    continue;

                Vector2 gui0 = HandleUtility.WorldToGUIPoint(world0);
                Vector2 gui1 = HandleUtility.WorldToGUIPoint(world1);

                float distance = DistancePointSegment(mousePosition, gui0, gui1);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Ignore Back Faces: only corners of the nearest front face under the cursor,
        /// so verts behind the surface cannot win on GUI distance.
        /// </summary>
        private static int PickVertexOnFrontFace(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition)
        {
            if (!TryPickFrontFace(ep, sceneView, mousePosition, out int faceIndex))
                return -1;

            var data = ep.Data;
            var face = data.faces[faceIndex];
            int[] corners = { face.a, face.b, face.c };
            float bestDistance = PickPixelDistance;
            int bestIndex = -1;

            for (int i = 0; i < corners.Length; i++)
            {
                int corner = corners[i];

                if (corner < 0 || corner >= data.vertices.Count)
                    continue;

                Vector2 gui = HandleUtility.WorldToGUIPoint(
                    ep.transform.TransformPoint(data.vertices[corner]));
                float distance = Vector2.Distance(mousePosition, gui);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = corner;
                }
            }

            return bestIndex;
        }

        private static int PickEdgeOnFrontFace(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition)
        {
            return TryPickFrontEdge(ep, sceneView, mousePosition, out int v0, out int v1)
                ? FindEdgeIndex(ep.Data, v0, v1)
                : -1;
        }

        private static bool TryPickFrontFace(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            out int faceIndex)
        {
            Camera camera = sceneView != null ? sceneView.camera : Camera.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            return RaycastFace(ep, ray, out faceIndex) ||
                   PickFaceScreenSpace(ep, camera, mousePosition, out faceIndex);
        }

        /// <summary>
        /// Edges of the front face under the cursor. The edge index is the one
        /// whose endpoints are that face's corners, not a seam duplicate.
        /// </summary>
        private static bool TryPickFrontEdge(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition,
            out int bestV0,
            out int bestV1)
        {
            bestV0 = -1;
            bestV1 = -1;

            if (!TryPickFrontFace(ep, sceneView, mousePosition, out int faceIndex))
                return false;

            var data = ep.Data;
            var face = data.faces[faceIndex];
            int[] corners = { face.a, face.b, face.c };
            float bestDistance = PickPixelDistance;

            for (int i = 0; i < corners.Length; i++)
            {
                int v0 = corners[i];
                int v1 = corners[(i + 1) % corners.Length];

                if (v0 < 0 || v1 < 0 || v0 >= data.vertices.Count || v1 >= data.vertices.Count)
                    continue;

                float distance = DistancePointSegment(
                    mousePosition,
                    HandleUtility.WorldToGUIPoint(ep.transform.TransformPoint(data.vertices[v0])),
                    HandleUtility.WorldToGUIPoint(ep.transform.TransformPoint(data.vertices[v1]))
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

        public static int FindEdgeIndex(IndieEditableMeshData data, int v0, int v1)
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

        /// <summary>
        /// Fallback when the ray misses (grazing angles, sub-pixel slivers):
        /// nearest front face whose GUI projection contains the mouse.
        /// </summary>
        private static bool PickFaceScreenSpace(
            IndieEditMeshComponent ep,
            Camera camera,
            Vector2 mousePosition,
            out int faceIndex)
        {
            faceIndex = -1;

            if (ep == null || ep.Data == null || camera == null)
                return false;

            var data = ep.Data;
            var transform = ep.transform;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.b < 0 || face.c < 0 ||
                    face.a >= data.vertices.Count ||
                    face.b >= data.vertices.Count ||
                    face.c >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 wa = transform.TransformPoint(data.vertices[face.a]);
                Vector3 wb = transform.TransformPoint(data.vertices[face.b]);
                Vector3 wc = transform.TransformPoint(data.vertices[face.c]);

                if (!IsFrontFacingTriangle(camera, wa, wb, wc))
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

        private static bool IsVertexOnNearestFrontFace(
            IndieEditMeshComponent ep,
            Camera camera,
            int vertexIndex)
        {
            var data = ep.Data;

            if (camera == null || data == null ||
                vertexIndex < 0 || vertexIndex >= data.vertices.Count)
            {
                return false;
            }

            Vector3 world = ep.transform.TransformPoint(data.vertices[vertexIndex]);
            return IsOwnedByNearestFrontFace(ep, camera, world, vertexIndex, -1);
        }

        private static bool IsEdgeOnNearestFrontFace(
            IndieEditMeshComponent ep,
            Camera camera,
            int edgeIndex)
        {
            var data = ep.Data;

            if (camera == null || data == null ||
                edgeIndex < 0 || edgeIndex >= data.edges.Count)
            {
                return false;
            }

            var edge = data.edges[edgeIndex];

            if (edge.v0 < 0 || edge.v1 < 0 ||
                edge.v0 >= data.vertices.Count || edge.v1 >= data.vertices.Count)
            {
                return false;
            }

            Vector3 world = ep.transform.TransformPoint(
                (data.vertices[edge.v0] + data.vertices[edge.v1]) * 0.5f);
            return IsOwnedByNearestFrontFace(ep, camera, world, edge.v0, edge.v1);
        }

        /// <summary>
        /// The element is on the visible front surface when the closest front face
        /// along a ray through it is a face that owns the element.
        /// A negative second index selects a vertex; otherwise the two indices are an edge.
        /// </summary>
        private static bool IsOwnedByNearestFrontFace(
            IndieEditMeshComponent ep,
            Camera camera,
            Vector3 worldPoint,
            int index0,
            int index1)
        {
            var data = ep.Data;
            Ray worldRay = RayThroughWorldPoint(camera, worldPoint);
            Vector3 origin = ep.transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = ep.transform.InverseTransformDirection(worldRay.direction);

            if (direction.sqrMagnitude > 0.000001f)
                direction.Normalize();

            long edgeKey = index1 >= 0 ? IndieEditMeshMeshUtil.EdgeKey(index0, index1) : 0L;
            float bestAny = float.PositiveInfinity;
            float bestOwn = float.PositiveInfinity;
            Matrix4x4 localToWorld = ep.transform.localToWorldMatrix;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.b < 0 || face.c < 0 ||
                    face.a >= data.vertices.Count ||
                    face.b >= data.vertices.Count ||
                    face.c >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 v0 = data.vertices[face.a];
                Vector3 v1 = data.vertices[face.b];
                Vector3 v2 = data.vertices[face.c];
                Vector3 wa = localToWorld.MultiplyPoint3x4(v0);
                Vector3 wb = localToWorld.MultiplyPoint3x4(v1);
                Vector3 wc = localToWorld.MultiplyPoint3x4(v2);

                if (!IsFrontFacingTriangle(camera, wa, wb, wc))
                    continue;

                if (!IndieEditMeshPickMath.RayTriangle(
                        origin,
                        direction,
                        v0,
                        v1,
                        v2,
                        out float distance,
                        OcclusionBarycentricSlack) ||
                    distance <= 0f)
                {
                    continue;
                }

                if (distance < bestAny)
                    bestAny = distance;

                if (distance < bestOwn && FaceOwnsElement(face, index0, index1, edgeKey))
                    bestOwn = distance;
            }

            return bestOwn < float.PositiveInfinity &&
                   bestOwn <= bestAny + OcclusionDepthEpsilon;
        }

        private static bool FaceOwnsElement(IndieEditMeshFace face, int index0, int index1, long edgeKey)
        {
            if (index1 < 0)
                return face.a == index0 || face.b == index0 || face.c == index0;

            return IndieEditMeshMeshUtil.EdgeKey(face.a, face.b) == edgeKey ||
                   IndieEditMeshMeshUtil.EdgeKey(face.b, face.c) == edgeKey ||
                   IndieEditMeshMeshUtil.EdgeKey(face.c, face.a) == edgeKey;
        }

        private static Ray RayThroughWorldPoint(Camera camera, Vector3 world)
        {
            Transform cameraTransform = camera.transform;
            Vector3 forward = cameraTransform.forward;

            if (camera.orthographic)
            {
                float depth = Vector3.Dot(world - cameraTransform.position, forward);
                return new Ray(world - forward * (depth + 1f), forward);
            }

            Vector3 direction = world - cameraTransform.position;

            if (direction.sqrMagnitude < 1e-12f)
                direction = forward;

            return new Ray(cameraTransform.position, direction);
        }

        /// <summary>
        /// Marquee prefilter: elements that belong to at least one camera-facing triangle.
        /// Occlusion is tested afterwards with a ray through the element.
        /// </summary>
        private static void BuildFrontFacingMasks(
            IndieEditMeshComponent ep,
            Camera camera,
            out HashSet<int> frontEdges,
            out HashSet<int> frontVerts
        )
        {
            frontEdges = null;
            frontVerts = null;

            if (!IgnoreBackFaces || ep == null || ep.Data == null || camera == null)
                return;

            var data = ep.Data;
            Transform cameraTransform = camera.transform;
            Matrix4x4 localToWorld = ep.transform.localToWorldMatrix;

            if (_frontData == data &&
                _frontVersion == IndieEditMeshGeometryCache.Version &&
                _frontVertexCount == data.vertices.Count &&
                _frontFaceCount == data.faces.Count &&
                _frontEdgeCount == data.edges.Count &&
                _frontOrthographic == camera.orthographic &&
                _frontCameraPosition == cameraTransform.position &&
                _frontCameraForward == cameraTransform.forward &&
                _frontLocalToWorld == localToWorld)
            {
                frontEdges = FrontEdgesCache;
                frontVerts = FrontVertsCache;
                return;
            }

            var keyToEdge = new Dictionary<long, int>(data.edges.Count);

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (!keyToEdge.ContainsKey(key))
                    keyToEdge.Add(key, i);
            }

            FrontEdgesCache.Clear();
            FrontVertsCache.Clear();
            frontEdges = FrontEdgesCache;
            frontVerts = FrontVertsCache;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.a >= data.vertices.Count ||
                    face.b < 0 || face.b >= data.vertices.Count ||
                    face.c < 0 || face.c >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 wa = localToWorld.MultiplyPoint3x4(data.vertices[face.a]);
                Vector3 wb = localToWorld.MultiplyPoint3x4(data.vertices[face.b]);
                Vector3 wc = localToWorld.MultiplyPoint3x4(data.vertices[face.c]);

                if (!IsFrontFacingTriangle(camera, wa, wb, wc))
                    continue;

                frontVerts.Add(face.a);
                frontVerts.Add(face.b);
                frontVerts.Add(face.c);

                TryAddFrontEdge(keyToEdge, frontEdges, face.a, face.b);
                TryAddFrontEdge(keyToEdge, frontEdges, face.b, face.c);
                TryAddFrontEdge(keyToEdge, frontEdges, face.c, face.a);
            }

            _frontData = data;
            _frontVersion = IndieEditMeshGeometryCache.Version;
            _frontVertexCount = data.vertices.Count;
            _frontFaceCount = data.faces.Count;
            _frontEdgeCount = data.edges.Count;
            _frontOrthographic = camera.orthographic;
            _frontCameraPosition = cameraTransform.position;
            _frontCameraForward = cameraTransform.forward;
            _frontLocalToWorld = localToWorld;
        }

        // Front-facing masks are reused until the camera, object transform or mesh changes
        // (hover picking in Cut / repeated clicks no longer walk every face each time).
        private static readonly HashSet<int> FrontEdgesCache = new HashSet<int>();
        private static readonly HashSet<int> FrontVertsCache = new HashSet<int>();
        private static IndieEditableMeshData _frontData;
        private static int _frontVersion;
        private static int _frontVertexCount = -1;
        private static int _frontFaceCount = -1;
        private static int _frontEdgeCount = -1;
        private static bool _frontOrthographic;
        private static Vector3 _frontCameraPosition;
        private static Vector3 _frontCameraForward;
        private static Matrix4x4 _frontLocalToWorld;

        private static void TryAddFrontEdge(
            Dictionary<long, int> keyToEdge,
            HashSet<int> frontEdges,
            int v0,
            int v1
        )
        {
            if (keyToEdge.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(v0, v1), out int edgeIndex))
                frontEdges.Add(edgeIndex);
        }

        private static bool IsFrontFacingTriangle(Camera camera, Vector3 worldA, Vector3 worldB, Vector3 worldC) =>
            IndieEditMeshPickMath.IsFrontFacingTriangle(camera, worldA, worldB, worldC);

        private static float DistancePointSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;

            if (lengthSqr < 0.000001f)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            Vector2 projected = a + ab * t;

            return Vector2.Distance(point, projected);
        }

        public static int PickVertex(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            return PickVertexIndex(ep, sceneView, mousePosition);
        }

        public static int PickEdge(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            return PickEdgeIndex(ep, sceneView, mousePosition);
        }

        /// <summary>
        /// Endpoint of the edge under the cursor, nearest in GUI space.
        /// With Ignore Back Faces, only the front-face edge is considered;
        /// equal GUI distance goes to the endpoint closer to the camera.
        /// </summary>
        public static int PickEdgeVertex(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Vector2 mousePosition
        )
        {
            if (ep == null || ep.Data == null)
                return -1;

            int v0;
            int v1;

            if (IgnoreBackFaces)
            {
                if (!TryPickFrontEdge(ep, sceneView, mousePosition, out v0, out v1))
                    return -1;
            }
            else
            {
                int edgeIndex = PickEdgeIndex(ep, sceneView, mousePosition);

                if (edgeIndex < 0)
                    return -1;

                var edge = ep.Data.edges[edgeIndex];
                v0 = edge.v0;
                v1 = edge.v1;
            }

            if (v0 < 0 || v1 < 0 ||
                v0 >= ep.Data.vertices.Count ||
                v1 >= ep.Data.vertices.Count)
            {
                return -1;
            }

            Vector3 w0 = ep.transform.TransformPoint(ep.Data.vertices[v0]);
            Vector3 w1 = ep.transform.TransformPoint(ep.Data.vertices[v1]);
            float d0 = Vector2.Distance(mousePosition, HandleUtility.WorldToGUIPoint(w0));
            float d1 = Vector2.Distance(mousePosition, HandleUtility.WorldToGUIPoint(w1));

            if (!IgnoreBackFaces)
                return d0 <= d1 ? v0 : v1;

            if (!Mathf.Approximately(d0, d1))
                return d0 < d1 ? v0 : v1;

            Camera camera = sceneView != null ? sceneView.camera : Camera.current;

            if (camera == null)
                return v0;

            Vector3 eye = camera.transform.position;
            return (w0 - eye).sqrMagnitude <= (w1 - eye).sqrMagnitude ? v0 : v1;
        }

        public static float GetEdgeParam(
            IndieEditMeshComponent ep,
            int edgeIndex,
            Vector2 mousePosition
        )
        {
            if (ep == null || ep.Data == null ||
                edgeIndex < 0 || edgeIndex >= ep.Data.edges.Count)
            {
                return 0.5f;
            }

            var edge = ep.Data.edges[edgeIndex];
            Vector3 world0 = ep.transform.TransformPoint(ep.Data.vertices[edge.v0]);
            Vector3 world1 = ep.transform.TransformPoint(ep.Data.vertices[edge.v1]);
            Vector2 gui0 = HandleUtility.WorldToGUIPoint(world0);
            Vector2 gui1 = HandleUtility.WorldToGUIPoint(world1);
            Vector2 ab = gui1 - gui0;
            float lengthSqr = ab.sqrMagnitude;

            if (lengthSqr < 1e-8f)
                return 0.5f;

            return Mathf.Clamp01(Vector2.Dot(mousePosition - gui0, ab) / lengthSqr);
        }

        public static bool RaycastFace(IndieEditMeshComponent ep, Ray worldRay, out int faceIndex)
        {
            return RaycastFace(ep, worldRay, out faceIndex, out _);
        }

        public static bool RaycastFace(
            IndieEditMeshComponent ep,
            Ray worldRay,
            out int faceIndex,
            out Vector3 localPoint
        )
        {
            faceIndex = -1;
            localPoint = Vector3.zero;

            if (ep == null || ep.Data == null)
                return false;

            var data = ep.Data;

            if (data.vertices.Count == 0 || data.faces.Count == 0)
                return false;

            Vector3 origin = ep.transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = ep.transform.InverseTransformDirection(worldRay.direction);

            if (direction.sqrMagnitude > 0.000001f)
                direction.Normalize();

            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.a >= data.vertices.Count ||
                    face.b < 0 || face.b >= data.vertices.Count ||
                    face.c < 0 || face.c >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 v0 = data.vertices[face.a];
                Vector3 v1 = data.vertices[face.b];
                Vector3 v2 = data.vertices[face.c];

                // World-space front-face cull — same as wireframe Cull Back.
                Vector3 wa = ep.transform.TransformPoint(v0);
                Vector3 wb = ep.transform.TransformPoint(v1);
                Vector3 wc = ep.transform.TransformPoint(v2);
                if (IgnoreBackFaces)
                {
                    Camera camera = Camera.current != null
                        ? Camera.current
                        : SceneView.lastActiveSceneView != null
                            ? SceneView.lastActiveSceneView.camera
                            : null;

                    if (camera != null && !IsFrontFacingTriangle(camera, wa, wb, wc))
                        continue;
                }

                if (RayTriangle(origin, direction, v0, v1, v2, out float distance))
                {
                    if (distance > 0f && distance < bestDistance)
                    {
                        bestDistance = distance;
                        faceIndex = i;
                        localPoint = origin + direction * distance;
                    }
                }
            }

            return faceIndex >= 0;
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 v0, Vector3 v1, Vector3 v2, out float distance) =>
            IndieEditMeshPickMath.RayTriangle(origin, direction, v0, v1, v2, out distance);

        public static int[] GetAffectedVertices(
            IndieEditableMeshData data,
            IndieEditMeshSubMode mode,
            bool expandCoincident = true
        )
        {
            var set = new HashSet<int>();

            if (data == null)
                return new int[0];

            switch (mode)
            {
                case IndieEditMeshSubMode.Vertex:
                    foreach (int index in data.selectedVertices)
                    {
                        if (index >= 0 && index < data.vertices.Count)
                            set.Add(index);
                    }
                    break;

                case IndieEditMeshSubMode.Edge:
                    foreach (int edgeIndex in data.selectedEdges)
                    {
                        if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                            continue;

                        var edge = data.edges[edgeIndex];

                        if (edge.v0 >= 0 && edge.v0 < data.vertices.Count)
                            set.Add(edge.v0);

                        if (edge.v1 >= 0 && edge.v1 < data.vertices.Count)
                            set.Add(edge.v1);
                    }
                    break;

                case IndieEditMeshSubMode.Polygon:
                    foreach (int faceIndex in data.selectedFaces)
                    {
                        if (faceIndex < 0 || faceIndex >= data.faces.Count)
                            continue;

                        var face = data.faces[faceIndex];

                        if (face.a >= 0 && face.a < data.vertices.Count)
                            set.Add(face.a);

                        if (face.b >= 0 && face.b < data.vertices.Count)
                            set.Add(face.b);

                        if (face.c >= 0 && face.c < data.vertices.Count)
                            set.Add(face.c);
                    }
                    break;
            }

            // Edge/Polygon: pull stacked UV/hard-edge verts along the selection perimeter.
            // Skip for Shift+Extrude: new caps start on top of the base and must move alone.
            if (expandCoincident && LinkCoincident)
                IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(data, set);

            var result = new int[set.Count];
            set.CopyTo(result);

            return result;
        }

        private static Rect NormalizeRect(Vector2 a, Vector2 b)
        {
            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float width = Mathf.Abs(a.x - b.x);
            float height = Mathf.Abs(a.y - b.y);

            return new Rect(xMin, yMin, width, height);
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
