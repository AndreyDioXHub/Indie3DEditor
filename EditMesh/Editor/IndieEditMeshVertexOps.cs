using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshVertexOps
    {
        public static void Remove(IndieEditMeshComponent ep)
        {
            if (!CanEditVertices(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Remove Vertices");
            ApplyRemove(ep.Data);
            Finish(ep);
        }

        public static void Break(IndieEditMeshComponent ep)
        {
            if (!CanEditVertices(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Break Vertices");
            ApplyBreak(ep.Data);
            Finish(ep);
        }

        public static void Extrude(IndieEditMeshComponent ep)
        {
            if (!CanEditVertices(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Extrude Vertices");
            ApplyExtrude(
                ep.Data,
                IndieEditMeshVertexParams.ExtrudeHeight,
                IndieEditMeshVertexParams.ExtrudeBaseWidth
            );
            Finish(ep);
        }

        public static void Weld(IndieEditMeshComponent ep)
        {
            if (!CanEditVertices(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Weld Vertices");
            ApplyWeld(ep.Data, IndieEditMeshVertexParams.WeldThreshold);
            Finish(ep);
        }

        public static void Chamfer(IndieEditMeshComponent ep)
        {
            if (!CanEditVertices(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Chamfer Vertices");
            ApplyChamfer(
                ep.Data,
                IndieEditMeshVertexParams.ChamferAmount,
                IndieEditMeshVertexParams.ChamferOpen
            );
            Finish(ep);
        }

        public static void ApplyRemove(IndieEditableMeshData data)
        {
            if (data == null || data.selectedVertices.Count == 0)
                return;

            var selected = new List<int>(data.selectedVertices);
            selected.Sort((a, b) => b.CompareTo(a));

            foreach (int vertex in selected)
            {
                if (vertex < 0 || vertex >= data.vertices.Count)
                    continue;

                RemoveSingleVertex(data, vertex);
            }

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
            data.ClearSelection();
        }

        public static void ApplyBreak(IndieEditableMeshData data)
        {
            if (data == null || data.selectedVertices.Count == 0)
                return;

            var selected = new List<int>(data.selectedVertices);
            var newSelection = new List<int>();

            foreach (int vertex in selected)
            {
                if (vertex < 0 || vertex >= data.vertices.Count)
                    continue;

                var faceIndices = new List<int>();

                for (int i = 0; i < data.faces.Count; i++)
                {
                    var face = data.faces[i];

                    if (face.a == vertex || face.b == vertex || face.c == vertex)
                        faceIndices.Add(i);
                }

                if (faceIndices.Count <= 1)
                {
                    newSelection.Add(vertex);
                    continue;
                }

                newSelection.Add(vertex);

                for (int i = 1; i < faceIndices.Count; i++)
                {
                    int faceIndex = faceIndices[i];
                    int copy = IndieEditMeshMeshUtil.AddVertex(data, vertex, data.vertices[vertex]);
                    var face = data.faces[faceIndex];

                    if (face.a == vertex)
                        face.a = copy;
                    else if (face.b == vertex)
                        face.b = copy;
                    else if (face.c == vertex)
                        face.c = copy;

                    data.faces[faceIndex] = face;
                    newSelection.Add(copy);
                }
            }

            data.RebuildEdges();
            data.selectedVertices = newSelection;
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        public static void ApplyWeld(IndieEditableMeshData data, float threshold)
        {
            if (data == null || data.selectedVertices.Count < 2)
                return;

            float thresholdSq = Mathf.Max(0f, threshold) * Mathf.Max(0f, threshold);
            var selected = new List<int>();

            foreach (int index in data.selectedVertices)
            {
                if (index >= 0 && index < data.vertices.Count && !selected.Contains(index))
                    selected.Add(index);
            }

            if (selected.Count < 2)
                return;

            int[] parent = new int[selected.Count];

            for (int i = 0; i < parent.Length; i++)
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

            for (int i = 0; i < selected.Count; i++)
            {
                Vector3 pi = data.vertices[selected[i]];

                for (int j = i + 1; j < selected.Count; j++)
                {
                    if ((pi - data.vertices[selected[j]]).sqrMagnitude <= thresholdSq)
                        Union(i, j);
                }
            }

            var clusters = new Dictionary<int, List<int>>();

            for (int i = 0; i < selected.Count; i++)
            {
                int root = Find(i);

                if (!clusters.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    clusters.Add(root, list);
                }

                list.Add(selected[i]);
            }

            int[] remap = new int[data.vertices.Count];

            for (int i = 0; i < remap.Length; i++)
                remap[i] = i;

            var keepSelection = new List<int>();

            foreach (var cluster in clusters.Values)
            {
                if (cluster.Count < 2)
                {
                    keepSelection.Add(cluster[0]);
                    continue;
                }

                Vector3 avgPos = Vector3.zero;
                Vector2 avgUv = Vector2.zero;
                Vector3 avgNormal = Vector3.zero;
                Vector4 avgTangent = Vector4.zero;
                Color avgColor = Color.clear;

                foreach (int index in cluster)
                {
                    avgPos += data.vertices[index];
                    avgUv += index < data.uvs.Count ? data.uvs[index] : Vector2.zero;
                    avgNormal += index < data.normals.Count ? data.normals[index] : Vector3.up;
                    avgTangent += index < data.tangents.Count
                        ? data.tangents[index]
                        : new Vector4(1f, 0f, 0f, -1f);
                    avgColor += index < data.colors.Count ? data.colors[index] : Color.white;
                }

                float inv = 1f / cluster.Count;
                int target = cluster[0];

                data.vertices[target] = avgPos * inv;

                if (target < data.uvs.Count)
                    data.uvs[target] = avgUv * inv;

                if (target < data.normals.Count)
                {
                    Vector3 n = avgNormal * inv;
                    data.normals[target] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                }

                if (target < data.tangents.Count)
                    data.tangents[target] = avgTangent * inv;

                if (target < data.colors.Count)
                    data.colors[target] = avgColor * inv;

                for (int i = 1; i < cluster.Count; i++)
                    remap[cluster[i]] = target;

                keepSelection.Add(target);
            }

            var newFaces = new List<IndieEditMeshFace>();

            foreach (var face in data.faces)
            {
                IndieEditMeshMeshUtil.AddTriangle(
                    newFaces,
                    remap[face.a],
                    remap[face.b],
                    remap[face.c],
                    face.materialId
                );
            }

            data.faces = newFaces;
            data.selectedVertices = keepSelection;
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
        }

        public static void ApplyExtrude(IndieEditableMeshData data, float height, float baseWidth)
        {
            if (data == null || data.selectedVertices.Count == 0)
                return;

            var selected = new List<int>(data.selectedVertices);
            selected.Sort((a, b) => b.CompareTo(a));

            var newSelection = new List<int>();

            foreach (int vertex in selected)
            {
                if (vertex < 0 || vertex >= data.vertices.Count)
                    continue;

                if (ExtrudeSingle(data, vertex, height, baseWidth))
                    newSelection.Add(vertex);
            }

            data.RebuildEdges();
            data.selectedVertices = newSelection;
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        public static void ApplyChamfer(IndieEditableMeshData data, float amount, bool open)
        {
            if (data == null || data.selectedVertices.Count == 0)
                return;

            var selected = new List<int>(data.selectedVertices);
            selected.Sort((a, b) => b.CompareTo(a));

            foreach (int vertex in selected)
            {
                if (vertex < 0 || vertex >= data.vertices.Count)
                    continue;

                ChamferSingle(data, vertex, amount, open);
            }

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
            data.ClearSelection();
        }

        private static void RemoveSingleVertex(IndieEditableMeshData data, int vertex)
        {
            var neighbors = new List<int>();
            var faceIndices = new List<int>();

            if (!IndieEditMeshMeshUtil.TryGetOrderedOneRing(data, vertex, neighbors, faceIndices))
            {
                // Isolated or fan failed — delete incident faces only.
                data.faces.RemoveAll(face =>
                    face.a == vertex || face.b == vertex || face.c == vertex
                );
                return;
            }

            int materialId = faceIndices.Count > 0
                ? data.faces[faceIndices[0]].materialId
                : 0;

            var removeSet = new HashSet<int>(faceIndices);
            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!removeSet.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            if (neighbors.Count >= 3)
                IndieEditMeshMeshUtil.TriangulatePolygonFan(newFaces, neighbors, materialId, false);

            data.faces = newFaces;
        }

        private static bool IsClosedRing(IndieEditableMeshData data, int vertex, List<int> neighbors)
        {
            if (neighbors.Count < 3)
                return false;

            int first = neighbors[0];
            int last = neighbors[neighbors.Count - 1];

            foreach (var face in data.faces)
            {
                if (face.a != vertex && face.b != vertex && face.c != vertex)
                    continue;

                bool hasFirst = face.a == first || face.b == first || face.c == first;
                bool hasLast = face.a == last || face.b == last || face.c == last;

                if (hasFirst && hasLast)
                    return true;
            }

            return false;
        }

        private static bool ExtrudeSingle(
            IndieEditableMeshData data,
            int vertex,
            float height,
            float baseWidth
        )
        {
            var neighbors = new List<int>();
            var faceIndices = new List<int>();

            if (!IndieEditMeshMeshUtil.TryGetOrderedOneRing(data, vertex, neighbors, faceIndices))
                return false;

            if (neighbors.Count < 2)
                return false;

            Vector3 origin = data.vertices[vertex];
            Vector3 normal = IndieEditMeshMeshUtil.ComputeVertexNormal(data, vertex);
            float width = Mathf.Max(0f, baseWidth);

            var baseVerts = new List<int>(neighbors.Count);

            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3 toNeighbor = data.vertices[neighbors[i]] - origin;
                float dist = toNeighbor.magnitude;
                float t = dist > 1e-8f ? Mathf.Clamp01(width / dist) : 0f;
                Vector3 basePos = origin + toNeighbor * t;
                baseVerts.Add(IndieEditMeshMeshUtil.AddVertex(data, vertex, basePos));
            }

            data.vertices[vertex] = origin + normal * height;

            var removeSet = new HashSet<int>(faceIndices);
            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!removeSet.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            bool closed = IsClosedRing(data, vertex, neighbors);
            int count = neighbors.Count;
            int sideCount = closed ? count : count - 1;

            for (int i = 0; i < sideCount; i++)
            {
                int next = (i + 1) % count;
                int faceIndex = i < faceIndices.Count ? faceIndices[i] : faceIndices[0];
                int materialId = data.faces[faceIndex].materialId;

                int n0 = neighbors[i];
                int n1 = neighbors[next];
                int b0 = baseVerts[i];
                int b1 = baseVerts[next];

                // Restitch surface where the original triangle sat.
                IndieEditMeshMeshUtil.AddTriangle(newFaces, b0, n0, n1, materialId);
                IndieEditMeshMeshUtil.AddTriangle(newFaces, b0, n1, b1, materialId);

                // Side faces to extruded tip.
                IndieEditMeshMeshUtil.AddTriangle(newFaces, vertex, b0, b1, materialId);
            }

            data.faces = newFaces;
            return true;
        }

        private static void ChamferSingle(
            IndieEditableMeshData data,
            int vertex,
            float amount,
            bool open
        )
        {
            var neighbors = new List<int>();
            var faceIndices = new List<int>();

            if (!IndieEditMeshMeshUtil.TryGetOrderedOneRing(data, vertex, neighbors, faceIndices))
                return;

            if (neighbors.Count < 2)
                return;

            Vector3 origin = data.vertices[vertex];
            float chamfer = Mathf.Max(0f, amount);

            var chamferVerts = new List<int>(neighbors.Count);

            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3 toNeighbor = data.vertices[neighbors[i]] - origin;
                float dist = toNeighbor.magnitude;
                float t = dist > 1e-8f ? Mathf.Clamp01(chamfer / dist) : 0f;
                Vector3 pos = origin + toNeighbor * t;
                chamferVerts.Add(IndieEditMeshMeshUtil.AddVertex(data, vertex, pos));
            }

            var removeSet = new HashSet<int>(faceIndices);
            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!removeSet.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            bool closed = IsClosedRing(data, vertex, neighbors);
            int count = neighbors.Count;
            int sideCount = closed ? count : count - 1;
            int materialId = faceIndices.Count > 0
                ? data.faces[faceIndices[0]].materialId
                : 0;

            for (int i = 0; i < sideCount; i++)
            {
                int next = (i + 1) % count;
                int faceIndex = i < faceIndices.Count ? faceIndices[i] : faceIndices[0];
                int faceMaterial = data.faces[faceIndex].materialId;

                int n0 = neighbors[i];
                int n1 = neighbors[next];
                int c0 = chamferVerts[i];
                int c1 = chamferVerts[next];

                IndieEditMeshMeshUtil.AddTriangle(newFaces, c0, n0, n1, faceMaterial);
                IndieEditMeshMeshUtil.AddTriangle(newFaces, c0, n1, c1, faceMaterial);
            }

            if (!open && closed && chamferVerts.Count >= 3)
            {
                IndieEditMeshMeshUtil.TriangulatePolygonFan(newFaces, chamferVerts, materialId, false);
            }

            data.faces = newFaces;
        }

        private static bool CanEditVertices(IndieEditMeshComponent ep)
        {
            return ep != null &&
                   ep.Data != null &&
                   IndieEditMeshSession.Active == ep &&
                   ep.subMode == IndieEditMeshSubMode.Vertex &&
                   ep.Data.selectedVertices.Count > 0;
        }

        private static void Finish(IndieEditMeshComponent ep)
        {
            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
        }
    }
}
