using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Edge chamfer after 3ds Max / Blender bmesh_bevel, run on the quad meta mesh
    /// (<see cref="IndieEditMeshQuadMeta"/>) so triangulation diagonals never get a slide.
    /// Each side of a bevel point gets one rail point: the slide along the next real edge
    /// (loop slide), the meet with the neighbouring bevel edge inside the polygon, or the
    /// meet of both offset lines for a sector of several polygons. The ribbon (profile of
    /// Segments pieces swept along the edges, patches at joints) is built first; polygons
    /// are then fitted to its rails and every new point takes UV from the polygon it lies on.
    /// Amount=offset, Tension 0=round/1=flat, Invert=concave, Open=no strip faces.
    /// </summary>
    public static class IndieEditMeshBevel
    {
        private const float Eps = 1e-5f;
        private const float EpsSqr = Eps * Eps;
        private const float UvMatchSqr = 1e-10f;

        /// <summary>A slide whose far end is a bevel point too stops before the middle of the edge.</summary>
        private const float SharedSlideLimit = 0.49f;

        private const float FreeSlideLimit = 0.98f;

        /// <summary>Caps offset / sin on edges nearly parallel to the bevel edge.</summary>
        private const float MinSlideSin = 0.2f;

        /// <summary>Below this the two bevel edges of a corner are treated as collinear.</summary>
        private const float MinMeetSin = 0.1f;

        private const int StripSmoothingGroup = 1 << 30;

        /// <summary>Stands for the bevel point itself in a cap outline.</summary>
        private const int VertexMarker = -1;

        public static void Apply(
            IndieEditableMeshData data,
            float offset,
            int segments,
            float tension,
            bool invert,
            bool open
        )
        {
            if (data == null || offset < Eps || data.selectedEdges.Count == 0)
                return;

            var builder = new Builder(
                data,
                offset,
                Mathf.Max(1, segments),
                Mathf.Clamp01(tension),
                invert
            );

            if (!builder.CollectBevelEdges())
                return;

            // The ribbon fixes its points first; polygons are then fitted to its rails.
            if (!open)
            {
                builder.BuildStrips();
                builder.BuildCaps();
            }

            builder.RebuildPolys();

            data.faces = builder.NewFaces;
            data.RebuildEdges();
        }

        /// <summary>Polygons and edges around a bevel point in walking order.</summary>
        private sealed class Fan
        {
            public int Point;

            /// <summary>Neighbour points. Closed: Polys[i] lies between Edges[i] and Edges[i + 1 mod n].</summary>
            public readonly List<int> Edges = new List<int>();

            public readonly List<int> Polys = new List<int>();
            public bool Closed;
        }

        private sealed class Builder
        {
            public readonly List<IndieEditMeshFace> NewFaces;

            private readonly IndieEditableMeshData _data;
            private readonly IndieEditMeshMetaMesh _meta;
            private readonly List<(int v0, int v1)> _selected;
            private readonly float _offset;
            private readonly int _segments;
            private readonly float _tension;
            private readonly bool _invert;
            private readonly bool _hasUv;
            private readonly bool _hasColor;
            private readonly int _stripSmoothingGroups;

            private readonly HashSet<long> _bevelKeys = new HashSet<long>();
            private readonly Dictionary<int, Fan> _fans = new Dictionary<int, Fan>();
            private readonly Dictionary<long, int> _slides = new Dictionary<long, int>();
            private readonly Dictionary<long, int> _meets = new Dictionary<long, int>();

            /// <summary>(bevel point, polygon) -> the one rail point of the fan sector holding that polygon.</summary>
            private readonly Dictionary<long, int> _sectorPoints = new Dictionary<long, int>();
            private readonly Dictionary<(int apex, int lo, int hi), int[]> _rows =
                new Dictionary<(int apex, int lo, int hi), int[]>();
            private readonly Dictionary<int, List<(Vector2 uv, int vertex)>> _pointVertices =
                new Dictionary<int, List<(Vector2 uv, int vertex)>>();

            /// <summary>First strip vertex of each profile point; caps reuse it to stay on the strip's UV island.</summary>
            private readonly Dictionary<int, int> _stripVertices = new Dictionary<int, int>();

            public Builder(IndieEditableMeshData data, float offset, int segments, float tension, bool invert)
            {
                _data = data;
                _offset = offset;
                _segments = segments;
                _tension = tension;
                _invert = invert;
                _hasUv = data.uvs.Count == data.vertices.Count;
                _hasColor = data.colors.Count == data.vertices.Count;
                _stripSmoothingGroups = IndieEditableMeshBuilder.HasAnySmoothingGroup(data) ? StripSmoothingGroup : 0;
                _selected = SelectedEdgeEnds(data);
                _meta = IndieEditMeshQuadMeta.Build(data, _selected);
                NewFaces = new List<IndieEditMeshFace>(data.faces.Count + data.faces.Count / 2);
            }

            /// <summary>
            /// Selected edges with exactly two polygons by position; endpoints whose
            /// polygons do not form one fan drop their edges.
            /// </summary>
            public bool CollectBevelEdges()
            {
                foreach (var edge in _selected)
                {
                    if (_meta.TryPointEdge(edge.v0, edge.v1, out long key) &&
                        _meta.EdgePolys.TryGetValue(key, out var polys) &&
                        polys.Count == 2)
                    {
                        _bevelKeys.Add(key);
                    }
                }

                var failed = new HashSet<int>();

                foreach (long key in _bevelKeys)
                {
                    IndieEditMeshMeshUtil.DecodeEdgeKey(key, out int p0, out int p1);
                    TryAddFan(p0, failed);
                    TryAddFan(p1, failed);
                }

                if (failed.Count > 0)
                {
                    _bevelKeys.RemoveWhere(key =>
                    {
                        IndieEditMeshMeshUtil.DecodeEdgeKey(key, out int p0, out int p1);
                        return failed.Contains(p0) || failed.Contains(p1);
                    });

                    var used = new HashSet<int>();

                    foreach (long key in _bevelKeys)
                    {
                        IndieEditMeshMeshUtil.DecodeEdgeKey(key, out int p0, out int p1);
                        used.Add(p0);
                        used.Add(p1);
                    }

                    var stale = new List<int>();

                    foreach (int point in _fans.Keys)
                    {
                        if (!used.Contains(point))
                            stale.Add(point);
                    }

                    foreach (int point in stale)
                        _fans.Remove(point);
                }

                BuildSectors();
                return _bevelKeys.Count > 0;
            }

            /// <summary>
            /// Blender build_boundary: between two neighbouring bevel edges of a fan with two or
            /// more plain edges between them, every polygon of that sector moves its corner to one
            /// point where the offset lines of both bevel edges meet, so the rail stays one line.
            /// One polygon or one plain edge between them is covered by MeetPoint / SlidePoint.
            /// </summary>
            private void BuildSectors()
            {
                var slots = new List<int>();

                foreach (var pair in _fans)
                {
                    int v = pair.Key;
                    var fan = pair.Value;
                    int n = fan.Edges.Count;

                    slots.Clear();

                    for (int i = 0; i < n; i++)
                    {
                        if (IsBevel(v, fan.Edges[i]))
                            slots.Add(i);
                    }

                    int sectorCount = fan.Closed ? (slots.Count >= 2 ? slots.Count : 0) : slots.Count - 1;

                    for (int k = 0; k < sectorCount; k++)
                    {
                        int s0 = slots[k];
                        int s1 = slots[(k + 1) % slots.Count];

                        if (s1 <= s0)
                            s1 += n;

                        if (s1 - s0 - 1 < 2)
                            continue;

                        int point = AddSectorPoint(v, fan, s0, s1);

                        for (int i = s0; i < s1; i++)
                            _sectorPoints[SectorKey(v, fan.Polys[i % n])] = point;
                    }
                }
            }

            /// <summary>Meet of the two offset lines in the average plane of the sector, kept short of its edges.</summary>
            private int AddSectorPoint(int v, Fan fan, int s0, int s1)
            {
                int n = fan.Edges.Count;
                Vector3 origin = _meta.Points[v];
                Vector3 normal = Vector3.zero;

                for (int i = s0; i < s1; i++)
                    normal += _meta.Polys[fan.Polys[i % n]].Normal;

                if (normal.sqrMagnitude < EpsSqr)
                    normal = _meta.Polys[fan.Polys[s0 % n]].Normal;

                normal.Normalize();

                Vector3 sideA = SideToward(origin, normal, fan.Edges[s0 % n], fan.Edges[(s0 + 1) % n]);
                Vector3 sideB = SideToward(origin, normal, fan.Edges[s1 % n], fan.Edges[(s1 - 1) % n]);
                float minLen = float.MaxValue;

                for (int j = s0; j <= s1; j++)
                    minLen = Mathf.Min(minLen, Vector3.Distance(origin, _meta.Points[fan.Edges[j % n]]));

                Vector3 sum = sideA + sideB;
                Vector3 dir = sideA;
                float dist = _offset;

                // Along sideA + sideB the distance to each offset line grows by 1 + dot(sideA, sideB).
                if (sum.sqrMagnitude > EpsSqr)
                {
                    dir = sum.normalized;
                    dist = _offset * sum.magnitude / Mathf.Max(1f + Vector3.Dot(sideA, sideB), MinMeetSin);
                }

                dist = Mathf.Min(dist, minLen * SharedSlideLimit);
                return _meta.AddPoint(origin + dir * dist);
            }

            /// <summary>Unit normal of edge v-edgePoint in the plane, on the side of towardPoint.</summary>
            private Vector3 SideToward(Vector3 origin, Vector3 normal, int edgePoint, int towardPoint)
            {
                Vector3 along = Vector3.ProjectOnPlane(_meta.Points[edgePoint] - origin, normal);
                Vector3 side = Vector3.Cross(normal, along);

                if (side.sqrMagnitude < EpsSqr)
                    return Vector3.zero;

                side.Normalize();
                return Vector3.Dot(side, _meta.Points[towardPoint] - origin) < 0f ? -side : side;
            }

            private static long SectorKey(int v, int p) => ((long)v << 32) | (uint)p;

            /// <summary>
            /// Fits every polygon to the ribbon rails. A corner next to a bevel edge goes to its
            /// rail point (meet, sector point or slide); a corner inside a sector to the sector
            /// point; any other corner keeps the point and picks up the slides on its edges, so
            /// no T-junction is left. A paired quad is cut back along its source diagonal and
            /// each half is triangulated in the plane of its own source triangle.
            /// </summary>
            public void RebuildPolys()
            {
                var corners = new List<int>();
                var starts = new List<int>();
                var half = new List<int>();

                for (int p = 0; p < _meta.Polys.Count; p++)
                {
                    var poly = _meta.Polys[p];

                    if (!TryRebuildCorners(p, poly, corners, starts))
                    {
                        EmitSource(poly);
                        continue;
                    }

                    if (poly.Points.Length != 4 || poly.SourceFaces.Length != 2)
                    {
                        EmitPolygon(corners, poly.Normal, poly);
                        continue;
                    }

                    // Corners are a, d, b, c around the diagonal a-b: triangles (b, a, d) and (a, b, c).
                    int split0 = SplitIndex(poly, 0, corners, starts);
                    int split2 = SplitIndex(poly, 2, corners, starts);

                    half.Clear();

                    for (int k = split0; k <= split2; k++)
                        half.Add(corners[k]);

                    EmitPolygon(half, CornerNormal(poly, 2, 0, 1), poly);

                    half.Clear();

                    for (int k = split2; k < corners.Count; k++)
                        half.Add(corners[k]);

                    for (int k = 0; k <= split0; k++)
                        half.Add(corners[k]);

                    EmitPolygon(half, CornerNormal(poly, 0, 2, 3), poly);
                }
            }

            /// <summary>Where the source diagonal leaves a corner's run: the kept corner, else the middle.</summary>
            private static int SplitIndex(IndieEditMeshMetaPoly poly, int corner, List<int> corners, List<int> starts)
            {
                int start = starts[corner];
                int end = starts[corner + 1];

                for (int k = start; k < end; k++)
                {
                    if (corners[k] == poly.Corners[corner])
                        return k;
                }

                return start + (end - start - 1) / 2;
            }

            private Vector3 CornerNormal(IndieEditMeshMetaPoly poly, int i0, int i1, int i2)
            {
                Vector3 p0 = _data.vertices[poly.Corners[i0]];
                Vector3 normal = Vector3.Cross(
                    _data.vertices[poly.Corners[i1]] - p0,
                    _data.vertices[poly.Corners[i2]] - p0
                );

                return normal.sqrMagnitude > EpsSqr * EpsSqr ? normal.normalized : poly.Normal;
            }

            private void EmitPolygon(List<int> corners, Vector3 normal, IndieEditMeshMetaPoly poly)
            {
                var clean = DedupClosed(corners);

                if (clean.Count < 3)
                    return;

                IndieEditMeshPolygonTriangulator.TriangulateEar(
                    _data,
                    NewFaces,
                    clean,
                    normal,
                    poly.MaterialId,
                    poly.SmoothingGroups
                );
            }

            /// <summary>Sweeps the profile along every bevel edge between the rails on its two polygons.</summary>
            public void BuildStrips()
            {
                foreach (long key in _bevelKeys)
                {
                    IndieEditMeshMeshUtil.DecodeEdgeKey(key, out int v, out int w);
                    var polys = _meta.EdgePolys[key];
                    int p1 = polys[0];
                    int p2 = polys[1];

                    int[] rowV = Row(v, CornerPoint(p1, v, w), CornerPoint(p2, v, w));
                    int[] rowW = Row(w, CornerPoint(p1, w, v), CornerPoint(p2, w, v));

                    if (rowV.Length != rowW.Length || rowV.Length < 2)
                        continue;

                    bool seam = IsUvSeam(p1, p2, v, w);
                    int[] vertsV = StripRowVertices(rowV, v, w, p1, p2, seam);
                    int[] vertsW = StripRowVertices(rowW, w, v, p1, p2, seam);

                    var poly1 = _meta.Polys[p1];
                    Vector3 outward = poly1.Normal + _meta.Polys[p2].Normal;

                    if (outward.sqrMagnitude < 1e-6f)
                        outward = poly1.Normal;

                    for (int s = 0; s < rowV.Length - 1; s++)
                    {
                        int q0 = vertsV[s];
                        int q1 = vertsW[s];
                        int q2 = vertsW[s + 1];
                        int q3 = vertsV[s + 1];

                        Vector3 quadN = Vector3.Cross(
                            _data.vertices[q2] - _data.vertices[q0],
                            _data.vertices[q3] - _data.vertices[q1]
                        );

                        if (Vector3.Dot(quadN, outward) < 0f)
                        {
                            IndieEditMeshMeshUtil.AddQuadStable(
                                NewFaces,
                                _data.vertices,
                                q0,
                                q3,
                                q2,
                                q1,
                                poly1.MaterialId,
                                _stripSmoothingGroups
                            );
                        }
                        else
                        {
                            IndieEditMeshMeshUtil.AddQuadStable(
                                NewFaces,
                                _data.vertices,
                                q0,
                                q1,
                                q2,
                                q3,
                                poly1.MaterialId,
                                _stripSmoothingGroups
                            );
                        }
                    }
                }
            }

            /// <summary>
            /// Closes the hole at each bevel point: the profile rows of its bevel edges in
            /// fan order, with the point itself wherever two or more plain edges keep it.
            /// </summary>
            public void BuildCaps()
            {
                var slots = new List<int>();
                var outline = new List<int>();

                foreach (var pair in _fans)
                {
                    int v = pair.Key;
                    var fan = pair.Value;
                    int n = fan.Edges.Count;

                    slots.Clear();

                    for (int i = 0; i < n; i++)
                    {
                        if (IsBevel(v, fan.Edges[i]))
                            slots.Add(i);
                    }

                    if (slots.Count == 0 || IsTerminalCorner(fan))
                        continue;

                    int owner = _meta.EdgePolys[IndieEditMeshMeshUtil.EdgeKey(v, fan.Edges[slots[0]])][0];

                    outline.Clear();

                    // Open fan: border edges count as plain edges of the end sectors.
                    if (!fan.Closed && slots[0] >= 2)
                        outline.Add(VertexMarker);

                    for (int k = 0; k < slots.Count; k++)
                    {
                        int i = slots[k];
                        int prevPoly = fan.Polys[(i - 1 + fan.Polys.Count) % fan.Polys.Count];
                        int nextPoly = fan.Polys[i % fan.Polys.Count];
                        int w = fan.Edges[i];

                        outline.AddRange(Row(v, CornerPoint(prevPoly, v, w), CornerPoint(nextPoly, v, w)));

                        // Between two bevel edges the rows share a rail point (meet, slide or
                        // sector), so the point itself survives only past the last bevel edge.
                        int gap = 0;

                        if (!fan.Closed && k == slots.Count - 1)
                            gap = n - 1 - i;
                        else if (slots.Count == 1)
                            gap = n - 1;

                        if (gap >= 2)
                            outline.Add(VertexMarker);
                    }

                    EmitCap(v, fan, owner, outline, slots.Count);
                }
            }

            /// <param name="owner">Polygon that owns the strip of the first bevel edge; the kept point takes its corner.</param>
            private void EmitCap(int v, Fan fan, int owner, List<int> outline, int bevelCount)
            {
                var pieces = new List<List<int>>();
                bool hasMarker = outline.Contains(VertexMarker);

                if (!hasMarker)
                {
                    // Open: the strip ends on the border (notch). Closed with two bevel
                    // edges: both rows share their ends and coincide.
                    if (!fan.Closed || bevelCount <= 2)
                        return;

                    pieces.Add(new List<int>(outline));
                }
                else
                {
                    // Every run of profile points next to the kept point closes with it.
                    int start = fan.Closed ? (outline.IndexOf(VertexMarker) + 1) % outline.Count : 0;
                    var piece = new List<int>();

                    for (int k = 0; k < outline.Count; k++)
                    {
                        int point = outline[(start + k) % outline.Count];

                        if (point != VertexMarker)
                        {
                            piece.Add(point);
                            continue;
                        }

                        if (piece.Count >= 2)
                        {
                            piece.Add(VertexMarker);
                            pieces.Add(piece);
                        }

                        piece = new List<int>();
                    }

                    if (piece.Count >= 2)
                    {
                        piece.Add(VertexMarker);
                        pieces.Add(piece);
                    }
                }

                var ownerPoly = _meta.Polys[owner];
                int pointVertex = ownerPoly.Corners[ownerPoly.IndexOfPoint(v)];
                Vector3 normal = Vector3.zero;

                foreach (int p in fan.Polys)
                    normal += _meta.Polys[p].Normal;

                if (normal.sqrMagnitude < EpsSqr)
                    normal = ownerPoly.Normal;

                var verts = new List<int>();

                foreach (var piece in pieces)
                {
                    verts.Clear();
                    bool complete = true;

                    foreach (int point in piece)
                    {
                        int vertex = point == VertexMarker ? pointVertex : CapVertex(point);

                        if (vertex < 0)
                        {
                            complete = false;
                            break;
                        }

                        verts.Add(vertex);
                    }

                    if (!complete)
                        continue;

                    var clean = DedupClosed(verts);

                    if (clean.Count < 3)
                        continue;

                    if (Vector3.Dot(IndieEditMeshPolygonTriangulator.PolygonNormal(_data, clean), normal) < 0f)
                        clean.Reverse();

                    IndieEditMeshPolygonTriangulator.TriangulateEar(
                        _data,
                        NewFaces,
                        clean,
                        normal,
                        ownerPoly.MaterialId,
                        _stripSmoothingGroups
                    );
                }
            }

            /// <param name="starts">Start of each corner's run in <paramref name="corners"/>, plus the end.</param>
            private bool TryRebuildCorners(int p, IndieEditMeshMetaPoly poly, List<int> corners, List<int> starts)
            {
                corners.Clear();
                starts.Clear();

                if (poly.IsDegenerate)
                    return false;

                bool changed = false;

                for (int i = 0; i < poly.Points.Length; i++)
                {
                    int v = poly.Points[i];
                    starts.Add(corners.Count);

                    if (!_fans.ContainsKey(v))
                    {
                        corners.Add(poly.Corners[i]);
                        continue;
                    }

                    int a = poly.PrevPoint(i);
                    int b = poly.NextPoint(i);
                    bool bevelA = IsBevel(v, a);
                    bool bevelB = IsBevel(v, b);

                    if (bevelA && bevelB)
                    {
                        corners.Add(VertexOnPoly(p, MeetPoint(p, i)));
                        changed = true;
                    }
                    else if (bevelA)
                    {
                        corners.Add(VertexOnPoly(p, SidePoint(p, v, b)));
                        changed = true;
                    }
                    else if (bevelB)
                    {
                        corners.Add(VertexOnPoly(p, SidePoint(p, v, a)));
                        changed = true;
                    }
                    else if (_sectorPoints.TryGetValue(SectorKey(v, p), out int sectorPoint))
                    {
                        corners.Add(VertexOnPoly(p, sectorPoint));
                        changed = true;
                    }
                    else if (IsTerminalCorner(_fans[v]))
                    {
                        // End face of a single bevel edge: the profile cuts its corner off.
                        foreach (int point in Row(v, SlidePoint(v, a), SlidePoint(v, b)))
                            corners.Add(VertexOnPoly(p, point));

                        changed = true;
                    }
                    else
                    {
                        if (HasSlide(v, a))
                        {
                            corners.Add(VertexOnPoly(p, SlidePoint(v, a)));
                            changed = true;
                        }

                        corners.Add(poly.Corners[i]);

                        if (HasSlide(v, b))
                        {
                            corners.Add(VertexOnPoly(p, SlidePoint(v, b)));
                            changed = true;
                        }
                    }
                }

                starts.Add(corners.Count);
                return changed;
            }

            private void EmitSource(IndieEditMeshMetaPoly poly)
            {
                foreach (int faceIndex in poly.SourceFaces)
                    NewFaces.Add(_data.faces[faceIndex]);
            }

            private void TryAddFan(int point, HashSet<int> failed)
            {
                if (_fans.ContainsKey(point) || failed.Contains(point))
                    return;

                if (TryBuildFan(point, out var fan))
                    _fans.Add(point, fan);
                else
                    failed.Add(point);
            }

            /// <summary>Walks the polygons around a point across shared edges. False when they are not one manifold fan.</summary>
            private bool TryBuildFan(int v, out Fan fan)
            {
                fan = new Fan { Point = v };

                if (!_meta.PointPolys.TryGetValue(v, out var polys) || polys.Count == 0)
                    return false;

                // An open fan has to start on a border edge; a closed one starts anywhere.
                int startPoly = -1;
                int startEdge = -1;

                foreach (int p in polys)
                {
                    var poly = _meta.Polys[p];
                    int corner = poly.IndexOfPoint(v);

                    if (corner < 0 || System.Array.LastIndexOf(poly.Points, v) != corner)
                        return false;

                    if (startPoly >= 0)
                        continue;

                    int a = poly.PrevPoint(corner);
                    int b = poly.NextPoint(corner);

                    if (_meta.EdgePolyCount(v, a) == 1)
                    {
                        startPoly = p;
                        startEdge = a;
                    }
                    else if (_meta.EdgePolyCount(v, b) == 1)
                    {
                        startPoly = p;
                        startEdge = b;
                    }
                }

                if (startPoly < 0)
                {
                    startPoly = polys[0];
                    var first = _meta.Polys[startPoly];
                    startEdge = first.PrevPoint(first.IndexOfPoint(v));
                }

                var visited = new HashSet<int>();
                int current = startPoly;
                int inEdge = startEdge;
                fan.Edges.Add(startEdge);

                while (true)
                {
                    if (!visited.Add(current))
                        return false;

                    var poly = _meta.Polys[current];
                    int corner = poly.IndexOfPoint(v);
                    int a = poly.PrevPoint(corner);
                    int b = poly.NextPoint(corner);
                    int outEdge = a == inEdge ? b : b == inEdge ? a : -1;

                    if (outEdge < 0)
                        return false;

                    fan.Polys.Add(current);

                    var shared = _meta.EdgePolys[IndieEditMeshMeshUtil.EdgeKey(v, outEdge)];

                    if (shared.Count == 1)
                    {
                        fan.Edges.Add(outEdge);
                        fan.Closed = false;
                        break;
                    }

                    if (shared.Count != 2)
                        return false;

                    int next = shared[0] == current ? shared[1] : shared[0];

                    if (next == startPoly)
                    {
                        if (outEdge != startEdge)
                            return false;

                        fan.Closed = true;
                        break;
                    }

                    fan.Edges.Add(outEdge);
                    current = next;
                    inEdge = outEdge;
                }

                return fan.Polys.Count == polys.Count;
            }

            /// <summary>
            /// One bevel edge and two plain edges around a closed fan: the only polygon
            /// between the plain edges holds the whole profile, so it needs no cap.
            /// </summary>
            private bool IsTerminalCorner(Fan fan)
            {
                if (!fan.Closed || fan.Edges.Count != 3)
                    return false;

                int bevelCount = 0;

                foreach (int edge in fan.Edges)
                {
                    if (IsBevel(fan.Point, edge))
                        bevelCount++;
                }

                return bevelCount == 1;
            }

            private bool IsBevel(int p0, int p1)
            {
                return _bevelKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(p0, p1));
            }

            /// <summary>Plain edge from a bevel point that a polygon on it turns onto a bevel edge.</summary>
            private bool HasSlide(int v, int x)
            {
                if (IsBevel(v, x) ||
                    !_meta.EdgePolys.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(v, x), out var polys))
                {
                    return false;
                }

                foreach (int p in polys)
                {
                    if (IsBevel(v, OtherNeighbor(p, v, x)) && !_sectorPoints.ContainsKey(SectorKey(v, p)))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Loop slide: the point on edge v->x whose distance to the adjacent bevel edge
            /// is the chamfer amount.
            /// </summary>
            private int SlidePoint(int v, int x)
            {
                long key = ((long)v << 32) | (uint)x;

                if (_slides.TryGetValue(key, out int existing))
                    return existing;

                Vector3 origin = _meta.Points[v];
                Vector3 dir = _meta.Points[x] - origin;
                float len = dir.magnitude;
                float sin = 0f;

                if (len > Eps &&
                    _meta.EdgePolys.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(v, x), out var polys))
                {
                    Vector3 unit = dir / len;

                    foreach (int p in polys)
                    {
                        int y = OtherNeighbor(p, v, x);

                        if (!IsBevel(v, y))
                            continue;

                        Vector3 bevelDir = _meta.Points[y] - origin;

                        if (bevelDir.sqrMagnitude > EpsSqr)
                            sin = Mathf.Max(sin, Vector3.Cross(unit, bevelDir.normalized).magnitude);
                    }
                }

                if (sin <= 0f)
                    sin = 1f;

                float limit = _fans.ContainsKey(x) ? SharedSlideLimit : FreeSlideLimit;
                float dist = Mathf.Min(_offset / Mathf.Max(sin, MinSlideSin), len * limit);
                Vector3 position = len > Eps ? origin + dir * (dist / len) : origin;

                int point = _meta.AddPoint(position);
                _slides.Add(key, point);
                return point;
            }

            /// <summary>Corner between two bevel edges: the point at the chamfer amount from both, inside the polygon.</summary>
            private int MeetPoint(int p, int corner)
            {
                long key = ((long)p << 32) | (uint)corner;

                if (_meets.TryGetValue(key, out int existing))
                    return existing;

                var poly = _meta.Polys[p];
                Vector3 origin = _meta.Points[poly.Points[corner]];
                Vector3 toA = _meta.Points[poly.PrevPoint(corner)] - origin;
                Vector3 toB = _meta.Points[poly.NextPoint(corner)] - origin;
                float lenA = toA.magnitude;
                float lenB = toB.magnitude;
                float limit = SharedSlideLimit * Mathf.Min(lenA, lenB);
                Vector3 position = origin;

                if (lenA > Eps && lenB > Eps)
                {
                    Vector3 uA = toA / lenA;
                    Vector3 uB = toB / lenB;
                    float sin = Vector3.Cross(uA, uB).magnitude;

                    if (sin >= MinMeetSin)
                    {
                        position = origin + (uA + uB) * Mathf.Min(_offset / sin, limit);
                    }
                    else
                    {
                        Vector3 perp = Vector3.Cross(poly.Normal, uA).normalized;

                        if (Vector3.Dot(perp, PolyCentroid(poly) - origin) < 0f)
                            perp = -perp;

                        position = origin + perp * Mathf.Min(_offset, limit);
                    }
                }

                int point = _meta.AddPoint(position);
                _meets.Add(key, point);
                return point;
            }

            /// <summary>Where the corner of polygon p at v goes for bevel edge v-w: one end of that edge's rail on p.</summary>
            private int CornerPoint(int p, int v, int w)
            {
                var poly = _meta.Polys[p];
                int corner = poly.IndexOfPoint(v);
                int x = OtherNeighbor(p, v, w);

                return IsBevel(v, x) ? MeetPoint(p, corner) : SidePoint(p, v, x);
            }

            /// <summary>Rail point of polygon p at v beside plain edge v-x: its sector point, else the slide on v-x.</summary>
            private int SidePoint(int p, int v, int x)
            {
                return _sectorPoints.TryGetValue(SectorKey(v, p), out int point) ? point : SlidePoint(v, x);
            }

            private int OtherNeighbor(int p, int v, int x)
            {
                var poly = _meta.Polys[p];
                int corner = poly.IndexOfPoint(v);
                int a = poly.PrevPoint(corner);

                return a == x ? poly.NextPoint(corner) : a;
            }

            /// <summary>Profile points from <paramref name="from"/> to <paramref name="to"/> around <paramref name="apex"/>.</summary>
            private int[] Row(int apex, int from, int to)
            {
                if (from == to)
                    return new[] { from };

                int lo = Mathf.Min(from, to);
                int hi = Mathf.Max(from, to);
                var key = (apex, lo, hi);

                if (!_rows.TryGetValue(key, out int[] row))
                {
                    row = new int[_segments + 1];
                    row[0] = lo;
                    row[_segments] = hi;

                    Vector3 apexPos = _meta.Points[apex];
                    Vector3 loPos = _meta.Points[lo];
                    Vector3 hiPos = _meta.Points[hi];

                    for (int s = 1; s < _segments; s++)
                        row[s] = _meta.AddPoint(ProfilePosition(apexPos, loPos, hiPos, s / (float)_segments));

                    _rows.Add(key, row);
                }

                if (from == lo)
                    return row;

                var reversed = (int[])row.Clone();
                System.Array.Reverse(reversed);
                return reversed;
            }

            /// <summary>
            /// Cross-section in the end plane. An arc around the apex bulges into the faces
            /// (concave, Invert); its mirror over the chord rounds the corner (convex).
            /// Tension blends toward the flat chord.
            /// </summary>
            private Vector3 ProfilePosition(Vector3 apex, Vector3 from, Vector3 to, float t)
            {
                Vector3 flat = Vector3.Lerp(from, to, t);
                float angle = Mathf.Clamp01(t) * Mathf.PI * 0.5f;
                Vector3 intoFace = apex + Mathf.Cos(angle) * (from - apex) + Mathf.Sin(angle) * (to - apex);

                Vector3 chord = to - from;
                float chordSqr = chord.sqrMagnitude;
                Vector3 towardCorner = flat;

                if (chordSqr > EpsSqr)
                {
                    float projT = Vector3.Dot(intoFace - from, chord) / chordSqr;
                    towardCorner = (from + chord * projT) * 2f - intoFace;
                }

                Vector3 curved = _invert ? intoFace : towardCorner;
                Vector3 position = Vector3.Lerp(curved, flat, _tension);
                Vector3 planeN = Vector3.Cross(from - apex, to - apex);

                if (planeN.sqrMagnitude > EpsSqr)
                {
                    planeN.Normalize();
                    position -= planeN * Vector3.Dot(position - apex, planeN);
                }

                return position;
            }

            /// <summary>
            /// Vertices of one strip row, p1 side first. Without a seam UV runs from the p1
            /// rail to the p2 rail. On a UV seam the strip is unfolded onto p1's UV island
            /// and the seam moves to the p2 rail.
            /// </summary>
            private int[] StripRowVertices(int[] row, int apex, int other, int p1, int p2, bool seam)
            {
                int count = row.Length;
                var result = new int[count];
                var arc = new float[count];

                for (int k = 1; k < count; k++)
                    arc[k] = arc[k - 1] + Vector3.Distance(_meta.Points[row[k - 1]], _meta.Points[row[k]]);

                var poly1 = _meta.Polys[p1];
                int baseVertex = poly1.Corners[poly1.IndexOfPoint(apex)];

                if (!seam)
                {
                    SampleOnPoly(p1, _meta.Points[row[0]], out Vector2 uvStart, out Color colorStart, out _);
                    SampleOnPoly(p2, _meta.Points[row[count - 1]], out Vector2 uvEnd, out Color colorEnd, out _);
                    float total = arc[count - 1];

                    for (int k = 0; k < count; k++)
                    {
                        float t = total > Eps ? arc[k] / total : k / (float)(count - 1);
                        result[k] = GetOrCreateVertex(
                            row[k],
                            baseVertex,
                            Vector2.Lerp(uvStart, uvEnd, t),
                            Color.Lerp(colorStart, colorEnd, t)
                        );
                    }

                    RememberStripVertices(row, result);
                    return result;
                }

                int face = SourceFaceWithEdge(poly1, apex, other);
                Vector3 origin = _meta.Points[apex];
                Vector3 along = (_meta.Points[other] - origin).normalized;
                Vector3 across = Vector3.Cross(poly1.Normal, along).normalized;

                if (Vector3.Dot(across, PolyCentroid(poly1) - origin) > 0f)
                    across = -across;

                Vector3 first = _meta.Points[row[0]] - origin;
                float startDist = (first - along * Vector3.Dot(first, along)).magnitude;

                for (int k = 0; k < count; k++)
                {
                    Vector3 rel = _meta.Points[row[k]] - origin;
                    Vector3 unfolded = origin + along * Vector3.Dot(rel, along) + across * (arc[k] - startDist);
                    Sample(face, Barycentric(face, unfolded), out Vector2 uv, out Color color, out _);
                    result[k] = GetOrCreateVertex(row[k], baseVertex, uv, color);
                }

                RememberStripVertices(row, result);
                return result;
            }

            private void RememberStripVertices(int[] row, int[] vertices)
            {
                for (int k = 0; k < row.Length; k++)
                {
                    if (!_stripVertices.ContainsKey(row[k]))
                        _stripVertices.Add(row[k], vertices[k]);
                }
            }

            private bool IsUvSeam(int p1, int p2, int v, int w)
            {
                if (!_hasUv)
                    return false;

                var poly1 = _meta.Polys[p1];
                var poly2 = _meta.Polys[p2];

                return !SameUv(poly1.Corners[poly1.IndexOfPoint(v)], poly2.Corners[poly2.IndexOfPoint(v)]) ||
                       !SameUv(poly1.Corners[poly1.IndexOfPoint(w)], poly2.Corners[poly2.IndexOfPoint(w)]);
            }

            private bool SameUv(int a, int b)
            {
                return a == b || (_data.uvs[a] - _data.uvs[b]).sqrMagnitude <= UvMatchSqr;
            }

            private int SourceFaceWithEdge(IndieEditMeshMetaPoly poly, int p0, int p1)
            {
                foreach (int faceIndex in poly.SourceFaces)
                {
                    var face = _data.faces[faceIndex];
                    int a = _meta.PointOf(face.a);
                    int b = _meta.PointOf(face.b);
                    int c = _meta.PointOf(face.c);

                    if ((a == p0 || b == p0 || c == p0) && (a == p1 || b == p1 || c == p1))
                        return faceIndex;
                }

                return poly.SourceFaces[0];
            }

            /// <summary>Vertex for a new point on polygon p, UV and color from the source triangle that holds it.</summary>
            private int VertexOnPoly(int p, int point)
            {
                SampleOnPoly(p, _meta.Points[point], out Vector2 uv, out Color color, out int baseVertex);
                return GetOrCreateVertex(point, baseVertex, uv, color);
            }

            private void SampleOnPoly(int p, Vector3 position, out Vector2 uv, out Color color, out int baseVertex)
            {
                var poly = _meta.Polys[p];
                int bestFace = poly.SourceFaces[0];
                Vector3 bestBary = Barycentric(bestFace, position);
                float bestScore = Mathf.Min(bestBary.x, Mathf.Min(bestBary.y, bestBary.z));

                for (int i = 1; i < poly.SourceFaces.Length; i++)
                {
                    Vector3 bary = Barycentric(poly.SourceFaces[i], position);
                    float score = Mathf.Min(bary.x, Mathf.Min(bary.y, bary.z));

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestFace = poly.SourceFaces[i];
                        bestBary = bary;
                    }
                }

                Sample(bestFace, bestBary, out uv, out color, out baseVertex);
            }

            private void Sample(int faceIndex, Vector3 bary, out Vector2 uv, out Color color, out int baseVertex)
            {
                var face = _data.faces[faceIndex];

                baseVertex = bary.x >= bary.y && bary.x >= bary.z ? face.a : bary.y >= bary.z ? face.b : face.c;
                uv = _hasUv
                    ? _data.uvs[face.a] * bary.x + _data.uvs[face.b] * bary.y + _data.uvs[face.c] * bary.z
                    : Vector2.zero;
                color = _hasColor
                    ? _data.colors[face.a] * bary.x + _data.colors[face.b] * bary.y + _data.colors[face.c] * bary.z
                    : Color.white;
            }

            /// <summary>Barycentric weights of the projection onto the triangle plane; may lie outside.</summary>
            private Vector3 Barycentric(int faceIndex, Vector3 p)
            {
                var face = _data.faces[faceIndex];
                Vector3 a = _data.vertices[face.a];
                Vector3 e0 = _data.vertices[face.b] - a;
                Vector3 e1 = _data.vertices[face.c] - a;
                Vector3 e2 = p - a;

                float d00 = Vector3.Dot(e0, e0);
                float d01 = Vector3.Dot(e0, e1);
                float d11 = Vector3.Dot(e1, e1);
                float d20 = Vector3.Dot(e2, e0);
                float d21 = Vector3.Dot(e2, e1);
                float denom = d00 * d11 - d01 * d01;

                if (Mathf.Abs(denom) < 1e-20f)
                    return new Vector3(1f, 0f, 0f);

                float v = (d11 * d20 - d01 * d21) / denom;
                float w = (d00 * d21 - d01 * d20) / denom;
                return new Vector3(1f - v - w, v, w);
            }

            /// <summary>One vertex per point and UV, so seams split and matching UVs share.</summary>
            private int GetOrCreateVertex(int point, int baseVertex, Vector2 uv, Color color)
            {
                if (!_pointVertices.TryGetValue(point, out var list))
                {
                    list = new List<(Vector2 uv, int vertex)>(2);
                    _pointVertices.Add(point, list);
                }

                foreach (var entry in list)
                {
                    if (!_hasUv || (entry.uv - uv).sqrMagnitude <= UvMatchSqr)
                        return entry.vertex;
                }

                int vertex = IndieEditMeshMeshUtil.AddVertex(_data, baseVertex, _meta.Points[point]);

                if (_hasUv)
                    _data.uvs[vertex] = uv;

                if (_hasColor)
                    _data.colors[vertex] = color;

                list.Add((uv, vertex));
                return vertex;
            }

            private int CapVertex(int point)
            {
                if (_stripVertices.TryGetValue(point, out int vertex))
                    return vertex;

                return _pointVertices.TryGetValue(point, out var list) && list.Count > 0 ? list[0].vertex : -1;
            }

            private Vector3 PolyCentroid(IndieEditMeshMetaPoly poly)
            {
                Vector3 sum = Vector3.zero;

                foreach (int point in poly.Points)
                    sum += _meta.Points[point];

                return sum / poly.Points.Length;
            }
        }

        private static List<(int v0, int v1)> SelectedEdgeEnds(IndieEditableMeshData data)
        {
            var result = new List<(int v0, int v1)>(data.selectedEdges.Count);

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                result.Add((edge.v0, edge.v1));
            }

            return result;
        }

        private static List<int> DedupClosed(List<int> poly)
        {
            var clean = new List<int>(poly.Count);

            for (int i = 0; i < poly.Count; i++)
            {
                if (clean.Count == 0 || clean[clean.Count - 1] != poly[i])
                    clean.Add(poly[i]);
            }

            while (clean.Count >= 3 && clean[0] == clean[clean.Count - 1])
                clean.RemoveAt(clean.Count - 1);

            return clean;
        }
    }
}
