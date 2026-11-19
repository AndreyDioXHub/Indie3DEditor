using System;
using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVSeamUtil
    {
        private const float PositionQuantize = 10000f;
        private const float UvEpsilonSq = 1e-8f;

        private struct EdgeIncidence
        {
            public Vector3 Pos0;
            public Vector3 Pos1;
            public Vector2 Uv0;
            public Vector2 Uv1;
        }

        private readonly struct GeoEdgeKey : IEquatable<GeoEdgeKey>
        {
            public readonly Vector3Int A;
            public readonly Vector3Int B;

            public GeoEdgeKey(Vector3Int a, Vector3Int b)
            {
                if (Compare(a, b) <= 0)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public bool Equals(GeoEdgeKey other)
            {
                return A.Equals(other.A) && B.Equals(other.B);
            }

            public override bool Equals(object obj)
            {
                return obj is GeoEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A.GetHashCode() * 397) ^ B.GetHashCode();
                }
            }

            private static int Compare(Vector3Int a, Vector3Int b)
            {
                if (a.x != b.x)
                    return a.x.CompareTo(b.x);

                if (a.y != b.y)
                    return a.y.CompareTo(b.y);

                return a.z.CompareTo(b.z);
            }
        }

        public static List<Vector3> CollectSeamLocalEndpoints(IndieEditableMeshData data)
        {
            var result = new List<Vector3>();

            if (data == null || data.faces.Count == 0 || data.vertices.Count == 0)
                return result;

            if (data.uvs.Count != data.vertices.Count)
                return result;

            var map = new Dictionary<GeoEdgeKey, List<EdgeIncidence>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddEdge(map, data, face.a, face.b);
                AddEdge(map, data, face.b, face.c);
                AddEdge(map, data, face.c, face.a);
            }

            foreach (var pair in map)
            {
                var incidences = pair.Value;

                if (incidences.Count < 2)
                    continue;

                bool isSeam = false;

                for (int i = 0; i < incidences.Count && !isSeam; i++)
                {
                    for (int j = i + 1; j < incidences.Count; j++)
                    {
                        if (IsUvDiscontinuous(incidences[i], incidences[j]))
                        {
                            isSeam = true;
                            break;
                        }
                    }
                }

                if (!isSeam)
                    continue;

                var sample = incidences[0];
                result.Add(sample.Pos0);
                result.Add(sample.Pos1);
            }

            return result;
        }

        /// <summary>
        /// UV-space endpoints of seam edges (both sides of each geometric seam).
        /// Flat list: uv0, uv1, uv0, uv1, …
        /// </summary>
        public static List<Vector2> CollectSeamUvEndpoints(IndieEditableMeshData data)
        {
            var result = new List<Vector2>();

            if (data == null || data.faces.Count == 0 || data.vertices.Count == 0)
                return result;

            if (data.uvs.Count != data.vertices.Count)
                return result;

            var map = new Dictionary<GeoEdgeKey, List<EdgeIncidence>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddEdge(map, data, face.a, face.b);
                AddEdge(map, data, face.b, face.c);
                AddEdge(map, data, face.c, face.a);
            }

            foreach (var pair in map)
            {
                var incidences = pair.Value;

                if (incidences.Count < 2)
                    continue;

                bool isSeam = false;

                for (int i = 0; i < incidences.Count && !isSeam; i++)
                {
                    for (int j = i + 1; j < incidences.Count; j++)
                    {
                        if (IsUvDiscontinuous(incidences[i], incidences[j]))
                        {
                            isSeam = true;
                            break;
                        }
                    }
                }

                if (!isSeam)
                    continue;

                for (int i = 0; i < incidences.Count; i++)
                {
                    result.Add(incidences[i].Uv0);
                    result.Add(incidences[i].Uv1);
                }
            }

            return result;
        }

        public static List<Vector3> CollectSeamLocalEndpoints(Mesh mesh)
        {
            if (mesh == null)
                return new List<Vector3>();

            IndieEditableMeshData data = IndieEditableMeshImporter.Import(mesh);
            return CollectSeamLocalEndpoints(data);
        }

        /// <summary>
        /// Edge keys (<see cref="IndieEditMeshMeshUtil.EdgeKey"/>) for geometric edges
        /// that have UV discontinuities across adjacent faces.
        /// </summary>
        public static HashSet<long> CollectBakedSeamEdgeKeys(IndieEditableMeshData data)
        {
            var result = new HashSet<long>();

            if (data == null || data.faces.Count == 0 || data.vertices.Count == 0)
                return result;

            if (data.uvs.Count != data.vertices.Count)
                return result;

            var map = new Dictionary<GeoEdgeKey, List<IndexIncidence>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddIndexEdge(map, data, face.a, face.b);
                AddIndexEdge(map, data, face.b, face.c);
                AddIndexEdge(map, data, face.c, face.a);
            }

            foreach (var pair in map)
            {
                var incidences = pair.Value;

                if (incidences.Count < 2)
                    continue;

                bool isSeam = false;

                for (int i = 0; i < incidences.Count && !isSeam; i++)
                {
                    for (int j = i + 1; j < incidences.Count; j++)
                    {
                        if (IsUvDiscontinuous(incidences[i], incidences[j]))
                        {
                            isSeam = true;
                            break;
                        }
                    }
                }

                if (!isSeam)
                    continue;

                for (int i = 0; i < incidences.Count; i++)
                    result.Add(IndieEditMeshMeshUtil.EdgeKey(incidences[i].I0, incidences[i].I1));
            }

            return result;
        }

        private struct IndexIncidence
        {
            public int I0;
            public int I1;
            public Vector3 Pos0;
            public Vector3 Pos1;
            public Vector2 Uv0;
            public Vector2 Uv1;
        }

        private static void AddIndexEdge(
            Dictionary<GeoEdgeKey, List<IndexIncidence>> map,
            IndieEditableMeshData data,
            int i0,
            int i1
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            Vector3 p0 = data.vertices[i0];
            Vector3 p1 = data.vertices[i1];
            var key = new GeoEdgeKey(Quantize(p0), Quantize(p1));

            if (!map.TryGetValue(key, out List<IndexIncidence> list))
            {
                list = new List<IndexIncidence>(2);
                map.Add(key, list);
            }

            list.Add(new IndexIncidence
            {
                I0 = i0,
                I1 = i1,
                Pos0 = p0,
                Pos1 = p1,
                Uv0 = data.uvs[i0],
                Uv1 = data.uvs[i1]
            });
        }

        private static bool IsUvDiscontinuous(IndexIncidence a, IndexIncidence b)
        {
            bool sameDirection =
                (a.Pos0 - b.Pos0).sqrMagnitude <= (a.Pos0 - b.Pos1).sqrMagnitude;

            Vector2 bUv0 = sameDirection ? b.Uv0 : b.Uv1;
            Vector2 bUv1 = sameDirection ? b.Uv1 : b.Uv0;

            return (a.Uv0 - bUv0).sqrMagnitude > UvEpsilonSq ||
                   (a.Uv1 - bUv1).sqrMagnitude > UvEpsilonSq;
        }

        private static void AddEdge(
            Dictionary<GeoEdgeKey, List<EdgeIncidence>> map,
            IndieEditableMeshData data,
            int i0,
            int i1
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            Vector3 p0 = data.vertices[i0];
            Vector3 p1 = data.vertices[i1];
            var key = new GeoEdgeKey(Quantize(p0), Quantize(p1));

            if (!map.TryGetValue(key, out List<EdgeIncidence> list))
            {
                list = new List<EdgeIncidence>(2);
                map.Add(key, list);
            }

            list.Add(new EdgeIncidence
            {
                Pos0 = p0,
                Pos1 = p1,
                Uv0 = data.uvs[i0],
                Uv1 = data.uvs[i1]
            });
        }

        private static bool IsUvDiscontinuous(EdgeIncidence a, EdgeIncidence b)
        {
            bool sameDirection =
                (a.Pos0 - b.Pos0).sqrMagnitude <= (a.Pos0 - b.Pos1).sqrMagnitude;

            Vector2 bUv0 = sameDirection ? b.Uv0 : b.Uv1;
            Vector2 bUv1 = sameDirection ? b.Uv1 : b.Uv0;

            return (a.Uv0 - bUv0).sqrMagnitude > UvEpsilonSq ||
                   (a.Uv1 - bUv1).sqrMagnitude > UvEpsilonSq;
        }

        private static Vector3Int Quantize(Vector3 p)
        {
            return new Vector3Int(
                Mathf.RoundToInt(p.x * PositionQuantize),
                Mathf.RoundToInt(p.y * PositionQuantize),
                Mathf.RoundToInt(p.z * PositionQuantize)
            );
        }
    }
}
