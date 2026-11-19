using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public enum IndieRetopologyPickKind
    {
        Vertex,
        Edge
    }

    public struct IndieRetopologyHit
    {
        public bool valid;
        public IndieRetopologyPickKind kind;
        public int a;
        public int b;
        public int c;
        public int faceIndex;
    }

    /// <summary>
    /// Target-surface raycasts and retopo element picks. Front faces only,
    /// using <see cref="IndieEditMeshPickMath"/> for the shared tests.
    /// </summary>
    public static class IndieRetopologyPick
    {
        public const float PickPixelDistance = 14f;

        private const float BiasFraction = 0.0015f;
        private const float MinBias = 0.002f;

        private static Mesh _cachedMesh;
        private static Vector3[] _localVertices;
        private static int[] _triangles;
        private static Mesh _frontMesh;
        private static Matrix4x4 _frontMatrix;
        private static Matrix4x4 _frontView;
        private static Matrix4x4 _frontProjection;
        private static bool _frontHasCamera;
        private static int[] _frontTris;
        private static int _frontCount;

        private static IndieRetopologyMeshData _maskData;
        private static int _maskRevision = -1;
        private static int _maskVertexCount = -1;
        private static int _maskFaceCount = -1;
        private static bool[] _onFace;
        private static int[] _loose;
        private static int _looseCount;

        private static IndieRetopologyMeshData _screenData;
        private static int _screenRevision = -1;
        private static int _screenVertexCount = -1;
        private static int _screenFaceCount = -1;
        private static Matrix4x4 _screenView;
        private static Matrix4x4 _screenProjection;
        private static Matrix4x4 _screenLocalToWorld;
        private static Vector2[] _guiPoints;
        private static int[] _screenNext;
        private static bool[] _front;
        private static readonly Dictionary<long, int> _screenHead = new Dictionary<long, int>();
        private static readonly Dictionary<long, int> _edgeHead = new Dictionary<long, int>();
        private static readonly Dictionary<long, int> _edgeKeys = new Dictionary<long, int>();
        private static int[] _edgeA;
        private static int[] _edgeB;
        private static int[] _edgeFace;
        private static int _edgeCount;
        private static int[] _edgeNodeNext;
        private static int[] _edgeNodeId;
        private static int _edgeNodeCount;
        private static int[] _edgeVisit;
        private static int _edgeVisitId;
        private static int _cameraEventSerial;
        private static int _settledEventSerial = -1;
        private static bool _settledPoseValid;
        private static Matrix4x4 _settledView;
        private static Matrix4x4 _settledProjection;
        private static Matrix4x4 _settledLocalToWorld;

        public static IndieRetopologyHit CurrentHover { get; private set; }

        public static void NoteCameraEvent()
        {
            _cameraEventSerial++;
        }

        public static bool TryRebuildSettledGrid(IndieRetopologyComponent retopo, Camera camera)
        {
            if (retopo == null || retopo.Data == null || IndieRetopologyDrag.IsDragging)
                return false;

            Matrix4x4 view = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 projection = camera != null ? camera.projectionMatrix : Matrix4x4.identity;
            Matrix4x4 localToWorld = retopo.transform.localToWorldMatrix;
            bool sameEvent = _settledEventSerial == _cameraEventSerial;
            bool samePose = _settledPoseValid &&
                _settledView == view &&
                _settledProjection == projection &&
                _settledLocalToWorld == localToWorld;

            if (!sameEvent || !samePose)
            {
                _settledEventSerial = _cameraEventSerial;
                _settledView = view;
                _settledProjection = projection;
                _settledLocalToWorld = localToWorld;
                _settledPoseValid = true;
                return false;
            }

            if (ScreenGridMatches(retopo, camera))
                return false;

            EnsureScreenGrid(retopo, camera);
            return true;
        }

        public static void InvalidateCache()
        {
            _cachedMesh = null;
            _localVertices = null;
            _triangles = null;
            _frontMesh = null;
            _frontCount = 0;
        }

        public static void ClearHover()
        {
            CurrentHover = default;
        }

        public static bool ScreenGridMatches(IndieRetopologyComponent retopo, Camera camera)
        {
            if (retopo == null || retopo.Data == null || _screenData != retopo.Data)
                return false;

            IndieRetopologyMeshData data = retopo.Data;
            int vertexCount = data.vertices != null ? data.vertices.Count : 0;
            int faceCount = data.faces != null ? data.faces.Count : 0;

            if (vertexCount > 0 && _guiPoints == null)
                return false;

            Matrix4x4 view = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 projection = camera != null ? camera.projectionMatrix : Matrix4x4.identity;
            Matrix4x4 localToWorld = retopo.transform.localToWorldMatrix;

            return _screenRevision == data.revision &&
                _screenVertexCount == vertexCount &&
                _screenFaceCount == faceCount &&
                _screenView == view &&
                _screenProjection == projection &&
                _screenLocalToWorld == localToWorld;
        }

        public static bool TryUpdateCachedHover(
            IndieRetopologyComponent retopo,
            Camera camera,
            Vector2 mousePosition,
            bool highlightEdge)
        {
            if (!ScreenGridMatches(retopo, camera))
                return false;

            if (highlightEdge && retopo.HasFaces)
                CurrentHover = PickFrontEdgeFromGrid(mousePosition);
            else
                CurrentHover = PickVertexFromGrid(mousePosition);

            return true;
        }

        public static void UpdateHover(
            IndieRetopologyComponent retopo,
            SceneView sceneView,
            Vector2 mousePosition,
            bool highlightEdge)
        {
            if (highlightEdge && retopo != null && retopo.HasFaces)
                CurrentHover = PickFrontEdge(retopo, sceneView, mousePosition);
            else
                CurrentHover = PickVertex(retopo, sceneView, mousePosition);
        }

        public static bool TrySurfaceLocal(
            IndieRetopologyComponent retopo,
            Ray worldRay,
            Camera camera,
            out Vector3 localPoint)
        {
            localPoint = default;

            if (!TrySurfaceHit(retopo, worldRay, camera, out Vector3 worldPoint, out Vector3 worldNormal))
                return false;

            localPoint = ToLocal(retopo, worldPoint, worldNormal);
            return true;
        }

        public static IndieRetopologyHit PickVertex(
            IndieRetopologyComponent retopo,
            SceneView sceneView,
            Vector2 mousePosition,
            int exclude = -1,
            HashSet<int> skip = null)
        {
            if (retopo == null || retopo.Data == null || retopo.Data.vertices == null || sceneView == null)
                return default;

            EnsureScreenGrid(retopo, sceneView.camera);
            return PickVertexFromGrid(mousePosition, exclude, skip);
        }

        private static IndieRetopologyHit PickVertexFromGrid(
            Vector2 mousePosition,
            int exclude = -1,
            HashSet<int> skip = null)
        {
            if (_guiPoints == null)
                return default;

            float bestDistance = PickPixelDistance;
            int bestIndex = -1;
            int cellX = Mathf.FloorToInt(mousePosition.x / PickPixelDistance);
            int cellY = Mathf.FloorToInt(mousePosition.y / PickPixelDistance);

            for (int y = cellY - 1; y <= cellY + 1; y++)
            {
                for (int x = cellX - 1; x <= cellX + 1; x++)
                {
                    if (!_screenHead.TryGetValue(ScreenKey(x, y), out int index))
                        continue;

                    while (index >= 0)
                    {
                        ConsiderGui(
                            index,
                            _guiPoints[index],
                            mousePosition,
                            exclude,
                            skip,
                            ref bestDistance,
                            ref bestIndex);
                        index = _screenNext[index];
                    }
                }
            }

            if (bestIndex < 0)
                return default;

            return new IndieRetopologyHit
            {
                valid = true,
                kind = IndieRetopologyPickKind.Vertex,
                a = bestIndex
            };
        }

        public static IndieRetopologyHit PickFrontEdge(
            IndieRetopologyComponent retopo,
            SceneView sceneView,
            Vector2 mousePosition)
        {
            if (retopo == null || retopo.Data == null || retopo.Data.faces == null || sceneView == null)
                return default;

            EnsureScreenGrid(retopo, sceneView.camera);
            return PickFrontEdgeFromGrid(mousePosition);
        }

        private static IndieRetopologyHit PickFrontEdgeFromGrid(Vector2 mousePosition)
        {
            if (_edgeCount == 0 || _edgeA == null || _guiPoints == null)
                return default;

            float bestDistance = PickPixelDistance;
            int bestEdge = -1;
            int cellX = Mathf.FloorToInt(mousePosition.x / PickPixelDistance);
            int cellY = Mathf.FloorToInt(mousePosition.y / PickPixelDistance);
            BeginEdgeVisits();

            for (int y = cellY - 1; y <= cellY + 1; y++)
            {
                for (int x = cellX - 1; x <= cellX + 1; x++)
                {
                    if (!_edgeHead.TryGetValue(ScreenKey(x, y), out int node))
                        continue;

                    while (node >= 0)
                    {
                        int edge = _edgeNodeId[node];
                        node = _edgeNodeNext[node];

                        if (edge < 0 || edge >= _edgeCount || _edgeVisit == null || edge >= _edgeVisit.Length)
                            continue;

                        if (_edgeVisit[edge] == _edgeVisitId)
                            continue;

                        _edgeVisit[edge] = _edgeVisitId;
                        int a = _edgeA[edge];
                        int b = _edgeB[edge];

                        if (a < 0 || b < 0 || a >= _guiPoints.Length || b >= _guiPoints.Length)
                            continue;

                        float distance = DistancePointSegment(mousePosition, _guiPoints[a], _guiPoints[b]);

                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestEdge = edge;
                    }
                }
            }

            if (bestEdge < 0)
                return default;

            return new IndieRetopologyHit
            {
                valid = true,
                kind = IndieRetopologyPickKind.Edge,
                a = _edgeA[bestEdge],
                b = _edgeB[bestEdge],
                faceIndex = _edgeFace[bestEdge]
            };
        }

        private static bool TryPickFrontFace(
            IndieRetopologyComponent retopo,
            SceneView sceneView,
            Vector2 mousePosition,
            out int faceIndex)
        {
            Camera camera = sceneView != null ? sceneView.camera : Camera.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            if (RaycastRetopo(retopo, ray, camera, out faceIndex, out _))
                return true;

            return PickFaceScreenSpace(retopo, camera, mousePosition, out faceIndex);
        }

        private static bool RaycastRetopo(
            IndieRetopologyComponent retopo,
            Ray worldRay,
            Camera camera,
            out int faceIndex,
            out float distance)
        {
            faceIndex = -1;
            distance = 0f;

            var data = retopo != null ? retopo.Data : null;

            if (data == null || data.faces == null || data.faces.Count == 0)
                return false;

            Transform transform = retopo.transform;
            float best = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IsValidVertex(data, face.a) ||
                    !IsValidVertex(data, face.b) ||
                    !IsValidVertex(data, face.c))
                {
                    continue;
                }

                Vector3 wa = transform.TransformPoint(data.vertices[face.a]);
                Vector3 wb = transform.TransformPoint(data.vertices[face.b]);
                Vector3 wc = transform.TransformPoint(data.vertices[face.c]);

                if (camera != null && !IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
                    continue;

                if (!IndieEditMeshPickMath.RayTriangle(
                        worldRay.origin,
                        worldRay.direction,
                        wa,
                        wb,
                        wc,
                        out float hitDistance))
                {
                    continue;
                }

                if (hitDistance >= best)
                    continue;

                best = hitDistance;
                faceIndex = i;
                distance = hitDistance;
            }

            return faceIndex >= 0;
        }

        private static bool PickFaceScreenSpace(
            IndieRetopologyComponent retopo,
            Camera camera,
            Vector2 mousePosition,
            out int faceIndex)
        {
            faceIndex = -1;

            var data = retopo != null ? retopo.Data : null;

            if (data == null || data.faces == null || camera == null)
                return false;

            Transform transform = retopo.transform;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IsValidVertex(data, face.a) ||
                    !IsValidVertex(data, face.b) ||
                    !IsValidVertex(data, face.c))
                {
                    continue;
                }

                Vector3 wa = transform.TransformPoint(data.vertices[face.a]);
                Vector3 wb = transform.TransformPoint(data.vertices[face.b]);
                Vector3 wc = transform.TransformPoint(data.vertices[face.c]);

                if (!IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
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

        private static bool IsOccluded(IndieRetopologyComponent retopo, Camera camera, Vector3 localPoint)
        {
            Vector3 worldPoint = retopo.transform.TransformPoint(localPoint);
            Ray ray = RayThroughWorldPoint(camera, worldPoint);

            if (!RaycastRetopo(retopo, ray, camera, out _, out float hitDistance))
                return false;

            float pointDistance = Vector3.Distance(ray.origin, worldPoint);
            return hitDistance < pointDistance - 0.0001f;
        }

        private static void ConsiderGui(
            int index,
            Vector2 gui,
            Vector2 mousePosition,
            int exclude,
            HashSet<int> skip,
            ref float bestDistance,
            ref int bestIndex)
        {
            if (index == exclude || (skip != null && skip.Contains(index)))
                return;

            float distance = Vector2.Distance(mousePosition, gui);

            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            bestIndex = index;
        }

        public static bool HasLooseVertices(IndieRetopologyMeshData data)
        {
            EnsureFaceMask(data);
            return _looseCount > 0;
        }

        public static int LooseCount(IndieRetopologyMeshData data)
        {
            EnsureFaceMask(data);
            return _looseCount;
        }

        public static int LooseAt(IndieRetopologyMeshData data, int index)
        {
            EnsureFaceMask(data);
            return _loose[index];
        }

        private static bool TrySurfaceHit(
            IndieRetopologyComponent retopo,
            Ray worldRay,
            Camera camera,
            out Vector3 worldPoint,
            out Vector3 worldNormal)
        {
            worldPoint = default;
            worldNormal = Vector3.up;

            if (!EnsureTargetCache(retopo))
                return false;

            Transform source = retopo.TargetTransform;
            Matrix4x4 matrix = source.localToWorldMatrix;
            EnsureFrontTriangles(retopo.TargetMesh, matrix, camera);

            Matrix4x4 inverse = source.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = inverse.MultiplyVector(worldRay.direction);

            if (direction.sqrMagnitude < 1e-12f)
                return false;

            direction.Normalize();

            float best = float.PositiveInfinity;
            int bestTriangle = -1;

            for (int i = 0; i < _frontCount; i++)
            {
                int triangle = _frontTris[i];
                Vector3 a = _localVertices[_triangles[triangle * 3]];
                Vector3 b = _localVertices[_triangles[triangle * 3 + 1]];
                Vector3 c = _localVertices[_triangles[triangle * 3 + 2]];

                if (!IndieEditMeshPickMath.RayTriangle(origin, direction, a, b, c, out float distance))
                    continue;

                if (distance >= best)
                    continue;

                best = distance;
                bestTriangle = triangle;
            }

            if (bestTriangle < 0)
                return false;

            Vector3 wa = matrix.MultiplyPoint3x4(_localVertices[_triangles[bestTriangle * 3]]);
            Vector3 wb = matrix.MultiplyPoint3x4(_localVertices[_triangles[bestTriangle * 3 + 1]]);
            Vector3 wc = matrix.MultiplyPoint3x4(_localVertices[_triangles[bestTriangle * 3 + 2]]);
            Vector3 normal = Vector3.Cross(wb - wa, wc - wa);

            worldPoint = matrix.MultiplyPoint3x4(origin + direction * best);
            worldNormal = normal.sqrMagnitude > 1e-20f ? normal.normalized : Vector3.up;
            return true;
        }

        private static bool EnsureTargetCache(IndieRetopologyComponent retopo)
        {
            Mesh mesh = retopo != null ? retopo.TargetMesh : null;
            Transform source = retopo != null ? retopo.TargetTransform : null;

            if (mesh == null || source == null)
                return false;

            if (_cachedMesh == mesh && _localVertices != null && _triangles != null)
                return _localVertices.Length > 0 && _triangles.Length >= 3;

            Vector3[] local = mesh.vertices;
            int[] triangles = mesh.triangles;

            if (local == null || triangles == null || local.Length == 0 || triangles.Length < 3)
            {
                InvalidateCache();
                return false;
            }

            _cachedMesh = mesh;
            _localVertices = local;
            _triangles = triangles;
            _frontMesh = null;
            return true;
        }

        private static void EnsureFrontTriangles(Mesh mesh, Matrix4x4 matrix, Camera camera)
        {
            bool hasCamera = camera != null;
            Matrix4x4 view = hasCamera ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 projection = hasCamera ? camera.projectionMatrix : Matrix4x4.identity;

            if (_frontMesh == mesh &&
                _frontHasCamera == hasCamera &&
                _frontMatrix == matrix &&
                _frontView == view &&
                _frontProjection == projection)
            {
                return;
            }

            _frontMesh = mesh;
            _frontHasCamera = hasCamera;
            _frontMatrix = matrix;
            _frontView = view;
            _frontProjection = projection;
            _frontCount = 0;

            int triangleCount = _triangles.Length / 3;
            int vertexCount = _localVertices.Length;

            if (_frontTris == null || _frontTris.Length < triangleCount)
                _frontTris = new int[triangleCount];

            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int ia = _triangles[triangle * 3];
                int ib = _triangles[triangle * 3 + 1];
                int ic = _triangles[triangle * 3 + 2];

                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
                    continue;

                if (hasCamera &&
                    !IndieEditMeshPickMath.IsFrontFacingTriangle(
                        camera,
                        matrix.MultiplyPoint3x4(_localVertices[ia]),
                        matrix.MultiplyPoint3x4(_localVertices[ib]),
                        matrix.MultiplyPoint3x4(_localVertices[ic])))
                {
                    continue;
                }

                _frontTris[_frontCount] = triangle;
                _frontCount++;
            }
        }

        private static Vector3 ToLocal(IndieRetopologyComponent retopo, Vector3 worldPoint, Vector3 worldNormal)
        {
            float bias = SurfaceBias(retopo);

            if (worldNormal.sqrMagnitude > 1e-12f)
                worldPoint += worldNormal.normalized * bias;

            return retopo.transform.InverseTransformPoint(worldPoint);
        }

        private static float SurfaceBias(IndieRetopologyComponent retopo)
        {
            Mesh mesh = retopo.TargetMesh;
            Transform source = retopo.TargetTransform;

            if (mesh == null)
                return MinBias;

            Vector3 extents = source != null
                ? source.TransformVector(mesh.bounds.extents)
                : mesh.bounds.extents;

            return Mathf.Max(extents.magnitude * BiasFraction, MinBias);
        }

        private static Ray RayThroughWorldPoint(Camera camera, Vector3 worldPoint)
        {
            if (camera.orthographic)
            {
                Vector3 forward = camera.transform.forward;
                float distance = Vector3.Dot(worldPoint - camera.transform.position, forward);
                Vector3 origin = worldPoint - forward * distance;
                return new Ray(origin, forward);
            }

            Vector3 direction = worldPoint - camera.transform.position;

            if (direction.sqrMagnitude < 1e-12f)
                direction = camera.transform.forward;

            return new Ray(camera.transform.position, direction.normalized);
        }

        private static void EnsureFaceMask(IndieRetopologyMeshData data)
        {
            int vertexCount = data != null && data.vertices != null ? data.vertices.Count : 0;
            int faceCount = data != null && data.faces != null ? data.faces.Count : 0;
            int revision = data != null ? data.revision : 0;

            if (_maskData == data &&
                _maskRevision == revision &&
                _maskVertexCount == vertexCount &&
                _maskFaceCount == faceCount &&
                _onFace != null)
            {
                return;
            }

            _maskData = data;
            _maskRevision = revision;
            _maskVertexCount = vertexCount;
            _maskFaceCount = faceCount;
            _looseCount = 0;

            if (vertexCount == 0)
            {
                _onFace = System.Array.Empty<bool>();
                _loose = System.Array.Empty<int>();
                return;
            }

            if (_onFace == null || _onFace.Length < vertexCount)
                _onFace = new bool[vertexCount];

            System.Array.Clear(_onFace, 0, vertexCount);

            if (data.faces != null)
            {
                for (int i = 0; i < faceCount; i++)
                {
                    IndieRetopologyFace face = data.faces[i];

                    if (face.a >= 0 && face.a < vertexCount)
                        _onFace[face.a] = true;

                    if (face.b >= 0 && face.b < vertexCount)
                        _onFace[face.b] = true;

                    if (face.c >= 0 && face.c < vertexCount)
                        _onFace[face.c] = true;
                }
            }

            if (_loose == null || _loose.Length < vertexCount)
                _loose = new int[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                if (_onFace[i])
                    continue;

                _loose[_looseCount] = i;
                _looseCount++;
            }
        }

        private static void EnsureScreenGrid(IndieRetopologyComponent retopo, Camera camera)
        {
            IndieRetopologyMeshData data = retopo.Data;
            EnsureFaceMask(data);

            int vertexCount = data.vertices != null ? data.vertices.Count : 0;
            int faceCount = data.faces != null ? data.faces.Count : 0;
            Matrix4x4 view = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 projection = camera != null ? camera.projectionMatrix : Matrix4x4.identity;
            Matrix4x4 localToWorld = retopo.transform.localToWorldMatrix;

            if (_screenData == data &&
                _screenRevision == data.revision &&
                _screenVertexCount == vertexCount &&
                _screenFaceCount == faceCount &&
                _screenView == view &&
                _screenProjection == projection &&
                _screenLocalToWorld == localToWorld &&
                _guiPoints != null)
            {
                return;
            }

            _screenData = data;
            _screenRevision = data.revision;
            _screenVertexCount = vertexCount;
            _screenFaceCount = faceCount;
            _screenView = view;
            _screenProjection = projection;
            _screenLocalToWorld = localToWorld;
            _screenHead.Clear();
            _edgeHead.Clear();
            _edgeKeys.Clear();
            _edgeCount = 0;
            _edgeNodeCount = 0;

            if (vertexCount == 0)
                return;

            if (_guiPoints == null || _guiPoints.Length < vertexCount)
                _guiPoints = new Vector2[vertexCount];

            if (_screenNext == null || _screenNext.Length < vertexCount)
                _screenNext = new int[vertexCount];

            if (_front == null || _front.Length < vertexCount)
                _front = new bool[vertexCount];

            System.Array.Clear(_front, 0, vertexCount);

            if (data.faces != null)
            {
                for (int i = 0; i < faceCount; i++)
                {
                    IndieRetopologyFace face = data.faces[i];

                    if (!IsValidVertex(data, face.a) ||
                        !IsValidVertex(data, face.b) ||
                        !IsValidVertex(data, face.c))
                    {
                        continue;
                    }

                    if (camera != null)
                    {
                        Vector3 wa = retopo.transform.TransformPoint(data.vertices[face.a]);
                        Vector3 wb = retopo.transform.TransformPoint(data.vertices[face.b]);
                        Vector3 wc = retopo.transform.TransformPoint(data.vertices[face.c]);

                        if (!IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
                            continue;
                    }

                    _front[face.a] = true;
                    _front[face.b] = true;
                    _front[face.c] = true;
                    AddFrontEdge(face.a, face.b, i);
                    AddFrontEdge(face.b, face.c, i);
                    AddFrontEdge(face.c, face.a, i);
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                bool loose = i < _maskVertexCount && !_onFace[i];

                if (!_front[i] && !loose)
                    continue;

                if (loose && camera != null && IsOccluded(retopo, camera, data.vertices[i]))
                    continue;

                Vector3 world = retopo.transform.TransformPoint(data.vertices[i]);
                Vector2 gui = HandleUtility.WorldToGUIPoint(world);
                _guiPoints[i] = gui;

                long key = ScreenKey(
                    Mathf.FloorToInt(gui.x / PickPixelDistance),
                    Mathf.FloorToInt(gui.y / PickPixelDistance));
                _screenNext[i] = _screenHead.TryGetValue(key, out int head) ? head : -1;
                _screenHead[key] = i;
            }

            if (_edgeVisit == null || _edgeVisit.Length < _edgeCount)
                _edgeVisit = new int[Mathf.Max(_edgeCount, 1)];

            for (int edge = 0; edge < _edgeCount; edge++)
            {
                int a = _edgeA[edge];
                int b = _edgeB[edge];

                if (a < 0 || b < 0 || a >= _guiPoints.Length || b >= _guiPoints.Length)
                    continue;

                if (camera != null && !EdgeInFrontOfCamera(retopo, camera, a, b))
                    continue;

                RasterEdge(_guiPoints[a], _guiPoints[b], edge);
            }
        }

        private static void AddFrontEdge(int a, int b, int face)
        {
            if (a == b)
                return;

            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }

            long key = ((long)a << 32) ^ (uint)b;

            if (_edgeKeys.ContainsKey(key))
                return;

            if (_edgeA == null || _edgeCount >= _edgeA.Length)
            {
                int grown = _edgeA == null ? 256 : _edgeA.Length * 2;
                System.Array.Resize(ref _edgeA, grown);
                System.Array.Resize(ref _edgeB, grown);
                System.Array.Resize(ref _edgeFace, grown);
            }

            _edgeA[_edgeCount] = a;
            _edgeB[_edgeCount] = b;
            _edgeFace[_edgeCount] = face;
            _edgeKeys[key] = _edgeCount;
            _edgeCount++;
        }

        private static bool EdgeInFrontOfCamera(IndieRetopologyComponent retopo, Camera camera, int a, int b)
        {
            var vertices = retopo.Data.vertices;

            if (vertices == null || a >= vertices.Count || b >= vertices.Count)
                return false;

            Vector3 wa = retopo.transform.TransformPoint(vertices[a]);
            Vector3 wb = retopo.transform.TransformPoint(vertices[b]);
            return camera.WorldToScreenPoint(wa).z > 0f && camera.WorldToScreenPoint(wb).z > 0f;
        }

        private static void RasterEdge(Vector2 a, Vector2 b, int edge)
        {
            int x0 = Mathf.FloorToInt(a.x / PickPixelDistance);
            int y0 = Mathf.FloorToInt(a.y / PickPixelDistance);
            int x1 = Mathf.FloorToInt(b.x / PickPixelDistance);
            int y1 = Mathf.FloorToInt(b.y / PickPixelDistance);
            int dx = x1 - x0;
            int dy = y1 - y0;
            int nx = Mathf.Abs(dx);
            int ny = Mathf.Abs(dy);
            int sx = dx > 0 ? 1 : -1;
            int sy = dy > 0 ? 1 : -1;
            int x = x0;
            int y = y0;

            if (nx + ny > 2048)
            {
                LinkEdge(x0, y0, edge);
                LinkEdge(x1, y1, edge);
                return;
            }

            LinkEdge(x, y, edge);

            int ix = 0;
            int iy = 0;

            while (ix < nx || iy < ny)
            {
                float left = nx == 0 ? float.PositiveInfinity : (ix + 0.5f) / nx;
                float right = ny == 0 ? float.PositiveInfinity : (iy + 0.5f) / ny;

                if (left < right)
                {
                    x += sx;
                    ix++;
                }
                else if (right < left)
                {
                    y += sy;
                    iy++;
                }
                else
                {
                    x += sx;
                    y += sy;
                    ix++;
                    iy++;
                }

                LinkEdge(x, y, edge);
            }
        }

        private static void LinkEdge(int x, int y, int edge)
        {
            if (_edgeNodeNext == null || _edgeNodeCount >= _edgeNodeNext.Length)
            {
                int grown = _edgeNodeNext == null ? 256 : _edgeNodeNext.Length * 2;
                System.Array.Resize(ref _edgeNodeNext, grown);
                System.Array.Resize(ref _edgeNodeId, grown);
            }

            long key = ScreenKey(x, y);
            _edgeNodeNext[_edgeNodeCount] = _edgeHead.TryGetValue(key, out int head) ? head : -1;
            _edgeNodeId[_edgeNodeCount] = edge;
            _edgeHead[key] = _edgeNodeCount;
            _edgeNodeCount++;
        }

        private static void BeginEdgeVisits()
        {
            _edgeVisitId++;

            if (_edgeVisitId != int.MaxValue)
                return;

            if (_edgeVisit != null)
                System.Array.Clear(_edgeVisit, 0, _edgeVisit.Length);

            _edgeVisitId = 1;
        }

        private static long ScreenKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private static bool IsValidVertex(IndieRetopologyMeshData data, int index)
        {
            return data != null &&
                   data.vertices != null &&
                   index >= 0 &&
                   index < data.vertices.Count;
        }

        private static float DistancePointSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;

            if (lengthSqr < 0.000001f)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
