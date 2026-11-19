using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Non-destructive pelt skin: projected verts linked to the original mesh,
    /// already open along cyan cuts. Mutates nothing until <see cref="CommitToMesh"/>.
    /// </summary>
    public sealed class IndieEditUVPeltProxy
    {
        public struct Vert
        {
            public int OriginalVert;
            public Vector3 WorldPos;
            public Vector2 Pos;
            public Vector2 Pos0;
        }

        public struct Face
        {
            public int FaceIndex;
            public int A;
            public int B;
            public int C;
        }

        public readonly List<Vert> Verts = new List<Vert>();
        public readonly List<Face> Faces = new List<Face>();
        public readonly HashSet<int> RegionFaces = new HashSet<int>();
        public readonly HashSet<IndieEditUVSeamOps.GeoEdgeId> CutGeos = new HashSet<IndieEditUVSeamOps.GeoEdgeId>();
        public readonly HashSet<long> CutProxyEdges = new HashSet<long>();
        public readonly List<int> BoundaryOrdered = new List<int>();
        public readonly HashSet<int> BoundarySet = new HashSet<int>();
        public readonly List<Vector2> Stretchers = new List<Vector2>();
        public readonly Dictionary<int, List<int>> Neighbors = new Dictionary<int, List<int>>();

        public static bool TryBuild(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            HashSet<IndieEditUVSeamOps.GeoEdgeId> cutGeos,
            out IndieEditUVPeltProxy proxy,
            out string error
        )
        {
            proxy = null;
            error = null;

            if (data == null || regionFaces == null || regionFaces.Count == 0)
            {
                error = "Empty pelt region.";
                return false;
            }

            proxy = new IndieEditUVPeltProxy();

            foreach (int f in regionFaces)
                proxy.RegionFaces.Add(f);

            if (cutGeos != null)
            {
                foreach (var g in cutGeos)
                    proxy.CutGeos.Add(g);
            }

            if (!ComputePlaneBasis(
                    data,
                    regionFaces,
                    out Vector3 centroid,
                    out Vector3 tangent,
                    out Vector3 bitangent
                ))
            {
                error = "Cannot project region to a plane.";
                return false;
            }

            // One provisional proxy vert per face corner.
            var provisional = new List<Vert>();
            var faceCorners = new List<(int faceIndex, int a, int b, int c)>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (!ValidCorner(data, face.a) ||
                    !ValidCorner(data, face.b) ||
                    !ValidCorner(data, face.c))
                {
                    continue;
                }

                int ia = provisional.Count;
                provisional.Add(MakeVert(data, face.a, centroid, tangent, bitangent));
                int ib = provisional.Count;
                provisional.Add(MakeVert(data, face.b, centroid, tangent, bitangent));
                int ic = provisional.Count;
                provisional.Add(MakeVert(data, face.c, centroid, tangent, bitangent));
                faceCorners.Add((faceIndex, ia, ib, ic));
            }

            if (faceCorners.Count == 0)
            {
                error = "No valid faces in region.";
                return false;
            }

            // Weld along non-cut geo edges only.
            int[] parent = new int[provisional.Count];

            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            var geoIncidences = new Dictionary<IndieEditUVSeamOps.GeoEdgeId, List<(int i0, int i1)>>();

            for (int f = 0; f < faceCorners.Count; f++)
            {
                var fc = faceCorners[f];
                AccrueGeo(geoIncidences, provisional, fc.a, fc.b);
                AccrueGeo(geoIncidences, provisional, fc.b, fc.c);
                AccrueGeo(geoIncidences, provisional, fc.c, fc.a);
            }

            foreach (var pair in geoIncidences)
            {
                if (proxy.CutGeos.Contains(pair.Key))
                    continue;

                List<(int i0, int i1)> list = pair.Value;

                if (list.Count < 2)
                    continue;

                int p0 = list[0].i0;
                int p1 = list[0].i1;
                Vector3 pos0 = provisional[p0].WorldPos;
                Vector3 pos1 = provisional[p1].WorldPos;

                for (int i = 1; i < list.Count; i++)
                {
                    int o0 = list[i].i0;
                    int o1 = list[i].i1;
                    bool sameDir =
                        (provisional[o0].WorldPos - pos0).sqrMagnitude <=
                        (provisional[o0].WorldPos - pos1).sqrMagnitude;

                    int m0 = sameDir ? o0 : o1;
                    int m1 = sameDir ? o1 : o0;
                    Union(parent, p0, m0);
                    Union(parent, p1, m1);
                }
            }

            // Compact roots → final verts; average UV of welded group.
            var rootToNew = new Dictionary<int, int>();
            var uvSum = new Dictionary<int, Vector2>();
            var uvCount = new Dictionary<int, int>();

            for (int i = 0; i < provisional.Count; i++)
            {
                int root = Find(parent, i);

                if (!uvSum.ContainsKey(root))
                {
                    uvSum[root] = provisional[i].Pos;
                    uvCount[root] = 1;
                }
                else
                {
                    uvSum[root] += provisional[i].Pos;
                    uvCount[root]++;
                }
            }

            foreach (var pair in uvSum)
            {
                int root = pair.Key;
                int newIndex = proxy.Verts.Count;
                rootToNew[root] = newIndex;
                Vector2 avg = pair.Value / uvCount[root];
                Vert src = provisional[root];
                src.Pos = avg;
                src.Pos0 = avg;
                proxy.Verts.Add(src);
            }

            for (int f = 0; f < faceCorners.Count; f++)
            {
                var fc = faceCorners[f];
                proxy.Faces.Add(new Face
                {
                    FaceIndex = fc.faceIndex,
                    A = rootToNew[Find(parent, fc.a)],
                    B = rootToNew[Find(parent, fc.b)],
                    C = rootToNew[Find(parent, fc.c)]
                });
            }

            FitVertsToSlot(proxy.Verts, new Rect(1f, 0f, 1f, 1f), 0.05f);

            for (int i = 0; i < proxy.Verts.Count; i++)
            {
                Vert v = proxy.Verts[i];
                v.Pos0 = v.Pos;
                proxy.Verts[i] = v;
            }

            MarkCutProxyEdges(proxy);
            proxy.BuildNeighbors();
            proxy.BuildBoundaryOrdered();

            if (proxy.BoundaryOrdered.Count < 3)
            {
                error =
                    $"Pelt boundary too small ({proxy.BoundaryOrdered.Count} verts). " +
                    "Need a closed selection perimeter.";
                return false;
            }

            proxy.PlaceStretchersOnCircle();
            return true;
        }

        public void ResetPositions()
        {
            for (int i = 0; i < Verts.Count; i++)
            {
                Vert v = Verts[i];
                v.Pos = v.Pos0;
                Verts[i] = v;
            }

            PlaceStretchersOnCircle();
        }

        public void RunSimulation(int samples)
        {
            samples = Mathf.Max(1, samples);

            for (int s = 0; s < samples; s++)
            {
                for (int i = 0; i < BoundaryOrdered.Count; i++)
                {
                    int vert = BoundaryOrdered[i];
                    Vert v = Verts[vert];
                    v.Pos = Vector2.Lerp(v.Pos, Stretchers[i], 0.35f);
                    Verts[vert] = v;
                }

                var next = new Dictionary<int, Vector2>();

                for (int vert = 0; vert < Verts.Count; vert++)
                {
                    if (BoundarySet.Contains(vert))
                        continue;

                    if (!Neighbors.TryGetValue(vert, out List<int> neighbors) ||
                        neighbors.Count == 0)
                    {
                        continue;
                    }

                    Vector2 sum = Vector2.zero;
                    int count = 0;

                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        sum += Verts[neighbors[i]].Pos;
                        count++;
                    }

                    if (count > 0)
                        next[vert] = Vector2.Lerp(Verts[vert].Pos, sum / count, 0.5f);
                }

                foreach (var pair in next)
                {
                    Vert v = Verts[pair.Key];
                    v.Pos = pair.Value;
                    Verts[pair.Key] = v;
                }
            }
        }

        /// <summary>
        /// Materialize proxy into the mesh: new island verts + UVs from Pos.
        /// </summary>
        public void CommitToMesh(IndieEditableMeshData data)
        {
            if (data == null || Verts.Count == 0 || Faces.Count == 0)
                return;

            IndieEditMeshMeshUtil.EnsureUvChannel(data);

            var proxyToNew = new int[Verts.Count];

            for (int i = 0; i < Verts.Count; i++)
            {
                Vert pv = Verts[i];
                int src = pv.OriginalVert;

                if (src < 0 || src >= data.vertices.Count)
                    src = 0;

                int created = IndieEditMeshMeshUtil.AddVertex(data, src, pv.WorldPos);
                data.uvs[created] = pv.Pos;
                proxyToNew[i] = created;
            }

            for (int i = 0; i < Faces.Count; i++)
            {
                Face pf = Faces[i];

                if (pf.FaceIndex < 0 || pf.FaceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[pf.FaceIndex];
                face.a = proxyToNew[pf.A];
                face.b = proxyToNew[pf.B];
                face.c = proxyToNew[pf.C];
                data.faces[pf.FaceIndex] = face;
            }

            data.RebuildEdges();
            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
        }

        public Rect GetUvBounds()
        {
            if (Verts.Count == 0)
                return new Rect(1f, 0f, 1f, 1f);

            float minX = Verts[0].Pos.x;
            float maxX = minX;
            float minY = Verts[0].Pos.y;
            float maxY = minY;

            for (int i = 1; i < Verts.Count; i++)
            {
                Vector2 p = Verts[i].Pos;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            foreach (Vector2 s in Stretchers)
            {
                minX = Mathf.Min(minX, s.x);
                maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y);
                maxY = Mathf.Max(maxY, s.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public void PlaceStretchersOnCircle()
        {
            Stretchers.Clear();
            int n = BoundaryOrdered.Count;

            if (n == 0)
                return;

            Vector2 center = Vector2.zero;

            for (int i = 0; i < n; i++)
                center += Verts[BoundaryOrdered[i]].Pos;

            center /= n;

            if (center.sqrMagnitude < 1e-8f)
                center = new Vector2(1.5f, 0.5f);

            float maxDist = 0f;

            for (int i = 0; i < n; i++)
                maxDist = Mathf.Max(maxDist, Vector2.Distance(Verts[BoundaryOrdered[i]].Pos, center));

            for (int i = 0; i < Verts.Count; i++)
                maxDist = Mathf.Max(maxDist, Vector2.Distance(Verts[i].Pos, center));

            float radius = Mathf.Max(0.2f, maxDist * 1.35f);

            // Place each stretcher on the ray from center through its boundary
            // vert — same U-side after proxy flip; equal-angle walk was opposite
            // to mirrored boundary order and crossed all springs.
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = Verts[BoundaryOrdered[i]].Pos - center;

                if (dir.sqrMagnitude < 1e-12f)
                {
                    float t = (i / (float)n) * Mathf.PI * 2f;
                    dir = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                }
                else
                {
                    dir.Normalize();
                }

                Stretchers.Add(center + dir * radius);
            }
        }

        private void BuildNeighbors()
        {
            Neighbors.Clear();

            for (int i = 0; i < Faces.Count; i++)
            {
                Face f = Faces[i];
                Link(f.A, f.B);
                Link(f.B, f.C);
                Link(f.C, f.A);
            }
        }

        private void Link(int a, int b)
        {
            if (!Neighbors.TryGetValue(a, out List<int> la))
            {
                la = new List<int>(4);
                Neighbors[a] = la;
            }

            if (!la.Contains(b))
                la.Add(b);

            if (!Neighbors.TryGetValue(b, out List<int> lb))
            {
                lb = new List<int>(4);
                Neighbors[b] = lb;
            }

            if (!lb.Contains(a))
                lb.Add(a);
        }

        private void BuildBoundaryOrdered()
        {
            BoundaryOrdered.Clear();
            BoundarySet.Clear();

            var useCount = new Dictionary<long, int>();
            var directed = new List<(int from, int to)>();

            for (int i = 0; i < Faces.Count; i++)
            {
                Face f = Faces[i];
                AccrueChartEdge(useCount, directed, f.A, f.B);
                AccrueChartEdge(useCount, directed, f.B, f.C);
                AccrueChartEdge(useCount, directed, f.C, f.A);
            }

            var halfEdges = new List<(int from, int to)>();

            for (int i = 0; i < directed.Count; i++)
            {
                long key = IndieEditMeshMeshUtil.EdgeKey(directed[i].from, directed[i].to);

                if (!useCount.TryGetValue(key, out int count) || count != 1)
                    continue;

                halfEdges.Add(directed[i]);
            }

            if (halfEdges.Count == 0)
                return;

            var nextMap = new Dictionary<int, List<int>>();

            for (int i = 0; i < halfEdges.Count; i++)
            {
                int from = halfEdges[i].from;
                int to = halfEdges[i].to;

                if (!nextMap.TryGetValue(from, out List<int> list))
                {
                    list = new List<int>(2);
                    nextMap[from] = list;
                }

                list.Add(to);
            }

            var claimed = new HashSet<long>();
            List<int> bestCycle = null;

            for (int seed = 0; seed < halfEdges.Count; seed++)
            {
                long seedKey = DirectedKey(halfEdges[seed].from, halfEdges[seed].to);

                if (claimed.Contains(seedKey))
                    continue;

                if (!TryWalkCycle(
                        halfEdges[seed].from,
                        nextMap,
                        claimed,
                        out List<int> cycle,
                        out HashSet<long> cycleEdges
                    ))
                {
                    continue;
                }

                foreach (long e in cycleEdges)
                    claimed.Add(e);

                if (bestCycle == null || cycle.Count > bestCycle.Count)
                    bestCycle = cycle;
            }

            if (bestCycle == null)
            {
                for (int i = 0; i < halfEdges.Count; i++)
                {
                    if (BoundarySet.Add(halfEdges[i].from))
                        BoundaryOrdered.Add(halfEdges[i].from);

                    if (BoundarySet.Add(halfEdges[i].to))
                        BoundaryOrdered.Add(halfEdges[i].to);
                }

                return;
            }

            for (int i = 0; i < bestCycle.Count; i++)
            {
                BoundaryOrdered.Add(bestCycle[i]);
                BoundarySet.Add(bestCycle[i]);
            }
        }

        private bool TryWalkCycle(
            int start,
            Dictionary<int, List<int>> nextMap,
            HashSet<long> claimed,
            out List<int> cycle,
            out HashSet<long> cycleEdges
        )
        {
            cycle = new List<int>();
            cycleEdges = new HashSet<long>();
            int current = start;
            int prev = -1;
            int guard = nextMap.Count * 4 + 8;

            while (guard-- > 0)
            {
                if (current == start && cycle.Count > 0)
                    break;

                cycle.Add(current);

                if (!nextMap.TryGetValue(current, out List<int> nexts) || nexts.Count == 0)
                    return false;

                int next = PickNext(current, prev, nexts, cycleEdges, claimed);

                if (next < 0)
                    return false;

                cycleEdges.Add(DirectedKey(current, next));
                prev = current;
                current = next;
            }

            return cycle.Count >= 3 && current == start;
        }

        private int PickNext(
            int current,
            int prev,
            List<int> nexts,
            HashSet<long> cycleEdges,
            HashSet<long> claimed
        )
        {
            int best = -1;
            float bestCross = float.NegativeInfinity;
            Vector2 inDir = Vector2.right;

            if (prev >= 0)
            {
                inDir = Verts[current].Pos - Verts[prev].Pos;

                if (inDir.sqrMagnitude < 1e-12f)
                    inDir = Vector2.right;
            }

            for (int i = 0; i < nexts.Count; i++)
            {
                int candidate = nexts[i];
                long dKey = DirectedKey(current, candidate);

                if (cycleEdges.Contains(dKey) || claimed.Contains(dKey))
                    continue;

                Vector2 outDir = Verts[candidate].Pos - Verts[current].Pos;
                float cross = inDir.x * outDir.y - inDir.y * outDir.x;

                if (best < 0 || cross > bestCross)
                {
                    best = candidate;
                    bestCross = cross;
                }
            }

            return best;
        }

        private static void MarkCutProxyEdges(IndieEditUVPeltProxy proxy)
        {
            proxy.CutProxyEdges.Clear();

            for (int i = 0; i < proxy.Faces.Count; i++)
            {
                Face f = proxy.Faces[i];
                TryMarkCut(proxy, f.A, f.B);
                TryMarkCut(proxy, f.B, f.C);
                TryMarkCut(proxy, f.C, f.A);
            }
        }

        private static void TryMarkCut(IndieEditUVPeltProxy proxy, int a, int b)
        {
            var geo = IndieEditUVSeamOps.GeoEdgeId.FromPositions(
                proxy.Verts[a].WorldPos,
                proxy.Verts[b].WorldPos
            );

            if (!proxy.CutGeos.Contains(geo))
                return;

            proxy.CutProxyEdges.Add(IndieEditMeshMeshUtil.EdgeKey(a, b));
        }

        private static void AccrueChartEdge(
            Dictionary<long, int> useCount,
            List<(int from, int to)> directed,
            int a,
            int b
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (!useCount.TryGetValue(key, out int count))
                count = 0;

            useCount[key] = count + 1;
            directed.Add((a, b));
        }

        private static long DirectedKey(int from, int to)
        {
            return (((long)from) << 32) | (uint)to;
        }

        private static bool ValidCorner(IndieEditableMeshData data, int v)
        {
            return v >= 0 && v < data.vertices.Count;
        }

        private static Vert MakeVert(
            IndieEditableMeshData data,
            int original,
            Vector3 centroid,
            Vector3 tangent,
            Vector3 bitangent
        )
        {
            Vector3 world = data.vertices[original];
            Vector3 local = world - centroid;
            // Negate U so proxy/Commit match mesh facing (avoids 1-u flip).
            Vector2 uv = new Vector2(
                -Vector3.Dot(local, tangent),
                Vector3.Dot(local, bitangent)
            );

            return new Vert
            {
                OriginalVert = original,
                WorldPos = world,
                Pos = uv,
                Pos0 = uv
            };
        }

        private static bool ComputePlaneBasis(
            IndieEditableMeshData data,
            HashSet<int> regionFaces,
            out Vector3 centroid,
            out Vector3 tangent,
            out Vector3 bitangent
        )
        {
            centroid = Vector3.zero;
            tangent = Vector3.right;
            bitangent = Vector3.up;

            Vector3 normalSum = Vector3.zero;
            var regionVerts = new HashSet<int>();

            foreach (int faceIndex in regionFaces)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (!ValidCorner(data, face.a) ||
                    !ValidCorner(data, face.b) ||
                    !ValidCorner(data, face.c))
                {
                    continue;
                }

                Vector3 pa = data.vertices[face.a];
                Vector3 pb = data.vertices[face.b];
                Vector3 pc = data.vertices[face.c];
                normalSum += Vector3.Cross(pb - pa, pc - pa);
                regionVerts.Add(face.a);
                regionVerts.Add(face.b);
                regionVerts.Add(face.c);
            }

            if (regionVerts.Count == 0)
                return false;

            foreach (int v in regionVerts)
                centroid += data.vertices[v];

            centroid /= regionVerts.Count;

            Vector3 normal = normalSum.sqrMagnitude > 1e-12f
                ? normalSum.normalized
                : Vector3.up;

            Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up;
            tangent = Vector3.Cross(upHint, normal);

            if (tangent.sqrMagnitude < 1e-12f)
                tangent = Vector3.Cross(Vector3.right, normal);

            tangent.Normalize();
            bitangent = Vector3.Cross(normal, tangent).normalized;
            return true;
        }

        private static void AccrueGeo(
            Dictionary<IndieEditUVSeamOps.GeoEdgeId, List<(int i0, int i1)>> map,
            List<Vert> verts,
            int i0,
            int i1
        )
        {
            var geo = IndieEditUVSeamOps.GeoEdgeId.FromPositions(verts[i0].WorldPos, verts[i1].WorldPos);

            if (!map.TryGetValue(geo, out List<(int i0, int i1)> list))
            {
                list = new List<(int i0, int i1)>(2);
                map[geo] = list;
            }

            list.Add((i0, i1));
        }

        private static void FitVertsToSlot(List<Vert> verts, Rect slot, float padding)
        {
            if (verts.Count == 0)
                return;

            float rMinX = float.PositiveInfinity;
            float rMaxX = float.NegativeInfinity;
            float rMinY = float.PositiveInfinity;
            float rMaxY = float.NegativeInfinity;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 uv = verts[i].Pos;
                rMinX = Mathf.Min(rMinX, uv.x);
                rMaxX = Mathf.Max(rMaxX, uv.x);
                rMinY = Mathf.Min(rMinY, uv.y);
                rMaxY = Mathf.Max(rMaxY, uv.y);
            }

            float srcW = Mathf.Max(1e-6f, rMaxX - rMinX);
            float srcH = Mathf.Max(1e-6f, rMaxY - rMinY);
            float pad = Mathf.Clamp01(padding);
            float availW = slot.width * (1f - 2f * pad);
            float availH = slot.height * (1f - 2f * pad);
            float scale = Mathf.Min(availW / srcW, availH / srcH);
            float packedW = srcW * scale;
            float packedH = srcH * scale;
            float destMinX = slot.xMin + (slot.width - packedW) * 0.5f;
            float destMinY = slot.yMin + (slot.height - packedH) * 0.5f;

            for (int i = 0; i < verts.Count; i++)
            {
                Vert v = verts[i];
                v.Pos = new Vector2(
                    destMinX + (v.Pos.x - rMinX) * scale,
                    destMinY + (v.Pos.y - rMinY) * scale
                );
                verts[i] = v;
            }
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra == rb)
                return;

            if (ra < rb)
                parent[rb] = ra;
            else
                parent[ra] = rb;
        }
    }
}
