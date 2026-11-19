using System;
using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyWeld
    {
        public static int MergeCluster(IndieRetopologyMeshData data, List<int> sources, int into)
        {
            if (data == null || data.vertices == null)
                return -1;

            int count = data.vertices.Count;

            if (into < 0 || into >= count)
                return -1;

            var drop = new HashSet<int>();

            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    int index = sources[i];

                    if (index == into || index < 0 || index >= count)
                        continue;

                    drop.Add(index);
                }
            }

            if (drop.Count == 0)
                return into;

            if (data.faces != null)
                data.faces = RetargetFaces(data.faces, drop, into);

            if (data.selectedEdges != null)
                data.selectedEdges = RetargetEdges(data.selectedEdges, drop, into);

            return Compact(data, drop, into);
        }

        public static bool DeleteCluster(IndieRetopologyMeshData data, List<int> indices)
        {
            if (data == null || data.vertices == null || indices == null || indices.Count == 0)
                return false;

            int count = data.vertices.Count;
            var drop = new HashSet<int>();

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];

                if (index >= 0 && index < count)
                    drop.Add(index);
            }

            if (drop.Count == 0)
                return false;

            if (data.faces != null)
                data.faces = DropFaces(data.faces, drop);

            if (data.selectedEdges != null)
                data.selectedEdges = DropEdges(data.selectedEdges, drop);

            Compact(data, drop, -1);
            RemoveUnusedVertices(data);
            return true;
        }

        public static void CollectCoincident(IndieRetopologyMeshData data, int seed, List<int> into)
        {
            into.Clear();

            if (data == null || data.vertices == null || seed < 0 || seed >= data.vertices.Count)
                return;

            var editable = new IndieEditableMeshData();
            editable.vertices.AddRange(data.vertices);
            var set = new HashSet<int> { seed };
            IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(editable, set);
            into.AddRange(set);
        }

        private static int Compact(IndieRetopologyMeshData data, HashSet<int> drop, int keep)
        {
            int count = data.vertices.Count;
            var map = new int[count];
            var kept = new List<Vector3>(count);
            int survivor = -1;

            for (int i = 0; i < count; i++)
            {
                if (drop.Contains(i))
                {
                    map[i] = -1;
                    continue;
                }

                map[i] = kept.Count;

                if (i == keep)
                    survivor = kept.Count;

                kept.Add(data.vertices[i]);
            }

            data.vertices = kept;
            data.faces = RemapFaces(data.faces, map);
            data.selectedEdges = RemapEdges(data.selectedEdges, map);
            data.MarkChanged();
            return survivor;
        }

        private static void RemoveUnusedVertices(IndieRetopologyMeshData data)
        {
            if (data.vertices == null || data.faces == null || data.faces.Count == 0)
                return;

            int count = data.vertices.Count;
            var used = new bool[count];

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (face.a >= 0 && face.a < count)
                    used[face.a] = true;

                if (face.b >= 0 && face.b < count)
                    used[face.b] = true;

                if (face.c >= 0 && face.c < count)
                    used[face.c] = true;
            }

            var map = new int[count];
            var kept = new List<Vector3>(count);
            bool removed = false;

            for (int i = 0; i < count; i++)
            {
                if (!used[i])
                {
                    map[i] = -1;
                    removed = true;
                    continue;
                }

                map[i] = kept.Count;
                kept.Add(data.vertices[i]);
            }

            if (!removed)
                return;

            data.vertices = kept;

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];
                face.a = map[face.a];
                face.b = map[face.b];
                face.c = map[face.c];
                data.faces[i] = face;
            }

            if (data.selectedEdges == null)
                return;

            var edges = new List<IndieRetopologyEdge>(data.selectedEdges.Count);

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                IndieRetopologyEdge edge = data.selectedEdges[i];

                if (edge.v0 < 0 || edge.v0 >= count || edge.v1 < 0 || edge.v1 >= count)
                    continue;

                int v0 = map[edge.v0];
                int v1 = map[edge.v1];

                if (v0 < 0 || v1 < 0 || v0 == v1)
                    continue;

                edges.Add(IndieRetopologyMeshData.Ordered(v0, v1));
            }

            data.selectedEdges = edges;
        }

        private static List<IndieRetopologyFace> DropFaces(List<IndieRetopologyFace> faces, HashSet<int> drop)
        {
            var kept = new List<IndieRetopologyFace>(faces.Count);

            for (int i = 0; i < faces.Count; i++)
            {
                IndieRetopologyFace face = faces[i];

                if (drop.Contains(face.a) || drop.Contains(face.b) || drop.Contains(face.c))
                    continue;

                kept.Add(face);
            }

            return kept;
        }

        private static List<IndieRetopologyEdge> DropEdges(List<IndieRetopologyEdge> edges, HashSet<int> drop)
        {
            var kept = new List<IndieRetopologyEdge>(edges.Count);

            for (int i = 0; i < edges.Count; i++)
            {
                IndieRetopologyEdge edge = edges[i];

                if (drop.Contains(edge.v0) || drop.Contains(edge.v1))
                    continue;

                kept.Add(edge);
            }

            return kept;
        }

        private static List<IndieRetopologyFace> RetargetFaces(List<IndieRetopologyFace> faces, HashSet<int> drop, int into)
        {
            var rebuilt = new List<IndieRetopologyFace>(faces.Count);
            var seen = new HashSet<TriKey>();

            for (int i = 0; i < faces.Count; i++)
            {
                IndieRetopologyFace face = faces[i];
                int a = drop.Contains(face.a) ? into : face.a;
                int b = drop.Contains(face.b) ? into : face.b;
                int c = drop.Contains(face.c) ? into : face.c;

                if (a == b || b == c || c == a)
                    continue;

                TriKey key = FaceKey(a, b, c);

                if (!seen.Add(key))
                    continue;

                face.a = a;
                face.b = b;
                face.c = c;
                rebuilt.Add(face);
            }

            return rebuilt;
        }

        private static List<IndieRetopologyEdge> RetargetEdges(List<IndieRetopologyEdge> edges, HashSet<int> drop, int into)
        {
            var rebuilt = new List<IndieRetopologyEdge>(edges.Count);
            var seen = new HashSet<long>();

            for (int i = 0; i < edges.Count; i++)
            {
                IndieRetopologyEdge edge = edges[i];
                int a = drop.Contains(edge.v0) ? into : edge.v0;
                int b = drop.Contains(edge.v1) ? into : edge.v1;

                if (a == b)
                    continue;

                IndieRetopologyEdge ordered = IndieRetopologyMeshData.Ordered(a, b);
                long key = IndieEditMeshMeshUtil.EdgeKey(ordered.v0, ordered.v1);

                if (!seen.Add(key))
                    continue;

                rebuilt.Add(ordered);
            }

            return rebuilt;
        }

        private static List<IndieRetopologyFace> RemapFaces(List<IndieRetopologyFace> faces, int[] map)
        {
            if (faces == null)
                return null;

            var kept = new List<IndieRetopologyFace>(faces.Count);

            for (int i = 0; i < faces.Count; i++)
            {
                IndieRetopologyFace face = faces[i];
                int a = MapIndex(face.a, map);
                int b = MapIndex(face.b, map);
                int c = MapIndex(face.c, map);

                if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a)
                    continue;

                face.a = a;
                face.b = b;
                face.c = c;
                kept.Add(face);
            }

            return kept;
        }

        private static List<IndieRetopologyEdge> RemapEdges(List<IndieRetopologyEdge> edges, int[] map)
        {
            if (edges == null)
                return null;

            var kept = new List<IndieRetopologyEdge>(edges.Count);
            var seen = new HashSet<long>();

            for (int i = 0; i < edges.Count; i++)
            {
                IndieRetopologyEdge edge = edges[i];
                int a = MapIndex(edge.v0, map);
                int b = MapIndex(edge.v1, map);

                if (a < 0 || b < 0 || a == b)
                    continue;

                IndieRetopologyEdge ordered = IndieRetopologyMeshData.Ordered(a, b);
                long key = IndieEditMeshMeshUtil.EdgeKey(ordered.v0, ordered.v1);

                if (!seen.Add(key))
                    continue;

                kept.Add(ordered);
            }

            return kept;
        }

        private static int MapIndex(int index, int[] map)
        {
            if (index < 0 || index >= map.Length)
                return -1;

            return map[index];
        }

        private static TriKey FaceKey(int a, int b, int c)
        {
            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }

            if (b > c)
            {
                int swap = b;
                b = c;
                c = swap;
            }

            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }

            return new TriKey(a, b, c);
        }

        private readonly struct TriKey : IEquatable<TriKey>
        {
            private readonly int _a;
            private readonly int _b;
            private readonly int _c;

            public TriKey(int a, int b, int c)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            public bool Equals(TriKey other)
            {
                return _a == other._a && _b == other._b && _c == other._c;
            }

            public override bool Equals(object obj)
            {
                return obj is TriKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (_a * 397) ^ (_b * 17) ^ _c;
            }
        }
    }
}
