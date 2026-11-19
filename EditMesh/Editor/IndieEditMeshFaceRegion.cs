using System;
using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>How far around the selection <see cref="IndieEditMeshFaceRegions.Extract"/> copies.</summary>
    public enum IndieEditMeshFaceRegionSpan
    {
        /// <summary>Selected faces, every face that shares a vertex index, then one shading ring.</summary>
        SharedVertex,

        /// <summary>
        /// Selected faces plus two rings of faces that meet them by vertex position,
        /// then one shading ring. Outline slides stacked verts on those neighbours.
        /// </summary>
        OutlineDoubleBelt,

        /// <summary>
        /// Faces on the selected edges (by position) plus two rings like
        /// <see cref="OutlineDoubleBelt"/>, then one shading ring. The op rebuilds every
        /// live face, so context faces come first and live faces are replaced on merge.
        /// </summary>
        EdgeDoubleBelt
    }

    /// <summary>
    /// Selected faces plus the faces an op can move, copied out of the full mesh.
    /// Polygon ops only add vertices and faces and move vertices inside this piece,
    /// so running them here and merging back matches running them on the whole mesh.
    /// </summary>
    public sealed class IndieEditMeshFaceRegion
    {
        /// <summary>
        /// Live faces first (they may change), then context faces that only shade
        /// the seam. New vertices and faces from an op are appended after both.
        /// With <see cref="ReplacesLiveFaces"/> context faces come first and the op
        /// output after them stands for all live faces.
        /// </summary>
        public IndieEditableMeshData Data;

        /// <summary>The op rebuilds the live faces; run it through <see cref="IndieEditMeshFaceRegions.Run"/>.</summary>
        public bool ReplacesLiveFaces;

        public int[] ToFullVertex;
        public int[] ToFullFace;
        public int LiveFaceCount;
        public int ContextFaceCount;
        public int BaseVertexCount;

        /// <summary>Region face flags: true for context faces.</summary>
        public bool[] ContextFaces;

        /// <summary>Full mesh face flags: true for faces the region replaces.</summary>
        public bool[] FullLiveFaces;

        internal List<int> OutsideSelectedVertices;
        internal List<(int v0, int v1)> OutsideSelectedEdges;
    }

    public static class IndieEditMeshFaceRegions
    {
        /// <summary>
        /// Stands for the vertex / edge selection outside the region. An op that clears
        /// the list drops it, and the outside selection is dropped on merge as well.
        /// Every selection reader skips negative indices.
        /// </summary>
        private const int OutsideSelectionMarker = -1;

        /// <summary>Null when nothing valid is selected. Uses <see cref="IndieEditMeshFaceRegionSpan.SharedVertex"/>.</summary>
        public static IndieEditMeshFaceRegion Extract(IndieEditableMeshData full)
        {
            return Extract(full, IndieEditMeshFaceRegionSpan.SharedVertex);
        }

        /// <summary>Null when nothing valid is selected.</summary>
        public static IndieEditMeshFaceRegion Extract(IndieEditableMeshData full, IndieEditMeshFaceRegionSpan span)
        {
            if (full == null)
                return null;

            bool fromEdges = span == IndieEditMeshFaceRegionSpan.EdgeDoubleBelt;

            if (!fromEdges && full.selectedFaces.Count == 0)
                return null;

            int faceCount = full.faces.Count;
            var selected = new bool[faceCount];
            bool any = fromEdges
                ? MarkFacesOnSelectedEdges(full, selected)
                : MarkSelectedFaces(full, selected);

            if (!any)
                return null;

            var live = new bool[faceCount];
            var liveFaces = new List<int>();
            var contextFaces = new List<int>();

            if (span == IndieEditMeshFaceRegionSpan.SharedVertex)
                CollectSharedVertexRing(full, selected, live, liveFaces, contextFaces);
            else
                CollectOutlineBelts(full, selected, live, liveFaces, contextFaces);

            var regionData = new IndieEditableMeshData();
            var fullToRegion = new Dictionary<int, int>();
            var toFullVertex = new List<int>();

            int MapVertex(int fullIndex)
            {
                if (fullToRegion.TryGetValue(fullIndex, out int regionIndex))
                    return regionIndex;

                regionIndex = toFullVertex.Count;
                fullToRegion.Add(fullIndex, regionIndex);
                toFullVertex.Add(fullIndex);
                CopyVertex(full, fullIndex, regionData);
                return regionIndex;
            }

            var fullFaceToRegion = new Dictionary<int, int>(liveFaces.Count);

            void AddFace(int fullFace)
            {
                var face = full.faces[fullFace];
                fullFaceToRegion[fullFace] = regionData.faces.Count;
                face.a = MapVertex(face.a);
                face.b = MapVertex(face.b);
                face.c = MapVertex(face.c);
                regionData.faces.Add(face);
            }

            if (fromEdges)
            {
                foreach (int faceIndex in contextFaces)
                    AddFace(faceIndex);
            }

            foreach (int faceIndex in liveFaces)
                AddFace(faceIndex);

            if (!fromEdges)
            {
                foreach (int faceIndex in contextFaces)
                    AddFace(faceIndex);

                foreach (int faceIndex in full.selectedFaces)
                {
                    if (faceIndex >= 0 && faceIndex < faceCount && selected[faceIndex])
                        regionData.selectedFaces.Add(fullFaceToRegion[faceIndex]);
                }
            }

            var region = new IndieEditMeshFaceRegion
            {
                Data = regionData,
                ReplacesLiveFaces = fromEdges,
                ToFullVertex = toFullVertex.ToArray(),
                ToFullFace = liveFaces.ToArray(),
                LiveFaceCount = liveFaces.Count,
                ContextFaceCount = contextFaces.Count,
                BaseVertexCount = toFullVertex.Count,
                ContextFaces = new bool[liveFaces.Count + contextFaces.Count],
                FullLiveFaces = live,
                OutsideSelectedVertices = new List<int>(),
                OutsideSelectedEdges = new List<(int v0, int v1)>()
            };

            int contextStart = fromEdges ? 0 : liveFaces.Count;

            for (int i = 0; i < contextFaces.Count; i++)
                region.ContextFaces[contextStart + i] = true;

            foreach (int vertexIndex in full.selectedVertices)
            {
                if (fullToRegion.TryGetValue(vertexIndex, out int regionIndex))
                    regionData.selectedVertices.Add(regionIndex);
                else
                    region.OutsideSelectedVertices.Add(vertexIndex);
            }

            regionData.RebuildEdges();
            Dictionary<long, int> regionEdges = null;

            foreach (int edgeIndex in full.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= full.edges.Count)
                    continue;

                var edge = full.edges[edgeIndex];

                if (fullToRegion.TryGetValue(edge.v0, out int r0) &&
                    fullToRegion.TryGetValue(edge.v1, out int r1))
                {
                    regionEdges ??= BuildEdgeLookup(regionData);

                    if (regionEdges.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(r0, r1), out int regionEdge))
                    {
                        regionData.selectedEdges.Add(regionEdge);
                        continue;
                    }
                }

                region.OutsideSelectedEdges.Add((edge.v0, edge.v1));
            }

            if (region.OutsideSelectedVertices.Count > 0)
                regionData.selectedVertices.Add(OutsideSelectionMarker);

            if (region.OutsideSelectedEdges.Count > 0)
                regionData.selectedEdges.Add(OutsideSelectionMarker);

            return region;
        }

        /// <summary>
        /// Writes <paramref name="live"/> (an edited copy of region.Data) into the full mesh:
        /// live faces in place, new vertices and faces appended, selection remapped.
        /// </summary>
        public static void Merge(IndieEditMeshFaceRegion region, IndieEditableMeshData live, IndieEditableMeshData full)
        {
            if (region == null || live == null || full == null)
                return;

            int baseVertexCount = region.BaseVertexCount;
            int newVertexStart = full.vertices.Count;

            int MapVertex(int regionIndex)
            {
                return regionIndex < baseVertexCount
                    ? region.ToFullVertex[regionIndex]
                    : newVertexStart + regionIndex - baseVertexCount;
            }

            for (int i = 0; i < baseVertexCount && i < live.vertices.Count; i++)
            {
                int fullIndex = region.ToFullVertex[i];
                full.vertices[fullIndex] = live.vertices[i];
                CopyAttribute(live.uvs, i, full.uvs, fullIndex);
                CopyAttribute(live.normals, i, full.normals, fullIndex);
                CopyAttribute(live.tangents, i, full.tangents, fullIndex);
                CopyAttribute(live.colors, i, full.colors, fullIndex);
            }

            for (int i = baseVertexCount; i < live.vertices.Count; i++)
                CopyVertex(live, i, full);

            IndieEditMeshFace RemapFace(IndieEditMeshFace face)
            {
                face.a = MapVertex(face.a);
                face.b = MapVertex(face.b);
                face.c = MapVertex(face.c);
                return face;
            }

            full.selectedFaces = region.ReplacesLiveFaces
                ? ReplaceLiveFaces(region, live, full, RemapFace)
                : WriteLiveFaces(region, live, full, RemapFace);

            var selectedVertices = live.selectedVertices.Contains(OutsideSelectionMarker)
                ? new List<int>(region.OutsideSelectedVertices)
                : new List<int>();

            foreach (int vertexIndex in live.selectedVertices)
            {
                if (vertexIndex >= 0 && vertexIndex < live.vertices.Count)
                    selectedVertices.Add(MapVertex(vertexIndex));
            }

            full.selectedVertices = selectedVertices;

            var edgeEnds = live.selectedEdges.Contains(OutsideSelectionMarker)
                ? new List<(int v0, int v1)>(region.OutsideSelectedEdges)
                : new List<(int v0, int v1)>();

            foreach (int edgeIndex in live.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= live.edges.Count)
                    continue;

                var edge = live.edges[edgeIndex];
                edgeEnds.Add((MapVertex(edge.v0), MapVertex(edge.v1)));
            }

            full.RebuildEdges();
            full.selectedEdges = new List<int>(edgeEnds.Count);

            if (edgeEnds.Count > 0)
            {
                var fullEdges = BuildEdgeLookup(full);

                foreach (var ends in edgeEnds)
                {
                    if (fullEdges.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(ends.v0, ends.v1), out int fullEdge))
                        full.selectedEdges.Add(fullEdge);
                }
            }

            // Replaced faces can drop original corners.
            if (region.ReplacesLiveFaces)
                IndieEditMeshMeshUtil.CleanupUnusedVertices(full);
        }

        /// <summary>Live faces overwrite their full-mesh slots, op faces are appended. Returns the face selection.</summary>
        private static List<int> WriteLiveFaces(
            IndieEditMeshFaceRegion region,
            IndieEditableMeshData live,
            IndieEditableMeshData full,
            Func<IndieEditMeshFace, IndieEditMeshFace> remap
        )
        {
            int liveCount = region.LiveFaceCount;
            int keptCount = liveCount + region.ContextFaceCount;
            int newFaceStart = full.faces.Count;

            for (int i = 0; i < liveCount && i < live.faces.Count; i++)
                full.faces[region.ToFullFace[i]] = remap(live.faces[i]);

            for (int i = keptCount; i < live.faces.Count; i++)
                full.faces.Add(remap(live.faces[i]));

            var selectedFaces = new List<int>(live.selectedFaces.Count);

            foreach (int faceIndex in live.selectedFaces)
            {
                if (faceIndex >= 0 && faceIndex < liveCount)
                    selectedFaces.Add(region.ToFullFace[faceIndex]);
                else if (faceIndex >= keptCount && faceIndex < live.faces.Count)
                    selectedFaces.Add(newFaceStart + faceIndex - keptCount);
            }

            return selectedFaces;
        }

        /// <summary>
        /// Drops every live face of the full mesh and appends the op output that follows
        /// the context faces. Returns the face selection with outside faces renumbered.
        /// </summary>
        private static List<int> ReplaceLiveFaces(
            IndieEditMeshFaceRegion region,
            IndieEditableMeshData live,
            IndieEditableMeshData full,
            Func<IndieEditMeshFace, IndieEditMeshFace> remap
        )
        {
            int fullCount = full.faces.Count;
            var faceMap = new int[fullCount];
            var faces = new List<IndieEditMeshFace>(fullCount);

            for (int i = 0; i < fullCount; i++)
            {
                if (i < region.FullLiveFaces.Length && region.FullLiveFaces[i])
                {
                    faceMap[i] = -1;
                    continue;
                }

                faceMap[i] = faces.Count;
                faces.Add(full.faces[i]);
            }

            int contextCount = region.ContextFaceCount;
            int newFaceStart = faces.Count;

            for (int i = contextCount; i < live.faces.Count; i++)
                faces.Add(remap(live.faces[i]));

            var selectedFaces = new List<int>();

            foreach (int faceIndex in full.selectedFaces)
            {
                if (faceIndex >= 0 && faceIndex < fullCount && faceMap[faceIndex] >= 0)
                    selectedFaces.Add(faceMap[faceIndex]);
            }

            foreach (int faceIndex in live.selectedFaces)
            {
                if (faceIndex >= contextCount && faceIndex < live.faces.Count)
                    selectedFaces.Add(newFaceStart + faceIndex - contextCount);
            }

            full.faces = faces;
            return selectedFaces;
        }

        /// <summary>
        /// Runs <paramref name="op"/> on a copy of region.Data. A replacing op sees only
        /// the live faces; the context faces are put back in front afterwards.
        /// </summary>
        public static void Run(IndieEditMeshFaceRegion region, IndieEditableMeshData live, Action<IndieEditableMeshData> op)
        {
            if (region == null || live == null || op == null)
                return;

            if (!region.ReplacesLiveFaces)
            {
                op(live);
                return;
            }

            int contextCount = Mathf.Min(region.ContextFaceCount, live.faces.Count);
            var context = live.faces.GetRange(0, contextCount);

            // Edges are not rebuilt here: the selection still indexes the full region edge list.
            live.faces.RemoveRange(0, contextCount);
            op(live);
            live.faces.InsertRange(0, context);

            for (int i = 0; i < live.selectedFaces.Count; i++)
            {
                if (live.selectedFaces[i] >= 0)
                    live.selectedFaces[i] += contextCount;
            }

            var edgeEnds = new List<(int v0, int v1)>(live.selectedEdges.Count);
            bool outside = false;

            foreach (int edgeIndex in live.selectedEdges)
            {
                if (edgeIndex == OutsideSelectionMarker)
                    outside = true;
                else if (edgeIndex >= 0 && edgeIndex < live.edges.Count)
                    edgeEnds.Add((live.edges[edgeIndex].v0, live.edges[edgeIndex].v1));
            }

            live.RebuildEdges();
            live.selectedEdges.Clear();

            if (outside)
                live.selectedEdges.Add(OutsideSelectionMarker);

            if (edgeEnds.Count == 0)
                return;

            var lookup = BuildEdgeLookup(live);

            foreach (var ends in edgeEnds)
            {
                if (lookup.TryGetValue(IndieEditMeshMeshUtil.EdgeKey(ends.v0, ends.v1), out int edgeIndex))
                    live.selectedEdges.Add(edgeIndex);
            }
        }

        /// <summary>
        /// One-shot polygon op on the selection region; falls back to the full mesh
        /// when nothing is selected.
        /// </summary>
        public static void ApplyLocal(IndieEditableMeshData full, Action<IndieEditableMeshData> op)
        {
            ApplyLocal(full, op, IndieEditMeshFaceRegionSpan.SharedVertex);
        }

        /// <summary>
        /// One-shot polygon op on the selection region; falls back to the full mesh
        /// when nothing is selected.
        /// </summary>
        public static void ApplyLocal(
            IndieEditableMeshData full,
            Action<IndieEditableMeshData> op,
            IndieEditMeshFaceRegionSpan span
        )
        {
            if (full == null || op == null)
                return;

            var region = Extract(full, span);

            if (region == null)
            {
                op(full);
                return;
            }

            Run(region, region.Data, op);
            Merge(region, region.Data, full);
        }

        private static bool MarkSelectedFaces(IndieEditableMeshData full, bool[] selected)
        {
            bool any = false;

            foreach (int faceIndex in full.selectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= selected.Length || !IsValidFace(full, faceIndex))
                    continue;

                selected[faceIndex] = true;
                any = true;
            }

            return any;
        }

        /// <summary>
        /// Faces with an edge on a selected edge by position, so both sides of a UV seam
        /// or hard edge count.
        /// </summary>
        private static bool MarkFacesOnSelectedEdges(IndieEditableMeshData full, bool[] selected)
        {
            var pointOf = new Dictionary<int, int>();
            var selectedKeys = new HashSet<long>();
            var coincident = new List<int>();

            int PointOf(int vertex)
            {
                if (pointOf.TryGetValue(vertex, out int point))
                    return point;

                IndieEditMeshMeshUtil.CollectCoincidentVertices(
                    full,
                    vertex,
                    coincident,
                    IndieEditMeshFacePointGroups.CoincidentThreshold
                );

                foreach (int other in coincident)
                    pointOf[other] = vertex;

                pointOf[vertex] = vertex;
                return vertex;
            }

            foreach (int edgeIndex in full.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= full.edges.Count)
                    continue;

                var edge = full.edges[edgeIndex];

                if (edge.v0 < 0 || edge.v1 < 0 ||
                    edge.v0 >= full.vertices.Count || edge.v1 >= full.vertices.Count)
                {
                    continue;
                }

                int p0 = PointOf(edge.v0);
                int p1 = PointOf(edge.v1);

                if (p0 != p1)
                    selectedKeys.Add(IndieEditMeshMeshUtil.EdgeKey(p0, p1));
            }

            if (selectedKeys.Count == 0)
                return false;

            bool any = false;

            bool OnSelected(int a, int b)
            {
                return pointOf.TryGetValue(a, out int pa) &&
                       pointOf.TryGetValue(b, out int pb) &&
                       selectedKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(pa, pb));
            }

            for (int i = 0; i < full.faces.Count; i++)
            {
                if (!IsValidFace(full, i))
                    continue;

                var face = full.faces[i];

                if (OnSelected(face.a, face.b) || OnSelected(face.b, face.c) || OnSelected(face.c, face.a))
                {
                    selected[i] = true;
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Live faces share a vertex index with the selection. One more index ring shades the seam.</summary>
        private static void CollectSharedVertexRing(
            IndieEditableMeshData full,
            bool[] selected,
            bool[] live,
            List<int> liveFaces,
            List<int> contextFaces
        )
        {
            int vertexCount = full.vertices.Count;
            int faceCount = full.faces.Count;
            var selectedVerts = new bool[vertexCount];

            for (int i = 0; i < faceCount; i++)
            {
                if (!selected[i])
                    continue;

                var face = full.faces[i];
                selectedVerts[face.a] = true;
                selectedVerts[face.b] = true;
                selectedVerts[face.c] = true;
            }

            var liveVerts = new bool[vertexCount];

            for (int i = 0; i < faceCount; i++)
            {
                if (!IsValidFace(full, i))
                    continue;

                var face = full.faces[i];

                if (!selected[i] && !selectedVerts[face.a] && !selectedVerts[face.b] && !selectedVerts[face.c])
                    continue;

                live[i] = true;
                liveVerts[face.a] = true;
                liveVerts[face.b] = true;
                liveVerts[face.c] = true;
                liveFaces.Add(i);
            }

            for (int i = 0; i < faceCount; i++)
            {
                if (live[i] || !IsValidFace(full, i))
                    continue;

                var face = full.faces[i];

                if (liveVerts[face.a] || liveVerts[face.b] || liveVerts[face.c])
                    contextFaces.Add(i);
            }
        }

        /// <summary>
        /// Live: the selection and two rings of faces that meet it by vertex position.
        /// Context: one more ring, so the outer corners of the live faces get full normals.
        /// </summary>
        private static void CollectOutlineBelts(
            IndieEditableMeshData full,
            bool[] selected,
            bool[] live,
            List<int> liveFaces,
            List<int> contextFaces
        )
        {
            int faceCount = full.faces.Count;
            var touched = new HashSet<int>();

            for (int i = 0; i < faceCount; i++)
            {
                if (!selected[i])
                    continue;

                var face = full.faces[i];
                live[i] = true;
                liveFaces.Add(i);
                touched.Add(face.a);
                touched.Add(face.b);
                touched.Add(face.c);
            }

            // Each pass grows one ring. New corners join the seed only after the scan,
            // so a neighbour of a neighbour waits for the next belt.
            for (int belt = 0; belt < 2; belt++)
            {
                IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(
                    full,
                    touched,
                    IndieEditMeshFacePointGroups.CoincidentThreshold
                );

                var added = new List<int>();

                for (int i = 0; i < faceCount; i++)
                {
                    if (live[i] || !IsValidFace(full, i))
                        continue;

                    var face = full.faces[i];

                    if (!touched.Contains(face.a) &&
                        !touched.Contains(face.b) &&
                        !touched.Contains(face.c))
                        continue;

                    live[i] = true;
                    liveFaces.Add(i);
                    added.Add(face.a);
                    added.Add(face.b);
                    added.Add(face.c);
                }

                for (int i = 0; i < added.Count; i++)
                    touched.Add(added[i]);
            }

            IndieEditMeshMeshUtil.ExpandWithCoincidentPositions(
                full,
                touched,
                IndieEditMeshFacePointGroups.CoincidentThreshold
            );

            for (int i = 0; i < faceCount; i++)
            {
                if (live[i] || !IsValidFace(full, i))
                    continue;

                var face = full.faces[i];

                if (touched.Contains(face.a) || touched.Contains(face.b) || touched.Contains(face.c))
                    contextFaces.Add(i);
            }
        }

        private static bool IsValidFace(IndieEditableMeshData data, int faceIndex)
        {
            var face = data.faces[faceIndex];
            int count = data.vertices.Count;

            return face.a >= 0 && face.a < count &&
                   face.b >= 0 && face.b < count &&
                   face.c >= 0 && face.c < count;
        }

        /// <summary>Same defaults as <see cref="IndieEditMeshMeshUtil.AddVertex"/>.</summary>
        private static void CopyVertex(IndieEditableMeshData source, int index, IndieEditableMeshData target)
        {
            target.vertices.Add(source.vertices[index]);
            target.uvs.Add(index < source.uvs.Count ? source.uvs[index] : Vector2.zero);
            target.normals.Add(index < source.normals.Count ? source.normals[index] : Vector3.up);
            target.tangents.Add(
                index < source.tangents.Count
                    ? source.tangents[index]
                    : new Vector4(1f, 0f, 0f, -1f)
            );
            target.colors.Add(index < source.colors.Count ? source.colors[index] : Color.white);
        }

        private static void CopyAttribute<T>(List<T> source, int sourceIndex, List<T> target, int targetIndex)
        {
            if (sourceIndex < source.Count && targetIndex < target.Count)
                target[targetIndex] = source[sourceIndex];
        }

        private static Dictionary<long, int> BuildEdgeLookup(IndieEditableMeshData data)
        {
            var lookup = new Dictionary<long, int>(data.edges.Count);

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                long key = IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1);

                if (!lookup.ContainsKey(key))
                    lookup.Add(key, i);
            }

            return lookup;
        }
    }
}
