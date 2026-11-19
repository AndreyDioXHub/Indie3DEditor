using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Planar-ish n-gon triangulation shared by Bevel and Connect.
    /// </summary>
    public static class IndieEditMeshPolygonTriangulator
    {
        private const float Eps = 1e-5f;
        private const float EpsSqr = Eps * Eps;

        /// <summary>|ab x ac| / longest edge^2 below this counts as three points on one line.</summary>
        private const float CollinearSin = 1e-5f;

        public static Vector3 PolygonNormal(IndieEditableMeshData data, List<int> polygon)
        {
            Vector3 sum = Vector3.zero;

            for (int i = 1; i < polygon.Count - 1; i++)
            {
                Vector3 a = data.vertices[polygon[0]];
                Vector3 b = data.vertices[polygon[i]];
                Vector3 c = data.vertices[polygon[i + 1]];
                sum += Vector3.Cross(b - a, c - a);
            }

            return sum;
        }

        /// <summary>
        /// Ear clipping in the polygon winding; faceN must agree with that winding.
        /// Falls back to a fan from the most central vertex when no ear can be clipped.
        /// </summary>
        public static void TriangulateEar(
            IndieEditableMeshData data,
            List<IndieEditMeshFace> newFaces,
            List<int> poly,
            Vector3 faceN,
            int materialId,
            int sg
        )
        {
            if (poly.Count < 3)
                return;

            if (poly.Count == 3)
            {
                AddOriented(data, newFaces, poly[0], poly[1], poly[2], faceN, materialId, sg);
                return;
            }

            var work = new List<int>(poly);
            int guard = 0;

            while (work.Count > 3 && guard++ < 256)
            {
                bool clipped = false;

                for (int i = 0; i < work.Count; i++)
                {
                    int i0 = work[(i + work.Count - 1) % work.Count];
                    int i1 = work[i];
                    int i2 = work[(i + 1) % work.Count];

                    Vector3 a = data.vertices[i0];
                    Vector3 b = data.vertices[i1];
                    Vector3 c = data.vertices[i2];
                    Vector3 cross = Vector3.Cross(b - a, c - a);

                    // A point on a straight run must stay a corner of some triangle, or it leaves a T-junction.
                    if (Vector3.Dot(cross, faceN) <= 0f || IsCollinear(a, b, c))
                        continue;

                    if (EarContainsPoint(data, work, i0, i1, i2, a, b, c, faceN))
                        continue;

                    if (work.Count == 4 && IsCollinear(a, c, data.vertices[work[(i + 2) % 4]]))
                        continue;

                    AddOriented(data, newFaces, i0, i1, i2, faceN, materialId, sg);
                    work.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    break;
            }

            if (work.Count == 3)
            {
                AddOriented(data, newFaces, work[0], work[1], work[2], faceN, materialId, sg);
            }
            else if (work.Count > 3)
            {
                int apex = work[0];
                float best = float.MaxValue;

                for (int i = 0; i < work.Count; i++)
                {
                    float span = 0f;

                    for (int j = 0; j < work.Count; j++)
                    {
                        if (i == j)
                            continue;

                        span += (data.vertices[work[i]] - data.vertices[work[j]]).sqrMagnitude;
                    }

                    if (span < best)
                    {
                        best = span;
                        apex = work[i];
                    }
                }

                int apexIdx = work.IndexOf(apex);

                for (int i = 1; i < work.Count - 1; i++)
                {
                    int b = work[(apexIdx + i) % work.Count];
                    int c = work[(apexIdx + i + 1) % work.Count];
                    AddOriented(data, newFaces, apex, b, c, faceN, materialId, sg);
                }
            }
        }

        public static void AddOriented(
            IndieEditableMeshData data,
            List<IndieEditMeshFace> newFaces,
            int a,
            int b,
            int c,
            Vector3 outward,
            int materialId,
            int sg
        )
        {
            if (a == b || b == c || c == a)
                return;

            if (IsCollinear(data.vertices[a], data.vertices[b], data.vertices[c]))
                return;

            Vector3 n = Vector3.Cross(
                data.vertices[b] - data.vertices[a],
                data.vertices[c] - data.vertices[a]
            );

            if (Vector3.Dot(n, outward) < 0f)
            {
                int tmp = b;
                b = c;
                c = tmp;
            }

            IndieEditMeshMeshUtil.AddTriangle(newFaces, a, b, c, materialId, sg);
        }

        /// <summary>
        /// Relative to the longest edge, so a thin sliver of a small model still counts as a
        /// triangle (dropping it leaves a hole).
        /// </summary>
        private static bool IsCollinear(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            float longestSqr = Mathf.Max(ab.sqrMagnitude, Mathf.Max(ac.sqrMagnitude, (c - b).sqrMagnitude));

            return Vector3.Cross(ab, ac).sqrMagnitude <= CollinearSin * CollinearSin * longestSqr * longestSqr;
        }

        private static bool EarContainsPoint(
            IndieEditableMeshData data,
            List<int> work,
            int i0,
            int i1,
            int i2,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 faceN
        )
        {
            for (int k = 0; k < work.Count; k++)
            {
                int v = work[k];

                if (v == i0 || v == i1 || v == i2)
                    continue;

                if (PointInTriangle(data.vertices[v], a, b, c, faceN))
                    return true;
            }

            return false;
        }

        private static bool PointInTriangle(
            Vector3 p,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 faceN
        )
        {
            Vector3 n = faceN.sqrMagnitude > EpsSqr ? faceN.normalized : Vector3.up;
            float s1 = Vector3.Dot(n, Vector3.Cross(b - a, p - a));
            float s2 = Vector3.Dot(n, Vector3.Cross(c - b, p - b));
            float s3 = Vector3.Dot(n, Vector3.Cross(a - c, p - c));
            return (s1 >= -Eps && s2 >= -Eps && s3 >= -Eps) ||
                   (s1 <= Eps && s2 <= Eps && s3 <= Eps);
        }
    }
}
