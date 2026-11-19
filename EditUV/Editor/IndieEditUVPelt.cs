using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Proxy pelt session: edit a projected skin without touching mesh UV/topology
    /// until <see cref="Commit"/>.
    /// </summary>
    public static class IndieEditUVPelt
    {
        public const int DefaultSimulationSamples = 5;

        private static IndieEditUVPeltProxy _proxy;

        public static bool IsActive { get; private set; }
        public static bool SelectStretchersMode { get; set; }

        public static IndieEditUVPeltProxy Proxy => _proxy;

        public static IReadOnlyList<Vector2> Stretchers =>
            _proxy != null ? _proxy.Stretchers : null;

        public static IReadOnlyList<int> BoundaryVerts =>
            _proxy != null ? _proxy.BoundaryOrdered : null;

        public static IReadOnlyCollection<int> RegionFaces =>
            _proxy != null ? _proxy.RegionFaces : null;

        public static HashSet<int> SelectedStretcherIndices { get; } = new HashSet<int>();

        public static bool TryBegin(IndieEditUVComponent uv, out string error)
        {
            error = null;
            EndSession();

            if (uv == null || uv.Data == null)
            {
                error = "No Edit UV session.";
                return false;
            }

            var data = uv.Data;

            HashSet<IndieEditUVSeamOps.GeoEdgeId> manualSeams =
                IndieEditUVSeamOps.SnapshotWorkingGeoEdges(data);

            var candidateFaces = new HashSet<int>();

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int f = data.selectedFaces[i];

                if (f >= 0 && f < data.faces.Count)
                    candidateFaces.Add(f);
            }

            if (candidateFaces.Count == 0)
            {
                if (manualSeams.Count == 0)
                {
                    error = "Select faces (or place working seams) for Pelt.";
                    return false;
                }

                HashSet<int> seeds = FindSeedFacesTouchingWorkingSeams(data);

                if (seeds.Count == 0)
                {
                    error = "Select faces for the pelt region.";
                    return false;
                }

                candidateFaces = IndieEditUVSeamOps.CollectRegionFaces(
                    data,
                    seeds,
                    stopAtBaked: false
                );
            }

            HashSet<int> regionFaces = IndieEditUVSeamOps.KeepLargestComponentCutBySeams(
                data,
                candidateFaces,
                cuttingSeams: null
            );

            if (regionFaces.Count == 0)
            {
                error = "Empty pelt region after validation.";
                return false;
            }

            // Cuts = user cyan + 3D selection perimeter (geo-stable).
            var cutGeos = new HashSet<IndieEditUVSeamOps.GeoEdgeId>(manualSeams);
            HashSet<IndieEditUVSeamOps.GeoEdgeId> perimeterGeo =
                IndieEditUVSeamOps.CollectFaceSetPerimeterGeo(data, regionFaces);

            foreach (var g in perimeterGeo)
                cutGeos.Add(g);

            if (!IndieEditUVPeltProxy.TryBuild(
                    data,
                    regionFaces,
                    cutGeos,
                    out _proxy,
                    out error
                ))
            {
                _proxy = null;
                return false;
            }

            // Mesh untouched — no undo / no RefreshMesh on open.
            IsActive = true;
            SelectStretchersMode = true;
            SelectAllStretchers();
            return true;
        }

        public static void EndSession()
        {
            IsActive = false;
            SelectStretchersMode = false;
            SelectedStretcherIndices.Clear();
            IndieEditUVStretcherGizmo.Reset();
            _proxy = null;
        }

        public static void ResetToSnapshot(IndieEditUVComponent uv)
        {
            if (!IsActive || _proxy == null)
                return;

            _proxy.ResetPositions();
            IndieEditUVPeltWindow.RepaintIfOpen();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        /// <summary>
        /// Cancel / close: drop proxy only — mesh was never mutated.
        /// </summary>
        public static void RestoreSnapshotAndEnd(IndieEditUVComponent uv)
        {
            EndSession();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        public static void Commit(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null || _proxy == null)
            {
                EndSession();
                IndieEditUVNotify.SelectionOrMeshChanged();
                return;
            }

            IndieEditUVUndo.Register(uv, "Pelt Commit");
            _proxy.CommitToMesh(uv.Data);
            IndieEditUVSeamState.RebuildBaked(uv.Data);
            IndieEditUVSeamState.ClearWorking();
            IndieEditUVSession.RefreshMesh(uv);

            EndSession();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        public static void RunSimulation(IndieEditUVComponent uv, int samples)
        {
            if (!IsActive || _proxy == null)
                return;

            _proxy.RunSimulation(samples);
            IndieEditUVPeltWindow.RepaintIfOpen();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        public static void MoveSelectedStretchers(Vector2 deltaUv)
        {
            if (_proxy == null || SelectedStretcherIndices.Count == 0)
                return;

            var stretchers = _proxy.Stretchers;

            foreach (int index in SelectedStretcherIndices)
            {
                if (index < 0 || index >= stretchers.Count)
                    continue;

                stretchers[index] += deltaUv;
            }

            IndieEditUVPeltWindow.RepaintIfOpen();
        }

        public static void SetStretcher(int index, Vector2 uv)
        {
            if (_proxy == null || index < 0 || index >= _proxy.Stretchers.Count)
                return;

            _proxy.Stretchers[index] = uv;
            IndieEditUVPeltWindow.RepaintIfOpen();
        }

        public static void RotateSelectedStretchers(Vector2 pivot, float degrees)
        {
            if (_proxy == null || SelectedStretcherIndices.Count == 0)
                return;

            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            var stretchers = _proxy.Stretchers;

            foreach (int index in SelectedStretcherIndices)
            {
                if (index < 0 || index >= stretchers.Count)
                    continue;

                Vector2 p = stretchers[index] - pivot;
                stretchers[index] = pivot + new Vector2(
                    p.x * cos - p.y * sin,
                    p.x * sin + p.y * cos
                );
            }

            IndieEditUVPeltWindow.RepaintIfOpen();
        }

        public static void ScaleSelectedStretchers(Vector2 pivot, Vector2 scale)
        {
            if (_proxy == null || SelectedStretcherIndices.Count == 0)
                return;

            var stretchers = _proxy.Stretchers;

            foreach (int index in SelectedStretcherIndices)
            {
                if (index < 0 || index >= stretchers.Count)
                    continue;

                Vector2 p = stretchers[index] - pivot;
                stretchers[index] = pivot + new Vector2(p.x * scale.x, p.y * scale.y);
            }

            IndieEditUVPeltWindow.RepaintIfOpen();
        }

        public static void SelectAllStretchers()
        {
            SelectedStretcherIndices.Clear();

            if (_proxy == null)
                return;

            for (int i = 0; i < _proxy.Stretchers.Count; i++)
                SelectedStretcherIndices.Add(i);
        }

        public static bool TryGetSelectedStretcherBounds(out Rect bounds)
        {
            bounds = default;

            if (_proxy == null ||
                _proxy.Stretchers.Count == 0 ||
                SelectedStretcherIndices.Count == 0)
            {
                return false;
            }

            var stretchers = _proxy.Stretchers;
            bool any = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (int index in SelectedStretcherIndices)
            {
                if (index < 0 || index >= stretchers.Count)
                    continue;

                Vector2 p = stretchers[index];

                if (!any)
                {
                    minX = maxX = p.x;
                    minY = maxY = p.y;
                    any = true;
                }
                else
                {
                    minX = Mathf.Min(minX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxX = Mathf.Max(maxX, p.x);
                    maxY = Mathf.Max(maxY, p.y);
                }
            }

            if (!any)
                return false;

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        public static bool TryGetStretcherBounds(out Rect bounds)
        {
            if (TryGetSelectedStretcherBounds(out bounds))
                return true;

            bounds = default;

            if (_proxy == null || _proxy.Stretchers.Count == 0)
                return false;

            var stretchers = _proxy.Stretchers;
            float x0 = stretchers[0].x;
            float y0 = stretchers[0].y;
            float x1 = x0;
            float y1 = y0;

            for (int i = 1; i < stretchers.Count; i++)
            {
                Vector2 p = stretchers[i];
                x0 = Mathf.Min(x0, p.x);
                y0 = Mathf.Min(y0, p.y);
                x1 = Mathf.Max(x1, p.x);
                y1 = Mathf.Max(y1, p.y);
            }

            bounds = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        private static HashSet<int> FindSeedFacesTouchingWorkingSeams(IndieEditableMeshData data)
        {
            var seeds = new HashSet<int>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (IndieEditUVSeamState.IsWorking(IndieEditMeshMeshUtil.EdgeKey(face.a, face.b)) ||
                    IndieEditUVSeamState.IsWorking(IndieEditMeshMeshUtil.EdgeKey(face.b, face.c)) ||
                    IndieEditUVSeamState.IsWorking(IndieEditMeshMeshUtil.EdgeKey(face.c, face.a)))
                {
                    seeds.Add(i);
                    break;
                }
            }

            return seeds;
        }
    }
}
