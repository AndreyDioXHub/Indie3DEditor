using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVSeamOps
    {
        public static void ConvertSelectedEdgesToSeams(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            var data = uv.Data;

            if (data.selectedEdges.Count == 0)
                return;

            var keys = new List<long>(data.selectedEdges.Count);

            for (int i = 0; i < data.selectedEdges.Count; i++)
                keys.Add(IndieEditUVSeamState.EdgeKeyFromData(data, data.selectedEdges[i]));

            IndieEditUVSeamState.AddWorkingMany(keys);
        }

        /// <summary>
        /// Flood-fill faces from current selection until baked∪working seams.
        /// If the region is unbounded (no enclosing seams), selects all faces.
        /// </summary>
        public static void ExpandPolygonSelectionToSeams(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            var data = uv.Data;

            if (data.faces.Count == 0)
                return;

            HashSet<int> seeds = new HashSet<int>();

            if (data.selectedFaces.Count > 0)
            {
                for (int i = 0; i < data.selectedFaces.Count; i++)
                {
                    int f = data.selectedFaces[i];

                    if (f >= 0 && f < data.faces.Count)
                        seeds.Add(f);
                }
            }
            else
            {
                for (int i = 0; i < data.faces.Count; i++)
                    seeds.Add(i);

                data.ClearSelection();
                data.selectedFaces.AddRange(seeds);
                uv.subMode = IndieEditMeshSubMode.Polygon;
                IndieEditUVNotify.SelectionOrMeshChanged();
                return;
            }

            Dictionary<long, List<int>> edgeToFaces = BuildEdgeFaceAdjacency(data);
            HashSet<int> result = new HashSet<int>();
            Queue<int> queue = new Queue<int>();
            bool hitOpenBoundary = false;

            foreach (int seed in seeds)
            {
                queue.Enqueue(seed);
                result.Add(seed);
            }

            while (queue.Count > 0)
            {
                int faceIndex = queue.Dequeue();
                var face = data.faces[faceIndex];
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.a,
                    face.b,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpenBoundary,
                    stopAtBaked: true
                );
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.b,
                    face.c,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpenBoundary,
                    stopAtBaked: true
                );
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.c,
                    face.a,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpenBoundary,
                    stopAtBaked: true
                );
            }

            if (hitOpenBoundary || result.Count == data.faces.Count)
            {
                result.Clear();

                for (int i = 0; i < data.faces.Count; i++)
                    result.Add(i);
            }

            data.ClearSelection();
            data.selectedFaces.AddRange(result);
            uv.subMode = IndieEditMeshSubMode.Polygon;
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        /// <summary>
        /// 3D perimeter of the current face selection (not UV-index topology).
        /// </summary>
        public static List<long> CollectFaceSelectionPerimeter(IndieEditableMeshData data)
        {
            if (data == null || data.selectedFaces.Count == 0)
                return new List<long>();

            return CollectFaceSetPerimeter(data, new HashSet<int>(data.selectedFaces));
        }

        public static void EnsureWorkingFromFaceSelectionIfEmpty(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null)
                return;

            if (IndieEditUVSeamState.WorkingSeams.Count > 0)
                return;

            if (uv.Data.selectedFaces.Count == 0)
                return;

            List<long> perimeter = CollectFaceSelectionPerimeter(uv.Data);
            IndieEditUVSeamState.AddWorkingMany(perimeter);
        }

        /// <summary>
        /// Fully independent UV island: every vertex used by region faces is duplicated
        /// so the chart shares no indices with the rest of the mesh.
        /// </summary>
        public static int DetachRegionAsIndependentIsland(
            IndieEditableMeshData data,
            HashSet<int> regionFaces
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return 0;

            var used = new HashSet<int>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                used.Add(face.a);
                used.Add(face.b);
                used.Add(face.c);
            }

            if (used.Count == 0)
                return 0;

            var remap = new Dictionary<int, int>(used.Count);

            foreach (int vert in used)
            {
                if (vert < 0 || vert >= data.vertices.Count)
                    continue;

                remap[vert] = IndieEditMeshMeshUtil.AddVertex(data, vert, data.vertices[vert]);
            }

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                bool changed = false;

                if (remap.TryGetValue(face.a, out int a))
                {
                    face.a = a;
                    changed = true;
                }

                if (remap.TryGetValue(face.b, out int b))
                {
                    face.b = b;
                    changed = true;
                }

                if (remap.TryGetValue(face.c, out int c))
                {
                    face.c = c;
                    changed = true;
                }

                if (changed)
                    data.faces[faceIndex] = face;
            }

            data.RebuildEdges();
            return remap.Count;
        }

        /// <summary>
        /// Project region verts onto the plane of the area-weighted average face
        /// normal (temporary UV before packing into a fixed slot).
        /// </summary>
        public static void ProjectRegionToAveragePlane(
            IndieEditableMeshData data,
            HashSet<int> regionFaces
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return;

            if (data.uvs.Count != data.vertices.Count)
                return;

            var regionVerts = new HashSet<int>();
            Vector3 normalSum = Vector3.zero;
            Vector3 centroid = Vector3.zero;
            int centroidCount = 0;

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (face.a < 0 || face.a >= data.vertices.Count ||
                    face.b < 0 || face.b >= data.vertices.Count ||
                    face.c < 0 || face.c >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 pa = data.vertices[face.a];
                Vector3 pb = data.vertices[face.b];
                Vector3 pc = data.vertices[face.c];
                Vector3 faceNormal = Vector3.Cross(pb - pa, pc - pa);
                normalSum += faceNormal;

                regionVerts.Add(face.a);
                regionVerts.Add(face.b);
                regionVerts.Add(face.c);
            }

            if (regionVerts.Count == 0)
                return;

            foreach (int v in regionVerts)
            {
                centroid += data.vertices[v];
                centroidCount++;
            }

            if (centroidCount > 0)
                centroid /= centroidCount;

            Vector3 normal = normalSum.sqrMagnitude > 1e-12f
                ? normalSum.normalized
                : Vector3.up;

            Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up;
            Vector3 tangent = Vector3.Cross(upHint, normal);

            if (tangent.sqrMagnitude < 1e-12f)
                tangent = Vector3.Cross(Vector3.right, normal);

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

            foreach (int v in regionVerts)
            {
                Vector3 local = data.vertices[v] - centroid;
                data.uvs[v] = new Vector2(
                    Vector3.Dot(local, tangent),
                    Vector3.Dot(local, bitangent)
                );
            }
        }

        /// <summary>
        /// Uniformly fit region UVs into fixed slot U∈[1,2], V∈[0,1].
        /// Always the same rect — no drifting offset.
        /// </summary>
        public static void FitRegionUvToSlot(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            float padding = 0.05f
        )
        {
            FitRegionUvToSlot(data, regionFaces, new Rect(1f, 0f, 1f, 1f), padding);
        }

        public static void FitRegionUvToSlot(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            Rect slot,
            float padding = 0.05f
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return;

            if (data.uvs.Count != data.vertices.Count)
                return;

            Rect target = slot;
            var regionVerts = new HashSet<int>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                regionVerts.Add(face.a);
                regionVerts.Add(face.b);
                regionVerts.Add(face.c);
            }

            if (regionVerts.Count == 0)
                return;

            float rMinX = float.PositiveInfinity;
            float rMaxX = float.NegativeInfinity;
            float rMinY = float.PositiveInfinity;
            float rMaxY = float.NegativeInfinity;

            foreach (int v in regionVerts)
            {
                if (v < 0 || v >= data.uvs.Count)
                    continue;

                Vector2 uv = data.uvs[v];
                rMinX = Mathf.Min(rMinX, uv.x);
                rMaxX = Mathf.Max(rMaxX, uv.x);
                rMinY = Mathf.Min(rMinY, uv.y);
                rMaxY = Mathf.Max(rMaxY, uv.y);
            }

            float srcW = Mathf.Max(1e-6f, rMaxX - rMinX);
            float srcH = Mathf.Max(1e-6f, rMaxY - rMinY);
            float pad = Mathf.Clamp01(padding);
            float availW = target.width * (1f - 2f * pad);
            float availH = target.height * (1f - 2f * pad);
            float scale = Mathf.Min(availW / srcW, availH / srcH);

            float packedW = srcW * scale;
            float packedH = srcH * scale;
            float destMinX = target.xMin + (target.width - packedW) * 0.5f;
            float destMinY = target.yMin + (target.height - packedH) * 0.5f;

            foreach (int v in regionVerts)
            {
                if (v < 0 || v >= data.uvs.Count)
                    continue;

                Vector2 uv = data.uvs[v];
                data.uvs[v] = new Vector2(
                    destMinX + (uv.x - rMinX) * scale,
                    destMinY + (uv.y - rMinY) * scale
                );
            }
        }

        /// <summary>
        /// Drop vertices not referenced by any face (orphans from detach/stitch).
        /// Face indices stay stable when no faces are discarded.
        /// </summary>
        public static void RemoveUnusedVertices(IndieEditableMeshData data)
        {
            if (data == null)
                return;

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
        }

        /// <summary>
        /// Move region UVs into empty space so the island does not sit on top of
        /// the original chart (legacy; prefer <see cref="FitRegionUvToSlot"/>).
        /// </summary>
        public static void OffsetRegionIslandInUv(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            float padding = 0.15f
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return;

            if (data.uvs.Count != data.vertices.Count)
                return;

            var regionVerts = new HashSet<int>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                regionVerts.Add(face.a);
                regionVerts.Add(face.b);
                regionVerts.Add(face.c);
            }

            if (regionVerts.Count == 0)
                return;

            float rMinX = float.PositiveInfinity;
            float rMaxX = float.NegativeInfinity;
            float rMinY = float.PositiveInfinity;
            float rMaxY = float.NegativeInfinity;

            foreach (int v in regionVerts)
            {
                if (v < 0 || v >= data.uvs.Count)
                    continue;

                Vector2 uv = data.uvs[v];
                rMinX = Mathf.Min(rMinX, uv.x);
                rMaxX = Mathf.Max(rMaxX, uv.x);
                rMinY = Mathf.Min(rMinY, uv.y);
                rMaxY = Mathf.Max(rMaxY, uv.y);
            }

            float oMaxX = 0f;
            bool anyOutside = false;

            for (int i = 0; i < data.uvs.Count; i++)
            {
                if (regionVerts.Contains(i))
                    continue;

                anyOutside = true;
                oMaxX = Mathf.Max(oMaxX, data.uvs[i].x);
            }

            float width = Mathf.Max(0.01f, rMaxX - rMinX);
            float targetMinX = anyOutside ? oMaxX + padding : rMinX;
            float dx = targetMinX - rMinX;
            float dy = 0f;

            if (Mathf.Abs(dx) < 1e-6f && Mathf.Abs(dy) < 1e-6f)
                dx = width + padding;

            foreach (int v in regionVerts)
            {
                if (v < 0 || v >= data.uvs.Count)
                    continue;

                Vector2 uv = data.uvs[v];
                data.uvs[v] = new Vector2(uv.x + dx, uv.y + dy);
            }
        }

        /// <summary>
        /// Geo edges that form the 3D perimeter of the face region.
        /// </summary>
        public static HashSet<GeoEdgeId> CollectFaceSetPerimeterGeo(
            IndieEditableMeshData data,
            HashSet<int> regionFaces
        )
        {
            var perimeterGeo = new HashSet<GeoEdgeId>();

            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return perimeterGeo;

            var geoToFaces = new Dictionary<GeoEdgeId, List<int>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddGeoEdgeFace(geoToFaces, data, face.a, face.b, faceIndex);
                AddGeoEdgeFace(geoToFaces, data, face.b, face.c, faceIndex);
                AddGeoEdgeFace(geoToFaces, data, face.c, face.a, faceIndex);
            }

            foreach (var pair in geoToFaces)
            {
                int regionCount = 0;
                int outsideCount = 0;

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (regionFaces.Contains(pair.Value[i]))
                        regionCount++;
                    else
                        outsideCount++;
                }

                if (regionCount == 0)
                    continue;

                if (outsideCount > 0 || pair.Value.Count == 1)
                    perimeterGeo.Add(pair.Key);
            }

            return perimeterGeo;
        }

        /// <summary>
        /// Snapshot working seams as geometric edge keys (survive vertex remaps).
        /// </summary>
        public static HashSet<GeoEdgeId> SnapshotWorkingGeoEdges(IndieEditableMeshData data)
        {
            var result = new HashSet<GeoEdgeId>();

            if (data == null)
                return result;

            foreach (long key in IndieEditUVSeamState.WorkingSeams)
            {
                if (!IndieEditUVSeamState.TryGetEdgeEndpoints(data, key, out int v0, out int v1))
                    continue;

                result.Add(GeoEdgeId.FromPositions(data.vertices[v0], data.vertices[v1]));
            }

            return result;
        }

        /// <summary>
        /// Re-add working seams for every mesh edge whose geometry matches
        /// <paramref name="geoEdges"/>.
        /// </summary>
        public static void RestoreWorkingFromGeo(
            IndieEditableMeshData data,
            HashSet<GeoEdgeId> geoEdges
        )
        {
            if (data == null || geoEdges == null || geoEdges.Count == 0)
                return;

            var keys = new List<long>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                var geo = GeoEdgeId.FromPositions(
                    data.vertices[edge.v0],
                    data.vertices[edge.v1]
                );

                if (!geoEdges.Contains(geo))
                    continue;

                keys.Add(IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1));
            }

            IndieEditUVSeamState.AddWorkingMany(keys);
        }

        /// <summary>
        /// Topologically open working seams inside the region so cyan cuts stay
        /// as real UV splits (pelt can stretch them). Duplicates both endpoints.
        /// Prefer <see cref="OpenRegionManualSlitsHinged"/> for pelt slits.
        /// </summary>
        public static int SplitRegionAlongWorkingSeams(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> workingGeo
        )
        {
            return SplitRegionAlongWorkingSeams(data, regionFaces, workingGeo, tips: null);
        }

        /// <summary>
        /// Open manual interior cyan seams as hinged slits: tip/hinge verts stay
        /// shared so a pocket flap is not fully detached from the region mass.
        /// Perimeter-only seams (1 region face) are skipped — already open after detach.
        /// </summary>
        public static int OpenRegionManualSlitsHinged(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> manualSeams
        )
        {
            if (data == null ||
                regionFaces == null ||
                manualSeams == null ||
                manualSeams.Count == 0)
            {
                return 0;
            }

            HashSet<GeoEdgeId> interiorCuts = CollectInteriorManualCuts(
                data,
                regionFaces,
                manualSeams
            );

            if (interiorCuts.Count == 0)
                return 0;

            HashSet<int> tips = CollectSlitTipVerts(
                data,
                regionFaces,
                interiorCuts
            );

            int splits = SplitRegionAlongWorkingSeams(
                data,
                regionFaces,
                interiorCuts,
                tips
            );

            WeldVertsAtTipPositions(data, regionFaces, tips);
            return splits;
        }

        /// <summary>
        /// Interior manual seams shared by ≥2 region faces (true slits, not perimeter).
        /// </summary>
        public static HashSet<GeoEdgeId> CollectInteriorManualCuts(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> manualSeams
        )
        {
            var interior = new HashSet<GeoEdgeId>();

            if (data == null ||
                regionFaces == null ||
                manualSeams == null ||
                manualSeams.Count == 0)
            {
                return interior;
            }

            var geoToFaces = BuildGeoFaceAdjacency(data);

            foreach (GeoEdgeId geo in manualSeams)
            {
                if (!geoToFaces.TryGetValue(geo, out List<int> faces))
                    continue;

                int regionCount = 0;

                for (int i = 0; i < faces.Count; i++)
                {
                    if (regionFaces.Contains(faces[i]))
                        regionCount++;
                }

                if (regionCount >= 2)
                    interior.Add(geo);
            }

            return interior;
        }

        /// <summary>
        /// Weld UV/index splits inside the region that are not intentional cyan cuts.
        /// Keeps hinged slits open; removes foreign greens (e.g. diagonal after Commit).
        /// </summary>
        public static int SealForeignCutsInRegion(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> intentionalCuts
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return 0;

            if (data.uvs.Count != data.vertices.Count)
                return 0;

            if (intentionalCuts == null)
                intentionalCuts = new HashSet<GeoEdgeId>();

            int total = 0;
            int guard = 16;

            while (guard-- > 0)
            {
                int sealedCount = SealForeignCutsInRegionOnce(
                    data,
                    regionFaces,
                    intentionalCuts
                );

                if (sealedCount <= 0)
                    break;

                total += sealedCount;
            }

            return total;
        }

        private static int SealForeignCutsInRegionOnce(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> intentionalCuts
        )
        {
            // Geo edge → region face edge incidences (index pairs).
            var geoIncidences = new Dictionary<GeoEdgeId, List<(int face, int i0, int i1)>>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                AccrueRegionGeoIncidence(geoIncidences, data, faceIndex, face.a, face.b);
                AccrueRegionGeoIncidence(geoIncidences, data, faceIndex, face.b, face.c);
                AccrueRegionGeoIncidence(geoIncidences, data, faceIndex, face.c, face.a);
            }

            var remap = new Dictionary<int, int>();
            int sealedEdges = 0;

            foreach (var pair in geoIncidences)
            {
                if (intentionalCuts.Contains(pair.Key))
                    continue;

                List<(int face, int i0, int i1)> list = pair.Value;

                if (list.Count < 2)
                    continue;

                int p0 = list[0].i0;
                int p1 = list[0].i1;
                Vector3 pos0 = data.vertices[p0];
                Vector3 pos1 = data.vertices[p1];
                bool anySplit = false;

                for (int i = 1; i < list.Count; i++)
                {
                    int o0 = list[i].i0;
                    int o1 = list[i].i1;
                    Vector3 oPos0 = data.vertices[o0];
                    Vector3 oPos1 = data.vertices[o1];

                    bool sameDirection =
                        (pos0 - oPos0).sqrMagnitude <= (pos0 - oPos1).sqrMagnitude;

                    int m0 = sameDirection ? o0 : o1;
                    int m1 = sameDirection ? o1 : o0;

                    if (p0 != m0 || p1 != m1)
                        anySplit = true;

                    Vector2 uvP0 = data.uvs[p0];
                    Vector2 uvP1 = data.uvs[p1];
                    Vector2 uvM0 = data.uvs[m0];
                    Vector2 uvM1 = data.uvs[m1];
                    const float uvEps = 1e-8f;

                    if ((uvP0 - uvM0).sqrMagnitude > uvEps ||
                        (uvP1 - uvM1).sqrMagnitude > uvEps)
                    {
                        anySplit = true;
                    }

                    if (p0 != m0)
                        RemapSealVert(remap, m0, p0);

                    if (p1 != m1)
                        RemapSealVert(remap, m1, p1);

                    if (p0 != m0)
                        data.uvs[p0] = (data.uvs[p0] + data.uvs[m0]) * 0.5f;

                    if (p1 != m1)
                        data.uvs[p1] = (data.uvs[p1] + data.uvs[m1]) * 0.5f;
                }

                if (anySplit)
                    sealedEdges++;
            }

            if (remap.Count == 0)
                return 0;

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                bool changed = false;

                if (TryResolveSealRemap(remap, face.a, out int a))
                {
                    face.a = a;
                    changed = true;
                }

                if (TryResolveSealRemap(remap, face.b, out int b))
                {
                    face.b = b;
                    changed = true;
                }

                if (TryResolveSealRemap(remap, face.c, out int c))
                {
                    face.c = c;
                    changed = true;
                }

                if (changed)
                    data.faces[faceIndex] = face;
            }

            data.RebuildEdges();
            return sealedEdges;
        }

        private static void AccrueRegionGeoIncidence(
            Dictionary<GeoEdgeId, List<(int face, int i0, int i1)>> map,
            IndieEditableMeshData data,
            int faceIndex,
            int i0,
            int i1
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            var geo = GeoEdgeId.FromPositions(data.vertices[i0], data.vertices[i1]);

            if (!map.TryGetValue(geo, out List<(int face, int i0, int i1)> list))
            {
                list = new List<(int face, int i0, int i1)>(2);
                map.Add(geo, list);
            }

            list.Add((faceIndex, i0, i1));
        }

        private static void RemapSealVert(Dictionary<int, int> remap, int from, int to)
        {
            if (from == to)
                return;

            // Follow existing chain for to.
            while (remap.TryGetValue(to, out int next) && next != to)
                to = next;

            if (from == to)
                return;

            remap[from] = to;
        }

        private static bool TryResolveSealRemap(
            Dictionary<int, int> remap,
            int vert,
            out int resolved
        )
        {
            resolved = vert;

            if (!remap.TryGetValue(vert, out int next))
                return false;

            int guard = 8;

            while (guard-- > 0 && remap.TryGetValue(next, out int n2) && n2 != next)
                next = n2;

            resolved = next;
            return resolved != vert;
        }

        private static int SplitRegionAlongWorkingSeams(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> workingGeo,
            HashSet<int> tips
        )
        {
            if (data == null ||
                regionFaces == null ||
                workingGeo == null ||
                workingGeo.Count == 0)
            {
                return 0;
            }

            int splits = 0;
            bool changed = true;
            int guard = workingGeo.Count * 4 + 8;

            while (changed && guard-- > 0)
            {
                changed = false;

                for (int edgeIndex = 0; edgeIndex < data.edges.Count; edgeIndex++)
                {
                    var edge = data.edges[edgeIndex];
                    var geo = GeoEdgeId.FromPositions(
                        data.vertices[edge.v0],
                        data.vertices[edge.v1]
                    );

                    if (!workingGeo.Contains(geo))
                        continue;

                    if (!TryForceSplitSharedRegionEdge(
                            data,
                            regionFaces,
                            edge.v0,
                            edge.v1,
                            tips
                        ))
                    {
                        continue;
                    }

                    splits++;
                    changed = true;
                    data.RebuildEdges();
                    break;
                }
            }

            return splits;
        }

        private static HashSet<int> CollectSlitTipVerts(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> interiorCuts
        )
        {
            var tips = new HashSet<int>();

            if (interiorCuts.Count == 0)
                return tips;

            HashSet<GeoEdgeId> perimeterGeo = CollectFaceSetPerimeterGeo(data, regionFaces);

            // Cut graph: vert → neighbor verts along interior cut edges.
            var neighbors = new Dictionary<int, List<int>>();
            var cutVertEdges = new List<(int a, int b)>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                var geo = GeoEdgeId.FromPositions(
                    data.vertices[edge.v0],
                    data.vertices[edge.v1]
                );

                if (!interiorCuts.Contains(geo))
                    continue;

                cutVertEdges.Add((edge.v0, edge.v1));
                LinkCutNeighbor(neighbors, edge.v0, edge.v1);
                LinkCutNeighbor(neighbors, edge.v1, edge.v0);
            }

            if (neighbors.Count == 0)
                return tips;

            var degree1 = new List<int>();

            foreach (var pair in neighbors)
            {
                if (pair.Value.Count == 1)
                    degree1.Add(pair.Key);
            }

            // Classic interior tip: degree-1 and not on 3D perimeter.
            for (int i = 0; i < degree1.Count; i++)
            {
                int vert = degree1[i];

                if (IsVertOnPerimeterGeo(data, vert, perimeterGeo, regionFaces))
                    continue;

                tips.Add(vert);
            }

            // Chord cuts (both ends on perimeter): hinge ONLY when the chord
            // isolates a single flap face (screenshot 1 pocket). Through-cuts
            // that split the region in two (screenshot 2) stay fully open.
            for (int i = 0; i < cutVertEdges.Count; i++)
            {
                int a = cutVertEdges[i].a;
                int b = cutVertEdges[i].b;
                bool aPerim = IsVertOnPerimeterGeo(data, a, perimeterGeo, regionFaces);
                bool bPerim = IsVertOnPerimeterGeo(data, b, perimeterGeo, regionFaces);

                if (!aPerim || !bPerim)
                    continue;

                int degA = neighbors.TryGetValue(a, out List<int> na) ? na.Count : 0;
                int degB = neighbors.TryGetValue(b, out List<int> nb) ? nb.Count : 0;

                if (degA != 1 || degB != 1)
                    continue;

                if (!ChordIsolatesFlapFace(
                        data,
                        regionFaces,
                        interiorCuts,
                        a,
                        b
                    ))
                {
                    continue;
                }

                int valenceA = CountRegionFaceValence(data, regionFaces, a);
                int valenceB = CountRegionFaceValence(data, regionFaces, b);
                tips.Add(valenceA >= valenceB ? a : b);
            }

            return tips;
        }

        /// <summary>
        /// True when one of the two region faces on edge (a,b) has no other
        /// connection into the region except across intentional cut edges.
        /// </summary>
        private static bool ChordIsolatesFlapFace(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> interiorCuts,
            int a,
            int b
        )
        {
            var faceIndices = new List<int>(2);

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!regionFaces.Contains(i))
                    continue;

                if (!IndieEditMeshMeshUtil.FaceHasEdge(data.faces[i], a, b))
                    continue;

                faceIndices.Add(i);
            }

            if (faceIndices.Count < 2)
                return false;

            for (int f = 0; f < faceIndices.Count; f++)
            {
                int faceIndex = faceIndices[f];
                var face = data.faces[faceIndex];
                int otherLinks = CountNonCutRegionLinks(
                    data,
                    regionFaces,
                    interiorCuts,
                    faceIndex,
                    face.a,
                    face.b
                );
                otherLinks += CountNonCutRegionLinks(
                    data,
                    regionFaces,
                    interiorCuts,
                    faceIndex,
                    face.b,
                    face.c
                );
                otherLinks += CountNonCutRegionLinks(
                    data,
                    regionFaces,
                    interiorCuts,
                    faceIndex,
                    face.c,
                    face.a
                );

                // Only connected to the rest via cut edges → flap.
                if (otherLinks == 0)
                    return true;
            }

            return false;
        }

        private static int CountNonCutRegionLinks(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> interiorCuts,
            int selfFace,
            int v0,
            int v1
        )
        {
            if (v0 < 0 || v1 < 0 ||
                v0 >= data.vertices.Count ||
                v1 >= data.vertices.Count)
            {
                return 0;
            }

            var geo = GeoEdgeId.FromPositions(data.vertices[v0], data.vertices[v1]);

            if (interiorCuts.Contains(geo))
                return 0;

            int links = 0;

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (i == selfFace || !regionFaces.Contains(i))
                    continue;

                if (IndieEditMeshMeshUtil.FaceHasEdge(data.faces[i], v0, v1))
                    links++;
            }

            return links;
        }

        private static void LinkCutNeighbor(
            Dictionary<int, List<int>> neighbors,
            int from,
            int to
        )
        {
            if (!neighbors.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(2);
                neighbors[from] = list;
            }

            if (!list.Contains(to))
                list.Add(to);
        }

        private static bool IsVertOnPerimeterGeo(
            IndieEditableMeshData data,
            int vert,
            HashSet<GeoEdgeId> perimeterGeo,
            HashSet<int> regionFaces
        )
        {
            if (vert < 0 || vert >= data.vertices.Count)
                return false;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (edge.v0 != vert && edge.v1 != vert)
                    continue;

                var geo = GeoEdgeId.FromPositions(
                    data.vertices[edge.v0],
                    data.vertices[edge.v1]
                );

                if (perimeterGeo.Contains(geo))
                    return true;
            }

            // Also: any region face edge from this vert whose geo is perimeter.
            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (face.a != vert && face.b != vert && face.c != vert)
                    continue;

                if (EdgeTouchesPerimeter(data, face.a, face.b, perimeterGeo) ||
                    EdgeTouchesPerimeter(data, face.b, face.c, perimeterGeo) ||
                    EdgeTouchesPerimeter(data, face.c, face.a, perimeterGeo))
                {
                    // Vert is on a perimeter edge of this face.
                    if ((face.a == vert || face.b == vert) &&
                        EdgeTouchesPerimeter(data, face.a, face.b, perimeterGeo))
                        return true;

                    if ((face.b == vert || face.c == vert) &&
                        EdgeTouchesPerimeter(data, face.b, face.c, perimeterGeo))
                        return true;

                    if ((face.c == vert || face.a == vert) &&
                        EdgeTouchesPerimeter(data, face.c, face.a, perimeterGeo))
                        return true;
                }
            }

            return false;
        }

        private static bool EdgeTouchesPerimeter(
            IndieEditableMeshData data,
            int a,
            int b,
            HashSet<GeoEdgeId> perimeterGeo
        )
        {
            if (a < 0 || b < 0 ||
                a >= data.vertices.Count ||
                b >= data.vertices.Count)
            {
                return false;
            }

            return perimeterGeo.Contains(
                GeoEdgeId.FromPositions(data.vertices[a], data.vertices[b])
            );
        }

        private static int CountRegionFaceValence(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            int vert
        )
        {
            int count = 0;

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (face.a == vert || face.b == vert || face.c == vert)
                    count++;
            }

            return count;
        }

        private static bool TryForceSplitSharedRegionEdge(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            int v0,
            int v1,
            HashSet<int> tips
        )
        {
            var faceIndices = new List<int>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!regionFaces.Contains(i))
                    continue;

                if (!IndieEditMeshMeshUtil.FaceHasEdge(data.faces[i], v0, v1))
                    continue;

                faceIndices.Add(i);
            }

            if (faceIndices.Count < 2)
                return false;

            bool keep0 = tips != null && tips.Contains(v0);
            bool keep1 = tips != null && tips.Contains(v1);

            // Keep first face on original verts; give others copies (except tips).
            for (int i = 1; i < faceIndices.Count; i++)
            {
                int faceIndex = faceIndices[i];
                var face = data.faces[faceIndex];

                if (!IndieEditMeshMeshUtil.FaceHasEdge(face, v0, v1))
                    continue;

                int c0 = keep0
                    ? v0
                    : IndieEditMeshMeshUtil.AddVertex(data, v0, data.vertices[v0]);
                int c1 = keep1
                    ? v1
                    : IndieEditMeshMeshUtil.AddVertex(data, v1, data.vertices[v1]);

                if (face.a == v0)
                    face.a = c0;
                else if (face.b == v0)
                    face.b = c0;
                else if (face.c == v0)
                    face.c = c0;

                if (face.a == v1)
                    face.a = c1;
                else if (face.b == v1)
                    face.b = c1;
                else if (face.c == v1)
                    face.c = c1;

                data.faces[faceIndex] = face;
            }

            return true;
        }

        /// <summary>
        /// Re-weld any leftover copies at tip/hinge positions after hinged split.
        /// </summary>
        private static void WeldVertsAtTipPositions(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<int> tipVerts
        )
        {
            if (data == null ||
                regionFaces == null ||
                tipVerts == null ||
                tipVerts.Count == 0)
            {
                return;
            }

            if (data.uvs.Count != data.vertices.Count)
                return;

            var tipPositions = new HashSet<Vector3Int>();

            foreach (int vert in tipVerts)
            {
                if (vert < 0 || vert >= data.vertices.Count)
                    continue;

                tipPositions.Add(QuantizePosition(data.vertices[vert]));
            }

            if (tipPositions.Count == 0)
                return;

            var outsideVerts = new HashSet<int>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                if (regionFaces.Contains(faceIndex))
                    continue;

                var face = data.faces[faceIndex];
                outsideVerts.Add(face.a);
                outsideVerts.Add(face.b);
                outsideVerts.Add(face.c);
            }

            var byPosition = new Dictionary<Vector3Int, List<int>>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                CollectRegionVertAtPosition(data, face.a, outsideVerts, byPosition);
                CollectRegionVertAtPosition(data, face.b, outsideVerts, byPosition);
                CollectRegionVertAtPosition(data, face.c, outsideVerts, byPosition);
            }

            var remap = new Dictionary<int, int>();

            foreach (Vector3Int pos in tipPositions)
            {
                if (!byPosition.TryGetValue(pos, out List<int> group) || group.Count < 2)
                    continue;

                group.Sort();
                int primary = group[0];
                Vector2 uvSum = data.uvs[primary];
                int uvCount = 1;

                for (int i = 1; i < group.Count; i++)
                {
                    remap[group[i]] = primary;
                    uvSum += data.uvs[group[i]];
                    uvCount++;
                }

                data.uvs[primary] = uvSum / uvCount;
            }

            if (remap.Count == 0)
                return;

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                bool changed = false;

                if (remap.TryGetValue(face.a, out int a))
                {
                    face.a = a;
                    changed = true;
                }

                if (remap.TryGetValue(face.b, out int b))
                {
                    face.b = b;
                    changed = true;
                }

                if (remap.TryGetValue(face.c, out int c))
                {
                    face.c = c;
                    changed = true;
                }

                if (changed)
                    data.faces[faceIndex] = face;
            }

            data.RebuildEdges();
        }

        /// <summary>
        /// Weld coincident positions inside the region so baked (green) cuts stitch
        /// into one UV sheet. When <paramref name="preserveWorkingGeo"/> is set,
        /// never welds across those cyan geo seams. Pass null to weld everything
        /// (pelt open: ignore green, re-open manual slits after).
        /// </summary>
        public static int StitchRegionInternalCuts(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> preserveWorkingGeo = null
        )
        {
            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return 0;

            if (data.uvs.Count != data.vertices.Count)
                return 0;

            int totalWelded = 0;
            int guard = 8;

            while (guard-- > 0)
            {
                int welded = StitchRegionInternalCutsOnce(
                    data,
                    regionFaces,
                    preserveWorkingGeo
                );

                if (welded <= 0)
                    break;

                totalWelded += welded;
            }

            return totalWelded;
        }

        private static int StitchRegionInternalCutsOnce(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<GeoEdgeId> preserveWorkingGeo
        )
        {
            var outsideVerts = new HashSet<int>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                if (regionFaces.Contains(faceIndex))
                    continue;

                var face = data.faces[faceIndex];
                outsideVerts.Add(face.a);
                outsideVerts.Add(face.b);
                outsideVerts.Add(face.c);
            }

            // Verts that sit on a preserved working seam must not weld to copies.
            var protectedVerts = new HashSet<int>();

            if (preserveWorkingGeo != null && preserveWorkingGeo.Count > 0)
            {
                for (int i = 0; i < data.edges.Count; i++)
                {
                    var edge = data.edges[i];
                    var geo = GeoEdgeId.FromPositions(
                        data.vertices[edge.v0],
                        data.vertices[edge.v1]
                    );

                    if (!preserveWorkingGeo.Contains(geo))
                        continue;

                    protectedVerts.Add(edge.v0);
                    protectedVerts.Add(edge.v1);
                }
            }

            var byPosition = new Dictionary<Vector3Int, List<int>>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                CollectRegionVertAtPosition(data, face.a, outsideVerts, byPosition);
                CollectRegionVertAtPosition(data, face.b, outsideVerts, byPosition);
                CollectRegionVertAtPosition(data, face.c, outsideVerts, byPosition);
            }

            var remap = new Dictionary<int, int>();
            int weldedGroups = 0;

            foreach (var pair in byPosition)
            {
                List<int> group = pair.Value;

                if (group.Count < 2)
                    continue;

                var weldable = new List<int>();

                for (int i = 0; i < group.Count; i++)
                {
                    if (protectedVerts.Contains(group[i]))
                        continue;

                    weldable.Add(group[i]);
                }

                if (weldable.Count < 2)
                    continue;

                weldable.Sort();
                int primary = weldable[0];
                Vector2 uvSum = data.uvs[primary];
                int uvCount = 1;

                for (int i = 1; i < weldable.Count; i++)
                {
                    int other = weldable[i];
                    remap[other] = primary;
                    uvSum += data.uvs[other];
                    uvCount++;
                }

                if (uvCount > 1)
                {
                    data.uvs[primary] = uvSum / uvCount;
                    weldedGroups++;
                }
            }

            if (remap.Count == 0)
                return 0;

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                bool changed = false;

                if (remap.TryGetValue(face.a, out int a))
                {
                    face.a = a;
                    changed = true;
                }

                if (remap.TryGetValue(face.b, out int b))
                {
                    face.b = b;
                    changed = true;
                }

                if (remap.TryGetValue(face.c, out int c))
                {
                    face.c = c;
                    changed = true;
                }

                if (changed)
                    data.faces[faceIndex] = face;
            }

            data.RebuildEdges();
            return weldedGroups;
        }

        private static void CollectRegionVertAtPosition(
            IndieEditableMeshData data,
            int vert,
            HashSet<int> outsideVerts,
            Dictionary<Vector3Int, List<int>> byPosition
        )
        {
            if (vert < 0 || vert >= data.vertices.Count)
                return;

            if (outsideVerts.Contains(vert))
                return;

            Vector3Int key = QuantizePosition(data.vertices[vert]);

            if (!byPosition.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(2);
                byPosition.Add(key, list);
            }

            if (!list.Contains(vert))
                list.Add(vert);
        }

        private const float PositionQuantize = 10000f;

        private static Vector3Int QuantizePosition(Vector3 p)
        {
            return new Vector3Int(
                Mathf.RoundToInt(p.x * PositionQuantize),
                Mathf.RoundToInt(p.y * PositionQuantize),
                Mathf.RoundToInt(p.z * PositionQuantize)
            );
        }

        public readonly struct GeoEdgeId : System.IEquatable<GeoEdgeId>
        {
            public readonly Vector3Int A;
            public readonly Vector3Int B;

            public GeoEdgeId(Vector3Int a, Vector3Int b)
            {
                if (Compare(a, b) <= 0)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public static GeoEdgeId FromPositions(Vector3 p0, Vector3 p1)
            {
                return new GeoEdgeId(QuantizePosition(p0), QuantizePosition(p1));
            }

            public bool Equals(GeoEdgeId other) => A.Equals(other.A) && B.Equals(other.B);

            public override bool Equals(object obj) =>
                obj is GeoEdgeId other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A.GetHashCode() * 397) ^ B.GetHashCode();
                }
            }

            private static int Compare(Vector3Int a, Vector3Int b)
            {
                if (a.x != b.x)
                    return a.x.CompareTo(b.x);

                if (a.y != b.y)
                    return a.y.CompareTo(b.y);

                return a.z.CompareTo(b.z);
            }
        }

        /// <summary>
        /// Perimeter of a face region in <b>3D geometry</b> (quantized positions),
        /// not UV vertex indices. Internal UV-split (green) edges stay internal
        /// when both sides are in the region — they must not become working seams.
        /// </summary>
        public static List<long> CollectFaceSetPerimeter(
            IndieEditableMeshData data,
            HashSet<int> regionFaces
        )
        {
            var result = new List<long>();

            if (data == null || regionFaces == null || regionFaces.Count == 0)
                return result;

            var geoToFaces = new Dictionary<GeoEdgeId, List<int>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddGeoEdgeFace(geoToFaces, data, face.a, face.b, faceIndex);
                AddGeoEdgeFace(geoToFaces, data, face.b, face.c, faceIndex);
                AddGeoEdgeFace(geoToFaces, data, face.c, face.a, faceIndex);
            }

            var perimeterGeo = new HashSet<GeoEdgeId>();

            foreach (var pair in geoToFaces)
            {
                int regionCount = 0;
                int outsideCount = 0;

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (regionFaces.Contains(pair.Value[i]))
                        regionCount++;
                    else
                        outsideCount++;
                }

                if (regionCount == 0)
                    continue;

                // Selection meets the rest of the mesh, or open mesh border.
                if (outsideCount > 0 || pair.Value.Count == 1)
                    perimeterGeo.Add(pair.Key);
            }

            if (perimeterGeo.Count == 0)
                return result;

            // Emit index keys for region-side edges that sit on those geo edges.
            var seen = new HashSet<long>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                TryAddGeoPerimeterKey(data, perimeterGeo, seen, result, face.a, face.b);
                TryAddGeoPerimeterKey(data, perimeterGeo, seen, result, face.b, face.c);
                TryAddGeoPerimeterKey(data, perimeterGeo, seen, result, face.c, face.a);
            }

            return result;
        }

        private static void AddGeoEdgeFace(
            Dictionary<GeoEdgeId, List<int>> map,
            IndieEditableMeshData data,
            int i0,
            int i1,
            int faceIndex
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            var geo = GeoEdgeId.FromPositions(data.vertices[i0], data.vertices[i1]);

            if (!map.TryGetValue(geo, out List<int> list))
            {
                list = new List<int>(2);
                map.Add(geo, list);
            }

            if (!list.Contains(faceIndex))
                list.Add(faceIndex);
        }

        private static void TryAddGeoPerimeterKey(
            IndieEditableMeshData data,
            HashSet<GeoEdgeId> perimeterGeo,
            HashSet<long> seen,
            List<long> result,
            int i0,
            int i1
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            var geo = GeoEdgeId.FromPositions(data.vertices[i0], data.vertices[i1]);

            if (!perimeterGeo.Contains(geo))
                return;

            long key = IndieEditMeshMeshUtil.EdgeKey(i0, i1);

            if (!seen.Add(key))
                return;

            result.Add(key);
        }

        private static void AddVertFace(Dictionary<int, List<int>> map, int vert, int faceIndex)
        {
            if (!map.TryGetValue(vert, out List<int> list))
            {
                list = new List<int>(4);
                map.Add(vert, list);
            }

            list.Add(faceIndex);
        }

        /// <summary>
        /// Shortest 3D-length edge path (Dijkstra). With a camera, only edges of
        /// front-facing faces are walkable; near-equal lengths prefer the route closer to the camera.
        /// </summary>
        public static List<long> FindShortestEdgePath(
            IndieEditableMeshData data,
            int fromVertex,
            int toVertex,
            Transform transform,
            Camera camera
        )
        {
            return IndieEditMeshShortestPath.FindShortestEdgePath(
                data,
                fromVertex,
                toVertex,
                transform,
                camera
            );
        }

        /// <summary>
        /// Keep the largest face component inside <paramref name="faces"/> where
        /// adjacency crosses only non-seam geo edges (working seams cut the mass).
        /// Small islands fully cut off by seams are discarded.
        /// </summary>
        public static HashSet<int> KeepLargestComponentCutBySeams(
            IndieEditableMeshData data,
            HashSet<int> faces,
            HashSet<GeoEdgeId> cuttingSeams
        )
        {
            var result = new HashSet<int>();

            if (data == null || faces == null || faces.Count == 0)
                return result;

            if (cuttingSeams == null)
                cuttingSeams = new HashSet<GeoEdgeId>();

            Dictionary<GeoEdgeId, List<int>> geoToFaces = BuildGeoFaceAdjacency(data);
            var visited = new HashSet<int>();
            List<int> best = null;

            foreach (int seed in faces)
            {
                if (!visited.Add(seed))
                    continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                component.Add(seed);

                while (queue.Count > 0)
                {
                    int faceIndex = queue.Dequeue();
                    var face = data.faces[faceIndex];
                    TryWalkAcrossGeo(
                        data,
                        geoToFaces,
                        cuttingSeams,
                        faces,
                        face.a,
                        face.b,
                        faceIndex,
                        visited,
                        queue,
                        component
                    );
                    TryWalkAcrossGeo(
                        data,
                        geoToFaces,
                        cuttingSeams,
                        faces,
                        face.b,
                        face.c,
                        faceIndex,
                        visited,
                        queue,
                        component
                    );
                    TryWalkAcrossGeo(
                        data,
                        geoToFaces,
                        cuttingSeams,
                        faces,
                        face.c,
                        face.a,
                        faceIndex,
                        visited,
                        queue,
                        component
                    );
                }

                if (best == null || component.Count > best.Count)
                    best = component;
            }

            if (best == null)
                return result;

            for (int i = 0; i < best.Count; i++)
                result.Add(best[i]);

            return result;
        }

        private static void TryWalkAcrossGeo(
            IndieEditableMeshData data,
            Dictionary<GeoEdgeId, List<int>> geoToFaces,
            HashSet<GeoEdgeId> cuttingSeams,
            HashSet<int> allowedFaces,
            int i0,
            int i1,
            int faceIndex,
            HashSet<int> visited,
            Queue<int> queue,
            List<int> component
        )
        {
            if (i0 < 0 || i1 < 0 ||
                i0 >= data.vertices.Count ||
                i1 >= data.vertices.Count)
            {
                return;
            }

            var geo = GeoEdgeId.FromPositions(data.vertices[i0], data.vertices[i1]);

            // Working seams cut the mass — do not cross.
            if (cuttingSeams.Contains(geo))
                return;

            if (!geoToFaces.TryGetValue(geo, out List<int> list))
                return;

            for (int i = 0; i < list.Count; i++)
            {
                int other = list[i];

                if (other == faceIndex || !allowedFaces.Contains(other))
                    continue;

                if (!visited.Add(other))
                    continue;

                component.Add(other);
                queue.Enqueue(other);
            }
        }

        private static Dictionary<GeoEdgeId, List<int>> BuildGeoFaceAdjacency(IndieEditableMeshData data)
        {
            var map = new Dictionary<GeoEdgeId, List<int>>();

            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                var face = data.faces[faceIndex];
                AddGeoEdgeFace(map, data, face.a, face.b, faceIndex);
                AddGeoEdgeFace(map, data, face.b, face.c, faceIndex);
                AddGeoEdgeFace(map, data, face.c, face.a, faceIndex);
            }

            return map;
        }

        /// <param name="stopAtBaked">
        /// When true (Expand), baked∪working stop the flood. When false (Pelt),
        /// only working seams stop — baked green seams are ignored.
        /// </param>
        public static HashSet<int> CollectRegionFaces(
            IndieEditableMeshData data,
            HashSet<int> seedFaces,
            bool stopAtBaked = true
        )
        {
            var result = new HashSet<int>();

            if (data == null || seedFaces == null || seedFaces.Count == 0)
                return result;

            Dictionary<long, List<int>> edgeToFaces = BuildEdgeFaceAdjacency(data);
            var queue = new Queue<int>();
            bool hitOpen = false;

            foreach (int seed in seedFaces)
            {
                if (seed < 0 || seed >= data.faces.Count)
                    continue;

                queue.Enqueue(seed);
                result.Add(seed);
            }

            while (queue.Count > 0)
            {
                int faceIndex = queue.Dequeue();
                var face = data.faces[faceIndex];
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.a,
                    face.b,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpen,
                    stopAtBaked
                );
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.b,
                    face.c,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpen,
                    stopAtBaked
                );
                TryExpandAcross(
                    data,
                    edgeToFaces,
                    face.c,
                    face.a,
                    faceIndex,
                    result,
                    queue,
                    ref hitOpen,
                    stopAtBaked
                );
            }

            return result;
        }

        private static void TryExpandAcross(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            int a,
            int b,
            int faceIndex,
            HashSet<int> result,
            Queue<int> queue,
            ref bool hitOpenBoundary,
            bool stopAtBaked
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            // Expand: baked∪working. Pelt: working only (green must not block unwrap).
            bool blocked = stopAtBaked
                ? IndieEditUVSeamState.IsBoundary(key)
                : IndieEditUVSeamState.IsWorking(key);

            if (blocked)
                return;

            if (!edgeToFaces.TryGetValue(key, out List<int> faces))
            {
                hitOpenBoundary = true;
                return;
            }

            if (faces.Count < 2)
                hitOpenBoundary = true;

            for (int i = 0; i < faces.Count; i++)
            {
                int other = faces[i];

                if (other == faceIndex)
                    continue;

                if (result.Add(other))
                    queue.Enqueue(other);
            }
        }

        private static Dictionary<long, List<int>> BuildEdgeFaceAdjacency(IndieEditableMeshData data)
        {
            var map = new Dictionary<long, List<int>>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddAdj(map, face.a, face.b, i);
                AddAdj(map, face.b, face.c, i);
                AddAdj(map, face.c, face.a, i);
            }

            return map;
        }

        private static void AddAdj(Dictionary<long, List<int>> map, int a, int b, int faceIndex)
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (!map.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(2);
                map.Add(key, list);
            }

            if (!list.Contains(faceIndex))
                list.Add(faceIndex);
        }

        private static void CountEdge(Dictionary<long, int> map, int a, int b)
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (!map.ContainsKey(key))
                map[key] = 1;
            else
                map[key]++;
        }

    }
}
