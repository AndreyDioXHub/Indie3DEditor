using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyMeshBuilder
    {
        public const string PreviewMeshName = "Retopo Preview";

        private static readonly Vector3 BaryA = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 BaryB = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 BaryC = new Vector3(0f, 0f, 1f);
        private static readonly List<Vector3> PreviewVertices = new List<Vector3>();
        private static readonly List<Vector3> PreviewBarycentric = new List<Vector3>();
        private static readonly List<int> PreviewTriangles = new List<int>();
        private static readonly List<int> PreviewFaceCorners = new List<int>();
        private static Mesh _previewMesh;
        private static IndieRetopologyMeshData _previewData;
        private static int _previewRevision;

        public static Mesh Build(IndieRetopologyMeshData data)
        {
            var mesh = new Mesh();
            Fill(mesh, data, false);
            return mesh;
        }

        public static void Fill(Mesh mesh, IndieRetopologyMeshData data)
        {
            Fill(mesh, data, false);
        }

        public static void FillPreview(Mesh mesh, IndieRetopologyMeshData data)
        {
            Fill(mesh, data, true);
        }

        public static bool IsPreviewCurrent(Mesh mesh, IndieRetopologyMeshData data)
        {
            return mesh != null &&
                data != null &&
                mesh == _previewMesh &&
                data == _previewData &&
                data.revision == _previewRevision &&
                data.faces != null &&
                data.faces.Count == PreviewFaceCorners.Count;
        }

        public static bool WritePreviewPositions(Mesh mesh, IndieRetopologyMeshData data, List<int> faces)
        {
            if (!IsPreviewCurrent(mesh, data) || data.vertices == null)
                return false;

            if (faces == null || faces.Count == 0)
                return true;

            int vertexCount = data.vertices.Count;
            bool written = false;

            for (int i = 0; i < faces.Count; i++)
            {
                int faceIndex = faces[i];

                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                IndieRetopologyFace face = data.faces[faceIndex];
                int corner = PreviewFaceCorners[faceIndex];
                bool valid = IsValidFace(face, vertexCount);

                if (valid != corner >= 0 || corner + 2 >= PreviewVertices.Count)
                    return false;

                if (!valid)
                    continue;

                PreviewVertices[corner] = data.vertices[face.a];
                PreviewVertices[corner + 1] = data.vertices[face.b];
                PreviewVertices[corner + 2] = data.vertices[face.c];
                written = true;
            }

            if (!written)
                return true;

            mesh.SetVertices(PreviewVertices);
            mesh.RecalculateBounds();
            return true;
        }

        public static bool IsValidFace(IndieRetopologyFace face, int vertexCount)
        {
            return face.a >= 0 && face.b >= 0 && face.c >= 0 &&
                face.a < vertexCount && face.b < vertexCount && face.c < vertexCount;
        }

        private static void Fill(Mesh mesh, IndieRetopologyMeshData data, bool preview)
        {
            if (mesh == null)
                return;

            int vertexCount = data != null && data.vertices != null ? data.vertices.Count : 0;
            int faceCount = data != null && data.faces != null ? data.faces.Count : 0;
            int written = 0;

            for (int i = 0; i < faceCount; i++)
            {
                if (IsValidFace(data.faces[i], vertexCount))
                    written++;
            }

            int meshVertices = preview ? written * 3 : vertexCount;

            if (preview)
                BeginPreview(mesh, data, faceCount);

            mesh.Clear();
            mesh.indexFormat = meshVertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            if (vertexCount == 0 || (preview && written == 0))
            {
                mesh.RecalculateBounds();
                return;
            }

            if (!preview)
            {
                mesh.SetVertices(data.vertices);
                WriteSharedTriangles(mesh, data, vertexCount);
            }
            else
            {
                WritePreview(mesh, data, vertexCount, written);
            }

            if (!preview && written > 0)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
        }

        private static void BeginPreview(Mesh mesh, IndieRetopologyMeshData data, int faceCount)
        {
            _previewMesh = mesh;
            _previewData = data;
            _previewRevision = data != null ? data.revision : 0;
            PreviewVertices.Clear();
            PreviewFaceCorners.Clear();

            if (PreviewFaceCorners.Capacity < faceCount)
                PreviewFaceCorners.Capacity = faceCount;

            for (int i = 0; i < faceCount; i++)
                PreviewFaceCorners.Add(-1);
        }

        private static void WriteSharedTriangles(Mesh mesh, IndieRetopologyMeshData data, int vertexCount)
        {
            int faceCount = data.faces != null ? data.faces.Count : 0;

            if (faceCount == 0)
                return;

            var triangles = new List<int>(faceCount * 3);

            for (int i = 0; i < faceCount; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IsValidFace(face, vertexCount))
                    continue;

                triangles.Add(face.a);
                triangles.Add(face.b);
                triangles.Add(face.c);
            }

            if (triangles.Count > 0)
                mesh.SetTriangles(triangles, 0);
        }

        private static void WritePreview(Mesh mesh, IndieRetopologyMeshData data, int vertexCount, int written)
        {
            int count = written * 3;

            if (PreviewVertices.Capacity < count)
                PreviewVertices.Capacity = count;

            if (PreviewBarycentric.Capacity < count)
                PreviewBarycentric.Capacity = count;

            if (PreviewTriangles.Capacity < count)
                PreviewTriangles.Capacity = count;

            PreviewVertices.Clear();
            PreviewBarycentric.Clear();
            PreviewTriangles.Clear();
            int corner = 0;

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];

                if (!IsValidFace(face, vertexCount))
                    continue;

                PreviewFaceCorners[i] = corner;
                PreviewVertices.Add(data.vertices[face.a]);
                PreviewVertices.Add(data.vertices[face.b]);
                PreviewVertices.Add(data.vertices[face.c]);
                PreviewBarycentric.Add(BaryA);
                PreviewBarycentric.Add(BaryB);
                PreviewBarycentric.Add(BaryC);
                PreviewTriangles.Add(corner);
                PreviewTriangles.Add(corner + 1);
                PreviewTriangles.Add(corner + 2);
                corner += 3;
            }

            mesh.SetVertices(PreviewVertices);
            mesh.SetUVs(0, PreviewBarycentric);
            mesh.SetTriangles(PreviewTriangles, 0);
        }
    }
}
