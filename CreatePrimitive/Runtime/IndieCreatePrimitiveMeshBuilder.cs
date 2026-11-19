using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.CreatePrimitive
{
    public static class IndieCreatePrimitiveMeshBuilder
    {
        public const float MinSize = 0.001f;
        public const int MinSegments = 1;
        public const int MinRadial = 3;
        public const int MinRings = 2;
        public const int MaxResolution = 128;

        public static void Clamp(IndieCreatePrimitiveComponent component)
        {
            if (component == null)
                return;

            component.width = Size(component.width);
            component.height = Size(component.height);
            component.depth = Size(component.depth);
            component.widthSegments = Segments(component.widthSegments);
            component.heightSegments = Segments(component.heightSegments);
            component.depthSegments = Segments(component.depthSegments);
            component.radius = Size(component.radius);
            component.cylinderRadius = Size(component.cylinderRadius);
            component.cylinderHeight = Size(component.cylinderHeight);
            component.sphereSegments = Radial(component.sphereSegments);
            component.sphereRings = Rings(component.sphereRings);
            component.cylinderSides = Radial(component.cylinderSides);
        }

        public static int CountCubeTriangles(int widthSegments, int heightSegments, int depthSegments)
        {
            int sx = Segments(widthSegments);
            int sy = Segments(heightSegments);
            int sz = Segments(depthSegments);
            return 4 * (sx * sy + sx * sz + sy * sz);
        }

        public static int CountSphereTriangles(int segments, int rings)
        {
            segments = Radial(segments);
            rings = Rings(rings);
            return segments * 2 * (rings - 1);
        }

        public static int CountCylinderTriangles(int sides)
        {
            return Radial(sides) * 4;
        }

        public static Mesh Build(IndieCreatePrimitiveComponent component)
        {
            if (component == null)
                return new Mesh();

            switch (component.type)
            {
                case IndieCreatePrimitiveType.Sphere:
                    return BuildSphere(component.radius, component.sphereSegments, component.sphereRings);

                case IndieCreatePrimitiveType.Cylinder:
                    return BuildCylinder(
                        component.cylinderRadius,
                        component.cylinderHeight,
                        component.cylinderSides
                    );

                default:
                    return BuildCube(
                        component.width,
                        component.height,
                        component.depth,
                        component.widthSegments,
                        component.heightSegments,
                        component.depthSegments
                    );
            }
        }

        // Cube UV cross (4x3):
        //       +Y
        //  -X   +Z   +X   -Z
        //       -Y
        public static Mesh BuildCube(
            float width,
            float height,
            float depth,
            int widthSegments = 1,
            int heightSegments = 1,
            int depthSegments = 1
        )
        {
            width = Size(width);
            height = Size(height);
            depth = Size(depth);
            widthSegments = Segments(widthSegments);
            heightSegments = Segments(heightSegments);
            depthSegments = Segments(depthSegments);

            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float hz = depth * 0.5f;

            var draft = new MeshDraft();

            // +X (right) — cell (2,1), U: +Z → -Z
            draft.AddSubdividedFace(
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz),
                new Vector3(hx, hy, hz),
                new Vector3(hx, -hy, hz),
                Vector3.right,
                depthSegments,
                heightSegments,
                CubeCellUv(2, 1, 1f, 0f),
                CubeCellUv(2, 1, 1f, 1f),
                CubeCellUv(2, 1, 0f, 1f),
                CubeCellUv(2, 1, 0f, 0f)
            );

            // -X (left) — cell (0,1), U: -Z → +Z
            draft.AddSubdividedFace(
                new Vector3(-hx, -hy, hz),
                new Vector3(-hx, hy, hz),
                new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, -hz),
                Vector3.left,
                depthSegments,
                heightSegments,
                CubeCellUv(0, 1, 1f, 0f),
                CubeCellUv(0, 1, 1f, 1f),
                CubeCellUv(0, 1, 0f, 1f),
                CubeCellUv(0, 1, 0f, 0f)
            );

            // +Y (top) — cell (1,2), bottom edge matches +Z top
            draft.AddSubdividedFace(
                new Vector3(-hx, hy, -hz),
                new Vector3(-hx, hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(hx, hy, -hz),
                Vector3.up,
                widthSegments,
                depthSegments,
                CubeCellUv(1, 2, 0f, 1f),
                CubeCellUv(1, 2, 0f, 0f),
                CubeCellUv(1, 2, 1f, 0f),
                CubeCellUv(1, 2, 1f, 1f)
            );

            // -Y (bottom) — cell (1,0), top edge matches +Z bottom
            draft.AddSubdividedFace(
                new Vector3(-hx, -hy, hz),
                new Vector3(-hx, -hy, -hz),
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, -hy, hz),
                Vector3.down,
                widthSegments,
                depthSegments,
                CubeCellUv(1, 0, 0f, 1f),
                CubeCellUv(1, 0, 0f, 0f),
                CubeCellUv(1, 0, 1f, 0f),
                CubeCellUv(1, 0, 1f, 1f)
            );

            // +Z (front) — cell (1,1), U: -X → +X
            draft.AddSubdividedFace(
                new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(-hx, hy, hz),
                new Vector3(-hx, -hy, hz),
                Vector3.forward,
                widthSegments,
                heightSegments,
                CubeCellUv(1, 1, 1f, 0f),
                CubeCellUv(1, 1, 1f, 1f),
                CubeCellUv(1, 1, 0f, 1f),
                CubeCellUv(1, 1, 0f, 0f)
            );

            // -Z (back) — cell (3,1), U: +X → -X
            draft.AddSubdividedFace(
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, hy, -hz),
                new Vector3(hx, hy, -hz),
                new Vector3(hx, -hy, -hz),
                Vector3.back,
                widthSegments,
                heightSegments,
                CubeCellUv(3, 1, 1f, 0f),
                CubeCellUv(3, 1, 1f, 1f),
                CubeCellUv(3, 1, 0f, 1f),
                CubeCellUv(3, 1, 0f, 0f)
            );

            return draft.ToMesh("Cube");
        }

        public static Mesh BuildSphere(float radius, int segments, int rings)
        {
            radius = Size(radius);
            segments = Radial(segments);
            rings = Rings(rings);

            var draft = new MeshDraft();
            int stride = segments + 1;

            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings;
                float polar = v * Mathf.PI;
                float py = Mathf.Cos(polar);
                float pr = Mathf.Sin(polar);

                for (int x = 0; x <= segments; x++)
                {
                    int column = x == segments ? 0 : x;
                    Circle(column, segments, out float cx, out float cz);

                    var normal = new Vector3(cx * pr, py, cz * pr);
                    draft.AddVertex(normal * radius, normal, new Vector2((float)x / segments, 1f - v));
                }
            }

            for (int y = 0; y < rings; y++)
            {
                bool top = y == 0;
                bool bottom = y == rings - 1;

                for (int x = 0; x < segments; x++)
                {
                    int i0 = y * stride + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;

                    if (top)
                    {
                        draft.AddTriangle(i0, i2, i3);
                    }
                    else if (bottom)
                    {
                        draft.AddTriangle(i0, i2, i1);
                    }
                    else
                    {
                        draft.AddTriangle(i0, i2, i3);
                        draft.AddTriangle(i0, i3, i1);
                    }
                }
            }

            return draft.ToMesh("Sphere");
        }

        // Cylinder UV:
        //   (top half)  left circle = top cap, right circle = bottom cap
        //   (bottom half) side unwrap rectangle
        public static Mesh BuildCylinder(float radius, float height, int sides)
        {
            radius = Size(radius);
            height = Size(height);
            sides = Radial(sides);

            float hy = height * 0.5f;
            var draft = new MeshDraft();

            int sideTop = draft.Count;

            for (int i = 0; i <= sides; i++)
                AddSideVertex(draft, i, sides, radius, hy, 0.5f);

            int sideBottom = draft.Count;

            for (int i = 0; i <= sides; i++)
                AddSideVertex(draft, i, sides, radius, -hy, 0f);

            for (int i = 0; i < sides; i++)
            {
                int t0 = sideTop + i;
                int t1 = t0 + 1;
                int b0 = sideBottom + i;
                int b1 = b0 + 1;
                draft.AddQuad(t0, b0, b1, t1);
            }

            AddCap(draft, sides, radius, hy, Vector3.up, true, new Vector2(0.25f, 0.75f), 0.22f);
            AddCap(draft, sides, radius, -hy, Vector3.down, false, new Vector2(0.75f, 0.75f), 0.22f);

            return draft.ToMesh("Cylinder");
        }

        private static void AddSideVertex(
            MeshDraft draft,
            int index,
            int sides,
            float radius,
            float y,
            float v
        )
        {
            int column = index == sides ? 0 : index;
            Circle(column, sides, out float x, out float z);

            var normal = new Vector3(x, 0f, z);

            draft.AddVertex(
                new Vector3(x * radius, y, z * radius),
                normal,
                new Vector2((float)index / sides, v)
            );
        }

        private static void AddCap(
            MeshDraft draft,
            int sides,
            float radius,
            float y,
            Vector3 normal,
            bool top,
            Vector2 uvCenter,
            float uvRadius
        )
        {
            int center = draft.Count;
            draft.AddVertex(new Vector3(0f, y, 0f), normal, uvCenter);

            int ring = draft.Count;

            for (int i = 0; i < sides; i++)
            {
                Circle(i, sides, out float x, out float z);

                float uvX = top ? x : -x;
                float uvY = z;

                draft.AddVertex(
                    new Vector3(x * radius, y, z * radius),
                    normal,
                    uvCenter + new Vector2(uvX, uvY) * uvRadius
                );
            }

            for (int i = 0; i < sides; i++)
            {
                int current = ring + i;
                int next = ring + (i + 1) % sides;

                if (top)
                    draft.AddTriangle(center, current, next);
                else
                    draft.AddTriangle(center, next, current);
            }
        }

        private static Vector2 CubeCellUv(int col, int row, float localU, float localV)
        {
            const float cellW = 0.25f;
            const float cellH = 1f / 3f;
            return new Vector2((col + localU) * cellW, (row + localV) * cellH);
        }

        private static void Circle(int index, int count, out float x, out float z)
        {
            float angle = (float)index / count * Mathf.PI * 2f;
            x = Mathf.Sin(angle);
            z = Mathf.Cos(angle);
        }

        private static float Size(float value)
        {
            return Mathf.Max(MinSize, value);
        }

        private static int Segments(int value)
        {
            return Mathf.Clamp(value, MinSegments, MaxResolution);
        }

        private static int Radial(int value)
        {
            return Mathf.Clamp(value, MinRadial, MaxResolution);
        }

        private static int Rings(int value)
        {
            return Mathf.Clamp(value, MinRings, MaxResolution);
        }

        private sealed class MeshDraft
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public int Count => _vertices.Count;

            public void AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                _vertices.Add(position);
                _normals.Add(normal);
                _uvs.Add(uv);
            }

            public void AddTriangle(int a, int b, int c)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            public void AddQuad(int a, int b, int c, int d)
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }

            public void AddSubdividedFace(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d,
                Vector3 normal,
                int uSegments,
                int vSegments,
                Vector2 uvA,
                Vector2 uvB,
                Vector2 uvC,
                Vector2 uvD
            )
            {
                int start = Count;
                int stride = uSegments + 1;

                for (int v = 0; v <= vSegments; v++)
                {
                    float tv = (float)v / vSegments;
                    Vector3 left = Vector3.Lerp(a, b, tv);
                    Vector3 right = Vector3.Lerp(d, c, tv);
                    Vector2 uvLeft = Vector2.Lerp(uvA, uvB, tv);
                    Vector2 uvRight = Vector2.Lerp(uvD, uvC, tv);

                    for (int u = 0; u <= uSegments; u++)
                    {
                        float tu = (float)u / uSegments;
                        AddVertex(
                            Vector3.Lerp(left, right, tu),
                            normal,
                            Vector2.Lerp(uvLeft, uvRight, tu)
                        );
                    }
                }

                for (int v = 0; v < vSegments; v++)
                {
                    for (int u = 0; u < uSegments; u++)
                    {
                        int bl = start + v * stride + u;
                        int br = bl + 1;
                        int tl = bl + stride;
                        int tr = tl + 1;
                        AddQuad(bl, tl, tr, br);
                    }
                }
            }

            public Mesh ToMesh(string meshName)
            {
                var mesh = new Mesh();
                mesh.name = meshName;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
