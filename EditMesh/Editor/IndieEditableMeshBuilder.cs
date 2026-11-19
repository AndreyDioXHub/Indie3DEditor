using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditableMeshBuilder
    {
        public static Mesh BuildMesh(IndieEditableMeshData data)
        {
            return BuildMesh(data, null);
        }

        /// <param name="sourceVertexMap">
        /// Filled with the data vertex index for every output vertex (UV-only refresh
        /// can then rewrite UV0 without rebuilding). Pass null when not needed.
        /// </param>
        public static Mesh BuildMesh(IndieEditableMeshData data, List<int> sourceVertexMap)
        {
            return BuildMesh(data, sourceVertexMap, null, null, null);
        }

        /// <param name="sourceFaceMap">
        /// Filled per data face with (sub-mesh, triangle ordinal in it), or (-1, -1)
        /// when the face emits no triangle. Pass null when not needed.
        /// </param>
        /// <param name="hiddenFaces">
        /// Faces that still shade their neighbours but emit no triangles.
        /// Indices past the array end are visible. Pass null to show every face.
        /// </param>
        /// <param name="useSmoothingGroups">
        /// Shading mode of the whole mesh, so a piece of it shades the same way.
        /// Null detects it from <paramref name="data"/>.
        /// </param>
        public static Mesh BuildMesh(
            IndieEditableMeshData data,
            List<int> sourceVertexMap,
            List<Vector2Int> sourceFaceMap,
            bool[] hiddenFaces,
            bool? useSmoothingGroups
        )
        {
            sourceVertexMap?.Clear();
            sourceFaceMap?.Clear();

            var mesh = new Mesh();
            mesh.name = "Indie EditMesh Mesh";

            if (data == null || data.vertices.Count == 0 || data.faces.Count == 0)
            {
                mesh.RecalculateBounds();
                return mesh;
            }

            bool smoothingGroups = useSmoothingGroups ?? HasAnySmoothingGroup(data);

            if (!smoothingGroups)
                return BuildSmoothMesh(data, sourceVertexMap, sourceFaceMap, hiddenFaces);

            return BuildSmoothingGroupMesh(data, sourceVertexMap, sourceFaceMap, hiddenFaces);
        }

        public static bool HasAnySmoothingGroup(IndieEditableMeshData data)
        {
            for (int i = 0; i < data.faces.Count; i++)
            {
                if (data.faces[i].smoothingGroups != 0)
                    return true;
            }

            return false;
        }

        private static Mesh BuildSmoothMesh(
            IndieEditableMeshData data,
            List<int> sourceVertexMap,
            List<Vector2Int> sourceFaceMap,
            bool[] hiddenFaces
        )
        {
            var mesh = new Mesh();
            mesh.name = "Indie EditMesh Mesh";

            if (data.vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = data.vertices.ToArray();

            if (sourceVertexMap != null)
            {
                for (int i = 0; i < data.vertices.Count; i++)
                    sourceVertexMap.Add(i);
            }

            if (data.uvs.Count == data.vertices.Count)
                mesh.uv = data.uvs.ToArray();

            if (data.colors.Count == data.vertices.Count)
                mesh.colors = data.colors.ToArray();

            // Normals and tangents come from every face, hidden ones included, so the
            // seam with the rest of the mesh shades the same on both sides.
            ApplySubMeshes(mesh, data, null, data.vertices.Count, null, hiddenFaces == null ? sourceFaceMap : null);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            if (data.uvs.Count == data.vertices.Count)
                mesh.RecalculateTangents();

            if (hiddenFaces != null)
                ApplySubMeshes(mesh, data, null, data.vertices.Count, hiddenFaces, sourceFaceMap);

            return mesh;
        }

        private static Mesh BuildSmoothingGroupMesh(
            IndieEditableMeshData data,
            List<int> sourceVertexMap,
            List<Vector2Int> sourceFaceMap,
            bool[] hiddenFaces
        )
        {
            var mesh = new Mesh();
            mesh.name = "Indie EditMesh Mesh";

            var faceNormals = new Vector3[data.faces.Count];

            for (int i = 0; i < data.faces.Count; i++)
                faceNormals[i] = ComputeFaceNormal(data, i);

            var vertexFaces = BuildVertexFaceLists(data);

            var outVertices = new List<Vector3>(data.faces.Count * 3);
            var outNormals = new List<Vector3>(data.faces.Count * 3);
            var outUvs = new List<Vector2>();
            var outColors = new List<Color>();
            bool hasUv = data.uvs.Count == data.vertices.Count;
            bool hasColor = data.colors.Count == data.vertices.Count;

            if (hasUv)
                outUvs.Capacity = data.faces.Count * 3;

            if (hasColor)
                outColors.Capacity = data.faces.Count * 3;

            var weld = new Dictionary<long, int>();
            var faceCornerIndices = new int[data.faces.Count * 3];

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                int baseCorner = faceIndex * 3;

                if (IsHidden(hiddenFaces, faceIndex))
                {
                    faceCornerIndices[baseCorner] = -1;
                    faceCornerIndices[baseCorner + 1] = -1;
                    faceCornerIndices[baseCorner + 2] = -1;
                    continue;
                }

                faceCornerIndices[baseCorner] = GetOrCreateCornerVertex(
                    data,
                    faceIndex,
                    face.a,
                    face.smoothingGroups,
                    faceNormals,
                    vertexFaces,
                    outVertices,
                    outNormals,
                    outUvs,
                    outColors,
                    hasUv,
                    hasColor,
                    weld,
                    sourceVertexMap
                );
                faceCornerIndices[baseCorner + 1] = GetOrCreateCornerVertex(
                    data,
                    faceIndex,
                    face.b,
                    face.smoothingGroups,
                    faceNormals,
                    vertexFaces,
                    outVertices,
                    outNormals,
                    outUvs,
                    outColors,
                    hasUv,
                    hasColor,
                    weld,
                    sourceVertexMap
                );
                faceCornerIndices[baseCorner + 2] = GetOrCreateCornerVertex(
                    data,
                    faceIndex,
                    face.c,
                    face.smoothingGroups,
                    faceNormals,
                    vertexFaces,
                    outVertices,
                    outNormals,
                    outUvs,
                    outColors,
                    hasUv,
                    hasColor,
                    weld,
                    sourceVertexMap
                );
            }

            if (outVertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(outVertices);
            mesh.SetNormals(outNormals);

            if (hasUv)
                mesh.SetUVs(0, outUvs);

            if (hasColor)
                mesh.SetColors(outColors);

            ApplySubMeshes(mesh, data, faceCornerIndices, outVertices.Count, hiddenFaces, sourceFaceMap);
            mesh.RecalculateBounds();

            if (hasUv)
                mesh.RecalculateTangents();

            return mesh;
        }

        private static int GetOrCreateCornerVertex(
            IndieEditableMeshData data,
            int faceIndex,
            int vertexIndex,
            int faceSmoothingGroups,
            Vector3[] faceNormals,
            List<int>[] vertexFaces,
            List<Vector3> outVertices,
            List<Vector3> outNormals,
            List<Vector2> outUvs,
            List<Color> outColors,
            bool hasUv,
            bool hasColor,
            Dictionary<long, int> weld,
            List<int> sourceVertexMap
        )
        {
            Vector3 normal = ComputeCornerNormal(
                faceIndex,
                vertexIndex,
                faceSmoothingGroups,
                faceNormals,
                vertexFaces,
                data
            );

            int normalKey = QuantizeNormal(normal);
            int uvKey = hasUv ? QuantizeUv(data.uvs[vertexIndex]) : 0;
            // Split corners by shading AND UV so a seam never shares a render vertex.
            long key = ((long)vertexIndex << 32) ^ (uint)normalKey ^ ((long)uvKey << 1);

            if (weld.TryGetValue(key, out int existing))
                return existing;

            int created = outVertices.Count;
            outVertices.Add(data.vertices[vertexIndex]);
            outNormals.Add(normal);
            sourceVertexMap?.Add(vertexIndex);

            if (hasUv)
                outUvs.Add(data.uvs[vertexIndex]);

            if (hasColor)
                outColors.Add(data.colors[vertexIndex]);

            weld.Add(key, created);
            return created;
        }

        private static Vector3 ComputeCornerNormal(
            int faceIndex,
            int vertexIndex,
            int faceSmoothingGroups,
            Vector3[] faceNormals,
            List<int>[] vertexFaces,
            IndieEditableMeshData data
        )
        {
            Vector3 faceNormal = faceNormals[faceIndex];

            if (faceSmoothingGroups == 0)
                return faceNormal;

            Vector3 sum = Vector3.zero;
            int count = 0;
            var incident = vertexFaces[vertexIndex];

            for (int i = 0; i < incident.Count; i++)
            {
                int otherFace = incident[i];
                int otherSg = data.faces[otherFace].smoothingGroups;

                if ((faceSmoothingGroups & otherSg) == 0)
                    continue;

                sum += faceNormals[otherFace];
                count++;
            }

            if (count == 0 || sum.sqrMagnitude < 1e-12f)
                return faceNormal;

            return (sum / count).normalized;
        }

        private static List<int>[] BuildVertexFaceLists(IndieEditableMeshData data)
        {
            var lists = new List<int>[data.vertices.Count];

            for (int i = 0; i < lists.Length; i++)
                lists[i] = new List<int>(4);

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];

                if (face.a >= 0 && face.a < lists.Length)
                    lists[face.a].Add(faceIndex);

                if (face.b >= 0 && face.b < lists.Length)
                    lists[face.b].Add(faceIndex);

                if (face.c >= 0 && face.c < lists.Length)
                    lists[face.c].Add(faceIndex);
            }

            return lists;
        }

        private static bool IsHidden(bool[] hiddenFaces, int faceIndex)
        {
            return hiddenFaces != null && faceIndex < hiddenFaces.Length && hiddenFaces[faceIndex];
        }

        private static void ApplySubMeshes(
            Mesh mesh,
            IndieEditableMeshData data,
            int[] faceCornerIndices,
            int vertexCount,
            bool[] hiddenFaces,
            List<Vector2Int> sourceFaceMap
        )
        {
            int subMeshCount = 1;

            foreach (var face in data.faces)
            {
                if (face.materialId + 1 > subMeshCount)
                    subMeshCount = face.materialId + 1;
            }

            var triangles = new List<int>[subMeshCount];

            for (int i = 0; i < subMeshCount; i++)
                triangles[i] = new List<int>();

            sourceFaceMap?.Clear();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                sourceFaceMap?.Add(new Vector2Int(-1, -1));

                if (IsHidden(hiddenFaces, faceIndex))
                    continue;

                var face = data.faces[faceIndex];
                int a;
                int b;
                int c;

                if (faceCornerIndices != null)
                {
                    int baseCorner = faceIndex * 3;
                    a = faceCornerIndices[baseCorner];
                    b = faceCornerIndices[baseCorner + 1];
                    c = faceCornerIndices[baseCorner + 2];
                }
                else
                {
                    a = face.a;
                    b = face.b;
                    c = face.c;
                }

                if (a < 0 || a >= vertexCount ||
                    b < 0 || b >= vertexCount ||
                    c < 0 || c >= vertexCount)
                {
                    continue;
                }

                if (a == b || b == c || c == a)
                    continue;

                int materialId = Mathf.Clamp(face.materialId, 0, subMeshCount - 1);

                if (sourceFaceMap != null)
                    sourceFaceMap[faceIndex] = new Vector2Int(materialId, triangles[materialId].Count / 3);

                triangles[materialId].Add(a);
                triangles[materialId].Add(b);
                triangles[materialId].Add(c);
            }

            mesh.subMeshCount = subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
                mesh.SetTriangles(triangles[i], i);
        }

        private static Vector3 ComputeFaceNormal(IndieEditableMeshData data, int faceIndex)
        {
            var face = data.faces[faceIndex];
            Vector3 a = data.vertices[face.a];
            Vector3 b = data.vertices[face.b];
            Vector3 c = data.vertices[face.c];
            Vector3 n = Vector3.Cross(b - a, c - a);

            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        private static int QuantizeNormal(Vector3 normal)
        {
            const float scale = 1024f;

            int x = Mathf.RoundToInt(normal.x * scale);
            int y = Mathf.RoundToInt(normal.y * scale);
            int z = Mathf.RoundToInt(normal.z * scale);

            unchecked
            {
                int hash = x;
                hash = (hash * 73856093) ^ y;
                hash = (hash * 19349663) ^ z;
                return hash;
            }
        }

        private static int QuantizeUv(Vector2 uv)
        {
            const float scale = 4096f;

            int u = Mathf.RoundToInt(uv.x * scale);
            int v = Mathf.RoundToInt(uv.y * scale);

            unchecked
            {
                return (u * 73856093) ^ (v * 19349663);
            }
        }
    }
}
