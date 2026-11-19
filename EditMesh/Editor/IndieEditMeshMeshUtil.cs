using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshMeshUtil
    {
        public static long EdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);

            return (((long)min) << 32) | (uint)max;
        }

        public static void DecodeEdgeKey(long key, out int v0, out int v1)
        {
            v0 = (int)(key >> 32);
            v1 = (int)(uint)key;
        }

        public static int AddVertex(IndieEditableMeshData data, int sourceIndex, Vector3 position)
        {
            data.vertices.Add(position);
            data.uvs.Add(sourceIndex < data.uvs.Count ? data.uvs[sourceIndex] : Vector2.zero);
            data.normals.Add(sourceIndex < data.normals.Count ? data.normals[sourceIndex] : Vector3.up);
            data.tangents.Add(
                sourceIndex < data.tangents.Count
                    ? data.tangents[sourceIndex]
                    : new Vector4(1f, 0f, 0f, -1f)
            );
            data.colors.Add(sourceIndex < data.colors.Count ? data.colors[sourceIndex] : Color.white);

            return data.vertices.Count - 1;
        }

        /// <summary>
        /// Fill missing UV0 slots with (0, 0) so the channel matches vertex count.
        /// A full or longer channel is left as-is.
        /// </summary>
        public static void EnsureUvChannel(IndieEditableMeshData data)
        {
            if (data == null)
                return;

            while (data.uvs.Count < data.vertices.Count)
                data.uvs.Add(Vector2.zero);
        }

        /// <summary>
        /// Weld vertices that share (nearly) the same position into one.
        /// UV seams / vertex colors are never collapsed: only verts with matching
        /// attributes merge. Hard-edge normal splits with the same UV still weld;
        /// smoothing groups restore hard shading on rebuild.
        /// </summary>
        public static void WeldCoincidentVertices(IndieEditableMeshData data, float threshold)
        {
            if (data == null || data.vertices.Count < 2)
                return;

            float thresholdSq = Mathf.Max(0f, threshold) * Mathf.Max(0f, threshold);
            const float uvEpsilonSq = 1e-8f;
            const float colorEpsilonSq = 1e-6f;
            bool hasUv = data.uvs.Count == data.vertices.Count;
            bool hasColor = data.colors.Count == data.vertices.Count;
            int count = data.vertices.Count;
            int[] parent = new int[count];

            for (int i = 0; i < count; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

                if (ra != rb)
                    parent[rb] = ra;
            }

            bool CanWeldAttributes(int a, int b)
            {
                if (hasUv && (data.uvs[a] - data.uvs[b]).sqrMagnitude > uvEpsilonSq)
                    return false;

                if (hasColor)
                {
                    Color ca = data.colors[a];
                    Color cb = data.colors[b];
                    float dr = ca.r - cb.r;
                    float dg = ca.g - cb.g;
                    float db = ca.b - cb.b;
                    float da = ca.a - cb.a;

                    if (dr * dr + dg * dg + db * db + da * da > colorEpsilonSq)
                        return false;
                }

                return true;
            }

            // Spatial hash buckets to avoid O(n²) on large meshes.
            var buckets = new Dictionary<long, List<int>>();
            float inv = threshold > 1e-12f ? 1f / threshold : 1e6f;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = data.vertices[i];
                int bx = Mathf.FloorToInt(p.x * inv);
                int by = Mathf.FloorToInt(p.y * inv);
                int bz = Mathf.FloorToInt(p.z * inv);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = PackBucket(bx + dx, by + dy, bz + dz);

                    if (!buckets.TryGetValue(key, out var list))
                        continue;

                    for (int k = 0; k < list.Count; k++)
                    {
                        int j = list[k];

                        if ((data.vertices[i] - data.vertices[j]).sqrMagnitude > thresholdSq)
                            continue;

                        if (!CanWeldAttributes(i, j))
                            continue;

                        Union(i, j);
                    }
                }

                long selfKey = PackBucket(bx, by, bz);

                if (!buckets.TryGetValue(selfKey, out var selfList))
                {
                    selfList = new List<int>(4);
                    buckets.Add(selfKey, selfList);
                }

                selfList.Add(i);
            }

            var clusters = new Dictionary<int, List<int>>();

            for (int i = 0; i < count; i++)
            {
                int root = Find(i);

                if (!clusters.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    clusters.Add(root, list);
                }

                list.Add(i);
            }

            int[] remap = new int[count];

            for (int i = 0; i < count; i++)
                remap[i] = i;

            bool anyMerge = false;

            foreach (var cluster in clusters.Values)
            {
                if (cluster.Count < 2)
                    continue;

                anyMerge = true;
                Vector3 avgPos = Vector3.zero;
                Vector3 avgNormal = Vector3.zero;
                Vector4 avgTangent = Vector4.zero;

                for (int c = 0; c < cluster.Count; c++)
                {
                    int index = cluster[c];
                    avgPos += data.vertices[index];
                    avgNormal += index < data.normals.Count ? data.normals[index] : Vector3.up;
                    avgTangent += index < data.tangents.Count
                        ? data.tangents[index]
                        : new Vector4(1f, 0f, 0f, -1f);
                }

                float invCount = 1f / cluster.Count;
                int target = cluster[0];

                // UV/color already matched — keep target's attributes; only blend pos/normal/tangent.
                data.vertices[target] = avgPos * invCount;

                if (target < data.normals.Count)
                {
                    Vector3 n = avgNormal * invCount;
                    data.normals[target] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                }

                if (target < data.tangents.Count)
                    data.tangents[target] = avgTangent * invCount;

                for (int c = 1; c < cluster.Count; c++)
                    remap[cluster[c]] = target;
            }

            if (!anyMerge)
                return;

            var newFaces = new List<IndieEditMeshFace>(data.faces.Count);

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddTriangle(
                    newFaces,
                    remap[face.a],
                    remap[face.b],
                    remap[face.c],
                    face.materialId,
                    face.smoothingGroups
                );
            }

            data.faces = newFaces;
            data.ClearSelection();
            CleanupUnusedVertices(data);
        }

        /// <summary>
        /// Adds every vertex that shares (nearly) the same position as any seed.
        /// Used by Link Coincident transform/selection — does not weld or touch UVs.
        /// </summary>
        public static void ExpandWithCoincidentPositions(
            IndieEditableMeshData data,
            HashSet<int> indices,
            float threshold = 1e-5f
        )
        {
            if (data == null || indices == null || indices.Count == 0 || data.vertices.Count == 0)
                return;

            float thresholdSq = Mathf.Max(0f, threshold) * Mathf.Max(0f, threshold);
            float inv = threshold > 1e-12f ? 1f / threshold : 1e6f;
            var buckets = GetCoincidentBuckets(data, threshold, inv);
            var seeds = new List<int>(indices);

            for (int s = 0; s < seeds.Count; s++)
            {
                int seed = seeds[s];

                if (seed < 0 || seed >= data.vertices.Count)
                    continue;

                Vector3 p = data.vertices[seed];
                int bx = Mathf.FloorToInt(p.x * inv);
                int by = Mathf.FloorToInt(p.y * inv);
                int bz = Mathf.FloorToInt(p.z * inv);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = PackBucket(bx + dx, by + dy, bz + dz);

                    if (!buckets.TryGetValue(key, out var list))
                        continue;

                    for (int k = 0; k < list.Count; k++)
                    {
                        int other = list[k];

                        if (indices.Contains(other))
                            continue;

                        if ((data.vertices[other] - p).sqrMagnitude <= thresholdSq)
                            indices.Add(other);
                    }
                }
            }
        }

        private static readonly Dictionary<long, List<int>> CoincidentBuckets =
            new Dictionary<long, List<int>>();

        private static IndieEditableMeshData _coincidentData;
        private static List<Vector3> _coincidentVertices;
        private static int _coincidentVertexCount = -1;
        private static int _coincidentVersion;
        private static float _coincidentThreshold = -1f;

        /// <summary>
        /// Spatial hash of every vertex, rebuilt only when the geometry version,
        /// mesh data, vertex list or threshold changes (not on every SceneGUI event).
        /// </summary>
        private static Dictionary<long, List<int>> GetCoincidentBuckets(
            IndieEditableMeshData data,
            float threshold,
            float inv
        )
        {
            if (_coincidentData == data &&
                _coincidentVertices == data.vertices &&
                _coincidentVertexCount == data.vertices.Count &&
                _coincidentVersion == IndieEditMeshGeometryCache.Version &&
                Mathf.Approximately(_coincidentThreshold, threshold))
            {
                return CoincidentBuckets;
            }

            CoincidentBuckets.Clear();

            for (int i = 0; i < data.vertices.Count; i++)
            {
                Vector3 p = data.vertices[i];
                long key = PackBucket(
                    Mathf.FloorToInt(p.x * inv),
                    Mathf.FloorToInt(p.y * inv),
                    Mathf.FloorToInt(p.z * inv)
                );

                if (!CoincidentBuckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    CoincidentBuckets.Add(key, list);
                }

                list.Add(i);
            }

            _coincidentData = data;
            _coincidentVertices = data.vertices;
            _coincidentVertexCount = data.vertices.Count;
            _coincidentVersion = IndieEditMeshGeometryCache.Version;
            _coincidentThreshold = threshold;

            return CoincidentBuckets;
        }

        public static void CollectCoincidentVertices(
            IndieEditableMeshData data,
            int seedIndex,
            List<int> into,
            float threshold = 1e-5f
        )
        {
            into.Clear();

            if (data == null || seedIndex < 0 || seedIndex >= data.vertices.Count)
                return;

            var set = new HashSet<int> { seedIndex };
            ExpandWithCoincidentPositions(data, set, threshold);

            foreach (int index in set)
                into.Add(index);
        }

        private static long PackBucket(int x, int y, int z)
        {
            unchecked
            {
                return ((long)(x * 73856093)) ^ ((long)(y * 19349663) << 21) ^ ((long)(z * 83492791) << 42);
            }
        }

        /// <summary>
        /// Assign smoothing groups by face angle so welded topology still renders hard edges.
        /// </summary>
        public static void AutoSmoothByAngle(IndieEditableMeshData data, float angleDegrees)
        {
            if (data == null || data.faces.Count == 0)
                return;

            int faceCount = data.faces.Count;
            var normals = new Vector3[faceCount];

            for (int i = 0; i < faceCount; i++)
            {
                var face = data.faces[i];
                Vector3 n = Vector3.Cross(
                    data.vertices[face.b] - data.vertices[face.a],
                    data.vertices[face.c] - data.vertices[face.a]
                );
                normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            }

            int[] parent = new int[faceCount];

            for (int i = 0; i < faceCount; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

                if (ra != rb)
                    parent[rb] = ra;
            }

            float minDot = Mathf.Cos(Mathf.Clamp(angleDegrees, 0f, 180f) * Mathf.Deg2Rad);
            var edgeFaces = new Dictionary<long, List<int>>();

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddEdgeFace(edgeFaces, EdgeKey(face.a, face.b), faceIndex);
                AddEdgeFace(edgeFaces, EdgeKey(face.b, face.c), faceIndex);
                AddEdgeFace(edgeFaces, EdgeKey(face.c, face.a), faceIndex);
            }

            foreach (var pair in edgeFaces)
            {
                var faces = pair.Value;

                for (int i = 0; i < faces.Count; i++)
                {
                    for (int j = i + 1; j < faces.Count; j++)
                    {
                        if (Vector3.Dot(normals[faces[i]], normals[faces[j]]) >= minDot)
                            Union(faces[i], faces[j]);
                    }
                }
            }

            AssignSmoothingGroupBitsFromParents(data, parent, Find);
        }

        /// <summary>
        /// Reconstruct Max-style smoothing groups from Unity vertex splits:
        /// faces that share the same vertex indices across an edge are soft;
        /// coincident positions with different indices are hard (stay separate).
        /// Call before <see cref="WeldCoincidentVertices"/>.
        /// </summary>
        public static void AssignSmoothingGroupsFromUnitySplits(IndieEditableMeshData data)
        {
            if (data == null || data.faces.Count == 0)
                return;

            int faceCount = data.faces.Count;
            int[] parent = new int[faceCount];

            for (int i = 0; i < faceCount; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

                if (ra != rb)
                    parent[rb] = ra;
            }

            // Index-based edges: only soft (shared) seams appear here.
            var edgeFaces = new Dictionary<long, List<int>>();

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddEdgeFace(edgeFaces, EdgeKey(face.a, face.b), faceIndex);
                AddEdgeFace(edgeFaces, EdgeKey(face.b, face.c), faceIndex);
                AddEdgeFace(edgeFaces, EdgeKey(face.c, face.a), faceIndex);
            }

            foreach (var pair in edgeFaces)
            {
                var faces = pair.Value;

                for (int i = 0; i < faces.Count; i++)
                {
                    for (int j = i + 1; j < faces.Count; j++)
                        Union(faces[i], faces[j]);
                }
            }

            AssignSmoothingGroupBitsFromParents(data, parent, Find);
        }

        private static void AssignSmoothingGroupBitsFromParents(
            IndieEditableMeshData data,
            int[] parent,
            System.Func<int, int> find
        )
        {
            var rootToGroup = new Dictionary<int, int>();
            int nextGroup = 0;

            for (int i = 0; i < data.faces.Count; i++)
            {
                int root = find(i);

                if (!rootToGroup.TryGetValue(root, out int group))
                {
                    group = nextGroup++;
                    rootToGroup.Add(root, group);
                }

                var face = data.faces[i];
                face.smoothingGroups = 1 << (group % 32);
                data.faces[i] = face;
            }
        }

        private static void AddEdgeFace(
            Dictionary<long, List<int>> edgeFaces,
            long key,
            int faceIndex
        )
        {
            if (!edgeFaces.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                edgeFaces.Add(key, list);
            }

            if (!list.Contains(faceIndex))
                list.Add(faceIndex);
        }

        public static void CleanupUnusedVertices(IndieEditableMeshData data)
        {
            int oldCount = data.vertices.Count;

            if (oldCount == 0)
                return;

            bool[] used = new bool[oldCount];

            foreach (var face in data.faces)
            {
                if (face.a >= 0 && face.a < oldCount)
                    used[face.a] = true;

                if (face.b >= 0 && face.b < oldCount)
                    used[face.b] = true;

                if (face.c >= 0 && face.c < oldCount)
                    used[face.c] = true;
            }

            int[] map = new int[oldCount];

            for (int i = 0; i < oldCount; i++)
                map[i] = -1;

            var newVertices = new List<Vector3>();
            var newUvs = new List<Vector2>();
            var newNormals = new List<Vector3>();
            var newTangents = new List<Vector4>();
            var newColors = new List<Color>();

            for (int i = 0; i < oldCount; i++)
            {
                if (!used[i])
                    continue;

                map[i] = newVertices.Count;

                newVertices.Add(data.vertices[i]);
                newUvs.Add(i < data.uvs.Count ? data.uvs[i] : Vector2.zero);
                newNormals.Add(i < data.normals.Count ? data.normals[i] : Vector3.zero);
                newTangents.Add(i < data.tangents.Count ? data.tangents[i] : new Vector4(1f, 0f, 0f, -1f));
                newColors.Add(i < data.colors.Count ? data.colors[i] : Color.white);
            }

            data.vertices = newVertices;
            data.uvs = newUvs;
            data.normals = newNormals;
            data.tangents = newTangents;
            data.colors = newColors;

            var newFaces = new List<IndieEditMeshFace>();

            foreach (var face in data.faces)
            {
                int a = face.a >= 0 && face.a < map.Length ? map[face.a] : -1;
                int b = face.b >= 0 && face.b < map.Length ? map[face.b] : -1;
                int c = face.c >= 0 && face.c < map.Length ? map[face.c] : -1;

                if (a < 0 || b < 0 || c < 0)
                    continue;

                if (a == b || b == c || c == a)
                    continue;

                newFaces.Add(new IndieEditMeshFace
                {
                    a = a,
                    b = b,
                    c = c,
                    materialId = face.materialId,
                    smoothingGroups = face.smoothingGroups
                });
            }

            data.faces = newFaces;
            data.RebuildEdges();

            RemapSelection(data.selectedVertices, map);
            RemapSelection(data.selectedEdges, null);
            RemapSelection(data.selectedFaces, null);

            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        public static void RemapSelection(List<int> selection, int[] map)
        {
            if (selection == null || selection.Count == 0)
                return;

            if (map == null)
            {
                selection.Clear();
                return;
            }

            var remapped = new List<int>();
            var seen = new HashSet<int>();

            foreach (int index in selection)
            {
                if (index < 0 || index >= map.Length)
                    continue;

                int mapped = map[index];

                if (mapped < 0 || !seen.Add(mapped))
                    continue;

                remapped.Add(mapped);
            }

            selection.Clear();
            selection.AddRange(remapped);
        }

        public static bool TryGetOrderedOneRing(
            IndieEditableMeshData data,
            int vertex,
            List<int> neighborsOut,
            List<int> faceIndicesOut
        )
        {
            neighborsOut.Clear();
            faceIndicesOut.Clear();

            var next = new Dictionary<int, int>();
            var faceForEdge = new Dictionary<long, int>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];

                if (!TryGetOppositeDirectedEdge(face, vertex, out int from, out int to))
                    continue;

                next[from] = to;
                faceForEdge[EdgeKey(from, to)] = faceIndex;
            }

            if (next.Count == 0)
                return false;

            int start = -1;

            foreach (var pair in next)
            {
                bool hasIncoming = false;

                foreach (var other in next)
                {
                    if (other.Value == pair.Key)
                    {
                        hasIncoming = true;
                        break;
                    }
                }

                if (!hasIncoming)
                {
                    start = pair.Key;
                    break;
                }
            }

            if (start < 0)
            {
                foreach (var key in next.Keys)
                {
                    start = key;
                    break;
                }
            }

            int current = start;
            var visited = new HashSet<int>();

            while (visited.Add(current) && next.TryGetValue(current, out int following))
            {
                neighborsOut.Add(current);

                long key = EdgeKey(current, following);

                if (faceForEdge.TryGetValue(key, out int faceIndex))
                    faceIndicesOut.Add(faceIndex);

                current = following;

                if (current == start)
                    break;
            }

            return neighborsOut.Count >= 2 && neighborsOut.Count == faceIndicesOut.Count;
        }

        public static Vector3 ComputeVertexNormal(IndieEditableMeshData data, int vertex)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (var face in data.faces)
            {
                if (face.a != vertex && face.b != vertex && face.c != vertex)
                    continue;

                Vector3 a = data.vertices[face.a];
                Vector3 b = data.vertices[face.b];
                Vector3 c = data.vertices[face.c];
                Vector3 normal = Vector3.Cross(b - a, c - a);

                if (normal.sqrMagnitude < 1e-12f)
                    continue;

                sum += normal.normalized;
                count++;
            }

            if (count == 0)
                return Vector3.up;

            return (sum / count).normalized;
        }

        public static void AddTriangle(
            List<IndieEditMeshFace> faces,
            int a,
            int b,
            int c,
            int materialId,
            int smoothingGroups = 0
        )
        {
            if (a == b || b == c || c == a)
                return;

            faces.Add(new IndieEditMeshFace
            {
                a = a,
                b = b,
                c = c,
                materialId = materialId,
                smoothingGroups = smoothingGroups
            });
        }

        public static void AddQuad(
            List<IndieEditMeshFace> faces,
            int a,
            int b,
            int c,
            int d,
            int materialId,
            int smoothingGroups = 0
        )
        {
            AddTriangle(faces, a, b, c, materialId, smoothingGroups);
            AddTriangle(faces, a, c, d, materialId, smoothingGroups);
        }

        /// <summary>
        /// Adds a quad as two triangles, choosing the diagonal that keeps both
        /// triangle normals most aligned (avoids a twisted second triangle).
        /// Corner order must go around the quad: a -> b -> c -> d.
        /// </summary>
        public static void AddQuadStable(
            List<IndieEditMeshFace> faces,
            IList<Vector3> positions,
            int a,
            int b,
            int c,
            int d,
            int materialId,
            int smoothingGroups = 0
        )
        {
            if (a == b || b == c || c == d || d == a || a == c || b == d)
                return;

            Vector3 pa = positions[a];
            Vector3 pb = positions[b];
            Vector3 pc = positions[c];
            Vector3 pd = positions[d];

            // Diagonal a-c: (a,b,c) + (a,c,d)
            float scoreAc = QuadDiagonalScore(pa, pb, pc, pd);

            // Diagonal b-d: (a,b,d) + (b,c,d)
            float scoreBd = QuadDiagonalScore(pb, pc, pd, pa);

            if (scoreAc >= scoreBd)
            {
                AddTriangle(faces, a, b, c, materialId, smoothingGroups);
                AddTriangle(faces, a, c, d, materialId, smoothingGroups);
            }
            else
            {
                AddTriangle(faces, a, b, d, materialId, smoothingGroups);
                AddTriangle(faces, b, c, d, materialId, smoothingGroups);
            }
        }

        private static float QuadDiagonalScore(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            // Score triangulation (p0,p1,p2) + (p0,p2,p3)
            Vector3 n0 = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 n1 = Vector3.Cross(p2 - p0, p3 - p0);

            float m0 = n0.magnitude;
            float m1 = n1.magnitude;

            if (m0 < 1e-12f || m1 < 1e-12f)
                return -1f;

            return Vector3.Dot(n0 / m0, n1 / m1);
        }

        public static bool FaceHasEdge(IndieEditMeshFace face, int v0, int v1)
        {
            return EdgeKey(face.a, face.b) == EdgeKey(v0, v1) ||
                   EdgeKey(face.b, face.c) == EdgeKey(v0, v1) ||
                   EdgeKey(face.c, face.a) == EdgeKey(v0, v1);
        }

        public static bool TryGetThirdVertex(IndieEditMeshFace face, int v0, int v1, out int third)
        {
            long key = EdgeKey(v0, v1);

            if (EdgeKey(face.a, face.b) == key)
            {
                third = face.c;
                return true;
            }

            if (EdgeKey(face.b, face.c) == key)
            {
                third = face.a;
                return true;
            }

            if (EdgeKey(face.c, face.a) == key)
            {
                third = face.b;
                return true;
            }

            third = -1;
            return false;
        }

        public static void CollectFacesWithEdge(
            IndieEditableMeshData data,
            int v0,
            int v1,
            List<int> faceIndicesOut
        )
        {
            faceIndicesOut.Clear();
            long key = EdgeKey(v0, v1);

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (EdgeKey(face.a, face.b) == key ||
                    EdgeKey(face.b, face.c) == key ||
                    EdgeKey(face.c, face.a) == key)
                {
                    faceIndicesOut.Add(i);
                }
            }
        }

        public static int CountFacesWithEdge(IndieEditableMeshData data, int v0, int v1)
        {
            long key = EdgeKey(v0, v1);
            int count = 0;

            foreach (var face in data.faces)
            {
                if (EdgeKey(face.a, face.b) == key ||
                    EdgeKey(face.b, face.c) == key ||
                    EdgeKey(face.c, face.a) == key)
                {
                    count++;
                }
            }

            return count;
        }

        public static bool IsBorderEdge(IndieEditableMeshData data, int v0, int v1)
        {
            return CountFacesWithEdge(data, v0, v1) == 1;
        }

        public static Vector3 ComputeEdgeNormal(IndieEditableMeshData data, int v0, int v1)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            long key = EdgeKey(v0, v1);

            foreach (var face in data.faces)
            {
                if (EdgeKey(face.a, face.b) != key &&
                    EdgeKey(face.b, face.c) != key &&
                    EdgeKey(face.c, face.a) != key)
                {
                    continue;
                }

                Vector3 a = data.vertices[face.a];
                Vector3 b = data.vertices[face.b];
                Vector3 c = data.vertices[face.c];
                Vector3 normal = Vector3.Cross(b - a, c - a);

                if (normal.sqrMagnitude < 1e-12f)
                    continue;

                sum += normal.normalized;
                count++;
            }

            if (count == 0)
                return Vector3.up;

            return (sum / count).normalized;
        }

        public static int SplitEdgeAt(
            IndieEditableMeshData data,
            int v0,
            int v1,
            float t,
            Dictionary<long, List<int>> splitCache
        )
        {
            long key = EdgeKey(v0, v1);

            if (splitCache != null && splitCache.TryGetValue(key, out var existing))
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    int index = existing[i];
                    Vector3 pos = data.vertices[index];
                    Vector3 expected = Vector3.Lerp(data.vertices[v0], data.vertices[v1], t);

                    if ((pos - expected).sqrMagnitude < 1e-10f)
                        return index;
                }
            }

            Vector3 point = Vector3.Lerp(data.vertices[v0], data.vertices[v1], Mathf.Clamp01(t));
            int source = t < 0.5f ? v0 : v1;
            int created = AddVertex(data, source, point);

            if (data.uvs.Count > created)
            {
                Vector2 uv0 = v0 < data.uvs.Count ? data.uvs[v0] : Vector2.zero;
                Vector2 uv1 = v1 < data.uvs.Count ? data.uvs[v1] : Vector2.zero;
                data.uvs[created] = Vector2.Lerp(uv0, uv1, Mathf.Clamp01(t));
            }

            if (splitCache != null)
            {
                if (!splitCache.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    splitCache.Add(key, list);
                }

                list.Add(created);
            }

            return created;
        }

        public static void TriangulatePolygonFan(
            List<IndieEditMeshFace> faces,
            List<int> polygon,
            int materialId,
            bool reverse,
            int smoothingGroups = 0
        )
        {
            if (polygon == null || polygon.Count < 3)
                return;

            for (int i = 1; i < polygon.Count - 1; i++)
            {
                if (reverse)
                {
                    AddTriangle(
                        faces,
                        polygon[0],
                        polygon[i + 1],
                        polygon[i],
                        materialId,
                        smoothingGroups
                    );
                }
                else
                {
                    AddTriangle(
                        faces,
                        polygon[0],
                        polygon[i],
                        polygon[i + 1],
                        materialId,
                        smoothingGroups
                    );
                }
            }
        }

        private static bool TryGetOppositeDirectedEdge(
            IndieEditMeshFace face,
            int vertex,
            out int from,
            out int to
        )
        {
            if (face.a == vertex)
            {
                from = face.b;
                to = face.c;
                return true;
            }

            if (face.b == vertex)
            {
                from = face.c;
                to = face.a;
                return true;
            }

            if (face.c == vertex)
            {
                from = face.a;
                to = face.b;
                return true;
            }

            from = -1;
            to = -1;
            return false;
        }
    }
}
