using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Connectivity of selected polygons by vertex position rather than vertex index.
    /// Import keeps UV seams and hard edges as separate indices at one point, so by index
    /// neighbouring faces look unrelated and group operations tear the piece along the seam.
    /// Indices are never welded: stacked verts only share a point id.
    /// </summary>
    internal sealed class IndieEditMeshFacePointGroups
    {
        /// <summary>Same tolerance as Link Coincident.</summary>
        public const float CoincidentThreshold = 1e-5f;

        /// <summary>About 2.5°: a slightly bent quad stays one flat piece, a real corner splits.</summary>
        public const float PlanarNormalDot = 0.999f;

        public readonly struct BoundaryEdge
        {
            public readonly int From;
            public readonly int To;
            public readonly int Face;

            public BoundaryEdge(int from, int to, int face)
            {
                From = from;
                To = to;
                Face = face;
            }
        }

        private readonly IndieEditableMeshData _data;
        private readonly Dictionary<int, int> _pointOf = new Dictionary<int, int>();

        private IndieEditMeshFacePointGroups(IndieEditableMeshData data)
        {
            _data = data;
        }

        /// <summary>Point ids for every vertex used by <paramref name="faces"/>.</summary>
        public static IndieEditMeshFacePointGroups Build(IndieEditableMeshData data, IReadOnlyList<int> faces)
        {
            var result = new IndieEditMeshFacePointGroups(data);
            var verts = new List<int>();
            var parent = new Dictionary<int, int>();

            foreach (int faceIndex in faces)
            {
                if (!IsValidFace(data, faceIndex))
                    continue;

                var face = data.faces[faceIndex];
                AddVert(face.a);
                AddVert(face.b);
                AddVert(face.c);
            }

            float inv = 1f / CoincidentThreshold;
            float thresholdSq = CoincidentThreshold * CoincidentThreshold;
            var cells = new Dictionary<Vector3Int, List<int>>();

            foreach (int vert in verts)
            {
                Vector3 p = data.vertices[vert];
                var cell = new Vector3Int(
                    Mathf.FloorToInt(p.x * inv),
                    Mathf.FloorToInt(p.y * inv),
                    Mathf.FloorToInt(p.z * inv)
                );

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out var list))
                        continue;

                    foreach (int other in list)
                    {
                        if ((data.vertices[other] - p).sqrMagnitude <= thresholdSq)
                            Union(parent, vert, other);
                    }
                }

                if (!cells.TryGetValue(cell, out var own))
                {
                    own = new List<int>(2);
                    cells.Add(cell, own);
                }

                own.Add(vert);
            }

            foreach (int vert in verts)
                result._pointOf[vert] = Find(parent, vert);

            return result;

            void AddVert(int vert)
            {
                if (parent.ContainsKey(vert))
                    return;

                parent.Add(vert, vert);
                verts.Add(vert);
            }
        }

        public int PointOf(int vertex) => _pointOf.TryGetValue(vertex, out int point) ? point : vertex;

        /// <summary>False when <paramref name="vertex"/> was not part of the faces passed to <see cref="Build"/>.</summary>
        public bool TryGetPoint(int vertex, out int point)
        {
            return _pointOf.TryGetValue(vertex, out point);
        }

        /// <summary>Faces that share an edge by position end up in one group.</summary>
        public List<List<int>> GetGroups(IReadOnlyList<int> faces)
        {
            var parent = new Dictionary<int, int>();
            var order = new List<int>();

            foreach (int faceIndex in faces)
            {
                if (!IsValidFace(_data, faceIndex) || parent.ContainsKey(faceIndex))
                    continue;

                parent.Add(faceIndex, faceIndex);
                order.Add(faceIndex);
            }

            var edgeOwner = new Dictionary<long, int>();

            foreach (int faceIndex in order)
            {
                var face = _data.faces[faceIndex];
                Link(faceIndex, face.a, face.b);
                Link(faceIndex, face.b, face.c);
                Link(faceIndex, face.c, face.a);
            }

            var groups = new List<List<int>>();
            var groupOfRoot = new Dictionary<int, List<int>>();

            foreach (int faceIndex in order)
            {
                int root = Find(parent, faceIndex);

                if (!groupOfRoot.TryGetValue(root, out var group))
                {
                    group = new List<int>();
                    groupOfRoot.Add(root, group);
                    groups.Add(group);
                }

                group.Add(faceIndex);
            }

            return groups;

            void Link(int faceIndex, int v0, int v1)
            {
                if (!TryPointEdgeKey(v0, v1, out long key))
                    return;

                if (edgeOwner.TryGetValue(key, out int other))
                    Union(parent, faceIndex, other);
                else
                    edgeOwner.Add(key, faceIndex);
            }
        }

        /// <summary>
        /// Outer contour of the group: edges owned by exactly one group face, by position.
        /// Seams between selected faces are interior. Each edge keeps its owner's winding.
        /// </summary>
        public List<BoundaryEdge> GetBoundary(List<int> group)
        {
            var counts = new Dictionary<long, int>();
            var firstSeen = new Dictionary<long, BoundaryEdge>();
            var order = new List<long>();

            foreach (int faceIndex in group)
            {
                if (!IsValidFace(_data, faceIndex))
                    continue;

                var face = _data.faces[faceIndex];
                Visit(faceIndex, face.a, face.b);
                Visit(faceIndex, face.b, face.c);
                Visit(faceIndex, face.c, face.a);
            }

            var result = new List<BoundaryEdge>();

            foreach (long key in order)
            {
                if (counts[key] == 1)
                    result.Add(firstSeen[key]);
            }

            return result;

            void Visit(int faceIndex, int from, int to)
            {
                if (!TryPointEdgeKey(from, to, out long key))
                    return;

                if (counts.TryGetValue(key, out int count))
                {
                    counts[key] = count + 1;
                    return;
                }

                counts.Add(key, 1);
                firstSeen.Add(key, new BoundaryEdge(from, to, faceIndex));
                order.Add(key);
            }
        }

        /// <summary>
        /// Distinct planes of the group faces at each point: one normal per point on a flat
        /// piece, two along a fold, three at a box corner.
        /// </summary>
        public Dictionary<int, List<Vector3>> GetPointPlanes(List<int> group)
        {
            var result = new Dictionary<int, List<Vector3>>();

            foreach (int faceIndex in group)
            {
                if (!IsValidFace(_data, faceIndex))
                    continue;

                var face = _data.faces[faceIndex];
                Vector3 a = _data.vertices[face.a];
                Vector3 b = _data.vertices[face.b];
                Vector3 c = _data.vertices[face.c];

                if (Vector3.Cross(b - a, c - a).sqrMagnitude <= 1e-12f)
                    continue;

                Vector3 normal = IndieEditMeshFaceOps.FaceNormal(_data, face);
                AddPlane(face.a, normal);
                AddPlane(face.b, normal);
                AddPlane(face.c, normal);
            }

            return result;

            void AddPlane(int vertex, Vector3 normal)
            {
                int point = PointOf(vertex);

                if (!result.TryGetValue(point, out var planes))
                {
                    planes = new List<Vector3>(1);
                    result.Add(point, planes);
                }

                foreach (var plane in planes)
                {
                    if (Vector3.Dot(plane, normal) >= PlanarNormalDot)
                        return;
                }

                planes.Add(normal);
            }
        }

        private bool TryPointEdgeKey(int v0, int v1, out long key)
        {
            int p0 = PointOf(v0);
            int p1 = PointOf(v1);

            if (p0 == p1)
            {
                key = 0;
                return false;
            }

            key = IndieEditMeshMeshUtil.EdgeKey(p0, p1);
            return true;
        }

        private static bool IsValidFace(IndieEditableMeshData data, int faceIndex)
            => faceIndex >= 0 && faceIndex < data.faces.Count;

        private static int Find(Dictionary<int, int> parent, int x)
        {
            int root = x;

            while (parent[root] != root)
                root = parent[root];

            while (parent[x] != root)
            {
                int next = parent[x];
                parent[x] = root;
                x = next;
            }

            return root;
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra == rb)
                return;

            if (ra < rb)
                parent[rb] = ra;
            else
                parent[ra] = rb;
        }
    }
}
