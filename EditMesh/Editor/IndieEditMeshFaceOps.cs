using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshFaceOps
    {
        /// <summary>Cap for a Local Normal offset where planes nearly face each other.</summary>
        private const float MaxOffsetScale = 4f;

        /// <summary>Makes "stay in this plane" win over the outline distance at a fold.</summary>
        private const float PlaneKeepWeight = 100f;

        public static void Extrude(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Extrude Polygons");
            IndieEditMeshFaceRegions.ApplyLocal(
                ep.Data,
                data => ApplyExtrude(
                    data,
                    IndieEditMeshFaceParams.ExtrudeHeight,
                    IndieEditMeshFaceParams.ExtrudeType
                )
            );
            Finish(ep);
        }

        public static void Outline(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Outline Polygons");
            IndieEditMeshFaceRegions.ApplyLocal(
                ep.Data,
                data => ApplyOutline(data, IndieEditMeshFaceParams.OutlineAmount),
                IndieEditMeshFaceRegionSpan.OutlineDoubleBelt
            );
            Finish(ep);
        }

        public static void Bevel(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Bevel Polygons");
            IndieEditMeshFaceRegions.ApplyLocal(
                ep.Data,
                data => ApplyBevel(
                    data,
                    IndieEditMeshFaceParams.BevelHeight,
                    IndieEditMeshFaceParams.BevelOutline,
                    IndieEditMeshFaceParams.BevelType
                )
            );
            Finish(ep);
        }

        public static void Inset(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Inset Polygons");
            IndieEditMeshFaceRegions.ApplyLocal(
                ep.Data,
                data => ApplyInset(
                    data,
                    IndieEditMeshFaceParams.InsetAmount,
                    IndieEditMeshFaceParams.InsetType
                )
            );
            Finish(ep);
        }

        public static void Bridge(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep) || ep.Data.selectedFaces.Count < 2)
                return;

            IndieEditMeshUndo.Register(ep, "Bridge Polygons");
            ApplyBridge(
                ep.Data,
                IndieEditMeshFaceParams.BridgeSegments,
                IndieEditMeshFaceParams.BridgeTwist,
                IndieEditMeshFaceParams.BridgeTaper,
                IndieEditMeshFaceParams.BridgeBias
            );
            Finish(ep);
        }

        public static void Flip(IndieEditMeshComponent ep)
        {
            IndieEditMeshOps.FlipNormals(ep);
        }

        public const int StripMinFaces = 4;

        public const int RingMinFaces = StripMinFaces;

        public static bool CanRing(IndieEditableMeshData data)
        {
            return CanStripSelect(data);
        }

        public static bool CanLoop(IndieEditableMeshData data)
        {
            return CanStripSelect(data);
        }

        public static bool CanRow(IndieEditableMeshData data)
        {
            return CanStripSelect(data);
        }

        private static bool CanStripSelect(IndieEditableMeshData data)
        {
            return data != null && data.selectedFaces.Count >= StripMinFaces;
        }

        public static void Ring(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep) || !CanRing(ep.Data))
                return;

            IndieEditMeshUndo.Register(ep, "Ring Polygons");
            ApplyRing(ep.Data, IndieEditMeshFaceParams.RingAngle);
            Finish(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void Loop(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep) || !CanLoop(ep.Data))
                return;

            IndieEditMeshUndo.Register(ep, "Loop Polygons");
            // Seeds → Edge Loops → polygons between the two rings.
            ApplyLoopSelectSpokes(ep.Data);
            ep.subMode = IndieEditMeshSubMode.Polygon;
            Finish(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void Row(IndieEditMeshComponent ep)
        {
            if (!CanEditFaces(ep) || !CanRow(ep.Data))
                return;

            IndieEditMeshUndo.Register(ep, "Row Polygons");
            ApplyRow(ep.Data, IndieEditMeshFaceParams.RowAngle);
            Finish(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        /// <summary>
        /// Grow a row of quads left/right from ≥4 seed faces.
        /// Steps across width edges (point 1→2); stops on turns &gt; angleDegrees.
        /// </summary>
        public static void ApplyRow(IndieEditableMeshData data, float angleDegrees)
        {
            if (data == null || !CanRow(data) || data.faces.Count == 0)
                return;

            var seedFaces = new List<int>(data.selectedFaces);
            Vector3 seedNormal = AverageFaceNormal(data, seedFaces);

            if (seedNormal.sqrMagnitude < 1e-12f)
                return;

            seedNormal.Normalize();

            if (!TryGetFaceCentroidsAxis(data, seedFaces, out Vector3 rowAxis))
                return;

            Vector3 widthAxis = Vector3.Cross(seedNormal, rowAxis);

            if (widthAxis.sqrMagnitude < 1e-12f)
                return;

            widthAxis.Normalize();

            BuildAllFaceEdgeMap(data, out var edgeToFaces);

            float maxTurn = Mathf.Clamp(angleDegrees, 0.1f, 179f);
            float cosMaxTurn = Mathf.Cos(maxTurn * Mathf.Deg2Rad);
            const float parallelDegrees = 30f;
            float cosParallel = Mathf.Cos(parallelDegrees * Mathf.Deg2Rad);
            const float coplanarDot = 0.9f;

            var result = new HashSet<int>(seedFaces);
            var queue = new Queue<int>(seedFaces);

            while (queue.Count > 0)
            {
                int faceIndex = queue.Dequeue();
                var face = data.faces[faceIndex];
                Vector3 n0 = FaceNormal(data, face);
                var edges = new[]
                {
                    (face.a, face.b),
                    (face.b, face.c),
                    (face.c, face.a)
                };

                for (int e = 0; e < edges.Length; e++)
                {
                    int v0 = edges[e].Item1;
                    int v1 = edges[e].Item2;

                    if (!IsSegmentParallelToAxis(
                            data.vertices[v0],
                            data.vertices[v1],
                            widthAxis,
                            cosParallel))
                    {
                        continue;
                    }

                    long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

                    if (!edgeToFaces.TryGetValue(key, out var faces) || faces.Count < 2)
                        continue;

                    for (int f = 0; f < faces.Count; f++)
                    {
                        int other = faces[f];

                        if (other == faceIndex || result.Contains(other))
                            continue;

                        Vector3 n1 = FaceNormal(data, data.faces[other]);
                        float ndot = Vector3.Dot(n0.normalized, n1.normalized);

                        // Sharp bend → stop this branch (default 60°).
                        if (ndot < cosMaxTurn)
                            continue;

                        if (!result.Add(other))
                            continue;

                        queue.Enqueue(other);
                        TryEnqueueCoplanarQuadPartner(
                            data,
                            edgeToFaces,
                            other,
                            n1,
                            coplanarDot,
                            result,
                            queue
                        );
                    }
                }

                // Also complete the seed/current triangle into a quad.
                TryEnqueueCoplanarQuadPartner(
                    data,
                    edgeToFaces,
                    faceIndex,
                    n0,
                    coplanarDot,
                    result,
                    queue
                );
            }

            data.selectedFaces = new List<int>(result);
            data.selectedEdges = new List<int>();
            data.selectedVertices = new List<int>();
        }

        private static bool TryGetFaceCentroidsAxis(
            IndieEditableMeshData data,
            List<int> faces,
            out Vector3 axis
        )
        {
            axis = Vector3.zero;

            if (faces == null || faces.Count < 2)
                return false;

            Vector3 mean = Vector3.zero;
            int count = 0;
            var centroids = new List<Vector3>(faces.Count);

            for (int i = 0; i < faces.Count; i++)
            {
                int fi = faces[i];

                if (fi < 0 || fi >= data.faces.Count)
                    continue;

                var face = data.faces[fi];
                Vector3 c = (data.vertices[face.a] + data.vertices[face.b] +
                             data.vertices[face.c]) * (1f / 3f);
                centroids.Add(c);
                mean += c;
                count++;
            }

            if (count < 2)
                return false;

            mean /= count;

            // Covariance (symmetric) → largest eigenvector ≈ row direction.
            float xx = 0f, yy = 0f, zz = 0f, xy = 0f, xz = 0f, yz = 0f;

            for (int i = 0; i < centroids.Count; i++)
            {
                Vector3 d = centroids[i] - mean;
                xx += d.x * d.x;
                yy += d.y * d.y;
                zz += d.z * d.z;
                xy += d.x * d.y;
                xz += d.x * d.z;
                yz += d.y * d.z;
            }

            // Power iteration on 3×3 covariance.
            Vector3 v = new Vector3(xx, xy, xz);

            if (v.sqrMagnitude < 1e-12f)
                v = new Vector3(xy, yy, yz);

            if (v.sqrMagnitude < 1e-12f)
                v = Vector3.right;

            v.Normalize();

            for (int iter = 0; iter < 12; iter++)
            {
                Vector3 mv = new Vector3(
                    xx * v.x + xy * v.y + xz * v.z,
                    xy * v.x + yy * v.y + yz * v.z,
                    xz * v.x + yz * v.y + zz * v.z
                );

                if (mv.sqrMagnitude < 1e-16f)
                    return false;

                v = mv.normalized;
            }

            axis = v;
            return axis.sqrMagnitude > 1e-12f;
        }

        private static bool IsSegmentParallelToAxis(
            Vector3 a,
            Vector3 b,
            Vector3 axis,
            float cosParallel
        )
        {
            Vector3 delta = b - a;
            float len = delta.magnitude;

            if (len < 1e-8f)
                return false;

            return Mathf.Abs(Vector3.Dot(delta / len, axis)) >= cosParallel;
        }

        private static void BuildAllFaceEdgeMap(
            IndieEditableMeshData data,
            out Dictionary<long, List<int>> edgeToFaces
        )
        {
            edgeToFaces = new Dictionary<long, List<int>>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.a, face.b), i);
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.b, face.c), i);
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.c, face.a), i);
            }
        }

        private static void TryEnqueueCoplanarQuadPartner(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            int faceIndex,
            Vector3 faceNormal,
            float coplanarDot,
            HashSet<int> result,
            Queue<int> queue
        )
        {
            var face = data.faces[faceIndex];
            var edges = new[]
            {
                (face.a, face.b),
                (face.b, face.c),
                (face.c, face.a)
            };

            for (int i = 0; i < edges.Length; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(edges[i].Item1, edges[i].Item2);

                if (!edgeToFaces.TryGetValue(key, out var faces) || faces.Count < 2)
                    continue;

                for (int f = 0; f < faces.Count; f++)
                {
                    int other = faces[f];

                    if (other == faceIndex || result.Contains(other))
                        continue;

                    if (Vector3.Dot(faceNormal.normalized, FaceNormal(data, data.faces[other]).normalized) <
                        coplanarDot)
                    {
                        continue;
                    }

                    if (!result.Add(other))
                        continue;

                    queue.Enqueue(other);
                }
            }
        }

        /// <summary>
        /// Face Loop:
        /// 1) constant-radius seed lines (no radials/zigzag);
        /// 2) Edge Loop each seed chain → cache both rings;
        /// 3) select polygons that touch both rings (belt between them).
        /// </summary>
        public static void ApplyLoopSelectSpokes(IndieEditableMeshData data)
        {
            if (data == null || data.edges.Count == 0)
                return;

            var seedFaces = new List<int>(data.selectedFaces);

            data.selectedFaces.Clear();
            data.selectedVertices.Clear();
            data.selectedEdges.Clear();

            if (seedFaces.Count < StripMinFaces)
                return;

            var selectedSet = new HashSet<int>(seedFaces);

            BuildSelectionEdgeMaps(
                data,
                selectedSet,
                out _,
                out var edgeToFaces,
                out var perimeter,
                out _
            );

            if (perimeter.Count == 0)
                return;

            Vector3 axisXZ = ComputeMeshCentroidXZ(data);

            if (!TryGetSelectionRadiusRange(
                    data,
                    selectedSet,
                    axisXZ,
                    out float minR,
                    out float maxR))
            {
                Debug.LogWarning("EditMesh Face Loop: selection has no radial width.");
                return;
            }

            float stripWidth = Mathf.Max(1e-6f, maxR - minR);
            float t = Mathf.Clamp(IndieEditMeshFaceParams.LoopAngle, 0.1f, 90f) / 90f;
            float maxSpanRatio = Mathf.Clamp(1f - t, 0.02f, 0.95f);
            float maxSpan = stripWidth * maxSpanRatio;

            var ringLines = new HashSet<int>();

            AddConstantRadiusEdges(data, perimeter, axisXZ, maxSpan, ringLines);
            AddConstantRadiusEdges(
                data,
                CollectSharedSelectedEdgeIndices(data, selectedSet, edgeToFaces),
                axisXZ,
                maxSpan,
                ringLines
            );

            if (ringLines.Count == 0)
            {
                Debug.LogWarning(
                    "EditMesh Face Loop: no ring seed lines after dropping radials/zigzag."
                );
                return;
            }

            var seedChains = SplitEdgeChains(data, ringLines);
            var cachedLoops = new HashSet<int>();

            for (int c = 0; c < seedChains.Count; c++)
            {
                if (!TryPickEdgeLoopSeedPair(
                        data,
                        seedChains[c],
                        axisXZ,
                        maxSpan,
                        out int seedA,
                        out int seedB))
                {
                    Debug.LogWarning(
                        $"EditMesh Face Loop: no Edge Loop seed pair for chain {c} " +
                        $"({seedChains[c].Count} edges)."
                    );
                    continue;
                }

                data.selectedEdges = new List<int> { seedA, seedB };
                data.selectedVertices.Clear();
                data.selectedFaces.Clear();

                IndieEditMeshEdgeOps.ApplyLoop(data);

                for (int i = 0; i < data.selectedEdges.Count; i++)
                    cachedLoops.Add(data.selectedEdges[i]);
            }

            if (cachedLoops.Count == 0)
            {
                data.selectedEdges = new List<int>(ringLines);
                Debug.LogWarning("EditMesh Face Loop: Edge Loop expand failed — seeds only.");
                return;
            }

            // Drop Edge Loop "ear" spokes before splitting rings.
            var cleanLoops = new HashSet<int>();
            AddConstantRadiusEdges(
                data,
                new List<int>(cachedLoops),
                axisXZ,
                maxSpan,
                cleanLoops
            );

            if (cleanLoops.Count < 2)
                cleanLoops = cachedLoops;

            if (!TrySplitLoopsByRadius(
                    data,
                    cleanLoops,
                    axisXZ,
                    out var ringAEdges,
                    out var ringBEdges))
            {
                data.selectedEdges = new List<int>();
                data.selectedVertices = new List<int>();
                data.selectedFaces = new List<int>();
                Debug.LogWarning(
                    "EditMesh Face Loop: could not split two radius rings."
                );
                return;
            }

            var ringA = CollectEdgeVertices(data, ringAEdges);
            var ringB = CollectEdgeVertices(data, ringBEdges);
            var ringAKeys = CollectEdgeKeys(data, ringAEdges);
            var ringBKeys = CollectEdgeKeys(data, ringBEdges);
            Vector3 seedNormal = AverageFaceNormal(data, seedFaces);
            var beltFaces = CollectFacesTouchingBothRings(
                data,
                ringA,
                ringB,
                ringAKeys,
                ringBKeys,
                axisXZ,
                seedNormal
            );

            data.selectedEdges = new List<int>();
            data.selectedVertices = new List<int>();
            data.selectedFaces = beltFaces ?? new List<int>();

            if (data.selectedFaces.Count == 0)
            {
                Debug.LogWarning(
                    "EditMesh Face Loop: no belt polygons between the two rings " +
                    $"(A={ringA.Count} verts, B={ringB.Count} verts)."
                );
            }
        }

        /// <summary>
        /// Partition loop edges into inner/outer rings by midpoint radius.
        /// </summary>
        private static bool TrySplitLoopsByRadius(
            IndieEditableMeshData data,
            HashSet<int> loopEdges,
            Vector3 axisXZ,
            out List<int> ringA,
            out List<int> ringB
        )
        {
            ringA = new List<int>();
            ringB = new List<int>();

            if (loopEdges == null || loopEdges.Count == 0)
                return false;

            float minMid = float.MaxValue;
            float maxMid = float.MinValue;
            var mids = new List<(int edge, float midR)>(loopEdges.Count);

            foreach (int edgeIndex in loopEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                float r0 = RadiusXZ(data.vertices[edge.v0], axisXZ);
                float r1 = RadiusXZ(data.vertices[edge.v1], axisXZ);
                float midR = (r0 + r1) * 0.5f;
                mids.Add((edgeIndex, midR));
                minMid = Mathf.Min(minMid, midR);
                maxMid = Mathf.Max(maxMid, midR);
            }

            if (mids.Count == 0 || maxMid - minMid < 1e-6f)
                return false;

            float split = (minMid + maxMid) * 0.5f;

            for (int i = 0; i < mids.Count; i++)
            {
                if (mids[i].midR <= split)
                    ringA.Add(mids[i].edge);
                else
                    ringB.Add(mids[i].edge);
            }

            return ringA.Count > 0 && ringB.Count > 0;
        }

        private static HashSet<int> CollectEdgeVertices(
            IndieEditableMeshData data,
            List<int> edges
        )
        {
            var verts = new HashSet<int>();

            if (edges == null)
                return verts;

            for (int i = 0; i < edges.Count; i++)
            {
                int edgeIndex = edges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                verts.Add(edge.v0);
                verts.Add(edge.v1);
            }

            return verts;
        }

        private static HashSet<long> CollectEdgeKeys(
            IndieEditableMeshData data,
            List<int> edges
        )
        {
            var keys = new HashSet<long>();

            if (edges == null)
                return keys;

            for (int i = 0; i < edges.Count; i++)
            {
                int edgeIndex = edges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                keys.Add(IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1));
            }

            return keys;
        }

        private static float AverageVertexRadius(
            IndieEditableMeshData data,
            HashSet<int> verts,
            Vector3 axisXZ
        )
        {
            if (verts == null || verts.Count == 0)
                return 0f;

            float sum = 0f;
            int count = 0;

            foreach (int v in verts)
            {
                if (v < 0 || v >= data.vertices.Count)
                    continue;

                sum += RadiusXZ(data.vertices[v], axisXZ);
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private static float AverageVertexY(
            IndieEditableMeshData data,
            HashSet<int> verts
        )
        {
            if (verts == null || verts.Count == 0)
                return 0f;

            float sum = 0f;
            int count = 0;

            foreach (int v in verts)
            {
                if (v < 0 || v >= data.vertices.Count)
                    continue;

                sum += data.vertices[v].y;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        /// <summary>
        /// Belt polygons: touch both rings, own an edge of at least one ring,
        /// stay inside the radius/height band, same-side normal as seeds.
        /// </summary>
        private static List<int> CollectFacesTouchingBothRings(
            IndieEditableMeshData data,
            HashSet<int> ringA,
            HashSet<int> ringB,
            HashSet<long> ringAKeys,
            HashSet<long> ringBKeys,
            Vector3 axisXZ,
            Vector3 seedNormal
        )
        {
            var result = new List<int>();

            if (ringA == null || ringB == null || ringA.Count == 0 || ringB.Count == 0)
                return result;

            float rA = AverageVertexRadius(data, ringA, axisXZ);
            float rB = AverageVertexRadius(data, ringB, axisXZ);
            float rLo = Mathf.Min(rA, rB);
            float rHi = Mathf.Max(rA, rB);
            float bandPad = Mathf.Max(1e-5f, (rHi - rLo) * 0.08f);
            float rMin = rLo - bandPad;
            float rMax = rHi + bandPad;

            float yA = AverageVertexY(data, ringA);
            float yB = AverageVertexY(data, ringB);
            float yLo = Mathf.Min(yA, yB);
            float yHi = Mathf.Max(yA, yB);
            float yPad = Mathf.Max(1e-5f, (yHi - yLo) * 0.35f + (rHi - rLo) * 0.05f);
            float yMin = yLo - yPad;
            float yMax = yHi + yPad;

            if (seedNormal.sqrMagnitude > 1e-12f)
                seedNormal.Normalize();
            else
                seedNormal = Vector3.up;

            const float minNormalDot = 0.35f;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                bool touchA =
                    ringA.Contains(face.a) || ringA.Contains(face.b) || ringA.Contains(face.c);
                bool touchB =
                    ringB.Contains(face.a) || ringB.Contains(face.b) || ringB.Contains(face.c);

                if (!touchA || !touchB)
                    continue;

                long ab = IndieEditMeshMeshUtil.EdgeKey(face.a, face.b);
                long bc = IndieEditMeshMeshUtil.EdgeKey(face.b, face.c);
                long ca = IndieEditMeshMeshUtil.EdgeKey(face.c, face.a);
                bool onLoop =
                    ringAKeys.Contains(ab) || ringAKeys.Contains(bc) || ringAKeys.Contains(ca) ||
                    ringBKeys.Contains(ab) || ringBKeys.Contains(bc) || ringBKeys.Contains(ca);

                // Must actually use a loop edge — not only share a leaked vertex.
                if (!onLoop)
                    continue;

                Vector3 pa = data.vertices[face.a];
                Vector3 pb = data.vertices[face.b];
                Vector3 pc = data.vertices[face.c];
                float ra = RadiusXZ(pa, axisXZ);
                float rb = RadiusXZ(pb, axisXZ);
                float rc = RadiusXZ(pc, axisXZ);

                if (ra < rMin || ra > rMax ||
                    rb < rMin || rb > rMax ||
                    rc < rMin || rc > rMax)
                {
                    continue;
                }

                float cy = (pa.y + pb.y + pc.y) * (1f / 3f);

                if (cy < yMin || cy > yMax)
                    continue;

                float centroidR = (ra + rb + rc) * (1f / 3f);

                if (centroidR < rLo + bandPad * 0.5f || centroidR > rHi - bandPad * 0.5f)
                    continue;

                Vector3 n = FaceNormal(data, face);

                if (Vector3.Dot(n, seedNormal) < minNormalDot)
                    continue;

                result.Add(i);
            }

            return result;
        }

        private static List<List<int>> SplitEdgeChains(
            IndieEditableMeshData data,
            HashSet<int> edges
        )
        {
            var chains = new List<List<int>>();
            var remaining = new HashSet<int>(edges);

            while (remaining.Count > 0)
            {
                int start = -1;

                foreach (int edgeIndex in remaining)
                {
                    start = edgeIndex;
                    break;
                }

                var chain = new List<int>();
                GrowEdgeChain(data, remaining, start, chain);

                if (chain.Count > 0)
                    chains.Add(chain);
                else
                    remaining.Remove(start);
            }

            return chains;
        }

        /// <summary>
        /// Two adjacent edges for <see cref="IndieEditMeshEdgeOps.ApplyLoop"/> —
        /// from the seed chain, or a same-ring mesh neighbor of a lone seed.
        /// </summary>
        private static bool TryPickEdgeLoopSeedPair(
            IndieEditableMeshData data,
            List<int> chain,
            Vector3 axisXZ,
            float maxSpan,
            out int seedA,
            out int seedB
        )
        {
            seedA = seedB = -1;

            if (chain == null || chain.Count == 0)
                return false;

            if (TryPickAdjacentLoopSeed(data, chain, out seedA, out seedB))
                return true;

            // Lone seed edge (typical: one edge on inner, one on outer).
            return TryFindSameRingNeighbor(
                data,
                chain[0],
                axisXZ,
                maxSpan,
                out seedA,
                out seedB
            );
        }

        private static bool TryFindSameRingNeighbor(
            IndieEditableMeshData data,
            int edgeIndex,
            Vector3 axisXZ,
            float maxSpan,
            out int seedA,
            out int seedB
        )
        {
            seedA = edgeIndex;
            seedB = -1;

            if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                return false;

            var edge = data.edges[edgeIndex];

            if (!TryGetEdgeDirAndLength(data, edgeIndex, out Vector3 dirA, out _))
                return false;

            float bestDot = -1f;

            for (int i = 0; i < data.edges.Count; i++)
            {
                if (i == edgeIndex)
                    continue;

                var other = data.edges[i];

                if (!EdgesShareVertex(edge, other, out _))
                    continue;

                float r0 = RadiusXZ(data.vertices[other.v0], axisXZ);
                float r1 = RadiusXZ(data.vertices[other.v1], axisXZ);

                // Stay on the same ring — skip spokes / zigzag.
                if (Mathf.Abs(r0 - r1) > maxSpan)
                    continue;

                if (!TryGetEdgeDirAndLength(data, i, out Vector3 dirB, out _))
                    continue;

                // Prefer the straightest continuation around the ring.
                float absDot = Mathf.Abs(Vector3.Dot(dirA, dirB));

                if (absDot <= bestDot)
                    continue;

                bestDot = absDot;
                seedB = i;
            }

            return seedB >= 0;
        }

        private static void AddConstantRadiusEdges(
            IndieEditableMeshData data,
            List<int> candidates,
            Vector3 axisXZ,
            float maxSpan,
            HashSet<int> dst
        )
        {
            if (candidates == null)
                return;

            for (int i = 0; i < candidates.Count; i++)
            {
                int edgeIndex = candidates[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                float r0 = RadiusXZ(data.vertices[edge.v0], axisXZ);
                float r1 = RadiusXZ(data.vertices[edge.v1], axisXZ);

                // Radial / zigzag: ends on different rings. Drop.
                if (Mathf.Abs(r0 - r1) > maxSpan)
                    continue;

                dst.Add(edgeIndex);
            }
        }

        private static List<int> CollectSharedSelectedEdgeIndices(
            IndieEditableMeshData data,
            HashSet<int> selectedFaces,
            Dictionary<long, List<int>> edgeToFaces
        )
        {
            var result = new List<int>();

            if (selectedFaces == null || edgeToFaces == null)
                return result;

            var keyToEdge = new Dictionary<long, int>(data.edges.Count);

            for (int i = 0; i < data.edges.Count; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(data.edges[i].v0, data.edges[i].v1);

                if (!keyToEdge.ContainsKey(key))
                    keyToEdge.Add(key, i);
            }

            var seen = new HashSet<int>();

            foreach (var pair in edgeToFaces)
            {
                int selectedCount = 0;

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (selectedFaces.Contains(pair.Value[i]))
                        selectedCount++;
                }

                if (selectedCount < 2)
                    continue;

                if (!keyToEdge.TryGetValue(pair.Key, out int edgeIndex))
                    continue;

                if (!seen.Add(edgeIndex))
                    continue;

                result.Add(edgeIndex);
            }

            return result;
        }

        /// <summary>
        /// Legacy name — redirects to spoke selection (Loop step 1).
        /// </summary>
        public static void ApplyLoopPerimeterToInnerBelt(
            IndieEditableMeshData data,
            float angleDegrees
        )
        {
            ApplyLoopSelectSpokes(data);
        }

        /// <summary>
        /// Full face loop (Edge Loop → faces). Kept for later; Loop() uses
        /// <see cref="ApplyLoopPerimeterToInnerBelt"/> for now.
        /// </summary>
        public static void ApplyLoop(IndieEditableMeshData data)
        {
            if (data == null || !CanLoop(data) || data.edges.Count == 0)
                return;

            var seedFaces = new List<int>(data.selectedFaces);
            Vector3 seedNormal = AverageFaceNormal(data, seedFaces);
            var selectedSet = new HashSet<int>(seedFaces);
            Vector3 selectionCentroid = ComputeFacesCentroid(data, seedFaces);

            BuildSelectionEdgeMaps(
                data,
                selectedSet,
                out _,
                out var edgeToFaces,
                out var perimeter,
                out _
            );

            if (!TryCollectLoopDirectionEdges(
                    data,
                    perimeter,
                    selectedSet,
                    selectionCentroid,
                    IndieEditMeshFaceParams.LoopAngle,
                    out var directionEdges))
            {
                return;
            }

            if (!TryPickAdjacentLoopSeed(
                    data,
                    directionEdges,
                    out int seedA,
                    out int seedB))
            {
                return;
            }

            data.selectedEdges = new List<int> { seedA, seedB };
            data.selectedFaces.Clear();
            data.selectedVertices.Clear();

            var edgesBeforeLoop = data.selectedEdges;
            IndieEditMeshEdgeOps.ApplyLoop(data);

            if (ReferenceEquals(data.selectedEdges, edgesBeforeLoop))
            {
                data.selectedFaces = seedFaces;
                data.selectedEdges.Clear();
                return;
            }

            data.selectedFaces = CollectBeltFacesFromEdges(
                data,
                edgeToFaces,
                data.selectedEdges,
                seedNormal
            );
            data.selectedEdges.Clear();
            data.selectedVertices.Clear();
        }

        /// <summary>
        /// Face ring:
        /// 1) yellow direction edges from the face selection;
        /// 2) Edge Ring with angle threshold;
        /// 3) select polygons on the seed side of those edges.
        /// </summary>
        public static void ApplyRing(IndieEditableMeshData data, float angleDegrees)
        {
            if (data == null || !CanRing(data) || data.edges.Count == 0)
                return;

            var seedFaces = new List<int>(data.selectedFaces);
            Vector3 seedNormal = AverageFaceNormal(data, seedFaces);
            var selectedSet = new HashSet<int>(seedFaces);

            BuildSelectionEdgeMaps(
                data,
                selectedSet,
                out _,
                out var edgeToFaces,
                out var perimeter,
                out var internalSeams
            );

            if (!TryCollectRingDirectionEdges(
                    data,
                    perimeter,
                    internalSeams,
                    out var directionEdges))
            {
                return;
            }

            data.selectedEdges = directionEdges;
            data.selectedFaces.Clear();
            data.selectedVertices.Clear();

            var edgesBeforeRing = data.selectedEdges;
            IndieEditMeshEdgeOps.ApplyRing(data, angleDegrees);

            // ApplyRing reassigns selectedEdges only on success.
            if (ReferenceEquals(data.selectedEdges, edgesBeforeRing))
            {
                data.selectedFaces = seedFaces;
                data.selectedEdges.Clear();
                return;
            }

            data.selectedFaces = CollectBeltFacesFromEdges(
                data,
                edgeToFaces,
                data.selectedEdges,
                seedNormal
            );
            data.selectedEdges.Clear();
            data.selectedVertices.Clear();
        }

        /// <summary>
        /// Yellow ring-direction edges: perimeter + internal seams parallel to the
        /// dominant internal axis (drops top/bottom horizontals).
        /// </summary>
        private static bool TryCollectRingDirectionEdges(
            IndieEditableMeshData data,
            List<int> perimeter,
            List<int> internalSeams,
            out List<int> directionEdges
        )
        {
            return TryCollectDirectionEdgesParallelToAxis(
                data,
                perimeter,
                internalSeams,
                preferParallelToDominant: true,
                out directionEdges
            );
        }

        /// <summary>
        /// Loop edges stay on one ring (small |Δradius|). Radial spokes span the
        /// belt width (large |Δradius|) — drop them, then keep the inner chain.
        /// </summary>
        private static bool TryCollectLoopDirectionEdges(
            IndieEditableMeshData data,
            List<int> perimeter,
            HashSet<int> selectedFaces,
            Vector3 selectionCentroid,
            float angleDegrees,
            out List<int> directionEdges
        )
        {
            directionEdges = new List<int>();

            if (perimeter == null || perimeter.Count < 2)
                return false;

            Vector3 axisXZ = ComputeMeshCentroidXZ(data);
            if (axisXZ.sqrMagnitude < 1e-12f)
            {
                axisXZ = selectionCentroid;
                axisXZ.y = 0f;
            }

            if (!TryGetSelectionRadiusRange(
                    data,
                    selectedFaces,
                    axisXZ,
                    out float minR,
                    out float maxR))
            {
                return false;
            }

            float stripWidth = Mathf.Max(1e-6f, maxR - minR);

            // Angle → max allowed |Δr| / stripWidth.
            // 60° → ~0.5 (half belt); 90° → only equal-radius; 0° → keep all.
            float t = Mathf.Clamp(angleDegrees, 0.1f, 90f) / 90f;
            float maxSpanRatio = Mathf.Clamp(1f - t, 0.02f, 1f);
            float maxSpan = stripWidth * maxSpanRatio;

            var ringEdges = new List<int>(perimeter.Count);

            for (int i = 0; i < perimeter.Count; i++)
            {
                int edgeIndex = perimeter[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                float r0 = RadiusXZ(data.vertices[edge.v0], axisXZ);
                float r1 = RadiusXZ(data.vertices[edge.v1], axisXZ);

                // Spoke: ends on different rings. Loop edge: same ring.
                if (Mathf.Abs(r0 - r1) > maxSpan)
                    continue;

                ringEdges.Add(edgeIndex);
            }

            if (ringEdges.Count < 2)
                return false;

            directionEdges = PickInnerEdgeChain(data, ringEdges, axisXZ);
            return directionEdges.Count >= 2;
        }

        private static Vector3 ComputeMeshCentroidXZ(IndieEditableMeshData data)
        {
            if (data == null || data.vertices.Count == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;

            for (int i = 0; i < data.vertices.Count; i++)
                sum += data.vertices[i];

            sum /= data.vertices.Count;
            sum.y = 0f;
            return sum;
        }

        private static float RadiusXZ(Vector3 point, Vector3 axisXZ)
        {
            point.y = 0f;
            return (point - axisXZ).magnitude;
        }

        private static bool TryGetSelectionRadiusRange(
            IndieEditableMeshData data,
            HashSet<int> selectedFaces,
            Vector3 axisXZ,
            out float minR,
            out float maxR
        )
        {
            minR = float.MaxValue;
            maxR = float.MinValue;

            if (selectedFaces == null || selectedFaces.Count == 0)
                return false;

            foreach (int faceIndex in selectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                float ra = RadiusXZ(data.vertices[face.a], axisXZ);
                float rb = RadiusXZ(data.vertices[face.b], axisXZ);
                float rc = RadiusXZ(data.vertices[face.c], axisXZ);

                minR = Mathf.Min(minR, Mathf.Min(ra, Mathf.Min(rb, rc)));
                maxR = Mathf.Max(maxR, Mathf.Max(ra, Mathf.Max(rb, rc)));
            }

            return maxR > minR + 1e-8f;
        }

        /// <summary>
        /// Split into chains; pick the inner ring (smaller cylindrical radius).
        /// </summary>
        private static List<int> PickInnerEdgeChain(
            IndieEditableMeshData data,
            List<int> edges,
            Vector3 axisXZ
        )
        {
            if (edges == null || edges.Count == 0)
                return new List<int>();

            var remaining = new HashSet<int>(edges);
            var chains = new List<List<int>>();

            while (remaining.Count > 0)
            {
                int start = -1;

                foreach (int edgeIndex in remaining)
                {
                    start = edgeIndex;
                    break;
                }

                var chain = new List<int>();
                GrowEdgeChain(data, remaining, start, chain);

                if (chain.Count > 0)
                    chains.Add(chain);
                else
                    remaining.Remove(start);
            }

            if (chains.Count == 0)
                return new List<int>(edges);

            if (chains.Count == 1)
                return chains[0];

            Vector3 axis = axisXZ;
            axis.y = 0f;

            int best = 0;
            float bestRadius = float.MaxValue;

            for (int c = 0; c < chains.Count; c++)
            {
                float radius = AverageCylindricalRadius(data, chains[c], axis);

                if (radius >= bestRadius)
                    continue;

                bestRadius = radius;
                best = c;
            }

            return chains[best];
        }

        private static float AverageCylindricalRadius(
            IndieEditableMeshData data,
            List<int> edges,
            Vector3 axisXZ
        )
        {
            float sum = 0f;
            int count = 0;

            for (int i = 0; i < edges.Count; i++)
            {
                int edgeIndex = edges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                Vector3 mid = (data.vertices[edge.v0] + data.vertices[edge.v1]) * 0.5f;
                mid.y = 0f;
                sum += (mid - axisXZ).magnitude;
                count++;
            }

            return count > 0 ? sum / count : float.MaxValue;
        }

        private static void GrowEdgeChain(
            IndieEditableMeshData data,
            HashSet<int> remaining,
            int start,
            List<int> chain
        )
        {
            if (!remaining.Remove(start))
                return;

            chain.Add(start);

            var vertEdges = new Dictionary<int, List<int>>();

            foreach (int edgeIndex in remaining)
            {
                var edge = data.edges[edgeIndex];
                AddVertEdge(vertEdges, edge.v0, edgeIndex);
                AddVertEdge(vertEdges, edge.v1, edgeIndex);
            }

            // Also index the start edge endpoints against remaining neighbors.
            {
                var edge = data.edges[start];
                // Seeds for BFS from both ends.
                var queue = new Queue<int>();
                queue.Enqueue(edge.v0);
                queue.Enqueue(edge.v1);
                var visitedVerts = new HashSet<int> { edge.v0, edge.v1 };

                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();

                    if (!vertEdges.TryGetValue(v, out var atVert))
                        continue;

                    for (int i = 0; i < atVert.Count; i++)
                    {
                        int next = atVert[i];

                        if (!remaining.Remove(next))
                            continue;

                        chain.Add(next);
                        var ne = data.edges[next];
                        int other = ne.v0 == v ? ne.v1 : ne.v0;

                        if (visitedVerts.Add(other))
                            queue.Enqueue(other);
                    }
                }
            }
        }

        private static void AddVertEdge(
            Dictionary<int, List<int>> vertEdges,
            int vertex,
            int edgeIndex
        )
        {
            if (!vertEdges.TryGetValue(vertex, out var list))
            {
                list = new List<int>(2);
                vertEdges.Add(vertex, list);
            }

            if (!list.Contains(edgeIndex))
                list.Add(edgeIndex);
        }

        private static Vector3 ComputeFacesCentroid(
            IndieEditableMeshData data,
            List<int> faces
        )
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < faces.Count; i++)
            {
                int fi = faces[i];

                if (fi < 0 || fi >= data.faces.Count)
                    continue;

                var face = data.faces[fi];
                sum += (data.vertices[face.a] + data.vertices[face.b] +
                        data.vertices[face.c]) * (1f / 3f);
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        private static bool TryCollectDirectionEdgesParallelToAxis(
            IndieEditableMeshData data,
            List<int> perimeter,
            List<int> internalSeams,
            bool preferParallelToDominant,
            out List<int> directionEdges
        )
        {
            directionEdges = new List<int>();

            if (perimeter == null || perimeter.Count < 2)
                return false;

            if (!TryGetDominantEdgeAxis(data, internalSeams, out Vector3 dominantAxis) &&
                !TryGetDominantEdgeAxis(data, perimeter, out dominantAxis))
            {
                return false;
            }

            const float parallelDegrees = 25f;
            float cosParallel = Mathf.Cos(parallelDegrees * Mathf.Deg2Rad);
            // Orthogonal enough to dominant (loop) / parallel to dominant (ring).
            float cosOrthogonalMax = Mathf.Sin(parallelDegrees * Mathf.Deg2Rad);
            var seen = new HashSet<int>();
            var collected = new List<int>();

            for (int pass = 0; pass < 2; pass++)
            {
                var source = pass == 0 ? perimeter : internalSeams;

                for (int i = 0; i < source.Count; i++)
                {
                    int edgeIndex = source[i];

                    if (!TryGetEdgeDirAndLength(data, edgeIndex, out Vector3 dir, out _))
                        continue;

                    float absDot = Mathf.Abs(Vector3.Dot(dir, dominantAxis));
                    bool keep = preferParallelToDominant
                        ? absDot >= cosParallel
                        : absDot <= cosOrthogonalMax;

                    if (!keep)
                        continue;

                    if (!seen.Add(edgeIndex))
                        continue;

                    collected.Add(edgeIndex);
                }
            }

            directionEdges = collected;
            return directionEdges.Count >= 2;
        }

        /// <summary>
        /// Two adjacent loop-direction edges that share a vertex (Edge Loop seed).
        /// </summary>
        private static bool TryPickAdjacentLoopSeed(
            IndieEditableMeshData data,
            List<int> directionEdges,
            out int seedA,
            out int seedB
        )
        {
            seedA = seedB = -1;
            float bestScore = float.MinValue;

            for (int i = 0; i < directionEdges.Count; i++)
            {
                int ea = directionEdges[i];
                var edgeA = data.edges[ea];

                if (!TryGetEdgeDirAndLength(data, ea, out Vector3 dirA, out float lenA))
                    continue;

                for (int j = i + 1; j < directionEdges.Count; j++)
                {
                    int eb = directionEdges[j];
                    var edgeB = data.edges[eb];

                    if (!EdgesShareVertex(edgeA, edgeB, out _))
                        continue;

                    if (!TryGetEdgeDirAndLength(data, eb, out Vector3 dirB, out float lenB))
                        continue;

                    // Prefer a continuing path (not folding back).
                    float turn = Mathf.Abs(Vector3.Dot(dirA, dirB));
                    float score = lenA + lenB + turn;

                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    seedA = ea;
                    seedB = eb;
                }
            }

            return seedA >= 0 && seedB >= 0;
        }

        private static bool EdgesShareVertex(IndieEditMeshEdge a, IndieEditMeshEdge b, out int shared)
        {
            if (IndieEditMeshMeshUtil.EdgeKey(a.v0, a.v1) == IndieEditMeshMeshUtil.EdgeKey(b.v0, b.v1))
            {
                shared = -1;
                return false;
            }

            if (a.v0 == b.v0 || a.v0 == b.v1)
            {
                shared = a.v0;
                return true;
            }

            if (a.v1 == b.v0 || a.v1 == b.v1)
            {
                shared = a.v1;
                return true;
            }

            shared = -1;
            return false;
        }

        /// <summary>
        /// Only polygons that use a full ring edge (both endpoints). Fig.1 keep,
        /// Fig.2 drop. Creases keep the seed side; coplanar partners only if they
        /// also sit on a full ring edge.
        /// </summary>
        private static List<int> CollectBeltFacesFromEdges(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            List<int> grownEdges,
            Vector3 seedNormal
        )
        {
            var result = new HashSet<int>();
            var ringEdgeKeys = new HashSet<long>();

            if (seedNormal.sqrMagnitude < 1e-12f)
                seedNormal = Vector3.up;
            else
                seedNormal.Normalize();

            const float creaseDot = 0.35f;

            for (int i = 0; i < grownEdges.Count; i++)
            {
                int edgeIndex = grownEdges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);
                ringEdgeKeys.Add(key);

                if (!edgeToFaces.TryGetValue(key, out var faces) || faces.Count == 0)
                    continue;

                var candidates = new List<int>(2);

                for (int f = 0; f < faces.Count; f++)
                {
                    int faceIndex = faces[f];

                    // Both edge endpoints must be vertices of the polygon (fig.1).
                    if (!FaceContainsFullEdge(data.faces[faceIndex], edge.v0, edge.v1))
                        continue;

                    candidates.Add(faceIndex);
                }

                if (candidates.Count == 0)
                    continue;

                if (candidates.Count == 1)
                {
                    result.Add(candidates[0]);
                    continue;
                }

                int f0 = candidates[0];
                int f1 = candidates[1];
                Vector3 n0 = FaceNormal(data, data.faces[f0]);
                Vector3 n1 = FaceNormal(data, data.faces[f1]);

                if (Vector3.Dot(n0, n1) >= creaseDot)
                {
                    result.Add(f0);
                    result.Add(f1);
                    continue;
                }

                float d0 = Vector3.Dot(n0, seedNormal);
                float d1 = Vector3.Dot(n1, seedNormal);
                result.Add(d0 >= d1 ? f0 : f1);
            }

            // Other half of a triangulated panel — only if it also owns a ring edge.
            foreach (int faceIndex in new List<int>(result))
            {
                TryAddCoplanarPartnerOnRing(
                    data,
                    edgeToFaces,
                    ringEdgeKeys,
                    faceIndex,
                    result
                );
            }

            return new List<int>(result);
        }

        private static bool FaceContainsFullEdge(IndieEditMeshFace face, int v0, int v1)
        {
            bool has0 = face.a == v0 || face.b == v0 || face.c == v0;
            bool has1 = face.a == v1 || face.b == v1 || face.c == v1;
            return has0 && has1;
        }

        private static bool FaceContainsAnyFullRingEdge(
            IndieEditMeshFace face,
            HashSet<long> ringEdgeKeys
        )
        {
            return ringEdgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.a, face.b)) ||
                   ringEdgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.b, face.c)) ||
                   ringEdgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.c, face.a));
        }

        private static void TryAddCoplanarPartnerOnRing(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<long> ringEdgeKeys,
            int faceIndex,
            HashSet<int> panel
        )
        {
            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return;

            var face = data.faces[faceIndex];
            Vector3 n0 = FaceNormal(data, face);
            var edges = new[]
            {
                (face.a, face.b),
                (face.b, face.c),
                (face.c, face.a)
            };

            for (int i = 0; i < edges.Length; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(edges[i].Item1, edges[i].Item2);

                if (!edgeToFaces.TryGetValue(key, out var faces) || faces.Count < 2)
                    continue;

                int other = faces[0] == faceIndex ? faces[1] : faces[0];

                if (other == faceIndex || panel.Contains(other))
                    continue;

                if (Vector3.Dot(n0, FaceNormal(data, data.faces[other])) < 0.9f)
                    continue;

                // Partner must also use a full ring edge (not a partial T-touch).
                if (!FaceContainsAnyFullRingEdge(data.faces[other], ringEdgeKeys))
                    continue;

                panel.Add(other);
            }
        }

        private static void BuildSelectionEdgeMaps(
            IndieEditableMeshData data,
            HashSet<int> selectedFaces,
            out Dictionary<long, int> keyToEdge,
            out Dictionary<long, List<int>> edgeToFaces,
            out List<int> perimeter,
            out List<int> internalSeams
        )
        {
            keyToEdge = new Dictionary<long, int>(data.edges.Count);
            edgeToFaces = new Dictionary<long, List<int>>();
            perimeter = new List<int>();
            internalSeams = new List<int>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(data.edges[i].v0, data.edges[i].v1);

                if (!keyToEdge.ContainsKey(key))
                    keyToEdge.Add(key, i);
            }

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.a, face.b), i);
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.b, face.c), i);
                AddEdgeFace(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.c, face.a), i);
            }

            var seenPerimeter = new HashSet<int>();
            var seenInternal = new HashSet<int>();

            foreach (var pair in edgeToFaces)
            {
                int selectedCount = 0;
                int f0 = -1;
                int f1 = -1;

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int faceIndex = pair.Value[i];

                    if (!selectedFaces.Contains(faceIndex))
                        continue;

                    selectedCount++;

                    if (f0 < 0)
                        f0 = faceIndex;
                    else if (f1 < 0)
                        f1 = faceIndex;
                }

                if (!keyToEdge.TryGetValue(pair.Key, out int edgeIndex))
                    continue;

                if (selectedCount == 1)
                {
                    if (seenPerimeter.Add(edgeIndex))
                        perimeter.Add(edgeIndex);
                }
                else if (selectedCount >= 2 && f0 >= 0 && f1 >= 0)
                {
                    // Skip triangulated-panel diagonals (nearly coplanar tris).
                    Vector3 n0 = FaceNormal(data, data.faces[f0]);
                    Vector3 n1 = FaceNormal(data, data.faces[f1]);

                    if (Vector3.Dot(n0, n1) >= 0.9f)
                        continue;

                    if (seenInternal.Add(edgeIndex))
                        internalSeams.Add(edgeIndex);
                }
            }
        }

        private static bool TryGetDominantEdgeAxis(
            IndieEditableMeshData data,
            List<int> edges,
            out Vector3 axis
        )
        {
            axis = Vector3.zero;

            if (edges == null || edges.Count == 0)
                return false;

            // Greedy cluster by direction; weight by edge length.
            var axes = new List<Vector3>();
            var weights = new List<float>();

            for (int i = 0; i < edges.Count; i++)
            {
                if (!TryGetEdgeDirAndLength(data, edges[i], out Vector3 dir, out float length))
                    continue;

                int best = -1;
                float bestAbsDot = 0.85f; // ~32°

                for (int c = 0; c < axes.Count; c++)
                {
                    float absDot = Mathf.Abs(Vector3.Dot(dir, axes[c]));

                    if (absDot < bestAbsDot)
                        continue;

                    bestAbsDot = absDot;
                    best = c;
                }

                if (best < 0)
                {
                    axes.Add(dir);
                    weights.Add(length);
                    continue;
                }

                if (Vector3.Dot(dir, axes[best]) < 0f)
                    dir = -dir;

                Vector3 merged = axes[best] * weights[best] + dir * length;
                weights[best] += length;

                if (merged.sqrMagnitude > 1e-16f)
                    axes[best] = merged.normalized;
            }

            float bestWeight = -1f;
            int bestIndex = -1;

            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= bestWeight)
                    continue;

                bestWeight = weights[i];
                bestIndex = i;
            }

            if (bestIndex < 0)
                return false;

            axis = axes[bestIndex];
            return axis.sqrMagnitude > 1e-12f;
        }

        private static bool IsEdgeParallelToAxis(
            IndieEditableMeshData data,
            int edgeIndex,
            Vector3 axis,
            float cosParallel
        )
        {
            if (!TryGetEdgeDirAndLength(data, edgeIndex, out Vector3 dir, out _))
                return false;

            return Mathf.Abs(Vector3.Dot(dir, axis)) >= cosParallel;
        }

        private static bool TryGetEdgeDirAndLength(
            IndieEditableMeshData data,
            int edgeIndex,
            out Vector3 dir,
            out float length
        )
        {
            dir = Vector3.zero;
            length = 0f;

            if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                return false;

            var edge = data.edges[edgeIndex];
            Vector3 delta = data.vertices[edge.v1] - data.vertices[edge.v0];
            length = delta.magnitude;

            if (length < 1e-8f)
                return false;

            dir = delta / length;
            return true;
        }

        private static void AddEdgeFace(
            Dictionary<long, List<int>> edgeToFaces,
            long key,
            int faceIndex
        )
        {
            if (!edgeToFaces.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                edgeToFaces.Add(key, list);
            }

            if (!list.Contains(faceIndex))
                list.Add(faceIndex);
        }

        public static void ApplyExtrude(
            IndieEditableMeshData data,
            float height,
            IndieEditMeshFaceExtrudeType type
        )
        {
            if (data == null || data.selectedFaces.Count == 0)
                return;

            var selectedFaces = new List<int>(data.selectedFaces);

            if (type == IndieEditMeshFaceExtrudeType.ByPolygon)
            {
                selectedFaces.Sort((a, b) => b.CompareTo(a));

                foreach (int faceIndex in selectedFaces)
                    ExtrudeSingleFace(data, faceIndex, height);
            }
            else
            {
                var points = IndieEditMeshFacePointGroups.Build(data, selectedFaces);

                foreach (var group in points.GetGroups(selectedFaces))
                {
                    ExtrudeFaceGroup(
                        data,
                        points,
                        group,
                        height,
                        type == IndieEditMeshFaceExtrudeType.LocalNormal
                    );
                }
            }

            // Caps keep the same face indices; side walls are new faces.
            data.selectedFaces = new List<int>(selectedFaces);
            data.selectedVertices.Clear();
            data.selectedEdges.Clear();
            data.RebuildEdges();
        }

        public static void ApplyOutline(
            IndieEditableMeshData data,
            float amount,
            IndieEditMeshFaceExtrudeType type = IndieEditMeshFaceExtrudeType.Group
        )
        {
            if (data == null || data.selectedFaces.Count == 0)
                return;

            var selectedFaces = new List<int>(data.selectedFaces);
            var points = IndieEditMeshFacePointGroups.Build(data, selectedFaces);
            List<List<int>> groups;

            if (type == IndieEditMeshFaceExtrudeType.ByPolygon)
            {
                groups = new List<List<int>>(selectedFaces.Count);

                foreach (int faceIndex in selectedFaces)
                    groups.Add(new List<int> { faceIndex });
            }
            else
            {
                groups = points.GetGroups(selectedFaces);
            }

            foreach (var group in groups)
            {
                if (type == IndieEditMeshFaceExtrudeType.LocalNormal)
                    OutlineFaceGroupInPlanes(data, points, group, amount);
                else
                    OutlineFaceGroup(data, points, group, amount);
            }

            data.RebuildEdges();
        }

        public static void ApplyBevel(
            IndieEditableMeshData data,
            float height,
            float outline,
            IndieEditMeshFaceExtrudeType type
        )
        {
            if (data == null || data.selectedFaces.Count == 0)
                return;

            ApplyExtrude(data, height, type);
            ApplyOutline(data, outline, type);
        }

        public static void ApplyInset(
            IndieEditableMeshData data,
            float amount,
            IndieEditMeshFaceExtrudeType type
        )
        {
            if (data == null || data.selectedFaces.Count == 0)
                return;

            float inset = Mathf.Max(0f, amount);

            if (inset <= 1e-8f)
                return;

            if (type == IndieEditMeshFaceExtrudeType.ByPolygon)
            {
                var selected = new List<int>(data.selectedFaces);

                foreach (int faceIndex in selected)
                    InsetSingleFace(data, faceIndex, inset);
            }
            else
            {
                var groups = GetContiguousFaceGroups(data, data.selectedFaces);

                foreach (var group in groups)
                    InsetFaceGroup(data, group, inset);
            }

            data.RebuildEdges();
        }

        public static void ApplyBridge(
            IndieEditableMeshData data,
            int segments,
            int twist,
            float taper,
            float bias
        )
        {
            if (data == null || data.selectedFaces.Count < 2)
                return;

            var groups = GetContiguousFaceGroups(data, data.selectedFaces);

            if (groups.Count < 2)
            {
                // Fallback: treat first two selected faces as ends.
                if (data.selectedFaces.Count < 2)
                    return;

                groups = new List<List<int>>
                {
                    new List<int> { data.selectedFaces[0] },
                    new List<int> { data.selectedFaces[1] }
                };
            }

            segments = Mathf.Max(1, segments);

            for (int i = 0; i + 1 < groups.Count; i += 2)
            {
                BridgeFaceGroups(
                    data,
                    groups[i],
                    groups[i + 1],
                    segments,
                    twist,
                    taper,
                    bias
                );
            }

            data.RebuildEdges();
            data.ClearSelection();
        }

        private static void ExtrudeSingleFace(IndieEditableMeshData data, int faceIndex, float height)
        {
            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return;

            var face = data.faces[faceIndex];
            Vector3 normal = FaceNormal(data, face);

            int a2 = IndieEditMeshMeshUtil.AddVertex(
                data,
                face.a,
                data.vertices[face.a] + normal * height
            );
            int b2 = IndieEditMeshMeshUtil.AddVertex(
                data,
                face.b,
                data.vertices[face.b] + normal * height
            );
            int c2 = IndieEditMeshMeshUtil.AddVertex(
                data,
                face.c,
                data.vertices[face.c] + normal * height
            );

            int materialId = face.materialId;

            IndieEditMeshMeshUtil.AddQuad(data.faces, face.a, face.b, b2, a2, materialId);
            IndieEditMeshMeshUtil.AddQuad(data.faces, face.b, face.c, c2, b2, materialId);
            IndieEditMeshMeshUtil.AddQuad(data.faces, face.c, face.a, a2, c2, materialId);

            face.a = a2;
            face.b = b2;
            face.c = c2;
            data.faces[faceIndex] = face;
        }

        private static void ExtrudeFaceGroup(
            IndieEditableMeshData data,
            IndieEditMeshFacePointGroups points,
            List<int> group,
            float height,
            bool localNormal
        )
        {
            if (group == null || group.Count == 0)
                return;

            var verts = CollectVerts(data, group);
            var boundary = points.GetBoundary(group);
            Vector3 groupOffset = AverageFaceNormal(data, group) * height;
            var pointOffsets = localNormal ? GetLocalNormalOffsets(points, group, height) : null;
            var map = new Dictionary<int, int>();

            foreach (int vert in verts)
            {
                Vector3 offset = groupOffset;

                if (localNormal && !pointOffsets.TryGetValue(points.PointOf(vert), out offset))
                    offset = groupOffset;

                int copy = IndieEditMeshMeshUtil.AddVertex(
                    data,
                    vert,
                    data.vertices[vert] + offset
                );
                map[vert] = copy;
            }

            foreach (int faceIndex in group)
            {
                var face = data.faces[faceIndex];
                face.a = map[face.a];
                face.b = map[face.b];
                face.c = map[face.c];
                data.faces[faceIndex] = face;
            }

            int materialId = data.faces[group[0]].materialId;

            foreach (var edge in boundary)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    data.faces,
                    edge.From,
                    edge.To,
                    map[edge.To],
                    map[edge.From],
                    materialId
                );
            }
        }

        /// <summary>
        /// Each plane of the group moves by <paramref name="height"/> along its own normal;
        /// a point on a fold goes to where the offset planes meet, so caps stay parallel
        /// to the original faces and stay welded.
        /// </summary>
        private static Dictionary<int, Vector3> GetLocalNormalOffsets(
            IndieEditMeshFacePointGroups points,
            List<int> group,
            float height
        )
        {
            var planesByPoint = points.GetPointPlanes(group);
            var offsets = new Dictionary<int, Vector3>(planesByPoint.Count);

            foreach (var pair in planesByPoint)
            {
                var solver = new IndieEditMeshOffsetSolver();

                foreach (var normal in pair.Value)
                    solver.Add(normal, height);

                offsets.Add(pair.Key, solver.Solve(height * MaxOffsetScale));
            }

            return offsets;
        }

        /// <summary>
        /// Outline for Local Normal: a boundary point slides by <paramref name="amount"/> away
        /// from each boundary edge but never leaves the planes it lies on, so a cap that
        /// spans a fold keeps every normal.
        /// </summary>
        private static void OutlineFaceGroupInPlanes(
            IndieEditableMeshData data,
            IndieEditMeshFacePointGroups points,
            List<int> group,
            float amount
        )
        {
            if (group == null || group.Count == 0 || Mathf.Abs(amount) < 1e-8f)
                return;

            var boundary = points.GetBoundary(group);

            if (boundary.Count == 0)
                return;

            var planesByPoint = points.GetPointPlanes(group);
            var solvers = new Dictionary<int, IndieEditMeshOffsetSolver>();

            foreach (var edge in boundary)
            {
                if (!TryGetOutlinePerp(data, edge, out Vector3 perp))
                    continue;

                AddPerp(points.PointOf(edge.From), perp);
                AddPerp(points.PointOf(edge.To), perp);
            }

            var offsets = new Dictionary<int, Vector3>(solvers.Count);

            foreach (var pair in solvers)
            {
                var solver = pair.Value;

                if (planesByPoint.TryGetValue(pair.Key, out var planes))
                {
                    foreach (var normal in planes)
                        solver.Add(normal, 0f, PlaneKeepWeight);
                }

                offsets.Add(pair.Key, solver.Solve(amount * MaxOffsetScale));
            }

            ApplyPointOffsets(data, points, offsets);

            void AddPerp(int point, Vector3 dir)
            {
                solvers.TryGetValue(point, out var solver);
                solver.Add(dir, amount);
                solvers[point] = solver;
            }
        }

        /// <summary>Unit vector in the owner face's plane, perpendicular to the edge, pointing out.</summary>
        private static bool TryGetOutlinePerp(
            IndieEditableMeshData data,
            IndieEditMeshFacePointGroups.BoundaryEdge edge,
            out Vector3 perp
        )
        {
            var face = data.faces[edge.Face];
            Vector3 p0 = data.vertices[edge.From];
            Vector3 p1 = data.vertices[edge.To];
            perp = Vector3.Cross(FaceNormal(data, face), p1 - p0);

            if (perp.sqrMagnitude < 1e-12f)
                return false;

            perp.Normalize();

            Vector3 faceCenter =
                (data.vertices[face.a] + data.vertices[face.b] + data.vertices[face.c]) / 3f;

            if (Vector3.Dot(perp, (p0 + p1) * 0.5f - faceCenter) < 0f)
                perp = -perp;

            return true;
        }

        private static void OutlineFaceGroup(
            IndieEditableMeshData data,
            IndieEditMeshFacePointGroups points,
            List<int> group,
            float amount
        )
        {
            if (group == null || group.Count == 0 || Mathf.Abs(amount) < 1e-8f)
                return;

            var boundary = points.GetBoundary(group);

            if (boundary.Count == 0)
                return;

            // Perpendicular lies in the owner face's plane, so a ring or a wall around a
            // corner widens along its own surface instead of one averaged plane.
            var outwardByPoint = new Dictionary<int, Vector3>();

            foreach (var edge in boundary)
            {
                if (!TryGetOutlinePerp(data, edge, out Vector3 perp))
                    continue;

                AddOutward(points.PointOf(edge.From), perp);
                AddOutward(points.PointOf(edge.To), perp);
            }

            var offsets = new Dictionary<int, Vector3>(outwardByPoint.Count);

            foreach (var pair in outwardByPoint)
            {
                if (pair.Value.sqrMagnitude < 1e-12f)
                    continue;

                offsets.Add(pair.Key, pair.Value.normalized * amount);
            }

            ApplyPointOffsets(data, points, offsets);

            void AddOutward(int point, Vector3 dir)
            {
                outwardByPoint.TryGetValue(point, out Vector3 sum);
                outwardByPoint[point] = sum + dir;
            }
        }

        /// <summary>
        /// Slides every vertex that sits on a moved point, including stacked copies on
        /// neighbouring faces. Positions only — each index keeps its UV.
        /// A stacked vertex that already belongs to another selected point keeps that
        /// point's own offset. Clusters are gathered before anything moves.
        /// </summary>
        private static void ApplyPointOffsets(
            IndieEditableMeshData data,
            IndieEditMeshFacePointGroups points,
            Dictionary<int, Vector3> offsets
        )
        {
            if (data == null || points == null || offsets == null || offsets.Count == 0)
                return;

            var moves = new Dictionary<int, Vector3>();

            foreach (var pair in offsets)
            {
                if (pair.Key < 0 || pair.Key >= data.vertices.Count)
                    continue;

                var cluster = new HashSet<int> { pair.Key };
                IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(
                    data,
                    cluster,
                    IndieEditMeshFacePointGroups.CoincidentThreshold
                );

                foreach (int vert in cluster)
                {
                    if (points.TryGetPoint(vert, out int point) && point != pair.Key)
                        continue;

                    if (!moves.ContainsKey(vert))
                        moves.Add(vert, pair.Value);
                }
            }

            foreach (var move in moves)
                data.vertices[move.Key] += move.Value;
        }

        private static void InsetSingleFace(IndieEditableMeshData data, int faceIndex, float amount)
        {
            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return;

            var face = data.faces[faceIndex];
            Vector3 centroid =
                (data.vertices[face.a] + data.vertices[face.b] + data.vertices[face.c]) / 3f;

            int a2 = InsetVertexToward(data, face.a, centroid, amount);
            int b2 = InsetVertexToward(data, face.b, centroid, amount);
            int c2 = InsetVertexToward(data, face.c, centroid, amount);
            int materialId = face.materialId;

            IndieEditMeshMeshUtil.AddQuad(data.faces, face.a, face.b, b2, a2, materialId);
            IndieEditMeshMeshUtil.AddQuad(data.faces, face.b, face.c, c2, b2, materialId);
            IndieEditMeshMeshUtil.AddQuad(data.faces, face.c, face.a, a2, c2, materialId);

            face.a = a2;
            face.b = b2;
            face.c = c2;
            data.faces[faceIndex] = face;
        }

        private static void InsetFaceGroup(IndieEditableMeshData data, List<int> group, float amount)
        {
            if (group == null || group.Count == 0)
                return;

            var selected = new HashSet<int>(group);
            var boundary = GetBoundaryEdges(data, selected);

            if (boundary.Count == 0)
                return;

            var boundaryVerts = new HashSet<int>();

            foreach (var edge in boundary)
            {
                boundaryVerts.Add(edge.v0);
                boundaryVerts.Add(edge.v1);
            }

            Vector3 centroid = Vector3.zero;

            foreach (int vert in boundaryVerts)
                centroid += data.vertices[vert];

            centroid /= boundaryVerts.Count;

            var map = new Dictionary<int, int>();

            foreach (int vert in boundaryVerts)
                map[vert] = InsetVertexToward(data, vert, centroid, amount);

            foreach (int faceIndex in group)
            {
                var face = data.faces[faceIndex];

                if (map.ContainsKey(face.a))
                    face.a = map[face.a];

                if (map.ContainsKey(face.b))
                    face.b = map[face.b];

                if (map.ContainsKey(face.c))
                    face.c = map[face.c];

                data.faces[faceIndex] = face;
            }

            int materialId = data.faces[group[0]].materialId;

            foreach (var edge in boundary)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    data.faces,
                    edge.v0,
                    edge.v1,
                    map[edge.v1],
                    map[edge.v0],
                    materialId
                );
            }

            data.selectedFaces = new List<int>(group);
            data.selectedVertices.Clear();
            data.selectedEdges.Clear();
        }

        private static int InsetVertexToward(
            IndieEditableMeshData data,
            int vert,
            Vector3 target,
            float amount
        )
        {
            Vector3 pos = data.vertices[vert];
            Vector3 toTarget = target - pos;
            float dist = toTarget.magnitude;

            if (dist < 1e-8f)
                return IndieEditMeshMeshUtil.AddVertex(data, vert, pos);

            float t = Mathf.Clamp01(amount / dist);
            return IndieEditMeshMeshUtil.AddVertex(data, vert, pos + toTarget * t);
        }

        private static void BridgeFaceGroups(
            IndieEditableMeshData data,
            List<int> groupA,
            List<int> groupB,
            int segments,
            int twist,
            float taper,
            float bias
        )
        {
            var loopA = GetOrderedBoundaryLoop(data, groupA);
            var loopB = GetOrderedBoundaryLoop(data, groupB);

            if (loopA.Count < 3 || loopB.Count < 3)
                return;

            // Align loops by nearest start + twist.
            AlignLoops(data, loopA, loopB, twist);

            int materialId = data.faces[groupA[0]].materialId;
            var remove = new HashSet<int>(groupA);

            foreach (int faceIndex in groupB)
                remove.Add(faceIndex);

            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!remove.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            int facesBefore = newFaces.Count;
            int count = Mathf.Min(loopA.Count, loopB.Count);

            for (int i = 0; i < count; i++)
            {
                int a0 = loopA[i];
                int a1 = loopA[(i + 1) % loopA.Count];
                int b0 = loopB[i];
                int b1 = loopB[(i + 1) % loopB.Count];

                var rows = new List<int[]>();
                rows.Add(new[] { a0, a1 });

                for (int s = 1; s < segments; s++)
                {
                    float t = s / (float)segments;
                    float taperScale = EvaluateTaperScale(t, taper, bias);
                    Vector3 p0 = Vector3.Lerp(data.vertices[a0], data.vertices[b0], t);
                    Vector3 p1 = Vector3.Lerp(data.vertices[a1], data.vertices[b1], t);

                    if (Mathf.Abs(taperScale - 1f) > 1e-5f)
                    {
                        Vector3 mid = (p0 + p1) * 0.5f;
                        p0 = mid + (p0 - mid) * taperScale;
                        p1 = mid + (p1 - mid) * taperScale;
                    }

                    rows.Add(new[]
                    {
                        IndieEditMeshMeshUtil.AddVertex(data, a0, p0),
                        IndieEditMeshMeshUtil.AddVertex(data, a1, p1)
                    });
                }

                rows.Add(new[] { b0, b1 });

                for (int r = 0; r < rows.Count - 1; r++)
                {
                    IndieEditMeshMeshUtil.AddQuadStable(
                        newFaces,
                        data.vertices,
                        rows[r][0],
                        rows[r + 1][0],
                        rows[r + 1][1],
                        rows[r][1],
                        materialId
                    );
                }
            }

            // Bridge strip winding comes out inverted vs the selected end faces — flip all new tris.
            for (int i = facesBefore; i < newFaces.Count; i++)
            {
                var face = newFaces[i];
                int tmp = face.b;
                face.b = face.c;
                face.c = tmp;
                newFaces[i] = face;
            }

            data.faces = newFaces;
        }

        private static void AlignLoops(
            IndieEditableMeshData data,
            List<int> loopA,
            List<int> loopB,
            int twist
        )
        {
            if (loopA.Count == 0 || loopB.Count == 0)
                return;

            float best = float.MaxValue;
            var bestLoop = new List<int>(loopB);

            for (int reverse = 0; reverse < 2; reverse++)
            {
                var candidate = new List<int>(loopB);

                if (reverse == 1)
                    candidate.Reverse();

                for (int offset = 0; offset < candidate.Count; offset++)
                {
                    float score = 0f;
                    int count = Mathf.Min(loopA.Count, candidate.Count);

                    for (int i = 0; i < count; i++)
                    {
                        int bi = candidate[(i + offset) % candidate.Count];
                        score += Vector3.Distance(data.vertices[loopA[i]], data.vertices[bi]);
                    }

                    if (score >= best)
                        continue;

                    best = score;
                    bestLoop.Clear();

                    for (int i = 0; i < candidate.Count; i++)
                        bestLoop.Add(candidate[(i + offset) % candidate.Count]);
                }
            }

            loopB.Clear();
            loopB.AddRange(bestLoop);

            int twistSteps = ((twist % loopB.Count) + loopB.Count) % loopB.Count;

            if (twistSteps <= 0)
                return;

            var twisted = new List<int>(loopB.Count);

            for (int i = 0; i < loopB.Count; i++)
                twisted.Add(loopB[(i + twistSteps) % loopB.Count]);

            loopB.Clear();
            loopB.AddRange(twisted);
        }

        private static List<int> GetOrderedBoundaryLoop(
            IndieEditableMeshData data,
            List<int> group
        )
        {
            var selected = new HashSet<int>(group);
            var boundary = GetBoundaryEdges(data, selected);
            var loop = new List<int>();

            if (boundary.Count == 0)
                return loop;

            var next = new Dictionary<int, int>();

            foreach (var edge in boundary)
            {
                // Prefer winding from a selected face.
                if (TryGetDirectedBoundary(data, selected, edge.v0, edge.v1, out int from, out int to))
                    next[from] = to;
                else
                    next[edge.v0] = edge.v1;
            }

            int start = boundary[0].v0;

            foreach (var key in next.Keys)
            {
                start = key;
                break;
            }

            int current = start;
            var visited = new HashSet<int>();

            while (visited.Add(current) && next.TryGetValue(current, out int following))
            {
                loop.Add(current);
                current = following;

                if (current == start)
                    break;
            }

            return loop;
        }

        private static bool TryGetDirectedBoundary(
            IndieEditableMeshData data,
            HashSet<int> selected,
            int v0,
            int v1,
            out int from,
            out int to
        )
        {
            from = v0;
            to = v1;
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!selected.Contains(i))
                    continue;

                var face = data.faces[i];

                if (IndieEditMeshMeshUtil.EdgeKey(face.a, face.b) == key)
                {
                    from = face.a;
                    to = face.b;
                    return true;
                }

                if (IndieEditMeshMeshUtil.EdgeKey(face.b, face.c) == key)
                {
                    from = face.b;
                    to = face.c;
                    return true;
                }

                if (IndieEditMeshMeshUtil.EdgeKey(face.c, face.a) == key)
                {
                    from = face.c;
                    to = face.a;
                    return true;
                }
            }

            return false;
        }

        private static List<List<int>> GetContiguousFaceGroups(
            IndieEditableMeshData data,
            List<int> selectedFaces
        )
        {
            var groups = new List<List<int>>();
            var remaining = new HashSet<int>(selectedFaces);

            while (remaining.Count > 0)
            {
                int start = -1;

                foreach (int faceIndex in remaining)
                {
                    start = faceIndex;
                    break;
                }

                var group = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start);
                remaining.Remove(start);

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    group.Add(current);

                    for (int other = 0; other < data.faces.Count; other++)
                    {
                        if (!remaining.Contains(other))
                            continue;

                        if (!FacesShareEdge(data.faces[current], data.faces[other]))
                            continue;

                        remaining.Remove(other);
                        stack.Push(other);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private static bool FacesShareEdge(IndieEditMeshFace a, IndieEditMeshFace b)
        {
            long[] edgesA =
            {
                IndieEditMeshMeshUtil.EdgeKey(a.a, a.b),
                IndieEditMeshMeshUtil.EdgeKey(a.b, a.c),
                IndieEditMeshMeshUtil.EdgeKey(a.c, a.a)
            };

            long kb0 = IndieEditMeshMeshUtil.EdgeKey(b.a, b.b);
            long kb1 = IndieEditMeshMeshUtil.EdgeKey(b.b, b.c);
            long kb2 = IndieEditMeshMeshUtil.EdgeKey(b.c, b.a);

            return edgesA[0] == kb0 || edgesA[0] == kb1 || edgesA[0] == kb2 ||
                   edgesA[1] == kb0 || edgesA[1] == kb1 || edgesA[1] == kb2 ||
                   edgesA[2] == kb0 || edgesA[2] == kb1 || edgesA[2] == kb2;
        }

        private static List<IndieEditMeshEdge> GetBoundaryEdges(
            IndieEditableMeshData data,
            HashSet<int> selectedFaces
        )
        {
            var counts = new Dictionary<long, IndieEditMeshEdge>();
            var faceCount = new Dictionary<long, int>();

            foreach (int faceIndex in selectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                AddBoundaryCandidate(counts, faceCount, face.a, face.b);
                AddBoundaryCandidate(counts, faceCount, face.b, face.c);
                AddBoundaryCandidate(counts, faceCount, face.c, face.a);
            }

            var result = new List<IndieEditMeshEdge>();

            foreach (var pair in faceCount)
            {
                if (pair.Value == 1)
                    result.Add(counts[pair.Key]);
            }

            return result;
        }

        private static void AddBoundaryCandidate(
            Dictionary<long, IndieEditMeshEdge> edges,
            Dictionary<long, int> counts,
            int v0,
            int v1
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            if (!edges.ContainsKey(key))
                edges[key] = new IndieEditMeshEdge { v0 = v0, v1 = v1 };

            if (!counts.ContainsKey(key))
                counts[key] = 0;

            counts[key]++;
        }

        private static HashSet<int> CollectVerts(IndieEditableMeshData data, List<int> faces)
        {
            var verts = new HashSet<int>();

            foreach (int faceIndex in faces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                verts.Add(face.a);
                verts.Add(face.b);
                verts.Add(face.c);
            }

            return verts;
        }

        internal static Vector3 FaceNormal(IndieEditableMeshData data, IndieEditMeshFace face)
        {
            Vector3 a = data.vertices[face.a];
            Vector3 b = data.vertices[face.b];
            Vector3 c = data.vertices[face.c];
            Vector3 n = Vector3.Cross(b - a, c - a);

            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        private static Vector3 AverageFaceNormal(IndieEditableMeshData data, List<int> faces)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int faceIndex in faces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                sum += FaceNormal(data, data.faces[faceIndex]);
                count++;
            }

            if (count == 0)
                return Vector3.up;

            return (sum / count).normalized;
        }

        private static float EvaluateTaperScale(float t, float taper, float bias)
        {
            if (Mathf.Abs(taper) < 1e-5f)
                return 1f;

            float bias01 = Mathf.Clamp(bias, -99f, 99f) / 99f;
            float peak = 0.5f + 0.5f * bias01;
            float dist = Mathf.Abs(t - peak);
            float influence = 1f - Mathf.Clamp01(dist * 2f);

            return Mathf.Max(0.05f, 1f + taper * 0.01f * influence);
        }

        private static bool CanEditFaces(IndieEditMeshComponent ep)
        {
            return ep != null &&
                   ep.Data != null &&
                   IndieEditMeshSession.Active == ep &&
                   ep.subMode == IndieEditMeshSubMode.Polygon &&
                   ep.Data.selectedFaces.Count > 0;
        }

        private static void Finish(IndieEditMeshComponent ep)
        {
            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
        }
    }
}
