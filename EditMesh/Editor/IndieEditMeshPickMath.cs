using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Scene-view pick math shared by Edit Mesh and Edit UV.
    /// </summary>
    public static class IndieEditMeshPickMath
    {
        private const float ParallelEpsilon = 1e-7f;
        private const float MinHitDistance = 1e-6f;

        public static bool IsFrontFacingTriangle(
            Camera camera,
            Vector3 worldA,
            Vector3 worldB,
            Vector3 worldC
        )
        {
            if (camera == null)
                return true;

            Vector3 normal = Vector3.Cross(worldB - worldA, worldC - worldA);

            if (normal.sqrMagnitude < 1e-20f)
                return false;

            Vector3 center = (worldA + worldB + worldC) * (1f / 3f);
            Vector3 view = camera.orthographic
                ? -camera.transform.forward
                : camera.transform.position - center;

            return Vector3.Dot(normal, view) > 0f;
        }

        /// <summary>
        /// Möller–Trumbore. The parallel test is relative to triangle size:
        /// an absolute epsilon rejects every small triangle (|det| scales with area).
        /// </summary>
        public static bool RayTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float distance,
            float barycentricSlack = 0f
        )
        {
            distance = 0f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;

            Vector3 h = Vector3.Cross(direction, edge2);
            float a = Vector3.Dot(edge1, h);
            float parallelEpsilon = ParallelEpsilon * edge1.magnitude * edge2.magnitude;

            if (Mathf.Abs(a) <= parallelEpsilon)
                return false;

            float f = 1f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < -barycentricSlack || u > 1f + barycentricSlack)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);

            if (v < -barycentricSlack || u + v > 1f + barycentricSlack)
                return false;

            float t = f * Vector3.Dot(edge2, q);

            if (t <= MinHitDistance)
                return false;

            distance = t;
            return true;
        }

        public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
