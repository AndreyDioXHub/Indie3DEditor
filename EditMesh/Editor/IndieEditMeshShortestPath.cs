using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Shortest 3D edge path shared by Edit Mesh point-to-point selection and Edit UV seams.
    /// </summary>
    public static class IndieEditMeshShortestPath
    {
        /// <summary>
        /// Shortest 3D-length edge path (Dijkstra). With a camera, only edges of
        /// front-facing faces are walkable; near-equal lengths prefer the route closer to the camera.
        /// Vertices stacked on one position (UV seams) are joined at zero cost;
        /// those hops are not edges and never appear in the result.
        /// </summary>
        public static List<long> FindShortestEdgePath(
            IndieEditableMeshData data,
            int fromVertex,
            int toVertex,
            Transform transform,
            Camera camera
        )
        {
            const float CameraTieBreak = 1e-3f;

            var result = new List<long>();

            if (data == null || transform == null || fromVertex == toVertex)
                return result;

            int vertexCount = data.vertices.Count;

            if (fromVertex < 0 || toVertex < 0 ||
                fromVertex >= vertexCount ||
                toVertex >= vertexCount)
            {
                return result;
            }

            var world = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
                world[i] = transform.TransformPoint(data.vertices[i]);

            Dictionary<int, List<int>> adjacency = BuildFrontVertexAdjacency(data, world, camera);

            if (!adjacency.ContainsKey(fromVertex) || !adjacency.ContainsKey(toVertex))
                return result;

            Dictionary<int, List<int>> coincident = BuildCoincidentLinks(data, adjacency);

            var depth = new float[vertexCount];
            float minDepth = float.PositiveInfinity;
            float maxDepth = float.NegativeInfinity;

            if (camera != null)
            {
                Vector3 eye = camera.transform.position;
                Vector3 forward = camera.transform.forward;

                foreach (int v in adjacency.Keys)
                {
                    depth[v] = camera.orthographic
                        ? Vector3.Dot(world[v] - eye, forward)
                        : (world[v] - eye).magnitude;
                    minDepth = Mathf.Min(minDepth, depth[v]);
                    maxDepth = Mathf.Max(maxDepth, depth[v]);
                }
            }

            float depthRange = maxDepth - minDepth;
            var distance = new Dictionary<int, float> { [fromVertex] = 0f };
            var prev = new Dictionary<int, int>();
            var viaLink = new Dictionary<int, bool>();
            var done = new HashSet<int>();
            var open = new SortedSet<(float cost, int vertex)> { (0f, fromVertex) };
            bool found = false;

            while (open.Count > 0)
            {
                var (cost, current) = open.Min;
                open.Remove(open.Min);

                if (!done.Add(current))
                    continue;

                if (current == toVertex)
                {
                    found = true;
                    break;
                }

                List<int> neighbors = adjacency[current];

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];

                    if (done.Contains(next))
                        continue;

                    float weight = (world[next] - world[current]).magnitude;

                    if (depthRange > 1e-12f)
                    {
                        float near = ((depth[current] + depth[next]) * 0.5f - minDepth) / depthRange;
                        weight *= 1f + CameraTieBreak * near;
                    }

                    float candidate = cost + weight;

                    if (distance.TryGetValue(next, out float known) && candidate >= known)
                        continue;

                    if (distance.ContainsKey(next))
                        open.Remove((known, next));

                    distance[next] = candidate;
                    prev[next] = current;
                    viaLink[next] = false;
                    open.Add((candidate, next));
                }

                if (!coincident.TryGetValue(current, out List<int> stack))
                    continue;

                for (int i = 0; i < stack.Count; i++)
                {
                    int next = stack[i];

                    if (next == current || done.Contains(next))
                        continue;

                    if (distance.TryGetValue(next, out float known) && cost >= known)
                        continue;

                    if (distance.ContainsKey(next))
                        open.Remove((known, next));

                    distance[next] = cost;
                    prev[next] = current;
                    viaLink[next] = true;
                    open.Add((cost, next));
                }
            }

            if (!found)
                return result;

            int walk = toVertex;

            while (walk != fromVertex)
            {
                int parent = prev[walk];

                if (!viaLink[walk])
                    result.Add(IndieEditMeshMeshUtil.EdgeKey(parent, walk));

                walk = parent;
            }

            result.Reverse();
            return result;
        }

        /// <summary>Edges of faces facing <paramref name="camera"/> (every face when camera is null).</summary>
        private static Dictionary<int, List<int>> BuildFrontVertexAdjacency(
            IndieEditableMeshData data,
            Vector3[] world,
            Camera camera
        )
        {
            var map = new Dictionary<int, List<int>>();
            int vertexCount = world.Length;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.b < 0 || face.c < 0 ||
                    face.a >= vertexCount || face.b >= vertexCount || face.c >= vertexCount)
                {
                    continue;
                }

                if (camera != null &&
                    !IndieEditMeshPickMath.IsFrontFacingTriangle(camera, world[face.a], world[face.b], world[face.c]))
                {
                    continue;
                }

                AddEdgeNeighbors(map, face.a, face.b);
                AddEdgeNeighbors(map, face.b, face.c);
                AddEdgeNeighbors(map, face.c, face.a);
            }

            return map;
        }

        /// <summary>
        /// Groups of walkable vertices that share one position. Back-face-only
        /// vertices are not in <paramref name="adjacency"/> and stay out.
        /// </summary>
        private static Dictionary<int, List<int>> BuildCoincidentLinks(
            IndieEditableMeshData data,
            Dictionary<int, List<int>> adjacency
        )
        {
            var links = new Dictionary<int, List<int>>();
            var stack = new HashSet<int>();

            foreach (int vertex in adjacency.Keys)
            {
                if (links.ContainsKey(vertex))
                    continue;

                stack.Clear();
                stack.Add(vertex);
                IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(data, stack);

                if (stack.Count < 2)
                    continue;

                var group = new List<int>(stack.Count);

                foreach (int member in stack)
                {
                    if (adjacency.ContainsKey(member))
                        group.Add(member);
                }

                if (group.Count < 2)
                    continue;

                for (int i = 0; i < group.Count; i++)
                    links[group[i]] = group;
            }

            return links;
        }

        private static void AddEdgeNeighbors(Dictionary<int, List<int>> map, int a, int b)
        {
            AddNeighbor(map, a, b);
            AddNeighbor(map, b, a);
        }

        private static void AddNeighbor(Dictionary<int, List<int>> map, int from, int to)
        {
            if (!map.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(4);
                map.Add(from, list);
            }

            if (!list.Contains(to))
                list.Add(to);
        }
    }
}
