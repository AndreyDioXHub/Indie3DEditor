using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditableMeshImporter
    {
        public static IndieEditableMeshData Import(Mesh mesh)
        {
            var data = new IndieEditableMeshData();

            data.vertices.AddRange(mesh.vertices);

            if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount)
                data.uvs.AddRange(mesh.uv);

            if (mesh.normals != null && mesh.normals.Length == mesh.vertexCount)
                data.normals.AddRange(mesh.normals);

            if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount)
                data.tangents.AddRange(mesh.tangents);

            if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount)
                data.colors.AddRange(mesh.colors);

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    data.faces.Add(new IndieEditMeshFace
                    {
                        a = triangles[i],
                        b = triangles[i + 1],
                        c = triangles[i + 2],
                        materialId = subMesh,
                        smoothingGroups = 0
                    });
                }
            }

            // Keep Unity vertex splits (UV / hard edges). Stacked verts are edited together
            // via Link Coincident — no import weld, so UVs stay intact.
            IndieEditMeshMeshUtil.AssignSmoothingGroupsFromUnitySplits(data);

            data.RebuildEdges();

            return data;
        }
    }
}
