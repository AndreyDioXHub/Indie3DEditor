using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshEdgeOps
    {
        public static void Remove(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Remove Edges");
            ApplyRemove(ep.Data);
            Finish(ep);
        }

        public static void Split(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Split Edges");
            ApplySplit(ep.Data);
            Finish(ep);
        }

        public static void Extrude(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Extrude Edges");
            ApplyExtrude(
                ep.Data,
                IndieEditMeshEdgeParams.ExtrudeHeight,
                IndieEditMeshEdgeParams.ExtrudeBaseWidth
            );
            Finish(ep);
        }

        public static void Weld(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Weld Edges");
            ApplyWeld(ep.Data, IndieEditMeshEdgeParams.WeldThreshold);
            Finish(ep);
        }

        public static void Chamfer(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Chamfer Edges");
            IndieEditMeshFaceRegions.ApplyLocal(
                ep.Data,
                data => ApplyChamfer(
                    data,
                    IndieEditMeshEdgeParams.ChamferAmount,
                    IndieEditMeshEdgeParams.ChamferSegments,
                    IndieEditMeshEdgeParams.ChamferTension,
                    IndieEditMeshEdgeParams.ChamferInvert,
                    IndieEditMeshEdgeParams.ChamferOpen
                ),
                IndieEditMeshFaceRegionSpan.EdgeDoubleBelt
            );
            Finish(ep);
        }

        public static void Bridge(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep))
                return;

            IndieEditMeshUndo.Register(ep, "Bridge Edges");
            ApplyBridge(
                ep.Data,
                IndieEditMeshEdgeParams.BridgeSegments,
                IndieEditMeshEdgeParams.BridgeTwist,
                IndieEditMeshEdgeParams.BridgeTaper,
                IndieEditMeshEdgeParams.BridgeBias
            );
            Finish(ep);
        }

        public static void Connect(IndieEditMeshComponent ep)
        {
            if (!CanEditEdges(ep) || ep.Data.selectedEdges.Count < 2)
                return;

            IndieEditMeshEdgeSettingsWindow.DismissConnectPreview();
            IndieEditMeshUndo.Register(ep, "Connect Edges");
            ApplyConnect(
                ep.Data,
                IndieEditMeshEdgeParams.ConnectSegments,
                IndieEditMeshEdgeParams.ConnectPinch,
                IndieEditMeshEdgeParams.ConnectSlide
            );
            Finish(ep);
        }

        public static void Ring(IndieEditMeshComponent ep)
        {
            if (ep == null ||
                ep.Data == null ||
                IndieEditMeshSession.Active != ep ||
                ep.subMode != IndieEditMeshSubMode.Edge ||
                ep.Data.selectedEdges.Count < 2)
            {
                return;
            }

            IndieEditMeshUndo.Register(ep, "Ring Edges");
            ApplyRing(ep.Data, IndieEditMeshEdgeParams.RingAngle);
            Finish(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void Loop(IndieEditMeshComponent ep)
        {
            if (ep == null ||
                ep.Data == null ||
                IndieEditMeshSession.Active != ep ||
                ep.subMode != IndieEditMeshSubMode.Edge ||
                !CanLoop(ep.Data))
            {
                return;
            }

            IndieEditMeshUndo.Register(ep, "Loop Edges");
            ApplyLoop(ep.Data);
            Finish(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        /// <summary>
        /// Loop needs exactly two edges that share a vertex (adjacent on the path).
        /// </summary>
        public static bool CanLoop(IndieEditableMeshData data)
        {
            if (data == null || data.selectedEdges.Count != 2)
                return false;

            int e0 = data.selectedEdges[0];
            int e1 = data.selectedEdges[1];

            if (e0 < 0 || e0 >= data.edges.Count || e1 < 0 || e1 >= data.edges.Count)
                return false;

            return TryGetSharedVertex(data.edges[e0], data.edges[e1], out _);
        }

        /// <summary>
        /// Grow a connected edge loop both ways from two adjacent seed edges.
        /// On triangles: at each vertex pick the straightest continuation that
        /// stays in the seed plane (rim wins over spokes / verticals).
        /// Stops at a dead-end or when the loop closes on itself.
        /// </summary>
        public static void ApplyLoop(IndieEditableMeshData data)
        {
            if (!CanLoop(data))
                return;

            int e0 = data.selectedEdges[0];
            int e1 = data.selectedEdges[1];
            var edge0 = data.edges[e0];
            var edge1 = data.edges[e1];

            if (!TryGetSharedVertex(edge0, edge1, out int shared))
                return;

            int endA = edge0.v0 == shared ? edge0.v1 : edge0.v0;
            int endB = edge1.v0 == shared ? edge1.v1 : edge1.v0;

            if (endA == endB)
                return;

            Vector3 pShared = data.vertices[shared];
            Vector3 dirA = data.vertices[endA] - pShared;
            Vector3 dirB = data.vertices[endB] - pShared;
            Vector3 loopNormal = Vector3.Cross(dirA, dirB);

            if (loopNormal.sqrMagnitude > 1e-12f)
                loopNormal.Normalize();
            else
                loopNormal = Vector3.zero;

            var vertEdges = BuildVertexEdgeMap(data);
            var selected = new HashSet<int> { e0, e1 };

            GrowLoopDirection(data, vertEdges, shared, endB, loopNormal, selected);
            GrowLoopDirection(data, vertEdges, shared, endA, loopNormal, selected);

            data.selectedEdges = new List<int>(selected);
            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
        }

        private static bool TryGetSharedVertex(
            IndieEditMeshEdge a,
            IndieEditMeshEdge b,
            out int shared
        )
        {
            // Reject identical edges.
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

        private static Dictionary<int, List<int>> BuildVertexEdgeMap(IndieEditableMeshData data)
        {
            var map = new Dictionary<int, List<int>>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                AddVertEdge(map, edge.v0, i);
                AddVertEdge(map, edge.v1, i);
            }

            return map;
        }

        private static void AddVertEdge(Dictionary<int, List<int>> map, int vert, int edgeIndex)
        {
            if (!map.TryGetValue(vert, out var list))
            {
                list = new List<int>(4);
                map.Add(vert, list);
            }

            list.Add(edgeIndex);
        }

        private static void GrowLoopDirection(
            IndieEditableMeshData data,
            Dictionary<int, List<int>> vertEdges,
            int prevVert,
            int currVert,
            Vector3 loopNormal,
            HashSet<int> selected
        )
        {
            const int maxSteps = 4096;

            for (int step = 0; step < maxSteps; step++)
            {
                int nextEdge = FindNextLoopEdge(
                    data,
                    vertEdges,
                    prevVert,
                    currVert,
                    loopNormal
                );

                if (nextEdge < 0)
                    break;

                if (selected.Contains(nextEdge))
                    break;

                selected.Add(nextEdge);

                var edge = data.edges[nextEdge];
                int nextVert = edge.v0 == currVert ? edge.v1 : edge.v0;
                prevVert = currVert;
                currVert = nextVert;
            }
        }

        private static int FindNextLoopEdge(
            IndieEditableMeshData data,
            Dictionary<int, List<int>> vertEdges,
            int prevVert,
            int currVert,
            Vector3 loopNormal
        )
        {
            if (!vertEdges.TryGetValue(currVert, out var edges) || edges.Count == 0)
                return -1;

            Vector3 pPrev = data.vertices[prevVert];
            Vector3 pCurr = data.vertices[currVert];
            Vector3 inDir = pCurr - pPrev;

            if (inDir.sqrMagnitude < 1e-16f)
                return -1;

            inDir.Normalize();
            bool hasPlane = loopNormal.sqrMagnitude > 1e-12f;

            int best = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < edges.Count; i++)
            {
                int edgeIndex = edges[i];
                var edge = data.edges[edgeIndex];
                int other = edge.v0 == currVert ? edge.v1 : edge.v0;

                if (other == prevVert)
                    continue;

                Vector3 outDir = data.vertices[other] - pCurr;

                if (outDir.sqrMagnitude < 1e-16f)
                    continue;

                outDir.Normalize();
                float alignment = Vector3.Dot(outDir, inDir);

                // Reject sharp back-turns.
                if (alignment < -0.35f)
                    continue;

                float planarPenalty = 0f;

                if (hasPlane)
                    planarPenalty = Mathf.Abs(Vector3.Dot(outDir, loopNormal));

                // Straightest in-plane continuation wins (rim over spoke/side).
                float score = alignment - planarPenalty * 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = edgeIndex;
                }
            }

            return best;
        }

        /// <summary>
        /// Grow edge selection into a ring both ways from a seed pair.
        /// Next edge is chosen by direction angle to the current mother edge.
        /// </summary>
        public static void ApplyRing(IndieEditableMeshData data, float angleDegrees)
        {
            if (data == null || data.selectedEdges.Count < 2 || data.edges.Count == 0)
                return;

            angleDegrees = Mathf.Clamp(angleDegrees, 0.1f, 89f);

            if (!TryPickSeedPair(data, angleDegrees, out int seedA, out int seedB))
                return;

            var selected = new HashSet<int>(data.selectedEdges);
            selected.Add(seedA);
            selected.Add(seedB);

            GrowRingDirection(data, seedA, seedB, angleDegrees, selected);
            GrowRingDirection(data, seedB, seedA, angleDegrees, selected);

            data.selectedEdges = new List<int>(selected);
            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
        }

        private static bool TryPickSeedPair(
            IndieEditableMeshData data,
            float angleDegrees,
            out int seedA,
            out int seedB
        )
        {
            seedA = seedB = -1;
            float cosLimit = Mathf.Cos(angleDegrees * Mathf.Deg2Rad);
            float bestDist = float.MaxValue;
            var seeds = data.selectedEdges;

            for (int i = 0; i < seeds.Count; i++)
            {
                int ea = seeds[i];

                if (ea < 0 || ea >= data.edges.Count)
                    continue;

                Vector3 dirA = GetAlignedEdgeDir(data, ea, Vector3.up);
                Vector3 midA = GetEdgeMid(data, ea);

                for (int j = i + 1; j < seeds.Count; j++)
                {
                    int eb = seeds[j];

                    if (eb < 0 || eb >= data.edges.Count)
                        continue;

                    Vector3 dirB = GetAlignedEdgeDir(data, eb, dirA);

                    if (Vector3.Dot(dirA, dirB) < cosLimit)
                        continue;

                    float dist = Vector3.Distance(midA, GetEdgeMid(data, eb));

                    if (dist < 1e-8f || dist >= bestDist)
                        continue;

                    bestDist = dist;
                    seedA = ea;
                    seedB = eb;
                }
            }

            return seedA >= 0 && seedB >= 0;
        }

        private static void GrowRingDirection(
            IndieEditableMeshData data,
            int prevEdge,
            int motherEdge,
            float angleDegrees,
            HashSet<int> selected
        )
        {
            const int maxSteps = 4096;
            int prev = prevEdge;
            int mother = motherEdge;

            for (int step = 0; step < maxSteps; step++)
            {
                int next = FindNextRingEdge(data, prev, mother, angleDegrees);

                if (next < 0)
                    break;

                // Hit an already selected edge (other side of the ring) — stop.
                if (selected.Contains(next))
                    break;

                selected.Add(next);
                prev = mother;
                mother = next;
            }
        }

        private static int FindNextRingEdge(
            IndieEditableMeshData data,
            int prevEdge,
            int motherEdge,
            float angleDegrees
        )
        {
            if (motherEdge < 0 || motherEdge >= data.edges.Count ||
                prevEdge < 0 || prevEdge >= data.edges.Count)
            {
                return -1;
            }

            Vector3 motherDir = GetAlignedEdgeDir(data, motherEdge, Vector3.up);
            motherDir = GetAlignedEdgeDir(data, motherEdge, motherDir);
            Vector3 prevDir = GetAlignedEdgeDir(data, prevEdge, motherDir);
            Vector3 refDir = (motherDir + prevDir).normalized;

            if (refDir.sqrMagnitude < 1e-12f)
                refDir = motherDir;

            Vector3 midMother = GetEdgeMid(data, motherEdge);
            Vector3 midPrev = GetEdgeMid(data, prevEdge);
            Vector3 step = midMother - midPrev;
            float stepLen = step.magnitude;

            if (stepLen < 1e-8f)
                return -1;

            step /= stepLen;
            float cosLimit = Mathf.Cos(angleDegrees * Mathf.Deg2Rad);
            float maxStep = stepLen * 2.5f;
            float minStep = stepLen * 0.2f;

            int best = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < data.edges.Count; i++)
            {
                if (i == motherEdge || i == prevEdge)
                    continue;

                Vector3 dir = GetAlignedEdgeDir(data, i, refDir);

                if (Vector3.Dot(dir, refDir) < cosLimit)
                    continue;

                Vector3 mid = GetEdgeMid(data, i);
                Vector3 to = mid - midMother;
                float dist = to.magnitude;

                if (dist < minStep || dist > maxStep)
                    continue;

                Vector3 toDir = to / dist;

                // Must continue on the same side of travel (away from prev).
                if (Vector3.Dot(toDir, step) <= 0.15f)
                    continue;

                // Ring step is across the mother, not along it.
                if (Mathf.Abs(Vector3.Dot(toDir, motherDir)) > 0.65f)
                    continue;

                // Prefer nearest, then best aligned.
                float align = Vector3.Dot(dir, refDir);
                float score = dist * (2f - align);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static Vector3 GetEdgeMid(IndieEditableMeshData data, int edgeIndex)
        {
            var edge = data.edges[edgeIndex];
            return (data.vertices[edge.v0] + data.vertices[edge.v1]) * 0.5f;
        }

        private static Vector3 GetAlignedEdgeDir(
            IndieEditableMeshData data,
            int edgeIndex,
            Vector3 reference
        )
        {
            var edge = data.edges[edgeIndex];
            Vector3 dir = data.vertices[edge.v1] - data.vertices[edge.v0];

            if (dir.sqrMagnitude < 1e-16f)
                return Vector3.up;

            dir.Normalize();

            if (reference.sqrMagnitude > 1e-12f && Vector3.Dot(dir, reference) < 0f)
                dir = -dir;

            return dir;
        }

        public static void ApplyRemove(IndieEditableMeshData data)
        {
            if (data == null || data.selectedEdges.Count == 0)
                return;

            var selected = new List<int>(data.selectedEdges);
            selected.Sort((a, b) => b.CompareTo(a));

            foreach (int edgeIndex in selected)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                RemoveSingleEdge(data, data.edges[edgeIndex].v0, data.edges[edgeIndex].v1);
            }

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
            data.ClearSelection();
        }

        public static void ApplySplit(IndieEditableMeshData data)
        {
            if (data == null || data.selectedEdges.Count == 0)
                return;

            var selectedKeys = new HashSet<long>();
            var edgePairs = new List<(int v0, int v1)>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (!selectedKeys.Add(key))
                    continue;

                edgePairs.Add((edge.v0, edge.v1));
            }

            if (selectedKeys.Count == 0)
                return;

            // 1) Split vertices along cut paths (separable one-ring cases).
            var verticesToProcess = new HashSet<int>();

            foreach (var edge in edgePairs)
            {
                verticesToProcess.Add(edge.v0);
                verticesToProcess.Add(edge.v1);
            }

            foreach (int vertex in verticesToProcess)
                SplitVertexAlongSelectedEdges(data, vertex, selectedKeys);

            // 2) Force-open any selected edge that is still shared by 2+ faces
            //    (e.g. single edge on a closed corner where the one-ring stays connected).
            foreach (var edge in edgePairs)
                ForceSplitSharedEdge(data, edge.v0, edge.v1);

            data.RebuildEdges();
            data.ClearSelection();
        }

        public static void ApplyExtrude(IndieEditableMeshData data, float height, float baseWidth)
        {
            if (data == null || data.selectedEdges.Count == 0)
                return;

            var selected = new List<int>(data.selectedEdges);
            selected.Sort((a, b) => b.CompareTo(a));

            foreach (int edgeIndex in selected)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                ExtrudeSingle(data, edge.v0, edge.v1, height, baseWidth, out _, out _);
            }

            data.RebuildEdges();
            data.ClearSelection();
        }

        /// <summary>
        /// Shift+Move on edges: extrude at height 0 (bridge quads stay between
        /// original and new edge), select the new edges so the handle moves them.
        /// Returns the face index where new strip tris begin (for normal fix after drag).
        /// </summary>
        public static int ApplyShiftExtrude(IndieEditableMeshData data)
        {
            if (data == null || data.selectedEdges.Count == 0)
                return data != null ? data.faces.Count : 0;

            int newFaceStart = data.faces.Count;
            var pairs = new List<(int v0, int v1)>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                pairs.Add((edge.v0, edge.v1));
            }

            var newEdges = new List<(int e0, int e1)>();

            for (int i = 0; i < pairs.Count; i++)
            {
                ExtrudeSingle(
                    data,
                    pairs[i].v0,
                    pairs[i].v1,
                    0f,
                    0f,
                    out int e0,
                    out int e1
                );

                if (e0 >= 0 && e1 >= 0)
                    newEdges.Add((e0, e1));
            }

            data.RebuildEdges();
            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
            data.selectedEdges.Clear();

            for (int i = 0; i < newEdges.Count; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(newEdges[i].e0, newEdges[i].e1);

                for (int e = 0; e < data.edges.Count; e++)
                {
                    if (IndieEditMeshMeshUtil.EdgeKey(data.edges[e].v0, data.edges[e].v1) != key)
                        continue;

                    if (!data.selectedEdges.Contains(e))
                        data.selectedEdges.Add(e);

                    break;
                }
            }

            return newFaceStart;
        }

        /// <summary>
        /// After Shift+Move drag: flip new strip faces whose normal sign disagrees
        /// with an adjacent older polygon that shares an edge.
        /// </summary>
        public static void MatchNewFaceNormalsToAdjacent(IndieEditableMeshData data, int newFaceStart)
        {
            if (data == null || newFaceStart < 0 || newFaceStart >= data.faces.Count)
                return;

            for (int fi = newFaceStart; fi < data.faces.Count; fi++)
            {
                var face = data.faces[fi];
                Vector3 faceN = FaceNormal(data, face);

                if (faceN.sqrMagnitude < 1e-12f)
                    continue;

                if (!TryFindAdjacentOldFaceNormal(
                        data,
                        face,
                        newFaceStart,
                        out Vector3 adjN))
                {
                    continue;
                }

                // Coplanar strip: require same sign. Near-orthogonal walls: leave as-is.
                if (Vector3.Dot(faceN, adjN) < -0.05f)
                {
                    int tmp = face.b;
                    face.b = face.c;
                    face.c = tmp;
                    data.faces[fi] = face;
                }
            }
        }

        private static bool TryFindAdjacentOldFaceNormal(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            int newFaceStart,
            out Vector3 adjacentNormal
        )
        {
            adjacentNormal = Vector3.zero;
            long[] keys =
            {
                IndieEditMeshMeshUtil.EdgeKey(face.a, face.b),
                IndieEditMeshMeshUtil.EdgeKey(face.b, face.c),
                IndieEditMeshMeshUtil.EdgeKey(face.c, face.a)
            };

            for (int i = 0; i < newFaceStart; i++)
            {
                var other = data.faces[i];
                long ka = IndieEditMeshMeshUtil.EdgeKey(other.a, other.b);
                long kb = IndieEditMeshMeshUtil.EdgeKey(other.b, other.c);
                long kc = IndieEditMeshMeshUtil.EdgeKey(other.c, other.a);

                for (int k = 0; k < keys.Length; k++)
                {
                    if (keys[k] != ka && keys[k] != kb && keys[k] != kc)
                        continue;

                    adjacentNormal = FaceNormal(data, other);
                    return adjacentNormal.sqrMagnitude > 1e-12f;
                }
            }

            return false;
        }

        public static void ApplyWeld(IndieEditableMeshData data, float threshold)
        {
            if (data == null || data.selectedEdges.Count == 0)
                return;

            var borderVerts = new List<int>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];

                if (!IndieEditMeshMeshUtil.IsBorderEdge(data, edge.v0, edge.v1))
                    continue;

                if (!borderVerts.Contains(edge.v0))
                    borderVerts.Add(edge.v0);

                if (!borderVerts.Contains(edge.v1))
                    borderVerts.Add(edge.v1);
            }

            if (borderVerts.Count < 2)
                return;

            data.selectedVertices = borderVerts;
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();

            IndieEditMeshVertexOps.ApplyWeld(data, threshold);
            data.ClearSelection();
        }

        public static void ApplyChamfer(
            IndieEditableMeshData data,
            float amount,
            int segments,
            float tension,
            bool invert,
            bool open
        )
        {
            if (data == null || data.selectedEdges.Count == 0)
                return;

            // Runs on a region copy too: vertex indices must stay put, so no cleanup here.
            IndieEditMeshBevel.Apply(
                data,
                Mathf.Max(0f, amount),
                Mathf.Max(1, segments),
                Mathf.Clamp01(tension),
                invert,
                open
            );
            data.ClearSelection();
        }

        public static void ApplyBridge(
            IndieEditableMeshData data,
            int segments,
            int twist,
            float taper,
            float bias
        )
        {
            if (data == null || data.selectedEdges.Count < 2)
                return;

            var borderEdges = new List<(int v0, int v1)>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];

                if (IndieEditMeshMeshUtil.IsBorderEdge(data, edge.v0, edge.v1))
                    borderEdges.Add((edge.v0, edge.v1));
            }

            if (borderEdges.Count < 2)
                return;

            segments = Mathf.Max(1, segments);

            var bridgeVerts = new List<int>();

            for (int i = 0; i + 1 < borderEdges.Count; i += 2)
            {
                BridgePair(
                    data,
                    borderEdges[i],
                    borderEdges[i + 1],
                    segments,
                    twist,
                    taper,
                    bias,
                    bridgeVerts
                );
            }

            // Merge bridge verts that sit on top of existing mesh verts so the
            // surface stays watertight (Max-like result after Bridge).
            WeldBridgeToNearby(data, bridgeVerts, 0.0001f);

            data.RebuildEdges();
            data.ClearSelection();
        }

        // Same threshold as Link Coincident / import weld (1e-5).
        private const float ConnectCoincidentEpsilonSq = 1e-10f;

        public static void ApplyConnect(
            IndieEditableMeshData data,
            int segments,
            float pinch,
            float slide
        )
        {
            if (data == null || data.selectedEdges.Count < 2)
                return;

            segments = Mathf.Max(1, segments);

            var keptSegments = new List<(Vector3 a, Vector3 b)>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];

                if (edge.v0 < 0 || edge.v1 < 0 ||
                    edge.v0 >= data.vertices.Count || edge.v1 >= data.vertices.Count)
                {
                    continue;
                }

                keptSegments.Add((data.vertices[edge.v0], data.vertices[edge.v1]));
            }

            var selectedKeys = new HashSet<long>();
            var selectedEdgeEnds = new List<(int v0, int v1)>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                selectedKeys.Add(IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1));
                selectedEdgeEnds.Add((edge.v0, edge.v1));
            }

            // Pull in UV/hard-edge duplicates of the selection (same idea as Link Coincident).
            ExpandSelectedEdgesWithCoincident(data, selectedEdgeEnds, selectedKeys);

            // Common slide axis so every selected edge uses the same t along height
            // (avoids broken ring when adjacent edges wind opposite ways).
            Vector3 slideAxis = GetConnectSlideAxis(data, selectedKeys);

            var splitCache = new Dictionary<long, List<int>>();
            var facesToRemove = new HashSet<int>();
            var facesToAdd = new List<IndieEditMeshFace>();
            var connectedQuadKeys = new HashSet<long>();

            // Same-triangle pairs (index match, then geometric).
            for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
            {
                if (facesToRemove.Contains(faceIndex))
                    continue;

                var face = data.faces[faceIndex];
                var selectedOnFace = GetSelectedEdgesOnFace(face, selectedKeys);

                if (selectedOnFace.Count != 2)
                {
                    selectedOnFace = GetGeometricSelectedEdgesOnFace(
                        data,
                        face,
                        selectedEdgeEnds
                    );
                }

                if (selectedOnFace.Count != 2)
                    continue;

                Vector3 faceRefNormal = FaceNormal(data, face);
                int addStart = facesToAdd.Count;

                var newFaces = ConnectOnFace(
                    data,
                    face,
                    selectedOnFace[0].v0,
                    selectedOnFace[0].v1,
                    selectedOnFace[1].v0,
                    selectedOnFace[1].v1,
                    segments,
                    pinch,
                    slide,
                    slideAxis,
                    splitCache
                );

                facesToRemove.Add(faceIndex);
                facesToAdd.AddRange(newFaces);
                OrientNewFacesToReference(data, facesToAdd, addStart, faceRefNormal);
            }

            // Opposite pairs across triangulated quads (exact index match).
            TryConnectQuads(
                data,
                selectedKeys,
                selectedEdgeEnds,
                segments,
                pinch,
                slide,
                slideAxis,
                splitCache,
                facesToRemove,
                facesToAdd,
                connectedQuadKeys,
                geometric: false
            );

            // Hard-edge / UV stacks: try every remaining quad whose opposite edges
            // match the (expanded) selection by position — keep that face's UV.
            TryConnectQuads(
                data,
                selectedKeys,
                selectedEdgeEnds,
                segments,
                pinch,
                slide,
                slideAxis,
                splitCache,
                facesToRemove,
                facesToAdd,
                connectedQuadKeys,
                geometric: true
            );

            // Pairs separated by several triangles (extra diagonals / inner verts).
            bool removedInterior = TryConnectCorridors(
                data,
                selectedKeys,
                segments,
                pinch,
                slide,
                slideAxis,
                splitCache,
                facesToRemove,
                facesToAdd
            );

            SplitFacesAdjacentToConnect(data, splitCache, facesToRemove, facesToAdd);

            if (facesToRemove.Count == 0 && facesToAdd.Count == 0)
                return;

            var splitSegments = new List<(Vector3 a, Vector3 b)>();

            foreach (var pair in splitCache)
            {
                IndieEditMeshMeshUtil.DecodeEdgeKey(pair.Key, out int splitV0, out int splitV1);

                if (splitV0 < 0 || splitV1 < 0 ||
                    splitV0 >= data.vertices.Count || splitV1 >= data.vertices.Count)
                {
                    continue;
                }

                splitSegments.Add((data.vertices[splitV0], data.vertices[splitV1]));
            }

            var rebuilt = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!facesToRemove.Contains(i))
                    rebuilt.Add(data.faces[i]);
            }

            rebuilt.AddRange(facesToAdd);
            data.faces = rebuilt;

            if (removedInterior)
                IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
            else
                data.RebuildEdges();

            RestoreEdgesAlongSegments(data, keptSegments, splitSegments);
        }

        private const float KeptSegmentEpsilonSq = 1e-8f;

        /// <summary>
        /// Cyan stays on selected edges Connect did not cut. Pieces of a split edge,
        /// including the new segment points, are left unselected. A new edge that only
        /// bridges two selected edges has its ends on different segments and is skipped.
        /// </summary>
        private static void RestoreEdgesAlongSegments(
            IndieEditableMeshData data,
            List<(Vector3 a, Vector3 b)> segments,
            List<(Vector3 a, Vector3 b)> splitSegments
        )
        {
            data.selectedVertices.Clear();
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();

            if (segments == null || segments.Count == 0)
                return;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];

                if (edge.v0 < 0 || edge.v1 < 0 ||
                    edge.v0 >= data.vertices.Count || edge.v1 >= data.vertices.Count)
                {
                    continue;
                }

                Vector3 p0 = data.vertices[edge.v0];
                Vector3 p1 = data.vertices[edge.v1];

                for (int s = 0; s < segments.Count; s++)
                {
                    var segment = segments[s];

                    if (SegmentWasSplit(segment, splitSegments))
                        continue;

                    if (!LiesOnSegment(p0, segment.a, segment.b) ||
                        !LiesOnSegment(p1, segment.a, segment.b))
                    {
                        continue;
                    }

                    data.selectedEdges.Add(i);
                    break;
                }
            }
        }

        private static bool SegmentWasSplit(
            (Vector3 a, Vector3 b) segment,
            List<(Vector3 a, Vector3 b)> splitSegments
        )
        {
            if (splitSegments == null)
                return false;

            for (int i = 0; i < splitSegments.Count; i++)
            {
                var split = splitSegments[i];
                bool keptOnSplit = LiesOnSegment(segment.a, split.a, split.b) &&
                                   LiesOnSegment(segment.b, split.a, split.b);
                bool splitOnKept = LiesOnSegment(split.a, segment.a, segment.b) &&
                                   LiesOnSegment(split.b, segment.a, segment.b);

                if (keptOnSplit || splitOnKept)
                    return true;
            }

            return false;
        }

        private static bool LiesOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;

            if (lengthSq < 1e-16f)
                return (point - a).sqrMagnitude <= KeptSegmentEpsilonSq;

            float t = Vector3.Dot(point - a, ab) / lengthSq;

            if (t < -1e-4f || t > 1f + 1e-4f)
                return false;

            Vector3 closest = a + ab * Mathf.Clamp01(t);
            return (point - closest).sqrMagnitude <= KeptSegmentEpsilonSq;
        }

        /// <summary>
        /// Dashed connect lines. Topology is cached until the selection changes;
        /// sliders only move t. The mesh is rebuilt later, on Accept or the toolbar button.
        /// </summary>
        public static void DrawConnectPreview(IndieEditMeshComponent ep)
        {
            if (!IndieEditMeshEdgeSettingsWindow.IsConnectPreviewFor(ep))
                return;

            if (ep == null || ep.Data == null)
                return;

            DrawConnectPreview(ep.Data, ep.transform, ep.GetEntityId().GetHashCode());
        }

        public static void DrawConnectPreview(IndieEditableMeshData data, Transform transform, int cacheId)
        {
            if (data == null || transform == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            ConnectPreview.Draw(data, transform, cacheId);
        }

        /// <summary>
        /// Read-only connect lines between the selected edges. Adjacent fans are
        /// left to ApplyConnect. No vertices are created and no faces are rewritten.
        /// </summary>
        private static class ConnectPreview
        {
            private struct Bridge
            {
                public int A0;
                public int A1;
                public int B0;
                public int B1;
                public bool Corridor;
            }

            private const float DashThickness = 2f;
            private const float DashPixels = 4f;
            private const float GapPixels = 4f;

            private static int _targetId;
            private static int _faceCount;
            private static int _vertexCount;
            private static int _edgeCount;
            private static int[] _selected;
            private static Vector3 _slideAxis;
            private static List<Bridge> _bridges;

            public static void Draw(IndieEditableMeshData data, Transform transform, int cacheId)
            {
                if (data == null || data.selectedEdges == null || data.selectedEdges.Count < 2)
                    return;

                if (!IsCurrent(cacheId, data))
                    Rebuild(cacheId, data);

                if (_bridges == null || _bridges.Count == 0)
                    return;

                int segments = Mathf.Max(1, IndieEditMeshEdgeParams.ConnectSegments);
                float pinch = IndieEditMeshEdgeParams.ConnectPinch;
                float slide = IndieEditMeshEdgeParams.ConnectSlide;
                var previousColor = Handles.color;
                var previousZTest = Handles.zTest;
                Handles.color = new Color(1f, 0.15f, 0.12f, 0.45f);
                Handles.zTest = CompareFunction.Always;

                for (int b = 0; b < _bridges.Count; b++)
                {
                    var bridge = _bridges[b];

                    for (int i = 0; i < segments; i++)
                    {
                        float t = GetConnectT(i, segments, pinch, slide);
                        Vector3 from = PreviewPoint(
                            data,
                            bridge.A0,
                            bridge.A1,
                            t,
                            _slideAxis,
                            bridge.Corridor,
                            bridge.A0,
                            bridge.A1
                        );
                        Vector3 to = PreviewPoint(
                            data,
                            bridge.B0,
                            bridge.B1,
                            t,
                            _slideAxis,
                            bridge.Corridor,
                            bridge.A0,
                            bridge.A1
                        );
                        DrawSegment(transform, from, to);
                    }
                }

                Handles.color = previousColor;
                Handles.zTest = previousZTest;
            }

            private static bool IsCurrent(int cacheId, IndieEditableMeshData data)
            {
                if (_bridges == null || _targetId != cacheId)
                    return false;

                if (_faceCount != data.faces.Count ||
                    _vertexCount != data.vertices.Count ||
                    _edgeCount != data.edges.Count)
                {
                    return false;
                }

                var selected = data.selectedEdges;

                if (_selected == null || _selected.Length != selected.Count)
                    return false;

                for (int i = 0; i < _selected.Length; i++)
                {
                    if (_selected[i] != selected[i])
                        return false;
                }

                return true;
            }

            private static void Rebuild(int cacheId, IndieEditableMeshData data)
            {
                _targetId = cacheId;
                _faceCount = data.faces.Count;
                _vertexCount = data.vertices.Count;
                _edgeCount = data.edges.Count;
                _selected = data.selectedEdges.ToArray();
                Collect(data, out _bridges, out _slideAxis);
            }

            private static void Collect(
                IndieEditableMeshData data,
                out List<Bridge> bridges,
                out Vector3 slideAxis
            )
            {
                bridges = new List<Bridge>();
                slideAxis = Vector3.up;

                if (data == null || data.selectedEdges == null || data.selectedEdges.Count < 2)
                    return;

                var selectedKeys = new HashSet<long>();
                var selectedEdgeEnds = new List<(int v0, int v1)>();

                foreach (int edgeIndex in data.selectedEdges)
                {
                    if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                        continue;

                    var edge = data.edges[edgeIndex];
                    selectedKeys.Add(IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1));
                    selectedEdgeEnds.Add((edge.v0, edge.v1));
                }

                if (selectedKeys.Count < 2)
                    return;

                ExpandSelectedEdgesWithCoincident(data, selectedEdgeEnds, selectedKeys);
                slideAxis = GetConnectSlideAxis(data, selectedKeys);

                var consumed = new HashSet<int>();
                var connectedQuadKeys = new HashSet<long>();
                var edgeFaces = BuildEdgeFaceMap(data);

                CollectPreviewSameFace(
                    data,
                    selectedKeys,
                    selectedEdgeEnds,
                    consumed,
                    bridges
                );
                CollectPreviewQuads(
                    data,
                    selectedKeys,
                    selectedEdgeEnds,
                    edgeFaces,
                    consumed,
                    connectedQuadKeys,
                    bridges,
                    geometric: false
                );
                CollectPreviewQuads(
                    data,
                    selectedKeys,
                    selectedEdgeEnds,
                    edgeFaces,
                    consumed,
                    connectedQuadKeys,
                    bridges,
                    geometric: true
                );
                CollectPreviewCorridors(
                    data,
                    selectedKeys,
                    edgeFaces,
                    consumed,
                    bridges
                );
            }

            private static void CollectPreviewSameFace(
                IndieEditableMeshData data,
                HashSet<long> selectedKeys,
                List<(int v0, int v1)> selectedEdgeEnds,
                HashSet<int> consumed,
                List<Bridge> bridges
            )
            {
                for (int faceIndex = 0; faceIndex < data.faces.Count; faceIndex++)
                {
                    var face = data.faces[faceIndex];
                    var selectedOnFace = GetSelectedEdgesOnFace(face, selectedKeys);

                    if (selectedOnFace.Count != 2)
                    {
                        selectedOnFace = GetGeometricSelectedEdgesOnFace(
                            data,
                            face,
                            selectedEdgeEnds
                        );
                    }

                    if (selectedOnFace.Count != 2)
                        continue;

                    bridges.Add(new Bridge
                    {
                        A0 = selectedOnFace[0].v0,
                        A1 = selectedOnFace[0].v1,
                        B0 = selectedOnFace[1].v0,
                        B1 = selectedOnFace[1].v1,
                        Corridor = false
                    });
                    consumed.Add(faceIndex);
                }
            }

            private static void CollectPreviewQuads(
                IndieEditableMeshData data,
                HashSet<long> selectedKeys,
                List<(int v0, int v1)> selectedEdgeEnds,
                Dictionary<long, List<int>> edgeFaces,
                HashSet<int> consumed,
                HashSet<long> connectedQuadKeys,
                List<Bridge> bridges,
                bool geometric
            )
            {
                for (int i = 0; i < data.faces.Count; i++)
                {
                    if (consumed.Contains(i))
                        continue;

                    var keys = FaceEdgeKeys(data.faces[i]);

                    for (int k = 0; k < keys.Length; k++)
                    {
                        long sharedKey = keys[k];

                        if (connectedQuadKeys.Contains(sharedKey))
                            continue;

                        if (!edgeFaces.TryGetValue(sharedKey, out var neighbors))
                            continue;

                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            int j = neighbors[n];

                            if (j <= i || consumed.Contains(j))
                                continue;

                            if (!TryGetSharedEdge(data.faces[i], data.faces[j], out int s0, out int s1))
                                continue;

                            if (IndieEditMeshMeshUtil.EdgeKey(s0, s1) != sharedKey)
                                continue;

                            bool matched = geometric
                                ? TryGetQuadOppositeGeometricEdges(
                                    data,
                                    data.faces[i],
                                    data.faces[j],
                                    s0,
                                    s1,
                                    selectedEdgeEnds,
                                    out int a0,
                                    out int a1,
                                    out int b0,
                                    out int b1)
                                : TryGetQuadOppositeSelectedEdges(
                                    data.faces[i],
                                    data.faces[j],
                                    s0,
                                    s1,
                                    selectedKeys,
                                    out a0,
                                    out a1,
                                    out b0,
                                    out b1);

                            if (!matched)
                                continue;

                            connectedQuadKeys.Add(sharedKey);
                            consumed.Add(i);
                            consumed.Add(j);
                            bridges.Add(new Bridge
                            {
                                A0 = a0,
                                A1 = a1,
                                B0 = b0,
                                B1 = b1,
                                Corridor = false
                            });
                        }
                    }
                }
            }

            private static void CollectPreviewCorridors(
                IndieEditableMeshData data,
                HashSet<long> selectedKeys,
                Dictionary<long, List<int>> edgeFaces,
                HashSet<int> consumed,
                List<Bridge> bridges
            )
            {
                if (selectedKeys.Count < 2)
                    return;

                var keys = new List<long>(selectedKeys);

                for (int i = 0; i < keys.Count; i++)
                {
                    long keyA = keys[i];

                    if (!edgeFaces.TryGetValue(keyA, out var startFaces))
                        continue;

                    var starts = new List<int>(startFaces);

                    for (int s = 0; s < starts.Count; s++)
                    {
                        int startFace = starts[s];

                        if (consumed.Contains(startFace))
                            continue;

                        var reached = CollectReachableSelectedEdges(
                            data,
                            startFace,
                            keyA,
                            selectedKeys,
                            edgeFaces,
                            consumed
                        );

                        foreach (long keyB in reached)
                        {
                            if (consumed.Contains(startFace))
                                break;

                            TryCollectPreviewCorridor(
                                data,
                                startFace,
                                keyA,
                                keyB,
                                selectedKeys,
                                edgeFaces,
                                consumed,
                                bridges
                            );
                        }
                    }
                }
            }

            private static bool TryCollectPreviewCorridor(
                IndieEditableMeshData data,
                int startFace,
                long keyA,
                long keyB,
                HashSet<long> selectedKeys,
                Dictionary<long, List<int>> edgeFaces,
                HashSet<int> consumed,
                List<Bridge> bridges
            )
            {
                IndieEditMeshMeshUtil.DecodeEdgeKey(keyA, out int a0, out int a1);
                IndieEditMeshMeshUtil.DecodeEdgeKey(keyB, out int b0, out int b1);

                if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                    return false;

                var corridor = CollectCorridorFaces(
                    data,
                    startFace,
                    a0,
                    a1,
                    b0,
                    b1,
                    selectedKeys,
                    edgeFaces,
                    consumed
                );

                if (corridor == null || corridor.Count < 2 || !CorridorHasEdge(data, corridor, keyB))
                    return false;

                if (!TryGetCorridorLoop(data, corridor, out var loop))
                    return false;

                if (!TrySplitLoopAtEdges(
                        loop,
                        a0,
                        a1,
                        b0,
                        b1,
                        out int x,
                        out int y,
                        out int p,
                        out int q,
                        out _,
                        out _))
                {
                    return false;
                }

                bridges.Add(new Bridge
                {
                    A0 = x,
                    A1 = y,
                    B0 = q,
                    B1 = p,
                    Corridor = true
                });

                for (int i = 0; i < corridor.Count; i++)
                    consumed.Add(corridor[i]);

                return true;
            }

            private static Vector3 PreviewPoint(
                IndieEditableMeshData data,
                int v0,
                int v1,
                float t,
                Vector3 axis,
                bool corridor,
                int flip0,
                int flip1
            )
            {
                if (v0 < 0 || v1 < 0 ||
                    v0 >= data.vertices.Count || v1 >= data.vertices.Count)
                {
                    return Vector3.zero;
                }

                if (corridor)
                {
                    if (flip0 >= 0 && flip1 >= 0 &&
                        flip0 < data.vertices.Count && flip1 < data.vertices.Count)
                    {
                        Vector3 flipDir = data.vertices[flip1] - data.vertices[flip0];

                        if (flipDir.sqrMagnitude > 1e-16f && Vector3.Dot(flipDir, axis) < 0f)
                            t = 1f - t;
                    }
                }
                else
                {
                    Vector3 dir = data.vertices[v1] - data.vertices[v0];

                    if (dir.sqrMagnitude > 1e-16f && Vector3.Dot(dir, axis) < 0f)
                        t = 1f - t;
                }

                return Vector3.Lerp(data.vertices[v0], data.vertices[v1], t);
            }

            private static void DrawSegment(Transform transform, Vector3 localA, Vector3 localB)
            {
                if ((localA - localB).sqrMagnitude < 1e-12f)
                    return;

                DrawThickDottedLine(
                    transform.TransformPoint(localA),
                    transform.TransformPoint(localB)
                );
            }

            private static void DrawThickDottedLine(Vector3 a, Vector3 b)
            {
                float screenLen = Vector2.Distance(
                    HandleUtility.WorldToGUIPoint(a),
                    HandleUtility.WorldToGUIPoint(b)
                );

                if (screenLen < 0.5f)
                    return;

                float period = DashPixels + GapPixels;
                int count = Mathf.Max(1, Mathf.CeilToInt(screenLen / period));

                for (int i = 0; i < count; i++)
                {
                    float t0 = (i * period) / screenLen;
                    float t1 = (i * period + DashPixels) / screenLen;

                    if (t0 >= 1f)
                        break;

                    if (t1 > 1f)
                        t1 = 1f;

                    Handles.DrawLine(
                        Vector3.Lerp(a, b, t0),
                        Vector3.Lerp(a, b, t1),
                        DashThickness
                    );
                }
            }
        }

        private const int MaxCorridorFaces = 512;

        /// <summary>
        /// Selected pairs not joined by a single quad: replace every triangle between them
        /// with the same segment strip ConnectAcrossQuad builds. Returns true when
        /// vertices that only lived inside the old triangulation were dropped.
        /// </summary>
        private static bool TryConnectCorridors(
            IndieEditableMeshData data,
            HashSet<long> selectedKeys,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache,
            HashSet<int> facesToRemove,
            List<IndieEditMeshFace> facesToAdd
        )
        {
            if (selectedKeys.Count < 2)
                return false;

            var edgeFaces = BuildEdgeFaceMap(data);
            var keys = new List<long>(selectedKeys);
            bool removedInterior = false;

            for (int i = 0; i < keys.Count; i++)
            {
                long keyA = keys[i];

                if (!edgeFaces.TryGetValue(keyA, out var startFaces))
                    continue;

                var starts = new List<int>(startFaces);

                for (int s = 0; s < starts.Count; s++)
                {
                    int startFace = starts[s];

                    if (facesToRemove.Contains(startFace))
                        continue;

                    var reached = CollectReachableSelectedEdges(
                        data,
                        startFace,
                        keyA,
                        selectedKeys,
                        edgeFaces,
                        facesToRemove
                    );

                    foreach (long keyB in reached)
                    {
                        if (facesToRemove.Contains(startFace))
                            break;

                        if (TryConnectCorridor(
                                data,
                                startFace,
                                keyA,
                                keyB,
                                selectedKeys,
                                edgeFaces,
                                segments,
                                pinch,
                                slide,
                                slideAxis,
                                splitCache,
                                facesToRemove,
                                facesToAdd,
                                out bool hadInterior))
                        {
                            removedInterior |= hadInterior;
                        }
                    }
                }
            }

            return removedInterior;
        }

        private static Dictionary<long, List<int>> BuildEdgeFaceMap(IndieEditableMeshData data)
        {
            var map = new Dictionary<long, List<int>>(data.faces.Count * 2);
            int count = data.vertices.Count;

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a < 0 || face.b < 0 || face.c < 0 ||
                    face.a >= count || face.b >= count || face.c >= count)
                {
                    continue;
                }

                AddEdgeFace(map, IndieEditMeshMeshUtil.EdgeKey(face.a, face.b), i);
                AddEdgeFace(map, IndieEditMeshMeshUtil.EdgeKey(face.b, face.c), i);
                AddEdgeFace(map, IndieEditMeshMeshUtil.EdgeKey(face.c, face.a), i);
            }

            return map;
        }

        private static void AddEdgeFace(Dictionary<long, List<int>> map, long key, int faceIndex)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map.Add(key, list);
            }

            list.Add(faceIndex);
        }

        private static long[] FaceEdgeKeys(IndieEditMeshFace face)
        {
            return new[]
            {
                IndieEditMeshMeshUtil.EdgeKey(face.a, face.b),
                IndieEditMeshMeshUtil.EdgeKey(face.b, face.c),
                IndieEditMeshMeshUtil.EdgeKey(face.c, face.a)
            };
        }

        /// <summary>
        /// Selected edges met by a flood from startFace that never crosses a selected edge.
        /// </summary>
        private static HashSet<long> CollectReachableSelectedEdges(
            IndieEditableMeshData data,
            int startFace,
            long keyA,
            HashSet<long> selectedKeys,
            Dictionary<long, List<int>> edgeFaces,
            HashSet<int> facesToRemove
        )
        {
            var result = new HashSet<long>();
            var visited = new HashSet<int> { startFace };
            var queue = new Queue<int>();
            queue.Enqueue(startFace);

            while (queue.Count > 0 && visited.Count <= MaxCorridorFaces)
            {
                int current = queue.Dequeue();
                var keys = FaceEdgeKeys(data.faces[current]);

                for (int k = 0; k < keys.Length; k++)
                {
                    long key = keys[k];

                    if (selectedKeys.Contains(key))
                    {
                        if (key != keyA)
                            result.Add(key);

                        continue;
                    }

                    if (!edgeFaces.TryGetValue(key, out var neighbors))
                        continue;

                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int next = neighbors[n];

                        if (facesToRemove.Contains(next) || !visited.Add(next))
                            continue;

                        queue.Enqueue(next);
                    }
                }
            }

            return result;
        }

        private static bool TryConnectCorridor(
            IndieEditableMeshData data,
            int startFace,
            long keyA,
            long keyB,
            HashSet<long> selectedKeys,
            Dictionary<long, List<int>> edgeFaces,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache,
            HashSet<int> facesToRemove,
            List<IndieEditMeshFace> facesToAdd,
            out bool hadInterior
        )
        {
            hadInterior = false;

            IndieEditMeshMeshUtil.DecodeEdgeKey(keyA, out int a0, out int a1);
            IndieEditMeshMeshUtil.DecodeEdgeKey(keyB, out int b0, out int b1);

            if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                return false;

            var corridor = CollectCorridorFaces(
                data,
                startFace,
                a0,
                a1,
                b0,
                b1,
                selectedKeys,
                edgeFaces,
                facesToRemove
            );

            if (corridor == null || corridor.Count < 2 || !CorridorHasEdge(data, corridor, keyB))
                return false;

            if (!TryGetCorridorLoop(data, corridor, out var loop))
                return false;

            if (!TrySplitLoopAtEdges(loop, a0, a1, b0, b1, out int x, out int y, out int p, out int q,
                    out var chainYP, out var chainQX))
            {
                return false;
            }

            var onLoop = new HashSet<int>(loop);

            foreach (int faceIndex in corridor)
            {
                var face = data.faces[faceIndex];

                if (!onLoop.Contains(face.a) || !onLoop.Contains(face.b) || !onLoop.Contains(face.c))
                {
                    hadInterior = true;
                    break;
                }
            }

            BuildCorridorStrip(
                data,
                corridor,
                x,
                y,
                p,
                q,
                chainYP,
                chainQX,
                segments,
                pinch,
                slide,
                slideAxis,
                splitCache,
                facesToAdd
            );

            foreach (int faceIndex in corridor)
                facesToRemove.Add(faceIndex);

            return true;
        }

        /// <summary>
        /// Flood from startFace through non-selected edges, keeping only faces whose
        /// centroid lies inside the quad spanned by the two selected edges.
        /// </summary>
        private static List<int> CollectCorridorFaces(
            IndieEditableMeshData data,
            int startFace,
            int a0,
            int a1,
            int b0,
            int b1,
            HashSet<long> selectedKeys,
            Dictionary<long, List<int>> edgeFaces,
            HashSet<int> facesToRemove
        )
        {
            Vector3 pa0 = data.vertices[a0];
            Vector3 pa1 = data.vertices[a1];
            Vector3 pb0 = data.vertices[b0];
            Vector3 pb1 = data.vertices[b1];

            // Pair corners so the quad does not twist: a0 with the nearer B end.
            if ((pa0 - pb0).magnitude + (pa1 - pb1).magnitude >
                (pa0 - pb1).magnitude + (pa1 - pb0).magnitude)
            {
                Vector3 tmp = pb0;
                pb0 = pb1;
                pb1 = tmp;
            }

            var quad = new[] { pa0, pa1, pb1, pb0 };
            Vector3 normal = Vector3.Cross(pa1 - pa0, pb0 - pa0) + Vector3.Cross(pb0 - pb1, pa1 - pb1);

            if (normal.sqrMagnitude < 1e-16f)
                return null;

            normal.Normalize();
            Vector3 axisU = (pa1 - pa0).sqrMagnitude > 1e-16f ? (pa1 - pa0).normalized : Vector3.Cross(normal, Vector3.up);
            Vector3 axisV = Vector3.Cross(normal, axisU);
            var quad2D = new Vector2[quad.Length];

            for (int i = 0; i < quad.Length; i++)
                quad2D[i] = new Vector2(Vector3.Dot(quad[i] - pa0, axisU), Vector3.Dot(quad[i] - pa0, axisV));

            bool Inside(int faceIndex)
            {
                var face = data.faces[faceIndex];
                Vector3 c = (data.vertices[face.a] + data.vertices[face.b] + data.vertices[face.c]) * (1f / 3f);
                var c2 = new Vector2(Vector3.Dot(c - pa0, axisU), Vector3.Dot(c - pa0, axisV));
                return PointInPolygon2D(c2, quad2D);
            }

            if (!Inside(startFace))
                return null;

            var result = new List<int>();
            var visited = new HashSet<int> { startFace };
            var queue = new Queue<int>();
            queue.Enqueue(startFace);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                result.Add(current);

                if (result.Count > MaxCorridorFaces)
                    return null;

                var keys = FaceEdgeKeys(data.faces[current]);

                for (int k = 0; k < keys.Length; k++)
                {
                    if (selectedKeys.Contains(keys[k]) ||
                        !edgeFaces.TryGetValue(keys[k], out var neighbors))
                    {
                        continue;
                    }

                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int next = neighbors[n];

                        if (facesToRemove.Contains(next) || !visited.Add(next) || !Inside(next))
                            continue;

                        queue.Enqueue(next);
                    }
                }
            }

            return result;
        }

        private static bool PointInPolygon2D(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 pi = polygon[i];
                Vector2 pj = polygon[j];

                if ((pi.y > point.y) != (pj.y > point.y) &&
                    point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool CorridorHasEdge(IndieEditableMeshData data, List<int> corridor, long key)
        {
            foreach (int faceIndex in corridor)
            {
                var keys = FaceEdgeKeys(data.faces[faceIndex]);

                if (keys[0] == key || keys[1] == key || keys[2] == key)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Outer boundary of the corridor in face winding order. Fails unless it is one simple loop.
        /// </summary>
        private static bool TryGetCorridorLoop(IndieEditableMeshData data, List<int> corridor, out List<int> loop)
        {
            loop = null;
            var edgeUse = new Dictionary<long, int>();

            foreach (int faceIndex in corridor)
            {
                var keys = FaceEdgeKeys(data.faces[faceIndex]);

                for (int k = 0; k < keys.Length; k++)
                {
                    edgeUse.TryGetValue(keys[k], out int used);
                    edgeUse[keys[k]] = used + 1;
                }
            }

            var next = new Dictionary<int, int>();

            foreach (int faceIndex in corridor)
            {
                var face = data.faces[faceIndex];
                var sides = new[] { (face.a, face.b), (face.b, face.c), (face.c, face.a) };

                for (int i = 0; i < sides.Length; i++)
                {
                    var (from, to) = sides[i];

                    if (edgeUse[IndieEditMeshMeshUtil.EdgeKey(from, to)] != 1)
                        continue;

                    if (next.ContainsKey(from))
                        return false;

                    next.Add(from, to);
                }
            }

            if (next.Count < 4)
                return false;

            loop = new List<int>(next.Count);
            int startVertex = 0;

            foreach (int key in next.Keys)
            {
                startVertex = key;
                break;
            }

            int current = startVertex;

            do
            {
                loop.Add(current);

                if (!next.TryGetValue(current, out current) || loop.Count > next.Count)
                    return false;
            }
            while (current != startVertex);

            return loop.Count == next.Count;
        }

        /// <summary>
        /// Rotates the loop to X → Y (edge A) → chain → P → Q (edge B) → chain → X.
        /// Q pairs with X and P with Y, so the cuts run between matching ends.
        /// </summary>
        private static bool TrySplitLoopAtEdges(
            List<int> loop,
            int a0,
            int a1,
            int b0,
            int b1,
            out int x,
            out int y,
            out int p,
            out int q,
            out List<int> chainYP,
            out List<int> chainQX
        )
        {
            x = y = p = q = -1;
            chainYP = null;
            chainQX = null;
            int n = loop.Count;
            int startIndex = -1;

            for (int i = 0; i < n; i++)
            {
                int u = loop[i];
                int v = loop[(i + 1) % n];

                if ((u == a0 && v == a1) || (u == a1 && v == a0))
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
                return false;

            var rotated = new List<int>(n);

            for (int i = 0; i < n; i++)
                rotated.Add(loop[(startIndex + i) % n]);

            int bIndex = -1;

            for (int k = 2; k < n - 1; k++)
            {
                int u = rotated[k];
                int v = rotated[k + 1];

                if ((u == b0 && v == b1) || (u == b1 && v == b0))
                {
                    bIndex = k;
                    break;
                }
            }

            if (bIndex < 0)
                return false;

            x = rotated[0];
            y = rotated[1];
            p = rotated[bIndex];
            q = rotated[bIndex + 1];
            chainYP = rotated.GetRange(2, bIndex - 2);
            chainQX = rotated.GetRange(bIndex + 2, n - bIndex - 2);
            return true;
        }

        private static void BuildCorridorStrip(
            IndieEditableMeshData data,
            List<int> corridor,
            int x,
            int y,
            int p,
            int q,
            List<int> chainYP,
            List<int> chainQX,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache,
            List<IndieEditMeshFace> facesToAdd
        )
        {
            Vector3 refNormal = Vector3.zero;

            foreach (int faceIndex in corridor)
                refNormal += FaceNormal(data, data.faces[faceIndex]);

            refNormal = refNormal.sqrMagnitude > 1e-12f ? refNormal.normalized : Vector3.up;

            var source = data.faces[corridor[0]];
            int materialId = source.materialId;
            int smoothingGroups = 0;

            foreach (int faceIndex in corridor)
                smoothingGroups |= data.faces[faceIndex].smoothingGroups;

            bool flipSlide = Vector3.Dot(data.vertices[y] - data.vertices[x], slideAxis) < 0f;
            var ts = new List<float>(segments);

            for (int i = 0; i < segments; i++)
            {
                float t = GetConnectT(i, segments, pinch, slide);
                ts.Add(flipSlide ? 1f - t : t);
            }

            ts.Sort();

            // Points on X → Y and on Q → P share t, so each cut joins matching heights.
            var pointsA = new List<int>(segments);
            var pointsB = new List<int>(segments);

            for (int i = 0; i < ts.Count; i++)
            {
                pointsA.Add(IndieEditMeshMeshUtil.SplitEdgeAt(data, x, y, ts[i], splitCache));
                pointsB.Add(IndieEditMeshMeshUtil.SplitEdgeAt(data, q, p, ts[i], splitCache));
            }

            int addStart = facesToAdd.Count;
            int last = pointsA.Count - 1;

            var capStart = new List<int> { x, pointsA[0], pointsB[0], q };
            capStart.AddRange(chainQX);
            IndieEditMeshPolygonTriangulator.TriangulateEar(
                data,
                facesToAdd,
                capStart,
                refNormal,
                materialId,
                smoothingGroups
            );

            for (int i = 0; i < last; i++)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    facesToAdd,
                    pointsA[i],
                    pointsA[i + 1],
                    pointsB[i + 1],
                    pointsB[i],
                    materialId,
                    smoothingGroups
                );
            }

            var capEnd = new List<int> { pointsA[last], y };
            capEnd.AddRange(chainYP);
            capEnd.Add(p);
            capEnd.Add(pointsB[last]);
            IndieEditMeshPolygonTriangulator.TriangulateEar(
                data,
                facesToAdd,
                capEnd,
                refNormal,
                materialId,
                smoothingGroups
            );

            OrientNewFacesToReference(data, facesToAdd, addStart, refNormal);
        }

        private const float AdjacentSplitEndEpsilon = 1e-5f;

        /// <summary>
        /// Faces that share a cut edge with the connect but were not rebuilt still run
        /// corner to corner; fan them from the opposite corner through the new points.
        /// Seam duplicates (same position, other indices) get their own verts so UV islands stay apart.
        /// </summary>
        private static void SplitFacesAdjacentToConnect(
            IndieEditableMeshData data,
            Dictionary<long, List<int>> splitCache,
            HashSet<int> facesToRemove,
            List<IndieEditMeshFace> facesToAdd
        )
        {
            if (splitCache.Count == 0)
                return;

            var cutEdges = new List<(long key, int v0, int v1, List<int> points)>(splitCache.Count);

            foreach (var pair in splitCache)
            {
                IndieEditMeshMeshUtil.DecodeEdgeKey(pair.Key, out int v0, out int v1);
                cutEdges.Add((pair.Key, v0, v1, new List<int>(pair.Value)));
            }

            int faceCount = data.faces.Count;

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                if (facesToRemove.Contains(faceIndex))
                    continue;

                var face = data.faces[faceIndex];

                if (!TryGetSingleCutEdge(
                        data,
                        face,
                        cutEdges,
                        out int start,
                        out int end,
                        out int opposite,
                        out int cutIndex))
                {
                    continue;
                }

                var points = GetFacePointsOnCutEdge(data, start, end, cutEdges[cutIndex], splitCache);

                if (points.Count == 0)
                    continue;

                Vector3 refNormal = FaceNormal(data, face);
                int addStart = facesToAdd.Count;
                int previous = start;

                for (int i = 0; i < points.Count; i++)
                {
                    IndieEditMeshMeshUtil.AddTriangle(
                        facesToAdd,
                        previous,
                        points[i],
                        opposite,
                        face.materialId,
                        face.smoothingGroups
                    );
                    previous = points[i];
                }

                IndieEditMeshMeshUtil.AddTriangle(
                    facesToAdd,
                    previous,
                    end,
                    opposite,
                    face.materialId,
                    face.smoothingGroups
                );

                facesToRemove.Add(faceIndex);
                OrientNewFacesToReference(data, facesToAdd, addStart, refNormal);
            }
        }

        /// <summary>
        /// Exactly one edge of the face lies on a cut edge (same indices first, then same positions).
        /// start → end follows the face winding; opposite is the remaining corner.
        /// </summary>
        private static bool TryGetSingleCutEdge(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            List<(long key, int v0, int v1, List<int> points)> cutEdges,
            out int start,
            out int end,
            out int opposite,
            out int cutIndex
        )
        {
            start = end = opposite = cutIndex = -1;
            int count = data.vertices.Count;

            if (face.a < 0 || face.b < 0 || face.c < 0 ||
                face.a >= count || face.b >= count || face.c >= count)
            {
                return false;
            }

            var sides = new[]
            {
                (s: face.a, e: face.b, o: face.c),
                (s: face.b, e: face.c, o: face.a),
                (s: face.c, e: face.a, o: face.b)
            };
            int matches = 0;

            for (int i = 0; i < sides.Length; i++)
            {
                int match = FindCutEdge(data, sides[i].s, sides[i].e, cutEdges);

                if (match < 0)
                    continue;

                matches++;
                start = sides[i].s;
                end = sides[i].e;
                opposite = sides[i].o;
                cutIndex = match;
            }

            return matches == 1;
        }

        private static int FindCutEdge(
            IndieEditableMeshData data,
            int v0,
            int v1,
            List<(long key, int v0, int v1, List<int> points)> cutEdges
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            for (int i = 0; i < cutEdges.Count; i++)
            {
                if (cutEdges[i].key == key)
                    return i;
            }

            for (int i = 0; i < cutEdges.Count; i++)
            {
                if (EdgesGeometricallyEqual(data, v0, v1, cutEdges[i].v0, cutEdges[i].v1))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// New points on start → end, sorted from start. The same edge reuses the cut verts;
        /// a seam duplicate gets its own verts at the same positions with UV lerped
        /// between this face's corners.
        /// </summary>
        private static List<int> GetFacePointsOnCutEdge(
            IndieEditableMeshData data,
            int start,
            int end,
            (long key, int v0, int v1, List<int> points) cut,
            Dictionary<long, List<int>> splitCache
        )
        {
            Vector3 p0 = data.vertices[start];
            Vector3 dir = data.vertices[end] - p0;
            float lengthSq = dir.sqrMagnitude;
            var ordered = new List<(float t, int index)>();

            if (lengthSq < 1e-16f)
                return new List<int>();

            bool sameEdge = IndieEditMeshMeshUtil.EdgeKey(start, end) == cut.key;

            for (int i = 0; i < cut.points.Count; i++)
            {
                int point = cut.points[i];
                float t = Vector3.Dot(data.vertices[point] - p0, dir) / lengthSq;

                if (t <= AdjacentSplitEndEpsilon || t >= 1f - AdjacentSplitEndEpsilon)
                    continue;

                int index = sameEdge
                    ? point
                    : IndieEditMeshMeshUtil.SplitEdgeAt(data, start, end, t, splitCache);

                ordered.Add((t, index));
            }

            ordered.Sort((x, y) => x.t.CompareTo(y.t));

            var result = new List<int>(ordered.Count);

            for (int i = 0; i < ordered.Count; i++)
            {
                int index = ordered[i].index;

                if (index == start || index == end || result.Contains(index))
                    continue;

                result.Add(index);
            }

            return result;
        }

        private static void ExpandSelectedEdgesWithCoincident(
            IndieEditableMeshData data,
            List<(int v0, int v1)> selectedEdgeEnds,
            HashSet<long> selectedKeys
        )
        {
            if (data == null || selectedEdgeEnds.Count == 0)
                return;

            var seeds = new List<(int v0, int v1)>(selectedEdgeEnds);

            for (int e = 0; e < data.edges.Count; e++)
            {
                var edge = data.edges[e];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (selectedKeys.Contains(key))
                    continue;

                for (int s = 0; s < seeds.Count; s++)
                {
                    if (!EdgesGeometricallyEqual(
                            data,
                            edge.v0,
                            edge.v1,
                            seeds[s].v0,
                            seeds[s].v1))
                    {
                        continue;
                    }

                    selectedKeys.Add(key);
                    selectedEdgeEnds.Add((edge.v0, edge.v1));
                    break;
                }
            }
        }

        private static void TryConnectQuads(
            IndieEditableMeshData data,
            HashSet<long> selectedKeys,
            List<(int v0, int v1)> selectedEdgeEnds,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache,
            HashSet<int> facesToRemove,
            List<IndieEditMeshFace> facesToAdd,
            HashSet<long> connectedQuadKeys,
            bool geometric
        )
        {
            for (int i = 0; i < data.faces.Count; i++)
            {
                if (facesToRemove.Contains(i))
                    continue;

                for (int j = i + 1; j < data.faces.Count; j++)
                {
                    if (facesToRemove.Contains(j))
                        continue;

                    if (!TryGetSharedEdge(data.faces[i], data.faces[j], out int s0, out int s1))
                        continue;

                    long sharedKey = IndieEditMeshMeshUtil.EdgeKey(s0, s1);

                    // Only skip quads we already connected — failed tries stay candidates.
                    if (connectedQuadKeys.Contains(sharedKey))
                        continue;

                    bool matched = geometric
                        ? TryGetQuadOppositeGeometricEdges(
                            data,
                            data.faces[i],
                            data.faces[j],
                            s0,
                            s1,
                            selectedEdgeEnds,
                            out int a0,
                            out int a1,
                            out int b0,
                            out int b1)
                        : TryGetQuadOppositeSelectedEdges(
                            data.faces[i],
                            data.faces[j],
                            s0,
                            s1,
                            selectedKeys,
                            out a0,
                            out a1,
                            out b0,
                            out b1);

                    if (!matched)
                        continue;

                    connectedQuadKeys.Add(sharedKey);
                    ConnectAcrossQuad(
                        data,
                        i,
                        j,
                        a0,
                        a1,
                        b0,
                        b1,
                        segments,
                        pinch,
                        slide,
                        slideAxis,
                        splitCache,
                        facesToRemove,
                        facesToAdd
                    );
                }
            }
        }

        private static List<(int v0, int v1)> GetSelectedEdgesOnFace(
            IndieEditMeshFace face,
            HashSet<long> selectedKeys
        )
        {
            var result = new List<(int v0, int v1)>();
            var edges = new[]
            {
                (face.a, face.b),
                (face.b, face.c),
                (face.c, face.a)
            };

            foreach (var edge in edges)
            {
                if (selectedKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(edge.Item1, edge.Item2)))
                    result.Add(edge);
            }

            return result;
        }

        private static List<(int v0, int v1)> GetGeometricSelectedEdgesOnFace(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            List<(int v0, int v1)> selectedEdgeEnds
        )
        {
            var result = new List<(int v0, int v1)>();
            var edges = new[]
            {
                (face.a, face.b),
                (face.b, face.c),
                (face.c, face.a)
            };

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                bool hit = false;

                for (int s = 0; s < selectedEdgeEnds.Count; s++)
                {
                    if (!EdgesGeometricallyEqual(
                            data,
                            edge.Item1,
                            edge.Item2,
                            selectedEdgeEnds[s].v0,
                            selectedEdgeEnds[s].v1))
                    {
                        continue;
                    }

                    hit = true;
                    break;
                }

                if (hit)
                    result.Add(edge);
            }

            return result;
        }

        private static bool TryGetQuadOppositeSelectedEdges(
            IndieEditMeshFace f0,
            IndieEditMeshFace f1,
            int s0,
            int s1,
            HashSet<long> selectedKeys,
            out int a0,
            out int a1,
            out int b0,
            out int b1
        )
        {
            a0 = a1 = b0 = b1 = -1;

            if (!TryGetQuadOppositeEdgeCandidates(f0, f1, s0, s1, out var candidates))
                return false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var pair = candidates[i];
                long k0 = IndieEditMeshMeshUtil.EdgeKey(pair.a0, pair.a1);
                long k1 = IndieEditMeshMeshUtil.EdgeKey(pair.b0, pair.b1);

                if (selectedKeys.Contains(k0) && selectedKeys.Contains(k1))
                {
                    a0 = pair.a0;
                    a1 = pair.a1;
                    b0 = pair.b0;
                    b1 = pair.b1;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetQuadOppositeGeometricEdges(
            IndieEditableMeshData data,
            IndieEditMeshFace f0,
            IndieEditMeshFace f1,
            int s0,
            int s1,
            List<(int v0, int v1)> selectedEdgeEnds,
            out int a0,
            out int a1,
            out int b0,
            out int b1
        )
        {
            a0 = a1 = b0 = b1 = -1;

            if (!TryGetQuadOppositeEdgeCandidates(f0, f1, s0, s1, out var candidates))
                return false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var pair = candidates[i];

                if (!SelectedEdgesCoverGeometricPair(
                        data,
                        selectedEdgeEnds,
                        pair.a0,
                        pair.a1,
                        pair.b0,
                        pair.b1))
                {
                    continue;
                }

                a0 = pair.a0;
                a1 = pair.a1;
                b0 = pair.b0;
                b1 = pair.b1;
                return true;
            }

            return false;
        }

        private static bool TryGetQuadOppositeEdgeCandidates(
            IndieEditMeshFace f0,
            IndieEditMeshFace f1,
            int s0,
            int s1,
            out (int a0, int a1, int b0, int b1)[] candidates
        )
        {
            candidates = null;

            if (!IndieEditMeshMeshUtil.TryGetThirdVertex(f0, s0, s1, out int c))
                return false;

            if (!IndieEditMeshMeshUtil.TryGetThirdVertex(f1, s0, s1, out int d))
                return false;

            // Quad boundary opposite pairs: (s0,c)-(s1,d) and (c,s1)-(d,s0)
            candidates = new[]
            {
                (a0: s0, a1: c, b0: s1, b1: d),
                (a0: c, a1: s1, b0: d, b1: s0)
            };
            return true;
        }

        private static bool SelectedEdgesCoverGeometricPair(
            IndieEditableMeshData data,
            List<(int v0, int v1)> selectedEdgeEnds,
            int a0,
            int a1,
            int b0,
            int b1
        )
        {
            bool hitA = false;
            bool hitB = false;

            for (int i = 0; i < selectedEdgeEnds.Count; i++)
            {
                var edge = selectedEdgeEnds[i];

                if (!hitA &&
                    EdgesGeometricallyEqual(data, a0, a1, edge.v0, edge.v1))
                {
                    hitA = true;
                }

                if (!hitB &&
                    EdgesGeometricallyEqual(data, b0, b1, edge.v0, edge.v1))
                {
                    hitB = true;
                }

                if (hitA && hitB)
                    return true;
            }

            return false;
        }

        private static bool EdgesGeometricallyEqual(
            IndieEditableMeshData data,
            int a0,
            int a1,
            int b0,
            int b1
        )
        {
            if (a0 < 0 || a1 < 0 || b0 < 0 || b1 < 0 ||
                a0 >= data.vertices.Count || a1 >= data.vertices.Count ||
                b0 >= data.vertices.Count || b1 >= data.vertices.Count)
            {
                return false;
            }

            Vector3 pa0 = data.vertices[a0];
            Vector3 pa1 = data.vertices[a1];
            Vector3 pb0 = data.vertices[b0];
            Vector3 pb1 = data.vertices[b1];

            return (PositionsNear(pa0, pb0) && PositionsNear(pa1, pb1)) ||
                   (PositionsNear(pa0, pb1) && PositionsNear(pa1, pb0));
        }

        private static bool PositionsNear(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= ConnectCoincidentEpsilonSq;
        }

        private static void ConnectAcrossQuad(
            IndieEditableMeshData data,
            int faceIndex0,
            int faceIndex1,
            int a0,
            int a1,
            int b0,
            int b1,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache,
            HashSet<int> facesToRemove,
            List<IndieEditMeshFace> facesToAdd
        )
        {
            // Keep the outward direction of the polygons we replace.
            Vector3 refNormal = FaceNormal(data, data.faces[faceIndex0]);
            Vector3 n1 = FaceNormal(data, data.faces[faceIndex1]);

            if (n1.sqrMagnitude > 1e-12f)
            {
                if (refNormal.sqrMagnitude < 1e-12f)
                    refNormal = n1;
                else if (Vector3.Dot(refNormal, n1) > 0f)
                    refNormal = (refNormal + n1).normalized;
            }

            // Unify winding along slide axis so the same t lands at the same height
            // on every edge (positive Slide → toward +slideAxis).
            AlignEdgeAlongAxis(data, ref a0, ref a1, slideAxis);
            AlignEdgeAlongAxis(data, ref b0, ref b1, slideAxis);

            var pointsA = new List<int>();
            var pointsB = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float t = GetConnectT(i, segments, pinch, slide);
                pointsA.Add(IndieEditMeshMeshUtil.SplitEdgeAt(data, a0, a1, t, splitCache));
                pointsB.Add(IndieEditMeshMeshUtil.SplitEdgeAt(data, b0, b1, t, splitCache));
            }

            int materialId = data.faces[faceIndex0].materialId;
            int smoothingGroups = data.faces[faceIndex0].smoothingGroups |
                                  data.faces[faceIndex1].smoothingGroups;
            facesToRemove.Add(faceIndex0);
            facesToRemove.Add(faceIndex1);

            int addStart = facesToAdd.Count;

            // Rebuild the quad as strips between the two selected edges.
            IndieEditMeshMeshUtil.AddQuad(
                facesToAdd,
                a0,
                pointsA[0],
                pointsB[0],
                b0,
                materialId,
                smoothingGroups
            );

            for (int i = 0; i < pointsA.Count - 1; i++)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    facesToAdd,
                    pointsA[i],
                    pointsA[i + 1],
                    pointsB[i + 1],
                    pointsB[i],
                    materialId,
                    smoothingGroups
                );
            }

            IndieEditMeshMeshUtil.AddQuad(
                facesToAdd,
                pointsA[pointsA.Count - 1],
                a1,
                b1,
                pointsB[pointsB.Count - 1],
                materialId,
                smoothingGroups
            );

            OrientNewFacesToReference(data, facesToAdd, addStart, refNormal);
        }

        /// <summary>
        /// Flip newly added triangles so they face the same way as the polygons
        /// they replaced (per-batch, axis-independent).
        /// </summary>
        private static void OrientNewFacesToReference(
            IndieEditableMeshData data,
            List<IndieEditMeshFace> faces,
            int fromIndex,
            Vector3 refNormal
        )
        {
            if (faces == null || fromIndex >= faces.Count || refNormal.sqrMagnitude < 1e-12f)
                return;

            for (int i = fromIndex; i < faces.Count; i++)
            {
                var face = faces[i];
                Vector3 n = FaceNormal(data, face);

                if (n.sqrMagnitude < 1e-12f)
                    continue;

                if (Vector3.Dot(n, refNormal) >= 0f)
                    continue;

                int tmp = face.b;
                face.b = face.c;
                face.c = tmp;
                faces[i] = face;
            }
        }

        /// <summary>
        /// Average direction of selected edges, biased to world-up so +Slide moves up.
        /// </summary>
        private static Vector3 GetConnectSlideAxis(
            IndieEditableMeshData data,
            HashSet<long> selectedKeys
        )
        {
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (!selectedKeys.Contains(key))
                    continue;

                Vector3 dir = data.vertices[edge.v1] - data.vertices[edge.v0];

                if (dir.sqrMagnitude < 1e-16f)
                    continue;

                dir.Normalize();

                if (Vector3.Dot(dir, Vector3.up) < 0f)
                    dir = -dir;

                sum += dir;
            }

            if (sum.sqrMagnitude < 1e-12f)
                return Vector3.up;

            return sum.normalized;
        }

        private static void AlignEdgeAlongAxis(
            IndieEditableMeshData data,
            ref int v0,
            ref int v1,
            Vector3 axis
        )
        {
            Vector3 dir = data.vertices[v1] - data.vertices[v0];

            if (dir.sqrMagnitude < 1e-16f)
                return;

            if (Vector3.Dot(dir, axis) < 0f)
            {
                int tmp = v0;
                v0 = v1;
                v1 = tmp;
            }
        }

        private static void RemoveSingleEdge(IndieEditableMeshData data, int v0, int v1)
        {
            var faceIndices = new List<int>();
            IndieEditMeshMeshUtil.CollectFacesWithEdge(data, v0, v1, faceIndices);

            if (faceIndices.Count != 2)
                return;

            var f0 = data.faces[faceIndices[0]];
            var f1 = data.faces[faceIndices[1]];

            if (!IndieEditMeshMeshUtil.TryGetThirdVertex(f0, v0, v1, out int c))
                return;

            if (!IndieEditMeshMeshUtil.TryGetThirdVertex(f1, v0, v1, out int d))
                return;

            if (c == d)
                return;

            int materialId = f0.materialId;
            var removeSet = new HashSet<int>(faceIndices);
            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!removeSet.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            // Alternate diagonal of the quad (v0,c,v1,d).
            IndieEditMeshMeshUtil.AddTriangle(newFaces, v0, c, d, materialId);
            IndieEditMeshMeshUtil.AddTriangle(newFaces, c, v1, d, materialId);

            data.faces = newFaces;
            data.RebuildEdges();
        }

        private static void SplitVertexAlongSelectedEdges(
            IndieEditableMeshData data,
            int vertex,
            HashSet<long> selectedKeys
        )
        {
            var faceIndices = new List<int>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];

                if (face.a == vertex || face.b == vertex || face.c == vertex)
                    faceIndices.Add(i);
            }

            if (faceIndices.Count <= 1)
                return;

            var adjacency = new Dictionary<int, List<int>>();

            void Link(int a, int b)
            {
                if (!adjacency.TryGetValue(a, out var listA))
                {
                    listA = new List<int>();
                    adjacency.Add(a, listA);
                }

                if (!adjacency.TryGetValue(b, out var listB))
                {
                    listB = new List<int>();
                    adjacency.Add(b, listB);
                }

                if (!listA.Contains(b))
                    listA.Add(b);

                if (!listB.Contains(a))
                    listB.Add(a);
            }

            for (int i = 0; i < faceIndices.Count; i++)
            {
                for (int j = i + 1; j < faceIndices.Count; j++)
                {
                    int fi = faceIndices[i];
                    int fj = faceIndices[j];
                    var fa = data.faces[fi];
                    var fb = data.faces[fj];

                    if (!TryGetSharedEdge(fa, fb, out int s0, out int s1))
                        continue;

                    if (s0 != vertex && s1 != vertex)
                        continue;

                    long key = IndieEditMeshMeshUtil.EdgeKey(s0, s1);

                    if (selectedKeys.Contains(key))
                        continue;

                    Link(fi, fj);
                }
            }

            var components = new List<List<int>>();
            var visited = new HashSet<int>();

            foreach (int faceIndex in faceIndices)
            {
                if (!visited.Add(faceIndex))
                    continue;

                var component = new List<int>();
                var stack = new Stack<int>();
                stack.Push(faceIndex);

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    component.Add(current);

                    if (!adjacency.TryGetValue(current, out var neighbors))
                        continue;

                    foreach (int neighbor in neighbors)
                    {
                        if (visited.Add(neighbor))
                            stack.Push(neighbor);
                    }
                }

                components.Add(component);
            }

            if (components.Count <= 1)
                return;

            for (int i = 1; i < components.Count; i++)
            {
                int copy = IndieEditMeshMeshUtil.AddVertex(data, vertex, data.vertices[vertex]);

                foreach (int faceIndex in components[i])
                {
                    var face = data.faces[faceIndex];

                    if (face.a == vertex)
                        face.a = copy;
                    else if (face.b == vertex)
                        face.b = copy;
                    else if (face.c == vertex)
                        face.c = copy;

                    data.faces[faceIndex] = face;
                }
            }
        }

        private static void ForceSplitSharedEdge(IndieEditableMeshData data, int v0, int v1)
        {
            // Resolve current indices: after prior vertex splits, the originally
            // selected endpoints may already be duplicated. Find faces that still
            // share an edge between ANY copies of the original positions is hard;
            // instead find faces that currently share edge (v0,v1) OR any edge
            // whose endpoints are still exactly those indices.
            var faceIndices = new List<int>();
            IndieEditMeshMeshUtil.CollectFacesWithEdge(data, v0, v1, faceIndices);

            if (faceIndices.Count < 2)
            {
                // Edge endpoints may have been renamed on one side already.
                // Scan for any pair of faces that share an edge and both touch
                // the cut region — fallback: find faces still sharing both verts.
                return;
            }

            // Keep face[0] on original verts; give every other face its own copies.
            for (int i = 1; i < faceIndices.Count; i++)
            {
                int faceIndex = faceIndices[i];
                var face = data.faces[faceIndex];

                // Re-read endpoints from this face (still v0/v1 if shared).
                if (!IndieEditMeshMeshUtil.FaceHasEdge(face, v0, v1))
                    continue;

                int c0 = IndieEditMeshMeshUtil.AddVertex(data, v0, data.vertices[v0]);
                int c1 = IndieEditMeshMeshUtil.AddVertex(data, v1, data.vertices[v1]);

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
        }

        private static bool TryGetSharedEdge(
            IndieEditMeshFace a,
            IndieEditMeshFace b,
            out int v0,
            out int v1
        )
        {
            var edgesA = new long[]
            {
                IndieEditMeshMeshUtil.EdgeKey(a.a, a.b),
                IndieEditMeshMeshUtil.EdgeKey(a.b, a.c),
                IndieEditMeshMeshUtil.EdgeKey(a.c, a.a)
            };

            var mapA = new Dictionary<long, (int, int)>
            {
                { edgesA[0], (a.a, a.b) },
                { edgesA[1], (a.b, a.c) },
                { edgesA[2], (a.c, a.a) }
            };

            long[] edgesB =
            {
                IndieEditMeshMeshUtil.EdgeKey(b.a, b.b),
                IndieEditMeshMeshUtil.EdgeKey(b.b, b.c),
                IndieEditMeshMeshUtil.EdgeKey(b.c, b.a)
            };

            foreach (long key in edgesB)
            {
                if (mapA.TryGetValue(key, out var ends))
                {
                    v0 = ends.Item1;
                    v1 = ends.Item2;
                    return true;
                }
            }

            v0 = -1;
            v1 = -1;
            return false;
        }

        /// <summary>
        /// Max-like edge extrude: leave every existing polygon untouched.
        /// Duplicate the edge, add one bridge strip (no back-side face).
        /// Strip winding is opposite to the adjacent face on the shared edge so
        /// the new polys inherit that face's normal sign.
        /// </summary>
        private static void ExtrudeSingle(
            IndieEditableMeshData data,
            int v0,
            int v1,
            float height,
            float baseWidth,
            out int extruded0,
            out int extruded1
        )
        {
            extruded0 = -1;
            extruded1 = -1;

            if (v0 < 0 || v1 < 0 || v0 >= data.vertices.Count || v1 >= data.vertices.Count)
                return;

            var faceIndices = new List<int>();
            IndieEditMeshMeshUtil.CollectFacesWithEdge(data, v0, v1, faceIndices);

            int materialId = 0;
            int sg = 0;
            bool faceRunsV0ToV1 = true;
            Vector3 adjacentNormal = Vector3.up;

            if (faceIndices.Count > 0)
            {
                var adj = data.faces[faceIndices[0]];
                materialId = adj.materialId;
                sg = adj.smoothingGroups;
                adjacentNormal = FaceNormal(data, adj);

                if (!TryGetFaceEdgeDirection(adj, v0, v1, out faceRunsV0ToV1))
                    faceRunsV0ToV1 = true;
            }

            Vector3 normal = IndieEditMeshMeshUtil.ComputeEdgeNormal(data, v0, v1);
            Vector3 p0 = data.vertices[v0];
            Vector3 p1 = data.vertices[v1];
            Vector3 edgeDir = p1 - p0;

            Vector3 lateral = Vector3.zero;
            float width = Mathf.Max(0f, baseWidth);

            if (width > 1e-8f && edgeDir.sqrMagnitude > 1e-12f)
            {
                lateral = Vector3.Cross(normal, edgeDir.normalized);

                if (lateral.sqrMagnitude > 1e-12f)
                    lateral = lateral.normalized * width;
            }

            Vector3 offset = normal * height + lateral;
            int e0 = IndieEditMeshMeshUtil.AddVertex(data, v0, p0 + offset);
            int e1 = IndieEditMeshMeshUtil.AddVertex(data, v1, p1 + offset);
            extruded0 = e0;
            extruded1 = e1;

            // Opposite winding on shared edge → same normal sign as adjacent poly.
            // Face v0→v1  =>  strip v1→v0→e0→e1
            // Face v1→v0  =>  strip v0→v1→e1→e0
            int a = faceRunsV0ToV1 ? v1 : v0;
            int b = faceRunsV0ToV1 ? v0 : v1;
            int c = faceRunsV0ToV1 ? e0 : e1;
            int d = faceRunsV0ToV1 ? e1 : e0;

            // Non-zero offset: if strip still faces against the adjacent normal, reverse.
            if (offset.sqrMagnitude > 1e-12f && adjacentNormal.sqrMagnitude > 1e-12f)
            {
                Vector3 stripN = Vector3.Cross(
                    data.vertices[b] - data.vertices[a],
                    data.vertices[c] - data.vertices[a]
                );

                if (stripN.sqrMagnitude > 1e-12f &&
                    Vector3.Dot(stripN, adjacentNormal) < 0f)
                {
                    // Reverse loop: a,b,c,d → a,d,c,b
                    int tmp = b;
                    b = d;
                    d = tmp;
                }
            }

            IndieEditMeshMeshUtil.AddQuadStable(
                data.faces,
                data.vertices,
                a,
                b,
                c,
                d,
                materialId,
                sg
            );

            data.RebuildEdges();
        }

        private static Vector3 FaceNormal(IndieEditableMeshData data, IndieEditMeshFace face)
        {
            Vector3 n = Vector3.Cross(
                data.vertices[face.b] - data.vertices[face.a],
                data.vertices[face.c] - data.vertices[face.a]
            );
            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        private static bool TryGetFaceEdgeDirection(
            IndieEditMeshFace face,
            int v0,
            int v1,
            out bool runsV0ToV1
        )
        {
            if ((face.a == v0 && face.b == v1) ||
                (face.b == v0 && face.c == v1) ||
                (face.c == v0 && face.a == v1))
            {
                runsV0ToV1 = true;
                return true;
            }

            if ((face.a == v1 && face.b == v0) ||
                (face.b == v1 && face.c == v0) ||
                (face.c == v1 && face.a == v0))
            {
                runsV0ToV1 = false;
                return true;
            }

            runsV0ToV1 = true;
            return false;
        }


        private static void BridgePair(
            IndieEditableMeshData data,
            (int v0, int v1) edgeA,
            (int v0, int v1) edgeB,
            int segments,
            int twist,
            float taper,
            float bias,
            List<int> bridgeVertsOut
        )
        {
            int a0 = edgeA.v0;
            int a1 = edgeA.v1;
            int b0 = edgeB.v0;
            int b1 = edgeB.v1;

            // Orient each border edge along its face winding.
            OrientBorderEdgeByFace(data, ref a0, ref a1);
            OrientBorderEdgeByFace(data, ref b0, ref b1);

            // Twist rotates correspondence at end B.
            if (((twist % 2) + 2) % 2 == 1)
            {
                int tmp = b0;
                b0 = b1;
                b1 = tmp;
            }

            // Pick endpoint pairing that does not cross (shortest total links).
            float straight =
                Vector3.Distance(data.vertices[a0], data.vertices[b0]) +
                Vector3.Distance(data.vertices[a1], data.vertices[b1]);
            float crossed =
                Vector3.Distance(data.vertices[a0], data.vertices[b1]) +
                Vector3.Distance(data.vertices[a1], data.vertices[b0]);

            if (crossed < straight - 1e-8f)
            {
                int tmp = b0;
                b0 = b1;
                b1 = tmp;
            }

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

                int i0 = IndieEditMeshMeshUtil.AddVertex(data, a0, p0);
                int i1 = IndieEditMeshMeshUtil.AddVertex(data, a1, p1);
                rows.Add(new[] { i0, i1 });
            }

            rows.Add(new[] { b0, b1 });

            foreach (var row in rows)
            {
                bridgeVertsOut.Add(row[0]);
                bridgeVertsOut.Add(row[1]);
            }

            int materialId = 0;
            Vector3 normalA = Vector3.zero;
            Vector3 normalB = Vector3.zero;

            if (TryGetBorderEdgeFaceNormal(data, a0, a1, out normalA, out int faceA))
                materialId = data.faces[faceA].materialId;

            TryGetBorderEdgeFaceNormal(data, b0, b1, out normalB, out _);

            Vector3 refNormal = BuildBridgeReferenceNormal(
                data,
                a0,
                a1,
                b0,
                b1,
                normalA,
                normalB
            );

            int facesBefore = data.faces.Count;

            // Quad strip: a0 -> b0 -> b1 -> a1 (two tris = one Max-style quad).
            for (int i = 0; i < rows.Count - 1; i++)
            {
                int q0 = rows[i][0];
                int q1 = rows[i + 1][0];
                int q2 = rows[i + 1][1];
                int q3 = rows[i][1];

                IndieEditMeshMeshUtil.AddQuadStable(
                    data.faces,
                    data.vertices,
                    q0,
                    q1,
                    q2,
                    q3,
                    materialId
                );
            }

            // One sign for the whole strip: match normals of faces on edges A/B.
            FlipFacesIfOppositeToReference(data, facesBefore, refNormal);
        }

        private static void OrientBorderEdgeByFace(IndieEditableMeshData data, ref int v0, ref int v1)
        {
            var faceIndices = new List<int>();
            IndieEditMeshMeshUtil.CollectFacesWithEdge(data, v0, v1, faceIndices);

            if (faceIndices.Count != 1)
                return;

            var face = data.faces[faceIndices[0]];

            bool matchesWinding =
                (face.a == v0 && face.b == v1) ||
                (face.b == v0 && face.c == v1) ||
                (face.c == v0 && face.a == v1);

            if (!matchesWinding)
            {
                int tmp = v0;
                v0 = v1;
                v1 = tmp;
            }
        }

        private static bool TryGetBorderEdgeFaceNormal(
            IndieEditableMeshData data,
            int v0,
            int v1,
            out Vector3 normal,
            out int faceIndex
        )
        {
            normal = Vector3.zero;
            faceIndex = -1;

            var faceIndices = new List<int>();
            IndieEditMeshMeshUtil.CollectFacesWithEdge(data, v0, v1, faceIndices);

            if (faceIndices.Count == 0)
                return false;

            // Border edge: one face. If several (non-manifold), average them.
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < faceIndices.Count; i++)
            {
                Vector3 n = ComputeFaceNormal(data, data.faces[faceIndices[i]]);

                if (n.sqrMagnitude < 1e-12f)
                    continue;

                sum += n.normalized;
                count++;
                faceIndex = faceIndices[i];
            }

            if (count == 0)
                return false;

            normal = (sum / count).normalized;
            return true;
        }

        private static Vector3 BuildBridgeReferenceNormal(
            IndieEditableMeshData data,
            int a0,
            int a1,
            int b0,
            int b1,
            Vector3 normalA,
            Vector3 normalB
        )
        {
            bool hasA = normalA.sqrMagnitude > 1e-8f;
            bool hasB = normalB.sqrMagnitude > 1e-8f;

            if (hasA && hasB && Vector3.Dot(normalA, normalB) > 0.1f)
                return (normalA + normalB).normalized;

            if (hasA && !hasB)
                return normalA;

            if (hasB && !hasA)
                return normalB;

            Vector3 aMid = (data.vertices[a0] + data.vertices[a1]) * 0.5f;
            Vector3 bMid = (data.vertices[b0] + data.vertices[b1]) * 0.5f;
            Vector3 edgeA = data.vertices[a1] - data.vertices[a0];
            Vector3 gap = bMid - aMid;
            Vector3 planeN = Vector3.Cross(edgeA, gap);

            if (planeN.sqrMagnitude < 1e-12f)
            {
                Vector3 edgeB = data.vertices[b1] - data.vertices[b0];
                planeN = Vector3.Cross(edgeB, -gap);
            }

            if (planeN.sqrMagnitude < 1e-12f)
            {
                if (hasA)
                    return normalA;

                if (hasB)
                    return normalB;

                return Vector3.up;
            }

            planeN.Normalize();

            // When N_a and N_b disagree, pick the plane sign that best matches both.
            float scorePos = 0f;
            float scoreNeg = 0f;

            if (hasA)
            {
                scorePos += Vector3.Dot(planeN, normalA);
                scoreNeg += Vector3.Dot(-planeN, normalA);
            }

            if (hasB)
            {
                scorePos += Vector3.Dot(planeN, normalB);
                scoreNeg += Vector3.Dot(-planeN, normalB);
            }

            return scoreNeg > scorePos ? -planeN : planeN;
        }

        private static void FlipFacesIfOppositeToReference(
            IndieEditableMeshData data,
            int facesFrom,
            Vector3 refNormal
        )
        {
            if (facesFrom >= data.faces.Count || refNormal.sqrMagnitude < 1e-12f)
                return;

            Vector3 stripNormal = Vector3.zero;
            int count = 0;

            for (int i = facesFrom; i < data.faces.Count; i++)
            {
                Vector3 n = ComputeFaceNormal(data, data.faces[i]);

                if (n.sqrMagnitude < 1e-12f)
                    continue;

                stripNormal += n.normalized;
                count++;
            }

            if (count == 0)
                return;

            stripNormal /= count;

            if (Vector3.Dot(stripNormal, refNormal) >= 0f)
                return;

            for (int i = facesFrom; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                int tmp = face.b;
                face.b = face.c;
                face.c = tmp;
                data.faces[i] = face;
            }
        }

        private static Vector3 ComputeFaceNormal(IndieEditableMeshData data, IndieEditMeshFace face)
        {
            Vector3 a = data.vertices[face.a];
            Vector3 b = data.vertices[face.b];
            Vector3 c = data.vertices[face.c];
            return Vector3.Cross(b - a, c - a);
        }

        private static void WeldBridgeToNearby(
            IndieEditableMeshData data,
            List<int> bridgeVerts,
            float threshold
        )
        {
            if (data == null || bridgeVerts == null || bridgeVerts.Count == 0)
                return;

            float thresholdSq = threshold * threshold;
            int[] remap = new int[data.vertices.Count];

            for (int i = 0; i < remap.Length; i++)
                remap[i] = i;

            var uniqueBridge = new List<int>();

            foreach (int index in bridgeVerts)
            {
                if (index >= 0 && index < data.vertices.Count && !uniqueBridge.Contains(index))
                    uniqueBridge.Add(index);
            }

            foreach (int bridgeIndex in uniqueBridge)
            {
                Vector3 pos = data.vertices[bridgeIndex];
                int best = bridgeIndex;
                float bestDist = thresholdSq;

                for (int i = 0; i < data.vertices.Count; i++)
                {
                    if (i == bridgeIndex)
                        continue;

                    // Prefer welding onto an older / non-bridge vertex when possible.
                    float dist = (data.vertices[i] - pos).sqrMagnitude;

                    if (dist > bestDist)
                        continue;

                    if (dist < bestDist || i < best)
                    {
                        bestDist = dist;
                        best = i;
                    }
                }

                if (best != bridgeIndex)
                    remap[bridgeIndex] = best;
            }

            // Flatten remap chains.
            for (int i = 0; i < remap.Length; i++)
            {
                int r = remap[i];

                while (r != remap[r])
                    r = remap[r];

                remap[i] = r;
            }

            bool any = false;

            for (int i = 0; i < remap.Length; i++)
            {
                if (remap[i] != i)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
                return;

            var newFaces = new List<IndieEditMeshFace>();

            foreach (var face in data.faces)
            {
                IndieEditMeshMeshUtil.AddTriangle(
                    newFaces,
                    remap[face.a],
                    remap[face.b],
                    remap[face.c],
                    face.materialId
                );
            }

            data.faces = newFaces;
            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
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

        private static List<IndieEditMeshFace> ConnectOnFace(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            int a0,
            int a1,
            int b0,
            int b1,
            int segments,
            float pinch,
            float slide,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache
        )
        {
            var result = new List<IndieEditMeshFace>();

            // Keep topology endpoints, but flip t per-edge so slide matches axis.
            var pointsA = new List<int>();
            var pointsB = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float t = GetConnectT(i, segments, pinch, slide);
                pointsA.Add(
                    SplitEdgeAtAlongAxis(data, a0, a1, t, slideAxis, splitCache)
                );
                pointsB.Add(
                    SplitEdgeAtAlongAxis(data, b0, b1, t, slideAxis, splitCache)
                );
            }

            // Determine shared vertex / topology on the triangle and rebuild.
            int shared = -1;

            if (a0 == b0 || a0 == b1)
                shared = a0;
            else if (a1 == b0 || a1 == b1)
                shared = a1;

            if (shared >= 0 && segments >= 1)
            {
                RebuildSharedVertexConnect(
                    data,
                    face,
                    a0,
                    a1,
                    b0,
                    b1,
                    shared,
                    pointsA,
                    pointsB,
                    result
                );
            }
            else
            {
                // Align corners for strip winding (points already height-matched).
                Vector3 aDir = data.vertices[a1] - data.vertices[a0];
                Vector3 bDir = data.vertices[b1] - data.vertices[b0];

                if (Vector3.Dot(aDir, bDir) < 0f)
                {
                    pointsB.Reverse();
                    int tmp = b0;
                    b0 = b1;
                    b1 = tmp;
                }

                RebuildOppositeConnect(
                    data,
                    face,
                    a0,
                    a1,
                    b0,
                    b1,
                    pointsA,
                    pointsB,
                    result
                );
            }

            if (result.Count == 0)
                result.Add(face);

            return result;
        }

        private static int SplitEdgeAtAlongAxis(
            IndieEditableMeshData data,
            int v0,
            int v1,
            float t,
            Vector3 slideAxis,
            Dictionary<long, List<int>> splitCache
        )
        {
            Vector3 dir = data.vertices[v1] - data.vertices[v0];

            if (dir.sqrMagnitude > 1e-16f && Vector3.Dot(dir, slideAxis) < 0f)
                t = 1f - t;

            return IndieEditMeshMeshUtil.SplitEdgeAt(data, v0, v1, t, splitCache);
        }

        private static void RebuildSharedVertexConnect(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            int a0,
            int a1,
            int b0,
            int b1,
            int shared,
            List<int> pointsA,
            List<int> pointsB,
            List<IndieEditMeshFace> result
        )
        {
            int aOther = a0 == shared ? a1 : a0;
            int bOther = b0 == shared ? b1 : b0;
            int materialId = face.materialId;
            int smoothingGroups = face.smoothingGroups;

            if (a0 != shared)
                pointsA.Reverse();

            if (b0 != shared)
                pointsB.Reverse();

            IndieEditMeshMeshUtil.AddTriangle(
                result,
                shared,
                pointsA[0],
                pointsB[0],
                materialId,
                smoothingGroups
            );

            for (int i = 0; i < pointsA.Count - 1; i++)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    result,
                    pointsA[i],
                    pointsA[i + 1],
                    pointsB[i + 1],
                    pointsB[i],
                    materialId,
                    smoothingGroups
                );
            }

            IndieEditMeshMeshUtil.AddQuad(
                result,
                pointsA[pointsA.Count - 1],
                aOther,
                bOther,
                pointsB[pointsB.Count - 1],
                materialId,
                smoothingGroups
            );
        }

        private static void RebuildOppositeConnect(
            IndieEditableMeshData data,
            IndieEditMeshFace face,
            int a0,
            int a1,
            int b0,
            int b1,
            List<int> pointsA,
            List<int> pointsB,
            List<IndieEditMeshFace> result
        )
        {
            int materialId = face.materialId;
            int smoothingGroups = face.smoothingGroups;

            IndieEditMeshMeshUtil.AddQuad(
                result,
                a0,
                pointsA[0],
                pointsB[0],
                b0,
                materialId,
                smoothingGroups
            );

            for (int i = 0; i < pointsA.Count - 1; i++)
            {
                IndieEditMeshMeshUtil.AddQuad(
                    result,
                    pointsA[i],
                    pointsA[i + 1],
                    pointsB[i + 1],
                    pointsB[i],
                    materialId,
                    smoothingGroups
                );
            }

            IndieEditMeshMeshUtil.AddQuad(
                result,
                pointsA[pointsA.Count - 1],
                a1,
                b1,
                pointsB[pointsB.Count - 1],
                materialId,
                smoothingGroups
            );
        }

        /// <summary>
        /// Pinch ∈ [-1, 1]: -1 = all lines at center, 0 = even spacing,
        /// +1 = max spread to the ends (odd middle line stays at center).
        /// Slide ∈ [-1, 1]: -1 / +1 = edge ends along slide axis.
        /// </summary>
        private static float GetConnectT(int index, int segments, float pinch, float slide)
        {
            float u = (index + 1f) / (segments + 1f);

            if (segments > 1)
            {
                float center = 0.5f;
                float p = Mathf.Clamp(pinch, -1f, 1f);
                float scale;

                if (p <= 0f)
                {
                    // -1 → 0 (collapse), 0 → 1 (uniform)
                    scale = Mathf.Lerp(0f, 1f, p + 1f);
                }
                else
                {
                    // 0 → 1, +1 → push outer lines to 0 and 1
                    float maxScale = (segments + 1f) / (segments - 1f);
                    scale = Mathf.Lerp(1f, maxScale, p);
                }

                u = center + (u - center) * scale;
            }

            u += 0.5f * Mathf.Clamp(slide, -1f, 1f);
            return Mathf.Clamp01(u);
        }

        private static bool CanEditEdges(IndieEditMeshComponent ep)
        {
            return ep != null &&
                   ep.Data != null &&
                   IndieEditMeshSession.Active == ep &&
                   ep.subMode == IndieEditMeshSubMode.Edge &&
                   ep.Data.selectedEdges.Count > 0;
        }

        private static void Finish(IndieEditMeshComponent ep)
        {
            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
        }
    }
}
