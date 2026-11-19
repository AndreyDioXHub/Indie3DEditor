using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>One polygon of <see cref="IndieEditMeshMetaMesh"/>: a source triangle or two of them paired into a quad.</summary>
    internal sealed class IndieEditMeshMetaPoly
    {
        /// <summary>Vertex index of every corner in winding order. Seams keep their own index per corner.</summary>
        public int[] Corners;

        /// <summary>Point id of every corner; null for a degenerate face, which is passed through untouched.</summary>
        public int[] Points;

        public int[] SourceFaces;
        public int MaterialId;
        public int SmoothingGroups;
        public Vector3 Normal;

        public bool IsDegenerate => Points == null;

        public int IndexOfPoint(int point)
        {
            if (Points == null)
                return -1;

            for (int i = 0; i < Points.Length; i++)
            {
                if (Points[i] == point)
                    return i;
            }

            return -1;
        }

        public int PrevPoint(int corner) => Points[(corner + Points.Length - 1) % Points.Length];

        public int NextPoint(int corner) => Points[(corner + 1) % Points.Length];
    }

    /// <summary>
    /// Quad-dominant view of a triangle mesh. Topology is by position (points), so UV
    /// seams and hard edges do not split it; attributes stay on the source vertex indices.
    /// </summary>
    internal sealed class IndieEditMeshMetaMesh
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        public readonly List<IndieEditMeshMetaPoly> Polys = new List<IndieEditMeshMetaPoly>();

        /// <summary>Point edge key -> polys that have that edge.</summary>
        public readonly Dictionary<long, List<int>> EdgePolys = new Dictionary<long, List<int>>();

        /// <summary>Point -> polys with a corner on it.</summary>
        public readonly Dictionary<int, List<int>> PointPolys = new Dictionary<int, List<int>>();

        private readonly Dictionary<int, int> _pointOfVertex = new Dictionary<int, int>();

        public int PointOf(int vertex) => _pointOfVertex.TryGetValue(vertex, out int point) ? point : -1;

        public void SetPointOf(int vertex, int point) => _pointOfVertex[vertex] = point;

        public bool TryPointEdge(int v0, int v1, out long key)
        {
            int p0 = PointOf(v0);
            int p1 = PointOf(v1);

            if (p0 < 0 || p1 < 0 || p0 == p1)
            {
                key = 0;
                return false;
            }

            key = IndieEditMeshMeshUtil.EdgeKey(p0, p1);
            return true;
        }

        public int AddPoint(Vector3 position)
        {
            Points.Add(position);
            return Points.Count - 1;
        }

        public int EdgePolyCount(int p0, int p1)
        {
            return EdgePolys.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(p0, p1), out var list) ? list.Count : 0;
        }
    }

    /// <summary>
    /// Builds <see cref="IndieEditMeshMetaMesh"/>: pairs triangles across a shared diagonal into
    /// convex quads, bent ones included; flatter and squarer pairs win. Each triangle joins at most one pair, so a flat island never
    /// collapses into one n-gon.
    /// </summary>
    internal static class IndieEditMeshQuadMeta
    {
        /// <summary>A corner closer to straight than this (sine) makes the pair a triangle with a T-vertex.</summary>
        private const float MinCornerSin = 0.05f;

        private const float MinPairNormalDot = 0f;

        private struct PairCandidate
        {
            public int FaceA;
            public int FaceB;
            public int[] Corners;
            public float Score;
        }

        /// <param name="keepEdges">Vertex pairs that must stay real edges (never a quad diagonal).</param>
        public static IndieEditMeshMetaMesh Build(IndieEditableMeshData data, List<(int v0, int v1)> keepEdges)
        {
            var meta = new IndieEditMeshMetaMesh();
            int faceCount = data.faces.Count;
            var validFaces = new List<int>(faceCount);

            for (int i = 0; i < faceCount; i++)
            {
                if (IsValidFace(data, data.faces[i]))
                    validFaces.Add(i);
            }

            AssignPoints(data, meta, validFaces);

            var keepKeys = new HashSet<long>();

            if (keepEdges != null)
            {
                foreach (var edge in keepEdges)
                {
                    if (meta.TryPointEdge(edge.v0, edge.v1, out long key))
                        keepKeys.Add(key);
                }
            }

            var facePoints = new int[faceCount][];
            var edgeFaces = new Dictionary<long, List<(int face, int slot)>>();

            foreach (int faceIndex in validFaces)
            {
                var face = data.faces[faceIndex];
                int pa = meta.PointOf(face.a);
                int pb = meta.PointOf(face.b);
                int pc = meta.PointOf(face.c);

                if (pa == pb || pb == pc || pc == pa)
                    continue;

                facePoints[faceIndex] = new[] { pa, pb, pc };

                for (int slot = 0; slot < 3; slot++)
                {
                    long key = IndieEditMeshMeshUtil.EdgeKey(facePoints[faceIndex][slot], facePoints[faceIndex][(slot + 1) % 3]);

                    if (!edgeFaces.TryGetValue(key, out var list))
                    {
                        list = new List<(int face, int slot)>(2);
                        edgeFaces.Add(key, list);
                    }

                    list.Add((faceIndex, slot));
                }
            }

            var candidates = new List<PairCandidate>();

            foreach (var pair in edgeFaces)
            {
                if (pair.Value.Count != 2 || keepKeys.Contains(pair.Key))
                    continue;

                if (TryMakePair(data, meta, facePoints, pair.Value[0], pair.Value[1], out var candidate))
                    candidates.Add(candidate);
            }

            candidates.Sort((x, y) => x.Score.CompareTo(y.Score));

            var partner = new Dictionary<int, int>();
            var quadOf = new Dictionary<int, int[]>();

            foreach (var candidate in candidates)
            {
                if (partner.ContainsKey(candidate.FaceA) || partner.ContainsKey(candidate.FaceB))
                    continue;

                partner.Add(candidate.FaceA, candidate.FaceB);
                partner.Add(candidate.FaceB, candidate.FaceA);
                quadOf.Add(Mathf.Min(candidate.FaceA, candidate.FaceB), candidate.Corners);
            }

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                var face = data.faces[faceIndex];

                if (partner.TryGetValue(faceIndex, out int other))
                {
                    if (other < faceIndex)
                        continue;

                    AddPoly(data, meta, quadOf[faceIndex], new[] { faceIndex, other }, face);
                    continue;
                }

                if (facePoints[faceIndex] == null)
                {
                    meta.Polys.Add(
                        new IndieEditMeshMetaPoly
                        {
                            Corners = new[] { face.a, face.b, face.c },
                            SourceFaces = new[] { faceIndex },
                            MaterialId = face.materialId,
                            SmoothingGroups = face.smoothingGroups,
                            Normal = Vector3.up
                        }
                    );
                    continue;
                }

                AddPoly(data, meta, new[] { face.a, face.b, face.c }, new[] { faceIndex }, face);
            }

            return meta;
        }

        private static void AssignPoints(IndieEditableMeshData data, IndieEditMeshMetaMesh meta, List<int> faces)
        {
            var groups = IndieEditMeshFacePointGroups.Build(data, faces);
            var compact = new Dictionary<int, int>();

            foreach (int faceIndex in faces)
            {
                var face = data.faces[faceIndex];
                Assign(face.a);
                Assign(face.b);
                Assign(face.c);
            }

            void Assign(int vertex)
            {
                if (meta.PointOf(vertex) >= 0)
                    return;

                int root = groups.PointOf(vertex);

                if (!compact.TryGetValue(root, out int point))
                {
                    point = meta.AddPoint(data.vertices[root]);
                    compact.Add(root, point);
                }

                meta.SetPointOf(vertex, point);
            }
        }

        private static bool TryMakePair(
            IndieEditableMeshData data,
            IndieEditMeshMetaMesh meta,
            int[][] facePoints,
            (int face, int slot) first,
            (int face, int slot) second,
            out PairCandidate candidate
        )
        {
            candidate = default;

            var faceA = data.faces[first.face];
            var faceB = data.faces[second.face];

            if (faceA.materialId != faceB.materialId || faceA.smoothingGroups != faceB.smoothingGroups)
                return false;

            int[] ia = Indices(faceA);
            int[] ib = Indices(faceB);

            int a = ia[first.slot];
            int b = ia[(first.slot + 1) % 3];
            int c = ia[(first.slot + 2) % 3];

            // Same indices in opposite order: a diagonal is never a UV seam or a fold.
            if (ib[second.slot] != b || ib[(second.slot + 1) % 3] != a)
                return false;

            int d = ib[(second.slot + 2) % 3];
            int pc = facePoints[first.face][(first.slot + 2) % 3];
            int pd = facePoints[second.face][(second.slot + 2) % 3];

            if (pc == pd)
                return false;

            Vector3 na = IndieEditMeshFaceOps.FaceNormal(data, faceA);
            Vector3 nb = IndieEditMeshFaceOps.FaceNormal(data, faceB);
            float bend = Vector3.Dot(na, nb);

            // A bent quad stays a quad; normals in opposite hemispheres make a fold.
            if (bend <= MinPairNormalDot)
                return false;

            // a -> d -> b -> c keeps the winding of face A.
            var corners = new[] { a, d, b, c };
            Vector3 n = (na + nb).normalized;
            float worstCos = 0f;

            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = data.vertices[corners[(i + 3) % 4]];
                Vector3 p1 = data.vertices[corners[i]];
                Vector3 p2 = data.vertices[corners[(i + 1) % 4]];
                Vector3 toPrev = p0 - p1;
                Vector3 toNext = p2 - p1;

                if (toPrev.sqrMagnitude < 1e-12f || toNext.sqrMagnitude < 1e-12f)
                    return false;

                toPrev.Normalize();
                toNext.Normalize();

                float turn = Vector3.Dot(Vector3.Cross(toNext, toPrev), n);

                if (turn < MinCornerSin)
                    return false;

                worstCos = Mathf.Max(worstCos, Mathf.Abs(Vector3.Dot(toPrev, toNext)));
            }

            candidate = new PairCandidate
            {
                FaceA = first.face,
                FaceB = second.face,
                Corners = corners,
                Score = worstCos + (1f - bend)
            };
            return true;
        }

        private static void AddPoly(
            IndieEditableMeshData data,
            IndieEditMeshMetaMesh meta,
            int[] corners,
            int[] sourceFaces,
            IndieEditMeshFace face
        )
        {
            var points = new int[corners.Length];

            for (int i = 0; i < corners.Length; i++)
                points[i] = meta.PointOf(corners[i]);

            Vector3 normal = Vector3.zero;

            foreach (int sourceFace in sourceFaces)
            {
                var f = data.faces[sourceFace];
                normal += Vector3.Cross(
                    data.vertices[f.b] - data.vertices[f.a],
                    data.vertices[f.c] - data.vertices[f.a]
                );
            }

            int polyIndex = meta.Polys.Count;

            meta.Polys.Add(
                new IndieEditMeshMetaPoly
                {
                    Corners = corners,
                    Points = points,
                    SourceFaces = sourceFaces,
                    MaterialId = face.materialId,
                    SmoothingGroups = face.smoothingGroups,
                    Normal = normal.sqrMagnitude > 1e-20f ? normal.normalized : Vector3.up
                }
            );

            for (int i = 0; i < points.Length; i++)
            {
                AddToList(meta.PointPolys, points[i], polyIndex);
                AddToList(meta.EdgePolys, IndieEditMeshMeshUtil.EdgeKey(points[i], points[(i + 1) % points.Length]), polyIndex);
            }
        }

        private static void AddToList<TKey>(Dictionary<TKey, List<int>> map, TKey key, int value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map.Add(key, list);
            }

            if (!list.Contains(value))
                list.Add(value);
        }

        private static int[] Indices(IndieEditMeshFace face) => new[] { face.a, face.b, face.c };

        private static bool IsValidFace(IndieEditableMeshData data, IndieEditMeshFace face)
        {
            int count = data.vertices.Count;

            return face.a >= 0 && face.a < count &&
                   face.b >= 0 && face.b < count &&
                   face.c >= 0 && face.c < count &&
                   face.a != face.b && face.b != face.c && face.c != face.a;
        }
    }
}
