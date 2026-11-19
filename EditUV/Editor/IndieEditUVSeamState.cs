using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public enum IndieEditUVSeamMode
    {
        None,
        EditSeams,
        PointToPoint
    }

    /// <summary>
    /// Session seam layers: baked (UV discontinuities) and working (pelt seams).
    /// Keys are <see cref="IndieEditMeshMeshUtil.EdgeKey"/> on mesh vertex indices.
    /// </summary>
    public static class IndieEditUVSeamState
    {
        private static readonly HashSet<long> Baked = new HashSet<long>();
        private static readonly HashSet<long> Working = new HashSet<long>();

        public static IndieEditUVSeamMode Mode { get; private set; }

        public static bool HasActiveSeamTool => Mode != IndieEditUVSeamMode.None;

        public static IReadOnlyCollection<long> BakedSeams => Baked;
        public static IReadOnlyCollection<long> WorkingSeams => Working;

        /// <summary>Incremented on every baked/working seam mutation.</summary>
        public static int Version { get; private set; }

        public static void ClearSession()
        {
            Mode = IndieEditUVSeamMode.None;
            Baked.Clear();
            Working.Clear();
            Version++;
            IndieEditUVSeamTools.ResetToolTransient();
        }

        public static void SetMode(IndieEditUVSeamMode mode)
        {
            if (Mode == mode)
            {
                Mode = IndieEditUVSeamMode.None;
            }
            else
            {
                Mode = mode;
            }

            IndieEditUVSeamTools.ResetToolTransient();
            IndieEditUVNotify.SeamsChanged();
        }

        public static void RebuildBaked(IndieEditableMeshData data)
        {
            Baked.Clear();
            Version++;

            if (data == null)
                return;

            HashSet<long> baked = IndieEditUVSeamUtil.CollectBakedSeamEdgeKeys(data);

            foreach (long key in baked)
                Baked.Add(key);
        }

        public static bool IsBaked(long edgeKey) => Baked.Contains(edgeKey);

        public static bool IsWorking(long edgeKey) => Working.Contains(edgeKey);

        public static bool IsBoundary(long edgeKey) =>
            Baked.Contains(edgeKey) || Working.Contains(edgeKey);

        public static void AddWorking(long edgeKey)
        {
            if (Working.Add(edgeKey))
                Version++;
        }

        public static void RemoveWorking(long edgeKey)
        {
            if (Working.Remove(edgeKey))
                Version++;
        }

        public static void ToggleWorking(long edgeKey)
        {
            if (!Working.Add(edgeKey))
                Working.Remove(edgeKey);

            Version++;
        }

        public static void ClearWorking()
        {
            Working.Clear();
            Version++;
            IndieEditUVNotify.SeamsChanged();
        }

        public static void AddWorkingMany(IEnumerable<long> keys)
        {
            if (keys == null)
                return;

            foreach (long key in keys)
                Working.Add(key);

            Version++;
            IndieEditUVNotify.SeamsChanged();
        }

        public static long EdgeKeyFromData(IndieEditableMeshData data, int edgeIndex)
        {
            if (data == null || edgeIndex < 0 || edgeIndex >= data.edges.Count)
                return 0;

            var edge = data.edges[edgeIndex];
            return IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);
        }

        /// <summary>
        /// O(1): endpoints are packed in the key (min &lt;&lt; 32 | max).
        /// </summary>
        public static bool TryGetEdgeEndpoints(
            IndieEditableMeshData data,
            long edgeKey,
            out int v0,
            out int v1
        )
        {
            v0 = (int)(edgeKey >> 32);
            v1 = (int)(uint)edgeKey;

            if (data == null ||
                v0 < 0 || v0 >= data.vertices.Count ||
                v1 < 0 || v1 >= data.vertices.Count)
            {
                v0 = -1;
                v1 = -1;
                return false;
            }

            return true;
        }
    }
}
