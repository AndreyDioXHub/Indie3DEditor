using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Vertex streams for Hidden/Indie3DEditor/EditUVSceneOverlay
    /// (fill triangles, screen-space thick lines, screen-space points).
    /// </summary>
    public sealed class IndieEditUVOverlayBuffer
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector4> Other = new List<Vector4>();
        public readonly List<Vector4> Param = new List<Vector4>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int> Indices = new List<int>();

        public bool IsEmpty => Indices.Count == 0;

        public void Clear()
        {
            Positions.Clear();
            Other.Clear();
            Param.Clear();
            Colors.Clear();
            Indices.Clear();
        }
    }

    public static class IndieEditUVOverlayGeometry
    {
        public static void AppendTriangle(IndieEditUVOverlayBuffer buffer, Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int start = buffer.Positions.Count;
            AddVertex(buffer, a, Vector4.zero, Vector4.zero, color);
            AddVertex(buffer, b, Vector4.zero, Vector4.zero, color);
            AddVertex(buffer, c, Vector4.zero, Vector4.zero, color);
            buffer.Indices.Add(start);
            buffer.Indices.Add(start + 1);
            buffer.Indices.Add(start + 2);
        }

        public static void AppendSegment(
            IndieEditUVOverlayBuffer buffer,
            Vector3 a,
            Vector3 b,
            float thicknessPx,
            Color color
        )
        {
            int start = buffer.Positions.Count;
            Vector4 other = new Vector4(b.x, b.y, b.z, 0f);
            AddVertex(buffer, a, other, new Vector4(0f, -1f, thicknessPx, 0f), color);
            AddVertex(buffer, a, other, new Vector4(0f, 1f, thicknessPx, 0f), color);
            AddVertex(buffer, a, other, new Vector4(1f, 1f, thicknessPx, 0f), color);
            AddVertex(buffer, a, other, new Vector4(1f, -1f, thicknessPx, 0f), color);
            AddQuad(buffer, start);
        }

        public static void AppendPoint(IndieEditUVOverlayBuffer buffer, Vector3 center, float sizePx, Color color)
        {
            int start = buffer.Positions.Count;
            AddVertex(buffer, center, Vector4.zero, new Vector4(-1f, -1f, sizePx, 0f), color);
            AddVertex(buffer, center, Vector4.zero, new Vector4(1f, -1f, sizePx, 0f), color);
            AddVertex(buffer, center, Vector4.zero, new Vector4(1f, 1f, sizePx, 0f), color);
            AddVertex(buffer, center, Vector4.zero, new Vector4(-1f, 1f, sizePx, 0f), color);
            AddQuad(buffer, start);
        }

        public static Mesh Apply(IndieEditUVOverlayBuffer buffer, Mesh mesh, string name)
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave
                };
                mesh.MarkDynamic();
            }

            mesh.Clear();

            if (buffer.IsEmpty)
                return mesh;

            mesh.indexFormat = buffer.Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(buffer.Positions);
            mesh.SetColors(buffer.Colors);
            mesh.SetUVs(0, buffer.Other);
            mesh.SetUVs(1, buffer.Param);
            mesh.SetTriangles(buffer.Indices, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        public static void Destroy(ref Mesh mesh)
        {
            if (mesh != null)
                Object.DestroyImmediate(mesh);

            mesh = null;
        }

        private static void AddVertex(IndieEditUVOverlayBuffer buffer, Vector3 position, Vector4 other, Vector4 param, Color color)
        {
            buffer.Positions.Add(position);
            buffer.Other.Add(other);
            buffer.Param.Add(param);
            buffer.Colors.Add(color);
        }

        private static void AddQuad(IndieEditUVOverlayBuffer buffer, int start)
        {
            buffer.Indices.Add(start);
            buffer.Indices.Add(start + 1);
            buffer.Indices.Add(start + 2);
            buffer.Indices.Add(start);
            buffer.Indices.Add(start + 2);
            buffer.Indices.Add(start + 3);
        }
    }
}
