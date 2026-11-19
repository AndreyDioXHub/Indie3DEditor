using System;
using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public enum IndieEditUVRelaxMethod
    {
        PolygonAngles,
        EdgeAngles
    }

    /// <summary>
    /// 3ds Max-style Relax Tool: iteratively moves UV nodes so UV angles match the 3D ones.
    /// Start runs until Stop; Apply runs <see cref="Iterations"/> frames. One gesture = one undo.
    /// Works on selected faces, or on every face when nothing is selected.
    /// </summary>
    public static class IndieEditUVRelax
    {
        public const int MaxIterations = 100000;

        public static IndieEditUVRelaxMethod Method = IndieEditUVRelaxMethod.EdgeAngles;
        public static int Iterations = 100;
        public static float Amount = 0.1f;
        public static float Stretch;
        public static bool KeepBoundary;

        public static bool IsRunning => _component != null;
        public static bool IsContinuous { get; private set; }
        public static int FramesDone { get; private set; }
        public static int FramesTotal { get; private set; }

        private const double PositionQuantum = 1e6;
        private const double UvQuantum = 1e6;
        private const float MaxOpenFan = Mathf.PI * 2f * 0.98f;

        private static IndieEditUVComponent _component;
        private static IndieEditableMeshData _data;
        private static int _vertexCount;

        // UV nodes: Unity vertex splits with equal position + UV act as one texture vertex.
        private static int _nodeCount;
        private static int[] _nodeRep;
        private static int[] _memberStart;
        private static int[] _members;
        private static float[] _nodeSign;
        private static bool[] _nodeBoundary;

        private static int[] _tri;
        private static float[] _triAngle;
        private static float[] _triArea;

        private static int[] _incStart;
        private static int[] _incCorner;

        private static int[] _ringStart;
        private static int[] _ringNeighbors;
        private static float[] _ringAngles;
        private static bool[] _ringClosed;
        private static bool[] _ringValid;

        private static Vector2[] _uv;
        private static Vector2[] _goal;
        private static bool[] _hasGoal;

        public static bool StartContinuous(IndieEditUVComponent uv) => Start(uv, true, 0);

        public static bool StartApply(IndieEditUVComponent uv) => Iterations > 0 && Start(uv, false, Iterations);

        public static void Stop()
        {
            if (_component == null)
                return;

            EditorApplication.update -= Tick;

            var uv = _component;
            _component = null;
            _data = null;
            IsContinuous = false;

            if (uv != null)
                EditorUtility.SetDirty(uv);

            IndieEditUVRelaxWindow.RepaintIfOpen();
        }

        private static bool Start(IndieEditUVComponent uv, bool continuous, int frames)
        {
            if (IsRunning || uv == null || uv.Data == null)
                return false;

            if (IndieEditUVSession.Active != uv || IndieEditUVPelt.IsActive)
                return false;

            if (!Build(uv.Data))
                return false;

            IndieEditUVUndo.Register(uv, "Relax");

            _component = uv;
            _data = uv.Data;
            _vertexCount = uv.Data.vertices.Count;
            IsContinuous = continuous;
            FramesDone = 0;
            FramesTotal = frames;

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return true;
        }

        private static void Tick()
        {
            if (!CanContinue())
            {
                Stop();
                return;
            }

            Step();
            FramesDone++;

            IndieEditUVSession.RefreshUvsOnly(_component);
            IndieEditUVNotify.UvsChanged(_component, _members);
            IndieEditUVRelaxWindow.RepaintIfOpen();

            if (!IsContinuous && FramesDone >= FramesTotal)
                Stop();
        }

        private static bool CanContinue()
        {
            return _component != null &&
                   IndieEditUVSession.Active == _component &&
                   _component.Data == _data &&
                   _data.vertices.Count == _vertexCount &&
                   _data.uvs.Count == _vertexCount &&
                   !IndieEditUVPelt.IsActive;
        }

        #region Build

        private static bool Build(IndieEditableMeshData data)
        {
            int vertexCount = data.vertices.Count;

            if (vertexCount == 0 || data.uvs.Count != vertexCount)
                return false;

            List<int> faces = CollectFaces(data);
            int[] vertexNode = BuildNodes(data, faces);

            if (_nodeCount == 0)
                return false;

            BuildTriangles(data, faces, vertexNode);

            if (_triArea.Length == 0)
                return false;

            BuildIncidence();
            BuildBoundary();
            BuildSigns(data);
            BuildRings();

            _uv = new Vector2[_nodeCount];
            _goal = new Vector2[_nodeCount];
            _hasGoal = new bool[_nodeCount];
            return true;
        }

        private static List<int> CollectFaces(IndieEditableMeshData data)
        {
            var faces = new List<int>();
            int faceCount = data.faces.Count;

            if (data.selectedFaces.Count > 0)
            {
                var seen = new HashSet<int>();

                for (int i = 0; i < data.selectedFaces.Count; i++)
                {
                    int f = data.selectedFaces[i];

                    if (f >= 0 && f < faceCount && seen.Add(f))
                        faces.Add(f);
                }
            }
            else
            {
                for (int f = 0; f < faceCount; f++)
                    faces.Add(f);
            }

            return faces;
        }

        private static int[] BuildNodes(IndieEditableMeshData data, List<int> faces)
        {
            int vertexCount = data.vertices.Count;
            var vertexNode = new int[vertexCount];

            for (int v = 0; v < vertexCount; v++)
                vertexNode[v] = -1;

            var keyToNode = new Dictionary<NodeKey, int>();
            var reps = new List<int>();

            for (int i = 0; i < faces.Count; i++)
            {
                var face = data.faces[faces[i]];

                if (!IsValid(face.a, vertexCount) || !IsValid(face.b, vertexCount) || !IsValid(face.c, vertexCount))
                    continue;

                AssignNode(data, face.a, vertexNode, keyToNode, reps);
                AssignNode(data, face.b, vertexNode, keyToNode, reps);
                AssignNode(data, face.c, vertexNode, keyToNode, reps);
            }

            // Splits outside the face set that share the texture vertex move along.
            for (int v = 0; v < vertexCount; v++)
            {
                if (vertexNode[v] >= 0)
                    continue;

                if (keyToNode.TryGetValue(new NodeKey(data.vertices[v], data.uvs[v]), out int node))
                    vertexNode[v] = node;
            }

            _nodeCount = reps.Count;
            _nodeRep = reps.ToArray();
            _memberStart = new int[_nodeCount + 1];

            for (int v = 0; v < vertexCount; v++)
            {
                if (vertexNode[v] >= 0)
                    _memberStart[vertexNode[v] + 1]++;
            }

            for (int n = 0; n < _nodeCount; n++)
                _memberStart[n + 1] += _memberStart[n];

            _members = new int[_memberStart[_nodeCount]];
            var fill = new int[_nodeCount];

            for (int v = 0; v < vertexCount; v++)
            {
                int node = vertexNode[v];

                if (node >= 0)
                    _members[_memberStart[node] + fill[node]++] = v;
            }

            return vertexNode;
        }

        private static void AssignNode(
            IndieEditableMeshData data,
            int vertex,
            int[] vertexNode,
            Dictionary<NodeKey, int> keyToNode,
            List<int> reps
        )
        {
            if (vertexNode[vertex] >= 0)
                return;

            var key = new NodeKey(data.vertices[vertex], data.uvs[vertex]);

            if (!keyToNode.TryGetValue(key, out int node))
            {
                node = reps.Count;
                reps.Add(vertex);
                keyToNode.Add(key, node);
            }

            vertexNode[vertex] = node;
        }

        private static void BuildTriangles(IndieEditableMeshData data, List<int> faces, int[] vertexNode)
        {
            var tri = new List<int>(faces.Count * 3);
            var angle = new List<float>(faces.Count * 3);
            var area = new List<float>(faces.Count);
            int vertexCount = data.vertices.Count;

            for (int i = 0; i < faces.Count; i++)
            {
                var face = data.faces[faces[i]];

                if (!IsValid(face.a, vertexCount) || !IsValid(face.b, vertexCount) || !IsValid(face.c, vertexCount))
                    continue;

                int na = vertexNode[face.a];
                int nb = vertexNode[face.b];
                int nc = vertexNode[face.c];

                if (na == nb || nb == nc || nc == na)
                    continue;

                Vector3 pa = data.vertices[face.a];
                Vector3 pb = data.vertices[face.b];
                Vector3 pc = data.vertices[face.c];
                Vector3 ab = pb - pa;
                Vector3 ac = pc - pa;
                float crossMagnitude = Vector3.Cross(ab, ac).magnitude;

                if (crossMagnitude <= 1e-7f * ab.magnitude * ac.magnitude)
                    continue;

                tri.Add(na);
                tri.Add(nb);
                tri.Add(nc);
                angle.Add(CornerAngle(pa, pb, pc));
                angle.Add(CornerAngle(pb, pc, pa));
                angle.Add(CornerAngle(pc, pa, pb));
                area.Add(crossMagnitude * 0.5f);
            }

            _tri = tri.ToArray();
            _triAngle = angle.ToArray();
            _triArea = area.ToArray();
        }

        private static void BuildIncidence()
        {
            _incStart = new int[_nodeCount + 1];

            for (int i = 0; i < _tri.Length; i++)
                _incStart[_tri[i] + 1]++;

            for (int n = 0; n < _nodeCount; n++)
                _incStart[n + 1] += _incStart[n];

            _incCorner = new int[_tri.Length];
            var fill = new int[_nodeCount];

            for (int i = 0; i < _tri.Length; i++)
            {
                int node = _tri[i];
                _incCorner[_incStart[node] + fill[node]++] = i;
            }
        }

        private static void BuildBoundary()
        {
            var edgeUse = new Dictionary<long, int>();

            for (int t = 0; t < _triArea.Length; t++)
            {
                for (int c = 0; c < 3; c++)
                {
                    long key = EdgeKey(_tri[t * 3 + c], _tri[t * 3 + (c + 1) % 3]);
                    edgeUse.TryGetValue(key, out int count);
                    edgeUse[key] = count + 1;
                }
            }

            _nodeBoundary = new bool[_nodeCount];

            foreach (var pair in edgeUse)
            {
                if (pair.Value >= 2)
                    continue;

                _nodeBoundary[(int)(pair.Key >> 32)] = true;
                _nodeBoundary[(int)(pair.Key & 0xffffffffL)] = true;
            }
        }

        /// <summary>UV winding per connected island (mirrored islands relax mirrored).</summary>
        private static void BuildSigns(IndieEditableMeshData data)
        {
            var parent = new int[_nodeCount];

            for (int n = 0; n < _nodeCount; n++)
                parent[n] = n;

            for (int t = 0; t < _triArea.Length; t++)
            {
                Union(parent, _tri[t * 3], _tri[t * 3 + 1]);
                Union(parent, _tri[t * 3], _tri[t * 3 + 2]);
            }

            var signedArea = new float[_nodeCount];
            var uvs = data.uvs;

            for (int t = 0; t < _triArea.Length; t++)
            {
                Vector2 a = uvs[_nodeRep[_tri[t * 3]]];
                Vector2 b = uvs[_nodeRep[_tri[t * 3 + 1]]];
                Vector2 c = uvs[_nodeRep[_tri[t * 3 + 2]]];
                signedArea[Find(parent, _tri[t * 3])] += IndieEditMeshPickMath.Cross(b - a, c - a);
            }

            _nodeSign = new float[_nodeCount];

            for (int n = 0; n < _nodeCount; n++)
                _nodeSign[n] = signedArea[Find(parent, n)] < 0f ? -1f : 1f;
        }

        /// <summary>Ordered fan of neighbors around each node, with 3D sector angles.</summary>
        private static void BuildRings()
        {
            var neighbors = new List<int>();
            var angles = new List<float>();
            var byFrom = new Dictionary<int, int>();
            var toSet = new HashSet<int>();
            var sectorFrom = new List<int>();
            var sectorTo = new List<int>();
            var sectorAngle = new List<float>();

            _ringStart = new int[_nodeCount + 1];
            _ringClosed = new bool[_nodeCount];
            _ringValid = new bool[_nodeCount];

            for (int n = 0; n < _nodeCount; n++)
            {
                _ringStart[n] = neighbors.Count;
                sectorFrom.Clear();
                sectorTo.Clear();
                sectorAngle.Clear();
                byFrom.Clear();
                toSet.Clear();

                bool valid = true;

                for (int i = _incStart[n]; i < _incStart[n + 1]; i++)
                {
                    int corner = _incCorner[i];
                    int t = corner / 3;
                    int c = corner % 3;
                    int from = _tri[t * 3 + (c + 1) % 3];
                    int to = _tri[t * 3 + (c + 2) % 3];

                    if (byFrom.ContainsKey(from) || !toSet.Add(to))
                    {
                        valid = false;
                        break;
                    }

                    byFrom.Add(from, sectorFrom.Count);
                    sectorFrom.Add(from);
                    sectorTo.Add(to);
                    sectorAngle.Add(_triAngle[corner]);
                }

                if (!valid || sectorFrom.Count == 0)
                    continue;

                int start = 0;
                bool closed = true;

                for (int s = 0; s < sectorFrom.Count; s++)
                {
                    if (!toSet.Contains(sectorFrom[s]))
                    {
                        start = s;
                        closed = false;
                        break;
                    }
                }

                int ringBegin = neighbors.Count;
                int visited = 0;
                int current = start;
                neighbors.Add(sectorFrom[current]);

                while (true)
                {
                    angles.Add(sectorAngle[current]);
                    visited++;
                    int next = sectorTo[current];

                    if (closed && next == sectorFrom[start])
                        break;

                    neighbors.Add(next);

                    if (!byFrom.TryGetValue(next, out current) || visited >= sectorFrom.Count)
                        break;
                }

                // Open fan: one more neighbor than sectors; pad angles to keep arrays parallel.
                if (!closed)
                    angles.Add(0f);

                if (visited != sectorFrom.Count || (closed && neighbors.Count - ringBegin != visited))
                {
                    neighbors.RemoveRange(ringBegin, neighbors.Count - ringBegin);
                    angles.RemoveRange(ringBegin, angles.Count - ringBegin);
                    continue;
                }

                _ringClosed[n] = closed;
                _ringValid[n] = true;
            }

            _ringStart[_nodeCount] = neighbors.Count;
            _ringNeighbors = neighbors.ToArray();
            _ringAngles = angles.ToArray();
        }

        #endregion

        #region Step

        private static void Step()
        {
            var uvs = _data.uvs;

            for (int n = 0; n < _nodeCount; n++)
                _uv[n] = uvs[_nodeRep[n]];

            float stretch = Mathf.Clamp01(Stretch);
            float amount = Mathf.Clamp01(Amount);

            for (int n = 0; n < _nodeCount; n++)
            {
                _hasGoal[n] = false;

                if (KeepBoundary && _nodeBoundary[n])
                    continue;

                if (_incStart[n] == _incStart[n + 1])
                    continue;

                Vector2 shape;
                bool hasShape = Method == IndieEditUVRelaxMethod.EdgeAngles && _ringValid[n]
                    ? TryEdgeAngleGoal(n, out shape)
                    : TryPolygonAngleGoal(n, out shape);

                if (!hasShape)
                    continue;

                if (stretch > 0f)
                    shape = Vector2.Lerp(shape, CentroidAverage(n), stretch);

                _goal[n] = shape;
                _hasGoal[n] = true;
            }

            for (int n = 0; n < _nodeCount; n++)
            {
                if (!_hasGoal[n])
                    continue;

                Vector2 moved = Vector2.LerpUnclamped(_uv[n], _goal[n], amount);

                for (int m = _memberStart[n]; m < _memberStart[n + 1]; m++)
                    uvs[_members[m]] = moved;
            }
        }

        /// <summary>
        /// Per incident triangle: rebuild this corner from the opposite UV edge using
        /// the 3D angles at the other two corners (law of sines); area-weighted average.
        /// </summary>
        private static bool TryPolygonAngleGoal(int node, out Vector2 goal)
        {
            goal = default;
            Vector2 sum = Vector2.zero;
            float weight = 0f;
            float sign = _nodeSign[node];

            for (int i = _incStart[node]; i < _incStart[node + 1]; i++)
            {
                int corner = _incCorner[i];
                int t = corner / 3;
                int c = corner % 3;
                int cornerB = t * 3 + (c + 1) % 3;
                int cornerC = t * 3 + (c + 2) % 3;

                Vector2 b = _uv[_tri[cornerB]];
                Vector2 edge = _uv[_tri[cornerC]] - b;
                float length = edge.magnitude;

                if (length < 1e-12f)
                    continue;

                float beta = _triAngle[cornerB];
                float gamma = _triAngle[cornerC];
                float sinSum = Mathf.Sin(beta + gamma);

                if (sinSum < 1e-6f)
                    continue;

                float side = length * Mathf.Sin(gamma) / sinSum;
                Vector2 direction = Rotate(edge / length, sign * beta);
                float area = _triArea[t];

                sum += (b + direction * side) * area;
                weight += area;
            }

            if (weight <= 0f)
                return false;

            goal = sum / weight;
            return true;
        }

        /// <summary>
        /// Spokes keep their UV lengths but take the 3D angles between neighbors
        /// (closed fans normalized to 2π), rotated to best fit the current spokes.
        /// </summary>
        private static bool TryEdgeAngleGoal(int node, out Vector2 goal)
        {
            goal = default;
            int start = _ringStart[node];
            int count = _ringStart[node + 1] - start;

            if (count < 2)
                return TryPolygonAngleGoal(node, out goal);

            bool closed = _ringClosed[node];
            int sectors = closed ? count : count - 1;
            float total = 0f;

            for (int i = 0; i < sectors; i++)
                total += _ringAngles[start + i];

            if (total < 1e-6f)
                return TryPolygonAngleGoal(node, out goal);

            float scale = closed ? Mathf.PI * 2f / total : Mathf.Min(1f, MaxOpenFan / total);
            float sign = _nodeSign[node];
            Vector2 p = _uv[node];
            Vector2 fit = Vector2.zero;
            float theta = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector2 spoke = _uv[_ringNeighbors[start + i]] - p;

                if (spoke.sqrMagnitude > 1e-24f)
                {
                    float relative = Mathf.Atan2(spoke.y, spoke.x) - sign * theta;
                    fit += new Vector2(Mathf.Cos(relative), Mathf.Sin(relative));
                }

                if (i < sectors)
                    theta += _ringAngles[start + i] * scale;
            }

            if (fit.sqrMagnitude < 1e-20f)
                return false;

            float phi = Mathf.Atan2(fit.y, fit.x);
            Vector2 sum = Vector2.zero;
            int used = 0;
            theta = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector2 neighbor = _uv[_ringNeighbors[start + i]];
                float length = (neighbor - p).magnitude;

                if (length > 1e-12f)
                {
                    float angle = phi + sign * theta;
                    sum += neighbor - new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                    used++;
                }

                if (i < sectors)
                    theta += _ringAngles[start + i] * scale;
            }

            if (used == 0)
                return false;

            goal = sum / used;
            return true;
        }

        private static Vector2 CentroidAverage(int node)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;

            for (int i = _incStart[node]; i < _incStart[node + 1]; i++)
            {
                int t = _incCorner[i] / 3;
                sum += (_uv[_tri[t * 3]] + _uv[_tri[t * 3 + 1]] + _uv[_tri[t * 3 + 2]]) * (1f / 3f);
                count++;
            }

            return count > 0 ? sum / count : _uv[node];
        }

        #endregion

        #region Helpers

        private static float CornerAngle(Vector3 at, Vector3 a, Vector3 b)
        {
            Vector3 u = a - at;
            Vector3 v = b - at;
            return Mathf.Atan2(Vector3.Cross(u, v).magnitude, Vector3.Dot(u, v));
        }

        private static Vector2 Rotate(Vector2 v, float angle)
        {
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        private static bool IsValid(int index, int count) => index >= 0 && index < count;

        private static long EdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra != rb)
                parent[rb] = ra;
        }

        private readonly struct NodeKey : IEquatable<NodeKey>
        {
            private readonly long _x;
            private readonly long _y;
            private readonly long _z;
            private readonly long _u;
            private readonly long _v;

            public NodeKey(Vector3 position, Vector2 uv)
            {
                _x = (long)Math.Round(position.x * PositionQuantum);
                _y = (long)Math.Round(position.y * PositionQuantum);
                _z = (long)Math.Round(position.z * PositionQuantum);
                _u = (long)Math.Round(uv.x * UvQuantum);
                _v = (long)Math.Round(uv.y * UvQuantum);
            }

            public bool Equals(NodeKey other) =>
                _x == other._x && _y == other._y && _z == other._z && _u == other._u && _v == other._v;

            public override bool Equals(object obj) => obj is NodeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    long hash = _x;
                    hash = hash * 397 ^ _y;
                    hash = hash * 397 ^ _z;
                    hash = hash * 397 ^ _u;
                    hash = hash * 397 ^ _v;
                    return (int)(hash ^ (hash >> 32));
                }
            }
        }

        #endregion
    }
}
