using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Precise numeric transform for the current Edit Mesh sub-object selection
    /// (centroid position, rotation around pivot, world AABB size).
    /// </summary>
    public static class IndieEditMeshTransformTypeIn
    {
        private const float SizeEpsilon = 1e-6f;

        public static bool HasSelection(IndieEditMeshComponent ep)
        {
            return GetAffected(ep).Length > 0;
        }

        public static int GetSelectionSignature(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return 0;

            var data = ep.Data;
            unchecked
            {
                int hash = (int)ep.subMode * 397;

                switch (ep.subMode)
                {
                    case IndieEditMeshSubMode.Vertex:
                        hash = MixIndices(hash, data.selectedVertices);
                        break;

                    case IndieEditMeshSubMode.Edge:
                        hash = MixIndices(hash, data.selectedEdges);
                        break;

                    case IndieEditMeshSubMode.Polygon:
                        hash = MixIndices(hash, data.selectedFaces);
                        break;
                }

                return hash;
            }
        }

        public static int[] GetAffected(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return new int[0];

            return IndieEditMeshTransformTool.DraggedVertices ??
                   IndieEditMeshSelection.GetAffectedVertices(ep.Data, ep.subMode);
        }

        public static Vector3 GetLocalPivot(IndieEditMeshComponent ep)
        {
            int[] affected = GetAffected(ep);

            if (affected.Length == 0)
                return Vector3.zero;

            Vector3 pivot = Vector3.zero;

            for (int i = 0; i < affected.Length; i++)
                pivot += ep.Data.vertices[affected[i]];

            return pivot / affected.Length;
        }

        public static Vector3 GetWorldPivot(IndieEditMeshComponent ep)
        {
            if (ep == null)
                return Vector3.zero;

            return ep.transform.TransformPoint(GetLocalPivot(ep));
        }

        public static Vector3 GetWorldSize(IndieEditMeshComponent ep)
        {
            int[] affected = GetAffected(ep);

            if (ep == null || affected.Length == 0)
                return Vector3.zero;

            if (affected.Length == 1)
                return Vector3.zero;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var transform = ep.transform;
            var data = ep.Data;

            for (int i = 0; i < affected.Length; i++)
            {
                Vector3 world = transform.TransformPoint(data.vertices[affected[i]]);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            return max - min;
        }

        public static void CaptureLocalPositions(
            IndieEditMeshComponent ep,
            out int[] indices,
            out Vector3[] localPositions,
            out Vector3 localPivot
        )
        {
            indices = GetAffected(ep);
            localPositions = new Vector3[indices.Length];
            localPivot = Vector3.zero;

            if (indices.Length == 0)
                return;

            var data = ep.Data;

            for (int i = 0; i < indices.Length; i++)
            {
                localPositions[i] = data.vertices[indices[i]];
                localPivot += localPositions[i];
            }

            localPivot /= indices.Length;
        }

        public static void ApplyWorldPosition(IndieEditMeshComponent ep, Vector3 newWorldPivot)
        {
            int[] affected = GetAffected(ep);

            if (affected.Length == 0)
                return;

            Vector3 currentWorld = GetWorldPivot(ep);
            Vector3 deltaWorld = newWorldPivot - currentWorld;

            if (deltaWorld.sqrMagnitude < 1e-16f)
                return;

            Vector3 deltaLocal = ep.transform.InverseTransformVector(deltaWorld);
            ApplyLocalDelta(ep, affected, deltaLocal);
        }

        public static void ApplyLocalPosition(IndieEditMeshComponent ep, Vector3 newLocalPivot)
        {
            int[] affected = GetAffected(ep);

            if (affected.Length == 0)
                return;

            Vector3 deltaLocal = newLocalPivot - GetLocalPivot(ep);

            if (deltaLocal.sqrMagnitude < 1e-16f)
                return;

            ApplyLocalDelta(ep, affected, deltaLocal);
        }

        private static void ApplyLocalDelta(
            IndieEditMeshComponent ep,
            int[] affected,
            Vector3 deltaLocal
        )
        {
            var data = ep.Data;

            IndieEditMeshUndo.Register(ep, "Edit Mesh Type-In Move");
            IndieEditMeshTransformTool.ResetDrag();

            for (int i = 0; i < affected.Length; i++)
            {
                int index = affected[i];
                data.vertices[index] = data.vertices[index] + deltaLocal;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        public static void ApplyWorldRotationFromBaseline(
            IndieEditMeshComponent ep,
            int[] indices,
            Vector3[] baselineLocal,
            Vector3 baselineLocalPivot,
            Quaternion baselineWorldRotation,
            Vector3 targetWorldEulerDegrees
        )
        {
            if (ep == null || indices == null || baselineLocal == null ||
                indices.Length == 0 || indices.Length != baselineLocal.Length)
            {
                return;
            }

            Quaternion targetWorldRotation = Quaternion.Euler(targetWorldEulerDegrees);
            Quaternion deltaWorld =
                targetWorldRotation * Quaternion.Inverse(baselineWorldRotation);

            if (Mathf.Abs(Quaternion.Dot(deltaWorld, Quaternion.identity)) > 0.999999f)
                return;

            Quaternion localRotation =
                Quaternion.Inverse(ep.transform.rotation) *
                deltaWorld *
                ep.transform.rotation;

            var data = ep.Data;

            IndieEditMeshUndo.Register(ep, "Edit Mesh Type-In Rotate");
            IndieEditMeshTransformTool.ResetDrag();

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.vertices.Count)
                    continue;

                Vector3 offset = baselineLocal[i] - baselineLocalPivot;
                data.vertices[index] = baselineLocalPivot + localRotation * offset;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        public static Vector3 SanitizeEuler(Vector3 euler)
        {
            return new Vector3(
                SanitizeEulerAxis(euler.x),
                SanitizeEulerAxis(euler.y),
                SanitizeEulerAxis(euler.z)
            );
        }

        private static float SanitizeEulerAxis(float degrees)
        {
            degrees %= 360f;

            if (degrees > 180f)
                degrees -= 360f;
            else if (degrees < -180f)
                degrees += 360f;

            return degrees;
        }

        public static void ApplyWorldSize(IndieEditMeshComponent ep, Vector3 newWorldSize)
        {
            int[] affected = GetAffected(ep);

            if (affected.Length < 2)
                return;

            Vector3 oldSize = GetWorldSize(ep);
            Vector3 scale = Vector3.one;

            scale.x = oldSize.x > SizeEpsilon ? newWorldSize.x / oldSize.x : 1f;
            scale.y = oldSize.y > SizeEpsilon ? newWorldSize.y / oldSize.y : 1f;
            scale.z = oldSize.z > SizeEpsilon ? newWorldSize.z / oldSize.z : 1f;

            if ((scale - Vector3.one).sqrMagnitude < 1e-16f)
                return;

            Vector3 worldPivot = GetWorldPivot(ep);
            var transform = ep.transform;
            var data = ep.Data;

            IndieEditMeshUndo.Register(ep, "Edit Mesh Type-In Scale");
            IndieEditMeshTransformTool.ResetDrag();

            for (int i = 0; i < affected.Length; i++)
            {
                int index = affected[i];
                Vector3 world = transform.TransformPoint(data.vertices[index]);
                Vector3 offset = world - worldPivot;
                offset = Vector3.Scale(offset, scale);
                data.vertices[index] = transform.InverseTransformPoint(worldPivot + offset);
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        private static int MixIndices(int hash, System.Collections.Generic.List<int> indices)
        {
            hash = (hash * 31) ^ indices.Count;

            for (int i = 0; i < indices.Count; i++)
                hash = (hash * 31) ^ indices[i];

            return hash;
        }
    }
}
