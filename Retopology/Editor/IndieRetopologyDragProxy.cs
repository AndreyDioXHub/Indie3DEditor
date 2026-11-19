using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyDragProxy
    {
        private const int Rings = 2;

        private static IndieRetopologyComponent _retopo;
        private static List<int>[] _neighbors;
        private static int[] _distance;
        private static readonly List<int> UsedVertices = new List<int>();
        private static readonly List<int> Queue = new List<int>();
        private static readonly List<int> FaceIds = new List<int>();
        private static readonly List<int> MovedFaceIds = new List<int>();

        public static bool IsActive => _retopo != null && FaceIds.Count > 0 && _distance != null;

        public static List<int> MovedFaces => MovedFaceIds;

        public static void Begin(IndieRetopologyComponent retopo, List<int> cluster, int edgeA, int edgeB)
        {
            End(false, null);

            if (retopo == null || retopo.Data == null)
                return;

            IndieRetopologySession.EnsurePreview(retopo);

            if (!CollectFaces(retopo.Data, cluster, edgeA, edgeB))
                return;

            CollectMovedFaces(retopo.Data, cluster);
            _retopo = retopo;
        }

        public static void OrientVertex(IndieRetopologyComponent retopo, Camera camera, int vertex)
        {
            if (!IsActive || camera == null || retopo == null || retopo.Data == null || retopo.Data.faces == null)
                return;

            var faces = retopo.Data.faces;
            var vertices = retopo.Data.vertices;

            if (vertices == null)
                return;

            for (int i = 0; i < FaceIds.Count; i++)
            {
                int faceIndex = FaceIds[i];

                if (faceIndex < 0 || faceIndex >= faces.Count)
                    continue;

                IndieRetopologyFace face = faces[faceIndex];

                if (face.a != vertex && face.b != vertex && face.c != vertex)
                    continue;

                if (!IndieRetopologyMeshBuilder.IsValidFace(face, vertices.Count))
                    continue;

                Vector3 wa = retopo.transform.TransformPoint(vertices[face.a]);
                Vector3 wb = retopo.transform.TransformPoint(vertices[face.b]);
                Vector3 wc = retopo.transform.TransformPoint(vertices[face.c]);

                if (IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
                    continue;

                if (!IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wc, wb))
                    continue;

                int swap = face.b;
                face.b = face.c;
                face.c = swap;
                faces[faceIndex] = face;
            }
        }

        public static void End(bool refresh, IndieRetopologyComponent retopo)
        {
            if (retopo == null)
                retopo = _retopo;

            _retopo = null;
            FaceIds.Clear();
            MovedFaceIds.Clear();

            if (refresh && retopo != null)
                IndieRetopologySession.RefreshMesh(retopo);
        }

        private static bool CollectFaces(IndieRetopologyMeshData data, List<int> cluster, int edgeA, int edgeB)
        {
            FaceIds.Clear();

            if (data.vertices == null || data.faces == null || data.faces.Count == 0)
                return false;

            int count = data.vertices.Count;
            BuildNeighbors(data, count);

            if (_distance == null || _distance.Length < count)
                _distance = new int[Mathf.NextPowerOfTwo(Mathf.Max(count, 1))];

            for (int i = 0; i < count; i++)
                _distance[i] = -1;

            Queue.Clear();
            Seed(cluster, edgeA, count);
            SeedOne(edgeB, count);

            int head = 0;

            while (head < Queue.Count)
            {
                int vertex = Queue[head++];

                if (_distance[vertex] >= Rings)
                    continue;

                List<int> neighbors = _neighbors[vertex];

                if (neighbors == null)
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];

                    if (_distance[next] >= 0)
                        continue;

                    _distance[next] = _distance[vertex] + 1;
                    Queue.Add(next);
                }
            }

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IndieRetopologyMeshBuilder.IsValidFace(face, count))
                    continue;

                if (_distance[face.a] < 0 || _distance[face.b] < 0 || _distance[face.c] < 0)
                    continue;

                FaceIds.Add(i);
            }

            return FaceIds.Count > 0;
        }

        private static void CollectMovedFaces(IndieRetopologyMeshData data, List<int> cluster)
        {
            MovedFaceIds.Clear();

            if (cluster == null || cluster.Count == 0)
                return;

            for (int i = 0; i < FaceIds.Count; i++)
            {
                IndieRetopologyFace face = data.faces[FaceIds[i]];

                if (cluster.Contains(face.a) || cluster.Contains(face.b) || cluster.Contains(face.c))
                    MovedFaceIds.Add(FaceIds[i]);
            }
        }

        private static void Seed(List<int> cluster, int edgeA, int count)
        {
            if (cluster != null)
            {
                for (int i = 0; i < cluster.Count; i++)
                    SeedOne(cluster[i], count);
            }

            SeedOne(edgeA, count);
        }

        private static void SeedOne(int index, int count)
        {
            if (index < 0 || index >= count || _distance[index] >= 0)
                return;

            _distance[index] = 0;
            Queue.Add(index);
        }

        private static void BuildNeighbors(IndieRetopologyMeshData data, int count)
        {
            if (_neighbors == null || _neighbors.Length < count)
            {
                var grown = new List<int>[Mathf.NextPowerOfTwo(Mathf.Max(count, 1))];

                if (_neighbors != null)
                    System.Array.Copy(_neighbors, grown, _neighbors.Length);

                _neighbors = grown;
            }

            for (int i = 0; i < UsedVertices.Count; i++)
                _neighbors[UsedVertices[i]].Clear();

            UsedVertices.Clear();

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IndieRetopologyMeshBuilder.IsValidFace(face, count))
                    continue;

                Link(face.a, face.b);
                Link(face.b, face.c);
                Link(face.c, face.a);
            }
        }

        private static void Link(int a, int b)
        {
            AddNeighbor(a, b);
            AddNeighbor(b, a);
        }

        private static void AddNeighbor(int vertex, int other)
        {
            List<int> neighbors = _neighbors[vertex];

            if (neighbors == null)
            {
                neighbors = new List<int>(6);
                _neighbors[vertex] = neighbors;
            }

            if (neighbors.Count == 0)
                UsedVertices.Add(vertex);

            neighbors.Add(other);
        }
    }
}
