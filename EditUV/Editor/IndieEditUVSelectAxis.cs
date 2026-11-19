using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Select U / Select V: replace the current edge or vertex line with the
    /// straight UV continuation of one axis, staying inside one seam island.
    /// </summary>
    public static class IndieEditUVSelectAxis
    {
        public enum Axis
        {
            U,
            V
        }

        private const float MaxAngleCos = 0.70710678118f;
        private const float AlignTieEpsilon = 1e-4f;
        private const float UvEpsilonSqr = 1e-12f;
        private const int MaxSteps = 4096;

        public static bool CanSelect(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return false;

            if (uv.subMode == IndieEditMeshSubMode.Edge)
                return HasSelectedEdge(uv.Data);

            if (uv.subMode == IndieEditMeshSubMode.Vertex)
                return IsVertexLine(uv.Data);

            return false;
        }

        public static void Apply(IndieEditUVComponent uv, Axis axis)
        {
            if (!CanSelect(uv))
                return;

            IndieEditableMeshData data = uv.Data;
            Vector2 axisDir = axis == Axis.U ? Vector2.right : Vector2.up;
            List<int> seedEdges = uv.subMode == IndieEditMeshSubMode.Edge
                ? ValidSelectedEdges(data)
                : PathEdges(data);

            if (seedEdges.Count == 0)
                return;

            List<List<int>> chains = BuildChains(data, seedEdges);
            Dictionary<long, List<int>> edgeToFaces = BuildEdgeFaceMap(data);
            Dictionary<int, List<int>> vertEdges = BuildVertexEdgeMap(data);
            Vector2 centroid = SelectionCentroid(data, uv.subMode);
            var result = new HashSet<int>();

            for (int i = 0; i < chains.Count; i++)
            {
                List<int> chain = chains[i];
                HashSet<int> region = CollectRegion(data, edgeToFaces, chain);
                int spine = PickSpine(
                    data,
                    chain,
                    vertEdges,
                    edgeToFaces,
                    region,
                    axisDir,
                    centroid
                );

                if (spine < 0)
                    continue;

                HashSet<int> line = GrowLine(
                    data,
                    spine,
                    vertEdges,
                    edgeToFaces,
                    region,
                    axisDir
                );

                foreach (int edgeIndex in line)
                    result.Add(edgeIndex);
            }

            if (result.Count == 0 || SameSelection(data, uv.subMode, result))
                return;

            IndieEditUVUndo.Register(uv, axis == Axis.U ? "Select U" : "Select V");
            WriteSelection(data, uv.subMode, result);
            IndieEditUVGizmo.Reset();
            IndieEditUVNotify.SelectionChanged();
        }

        /// <summary>
        /// Flat U sets every selected vertex U to the average U. Flat V does the same for V.
        /// The other axis stays put. No undo when that axis is already flat.
        /// </summary>
        public static void Flatten(IndieEditUVComponent uv, Axis axis)
        {
            if (!CanSelect(uv) || uv.Data == null)
                return;

            int[] indices = IndieEditUVTransform.GetAffectedVertices(uv);

            if (indices == null || indices.Length < 2)
                return;

            IndieEditableMeshData data = uv.Data;
            float sum = 0f;
            int count = 0;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                Vector2 coord = data.uvs[index];
                sum += axis == Axis.U ? coord.x : coord.y;
                count++;
            }

            if (count < 2)
                return;

            float average = sum / count;
            bool alreadyFlat = true;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                float value = axis == Axis.U ? data.uvs[index].x : data.uvs[index].y;

                if (Mathf.Abs(value - average) > 1e-6f)
                {
                    alreadyFlat = false;
                    break;
                }
            }

            if (alreadyFlat)
                return;

            IndieEditUVUndo.Register(uv, axis == Axis.U ? "Flat U" : "Flat V");

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                Vector2 coord = data.uvs[index];

                if (axis == Axis.U)
                    coord.x = average;
                else
                    coord.y = average;

                data.uvs[index] = coord;
            }

            IndieEditUVSession.RefreshUvsOnly(uv);
            IndieEditUVNotify.UvsChanged(uv, indices);
        }

        /// <summary>
        /// Selected vertices are one path: every vertex lies on the path,
        /// edges are n-1, degrees are 1 or 2, and there are two endpoints.
        /// </summary>
        private static bool IsVertexLine(IndieEditableMeshData data)
        {
            if (data.selectedVertices.Count < 2)
                return false;

            var selected = new HashSet<int>();

            for (int i = 0; i < data.selectedVertices.Count; i++)
            {
                int vertex = data.selectedVertices[i];

                if (vertex < 0 || vertex >= data.vertices.Count)
                    return false;

                selected.Add(vertex);
            }

            int count = selected.Count;

            if (count < 2)
                return false;

            var degree = new Dictionary<int, int>(count);
            int edgeCount = 0;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (!selected.Contains(edge.v0) || !selected.Contains(edge.v1))
                    continue;

                edgeCount++;
                AddDegree(degree, edge.v0);
                AddDegree(degree, edge.v1);
            }

            if (edgeCount != count - 1)
                return false;

            int ends = 0;

            foreach (int vertex in selected)
            {
                if (!degree.TryGetValue(vertex, out int d))
                    return false;

                if (d == 1)
                    ends++;
                else if (d != 2)
                    return false;
            }

            return ends == 2;
        }

        private static bool HasSelectedEdge(IndieEditableMeshData data)
        {
            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                int edgeIndex = data.selectedEdges[i];

                if (edgeIndex >= 0 && edgeIndex < data.edges.Count)
                    return true;
            }

            return false;
        }

        private static List<int> ValidSelectedEdges(IndieEditableMeshData data)
        {
            var list = new List<int>();
            var seen = new HashSet<int>();

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                int edgeIndex = data.selectedEdges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                if (seen.Add(edgeIndex))
                    list.Add(edgeIndex);
            }

            return list;
        }

        private static List<int> PathEdges(IndieEditableMeshData data)
        {
            var selected = new HashSet<int>(data.selectedVertices);
            var list = new List<int>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (selected.Contains(edge.v0) && selected.Contains(edge.v1))
                    list.Add(i);
            }

            return list;
        }

        private static List<List<int>> BuildChains(IndieEditableMeshData data, List<int> seedEdges)
        {
            var chains = new List<List<int>>();
            var remaining = new HashSet<int>(seedEdges);
            var vertToEdges = new Dictionary<int, List<int>>();

            for (int i = 0; i < seedEdges.Count; i++)
            {
                int edgeIndex = seedEdges[i];
                var edge = data.edges[edgeIndex];
                AddVertEdge(vertToEdges, edge.v0, edgeIndex);
                AddVertEdge(vertToEdges, edge.v1, edgeIndex);
            }

            while (remaining.Count > 0)
            {
                int start = -1;

                foreach (int edgeIndex in remaining)
                {
                    start = edgeIndex;
                    break;
                }

                var chain = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                remaining.Remove(start);

                while (queue.Count > 0)
                {
                    int edgeIndex = queue.Dequeue();
                    chain.Add(edgeIndex);
                    var edge = data.edges[edgeIndex];
                    EnqueueChainNeighbors(vertToEdges, remaining, queue, edge.v0);
                    EnqueueChainNeighbors(vertToEdges, remaining, queue, edge.v1);
                }

                chains.Add(chain);
            }

            return chains;
        }

        private static void EnqueueChainNeighbors(
            Dictionary<int, List<int>> vertToEdges,
            HashSet<int> remaining,
            Queue<int> queue,
            int vertex
        )
        {
            if (!vertToEdges.TryGetValue(vertex, out List<int> edges))
                return;

            for (int i = 0; i < edges.Count; i++)
            {
                int next = edges[i];

                if (remaining.Remove(next))
                    queue.Enqueue(next);
            }
        }

        private static int PickSpine(
            IndieEditableMeshData data,
            List<int> chain,
            Dictionary<int, List<int>> vertEdges,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> region,
            Vector2 axisDir,
            Vector2 centroid
        )
        {
            int seedSpine = BestAligned(data, chain, axisDir);

            if (seedSpine >= 0)
                return seedSpine;

            var chainSet = new HashSet<int>(chain);
            var chainVerts = new HashSet<int>();
            CollectChainVertices(data, chain, chainVerts);

            int best = -1;
            float bestAlign = -1f;
            float bestDist = float.PositiveInfinity;

            foreach (int vertex in chainVerts)
            {
                if (!vertEdges.TryGetValue(vertex, out List<int> edges))
                    continue;

                for (int i = 0; i < edges.Count; i++)
                {
                    int edgeIndex = edges[i];

                    if (chainSet.Contains(edgeIndex))
                        continue;

                    if (!EdgeInsideRegion(data, edgeIndex, edgeToFaces, region))
                        continue;

                    if (!TryUvDirection(data, edgeIndex, out Vector2 direction))
                        continue;

                    float align = Mathf.Abs(Vector2.Dot(direction, axisDir));

                    if (align < MaxAngleCos)
                        continue;

                    float dist = MidpointDistanceSqr(data, edgeIndex, centroid);
                    bool closerAlign = align > bestAlign + AlignTieEpsilon;
                    bool tie = Mathf.Abs(align - bestAlign) <= AlignTieEpsilon;

                    if (best < 0 || closerAlign || (tie && dist < bestDist))
                    {
                        best = edgeIndex;
                        bestAlign = align;
                        bestDist = dist;
                    }
                }
            }

            return best;
        }

        /// <summary>Highest |dot| with the axis among edges already within 45 degrees.</summary>
        private static int BestAligned(IndieEditableMeshData data, List<int> edges, Vector2 axisDir)
        {
            int best = -1;
            float bestAlign = -1f;

            for (int i = 0; i < edges.Count; i++)
            {
                int edgeIndex = edges[i];

                if (!TryUvDirection(data, edgeIndex, out Vector2 direction))
                    continue;

                float align = Mathf.Abs(Vector2.Dot(direction, axisDir));

                if (align < MaxAngleCos || align <= bestAlign)
                    continue;

                bestAlign = align;
                best = edgeIndex;
            }

            return best;
        }

        private static HashSet<int> GrowLine(
            IndieEditableMeshData data,
            int spine,
            Dictionary<int, List<int>> vertEdges,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> region,
            Vector2 axisDir
        )
        {
            var result = new HashSet<int> { spine };

            if (!TryOrientSpine(
                    data,
                    spine,
                    axisDir,
                    out int positive,
                    out int negative,
                    out Vector2 originalDir
                ))
            {
                return result;
            }

            var visited = new HashSet<int> { positive, negative };
            GrowDirection(
                data,
                positive,
                spine,
                originalDir,
                vertEdges,
                edgeToFaces,
                region,
                result,
                visited
            );
            GrowDirection(
                data,
                negative,
                spine,
                -originalDir,
                vertEdges,
                edgeToFaces,
                region,
                result,
                visited
            );
            return result;
        }

        private static void GrowDirection(
            IndieEditableMeshData data,
            int startVert,
            int incomingEdge,
            Vector2 travelDir,
            Dictionary<int, List<int>> vertEdges,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> region,
            HashSet<int> result,
            HashSet<int> visited
        )
        {
            int curr = startVert;
            int prevEdge = incomingEdge;

            for (int step = 0; step < MaxSteps; step++)
            {
                if (!vertEdges.TryGetValue(curr, out List<int> edges))
                    break;

                int best = -1;
                int bestOther = -1;
                float bestDot = MaxAngleCos;

                for (int i = 0; i < edges.Count; i++)
                {
                    int edgeIndex = edges[i];

                    if (edgeIndex == prevEdge || result.Contains(edgeIndex))
                        continue;

                    if (!EdgeInsideRegion(data, edgeIndex, edgeToFaces, region))
                        continue;

                    var edge = data.edges[edgeIndex];
                    int other = edge.v0 == curr ? edge.v1 : edge.v0;

                    if (other == curr || visited.Contains(other))
                        continue;

                    if (!TryUvDelta(data, curr, other, out Vector2 outDir))
                        continue;

                    float dot = Vector2.Dot(outDir, travelDir);

                    if (dot <= bestDot)
                        continue;

                    bestDot = dot;
                    best = edgeIndex;
                    bestOther = other;
                }

                if (best < 0)
                    break;

                result.Add(best);
                visited.Add(bestOther);
                prevEdge = best;
                curr = bestOther;
            }
        }

        /// <summary>
        /// Faces touched by the chain, flooded up to baked and working seams.
        /// A seam seed contributes one side only, so the opposite island stays out.
        /// An open mesh border does not expand the region to the whole mesh.
        /// </summary>
        private static HashSet<int> CollectRegion(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            List<int> chain
        )
        {
            var region = new HashSet<int>();
            var queue = new Queue<int>();

            for (int i = 0; i < chain.Count; i++)
            {
                int edgeIndex = chain[i];
                var edge = data.edges[edgeIndex];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (!edgeToFaces.TryGetValue(key, out List<int> faces) || faces.Count == 0)
                    continue;

                if (IndieEditUVSeamState.IsBoundary(key))
                    EnqueueFace(data, region, queue, faces[0]);
                else
                {
                    for (int f = 0; f < faces.Count; f++)
                        EnqueueFace(data, region, queue, faces[f]);
                }
            }

            while (queue.Count > 0)
            {
                int faceIndex = queue.Dequeue();
                var face = data.faces[faceIndex];
                ExpandAcross(data, edgeToFaces, region, queue, face.a, face.b, faceIndex);
                ExpandAcross(data, edgeToFaces, region, queue, face.b, face.c, faceIndex);
                ExpandAcross(data, edgeToFaces, region, queue, face.c, face.a, faceIndex);
            }

            return region;
        }

        private static void ExpandAcross(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> region,
            Queue<int> queue,
            int a,
            int b,
            int faceIndex
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (IndieEditUVSeamState.IsBoundary(key))
                return;

            if (!edgeToFaces.TryGetValue(key, out List<int> faces))
                return;

            for (int i = 0; i < faces.Count; i++)
            {
                int other = faces[i];

                if (other == faceIndex)
                    continue;

                EnqueueFace(data, region, queue, other);
            }
        }

        private static void EnqueueFace(
            IndieEditableMeshData data,
            HashSet<int> region,
            Queue<int> queue,
            int faceIndex
        )
        {
            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return;

            if (region.Add(faceIndex))
                queue.Enqueue(faceIndex);
        }

        /// <summary>
        /// Interior to the island: not a seam, and every incident face was flooded.
        /// </summary>
        private static bool EdgeInsideRegion(
            IndieEditableMeshData data,
            int edgeIndex,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> region
        )
        {
            if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                return false;

            var edge = data.edges[edgeIndex];
            long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

            if (IndieEditUVSeamState.IsBoundary(key))
                return false;

            if (!edgeToFaces.TryGetValue(key, out List<int> faces) || faces.Count == 0)
                return false;

            for (int i = 0; i < faces.Count; i++)
            {
                if (!region.Contains(faces[i]))
                    return false;
            }

            return true;
        }

        private static bool TryOrientSpine(
            IndieEditableMeshData data,
            int spine,
            Vector2 axisDir,
            out int positive,
            out int negative,
            out Vector2 originalDir
        )
        {
            positive = -1;
            negative = -1;
            originalDir = Vector2.zero;

            if (!TryUvDirection(data, spine, out Vector2 direction))
                return false;

            var edge = data.edges[spine];

            if (Vector2.Dot(direction, axisDir) >= 0f)
            {
                originalDir = direction;
                positive = edge.v1;
                negative = edge.v0;
            }
            else
            {
                originalDir = -direction;
                positive = edge.v0;
                negative = edge.v1;
            }

            return true;
        }

        private static bool TryUvDirection(
            IndieEditableMeshData data,
            int edgeIndex,
            out Vector2 direction
        )
        {
            direction = Vector2.zero;

            if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                return false;

            var edge = data.edges[edgeIndex];
            return TryUvDelta(data, edge.v0, edge.v1, out direction);
        }

        private static bool TryUvDelta(
            IndieEditableMeshData data,
            int from,
            int to,
            out Vector2 direction
        )
        {
            direction = Vector2.zero;

            if (from < 0 || to < 0 ||
                from >= data.uvs.Count || to >= data.uvs.Count)
            {
                return false;
            }

            Vector2 delta = data.uvs[to] - data.uvs[from];

            if (delta.sqrMagnitude < UvEpsilonSqr)
                return false;

            direction = delta.normalized;
            return true;
        }

        private static float MidpointDistanceSqr(
            IndieEditableMeshData data,
            int edgeIndex,
            Vector2 centroid
        )
        {
            var edge = data.edges[edgeIndex];
            Vector2 mid = (data.uvs[edge.v0] + data.uvs[edge.v1]) * 0.5f;
            return (mid - centroid).sqrMagnitude;
        }

        private static Vector2 SelectionCentroid(IndieEditableMeshData data, IndieEditMeshSubMode mode)
        {
            var verts = new HashSet<int>();

            if (mode == IndieEditMeshSubMode.Vertex)
            {
                for (int i = 0; i < data.selectedVertices.Count; i++)
                    verts.Add(data.selectedVertices[i]);
            }
            else
            {
                for (int i = 0; i < data.selectedEdges.Count; i++)
                {
                    int edgeIndex = data.selectedEdges[i];

                    if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                        continue;

                    var edge = data.edges[edgeIndex];
                    verts.Add(edge.v0);
                    verts.Add(edge.v1);
                }
            }

            Vector2 sum = Vector2.zero;
            int count = 0;

            foreach (int vertex in verts)
            {
                if (vertex < 0 || vertex >= data.uvs.Count)
                    continue;

                sum += data.uvs[vertex];
                count++;
            }

            return count > 0 ? sum / count : Vector2.zero;
        }

        private static void CollectChainVertices(
            IndieEditableMeshData data,
            List<int> chain,
            HashSet<int> verts
        )
        {
            for (int i = 0; i < chain.Count; i++)
            {
                int edgeIndex = chain[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                verts.Add(edge.v0);
                verts.Add(edge.v1);
            }
        }

        private static bool SameSelection(
            IndieEditableMeshData data,
            IndieEditMeshSubMode mode,
            HashSet<int> edges
        )
        {
            if (mode == IndieEditMeshSubMode.Edge)
            {
                if (data.selectedVertices.Count != 0 || data.selectedFaces.Count != 0)
                    return false;

                if (data.selectedEdges.Count != edges.Count)
                    return false;

                for (int i = 0; i < data.selectedEdges.Count; i++)
                {
                    if (!edges.Contains(data.selectedEdges[i]))
                        return false;
                }

                return true;
            }

            var verts = new HashSet<int>();

            foreach (int edgeIndex in edges)
            {
                var edge = data.edges[edgeIndex];
                verts.Add(edge.v0);
                verts.Add(edge.v1);
            }

            if (data.selectedEdges.Count != 0 || data.selectedFaces.Count != 0)
                return false;

            if (data.selectedVertices.Count != verts.Count)
                return false;

            for (int i = 0; i < data.selectedVertices.Count; i++)
            {
                if (!verts.Contains(data.selectedVertices[i]))
                    return false;
            }

            return true;
        }

        private static void WriteSelection(
            IndieEditableMeshData data,
            IndieEditMeshSubMode mode,
            HashSet<int> edges
        )
        {
            if (mode == IndieEditMeshSubMode.Edge)
            {
                data.selectedEdges = new List<int>(edges);
                data.selectedVertices.Clear();
                data.selectedFaces.Clear();
                return;
            }

            var verts = new HashSet<int>();

            foreach (int edgeIndex in edges)
            {
                var edge = data.edges[edgeIndex];
                verts.Add(edge.v0);
                verts.Add(edge.v1);
            }

            data.selectedVertices = new List<int>(verts);
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
        }

        private static Dictionary<long, List<int>> BuildEdgeFaceMap(IndieEditableMeshData data)
        {
            var map = new Dictionary<long, List<int>>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddFaceEdge(map, face.a, face.b, i);
                AddFaceEdge(map, face.b, face.c, i);
                AddFaceEdge(map, face.c, face.a, i);
            }

            return map;
        }

        private static void AddFaceEdge(
            Dictionary<long, List<int>> map,
            int a,
            int b,
            int faceIndex
        )
        {
            if (a < 0 || b < 0 || a == b)
                return;

            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (!map.TryGetValue(key, out List<int> faces))
            {
                faces = new List<int>(2);
                map.Add(key, faces);
            }

            if (!faces.Contains(faceIndex))
                faces.Add(faceIndex);
        }

        private static Dictionary<int, List<int>> BuildVertexEdgeMap(IndieEditableMeshData data)
        {
            var map = new Dictionary<int, List<int>>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                AddVertEdge(map, edge.v0, i);
                AddVertEdge(map, edge.v1, i);
            }

            return map;
        }

        private static void AddVertEdge(Dictionary<int, List<int>> map, int vertex, int edgeIndex)
        {
            if (!map.TryGetValue(vertex, out List<int> edges))
            {
                edges = new List<int>(4);
                map.Add(vertex, edges);
            }

            edges.Add(edgeIndex);
        }

        private static void AddDegree(Dictionary<int, int> degree, int vertex)
        {
            if (degree.TryGetValue(vertex, out int count))
                degree[vertex] = count + 1;
            else
                degree.Add(vertex, 1);
        }
    }
}
