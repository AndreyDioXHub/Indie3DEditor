using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVTransform
    {
        public static int[] GetAffectedVertices(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return new int[0];

            // Do not expand coincident 3D verts — they usually have different UVs.
            return IndieEditMeshSelection.GetAffectedVertices(uv.Data, uv.subMode, false);
        }

        public static bool TryGetSelectionUvBounds(IndieEditUVComponent uv, out Rect bounds)
        {
            bounds = new Rect();
            int[] indices = GetAffectedVertices(uv);

            if (indices == null || indices.Length == 0)
                return false;

            var data = uv.Data;
            float minU = float.PositiveInfinity;
            float minV = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float maxV = float.NegativeInfinity;
            int count = 0;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                Vector2 uvCoord = data.uvs[index];
                minU = Mathf.Min(minU, uvCoord.x);
                minV = Mathf.Min(minV, uvCoord.y);
                maxU = Mathf.Max(maxU, uvCoord.x);
                maxV = Mathf.Max(maxV, uvCoord.y);
                count++;
            }

            if (count == 0)
                return false;

            if (maxU - minU < 1e-5f)
            {
                minU -= 0.01f;
                maxU += 0.01f;
            }

            if (maxV - minV < 1e-5f)
            {
                minV -= 0.01f;
                maxV += 0.01f;
            }

            bounds = Rect.MinMaxRect(minU, minV, maxU, maxV);
            return true;
        }

        public static Vector2 GetSelectionUvPivot(IndieEditUVComponent uv)
        {
            if (TryGetSelectionUvBounds(uv, out Rect bounds))
                return bounds.center;

            return Vector2.zero;
        }

        public static bool TryGetSelectionUvAverage(IndieEditUVComponent uv, out Vector2 average)
        {
            average = Vector2.zero;
            int[] indices = GetAffectedVertices(uv);

            if (indices == null || indices.Length == 0 || uv?.Data == null)
                return false;

            var data = uv.Data;
            Vector2 sum = Vector2.zero;
            int count = 0;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                sum += data.uvs[index];
                count++;
            }

            if (count == 0)
                return false;

            average = sum / count;
            return true;
        }

        public static int GetSelectionSignature(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return 0;

            unchecked
            {
                int hash = (int)uv.subMode + 17;
                var data = uv.Data;

                hash = MixIndices(hash, data.selectedVertices);
                hash = MixIndices(hash, data.selectedEdges);
                hash = MixIndices(hash, data.selectedFaces);
                return hash;
            }
        }

        private static int MixIndices(int hash, List<int> indices)
        {
            if (indices == null)
                return hash;

            hash = hash * 31 + indices.Count;

            for (int i = 0; i < indices.Count; i++)
                hash = hash * 31 + indices[i];

            return hash;
        }

        public static Dictionary<int, Vector2> CaptureUvs(IndieEditUVComponent uv, int[] indices)
        {
            var map = new Dictionary<int, Vector2>(indices.Length);
            var data = uv.Data;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= data.uvs.Count)
                    continue;

                map[index] = data.uvs[index];
            }

            return map;
        }

        public static void ApplyMove(
            IndieEditUVComponent uv,
            Dictionary<int, Vector2> baseline,
            Vector2 delta
        )
        {
            if (uv == null || uv.Data == null || baseline == null)
                return;

            foreach (var pair in baseline)
                uv.Data.uvs[pair.Key] = pair.Value + delta;

            AfterUvChange(uv);
        }

        public static void ApplyRotate(
            IndieEditUVComponent uv,
            Dictionary<int, Vector2> baseline,
            Vector2 pivot,
            float angleRadians
        )
        {
            if (uv == null || uv.Data == null || baseline == null)
                return;

            float cos = Mathf.Cos(angleRadians);
            float sin = Mathf.Sin(angleRadians);

            foreach (var pair in baseline)
            {
                Vector2 local = pair.Value - pivot;
                Vector2 rotated = new Vector2(
                    local.x * cos - local.y * sin,
                    local.x * sin + local.y * cos
                );
                uv.Data.uvs[pair.Key] = pivot + rotated;
            }

            AfterUvChange(uv);
        }

        public static void ApplyScale(
            IndieEditUVComponent uv,
            Dictionary<int, Vector2> baseline,
            Vector2 pivot,
            Vector2 scale
        )
        {
            if (uv == null || uv.Data == null || baseline == null)
                return;

            foreach (var pair in baseline)
            {
                Vector2 local = pair.Value - pivot;
                uv.Data.uvs[pair.Key] = pivot + new Vector2(local.x * scale.x, local.y * scale.y);
            }

            AfterUvChange(uv);
        }

        private static void AfterUvChange(IndieEditUVComponent uv)
        {
            IndieEditUVSession.RefreshUvsOnly(uv);

            // Serialized mesh data: dirtying per drag event re-serializes it in the Inspector.
            if (!IndieEditUVGizmo.IsDragging)
                EditorUtility.SetDirty(uv);

            IndieEditUVNotify.UvDragChanged(uv);
        }
    }
}
