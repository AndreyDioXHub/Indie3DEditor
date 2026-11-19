using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshOps
    {
        public static void DeleteSelected(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return;

            var data = ep.Data;

            bool hasSelection =
                data.selectedVertices.Count > 0 ||
                data.selectedEdges.Count > 0 ||
                data.selectedFaces.Count > 0;

            if (!hasSelection)
                return;

            IndieEditMeshUndo.Register(ep, "Delete Selected");

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    DeleteBySelectedVertices(data);
                    break;

                case IndieEditMeshSubMode.Edge:
                    DeleteBySelectedEdges(data);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    DeleteBySelectedFaces(data);
                    break;
            }

            IndieEditMeshMeshUtil.CleanupUnusedVertices(data);
            data.ClearSelection();

            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void FlipNormals(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null)
                return;

            if (ep.Data.selectedFaces.Count == 0)
                return;

            IndieEditMeshUndo.Register(ep, "Flip Normals");

            var selected = new HashSet<int>(ep.Data.selectedFaces);
            var faces = ep.Data.faces;

            for (int i = 0; i < faces.Count; i++)
            {
                if (!selected.Contains(i))
                    continue;

                var face = faces[i];

                int temp = face.b;
                face.b = face.c;
                face.c = temp;

                faces[i] = face;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Blender-like Select More (region_extend, one hop):
        /// verts → edge neighbors of the seed only;
        /// edges → all edges incident to seed verts;
        /// faces → faces sharing an edge with the seed.
        /// </summary>
        public static void Grow(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || IndieEditMeshSession.Active != ep)
                return;

            var data = ep.Data;
            bool changed;

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    if (data.selectedVertices.Count == 0)
                        return;

                    IndieEditMeshUndo.Register(ep, "Grow Vertices");
                    changed = GrowVertices(data);
                    break;

                case IndieEditMeshSubMode.Edge:
                    if (data.selectedEdges.Count == 0)
                        return;

                    IndieEditMeshUndo.Register(ep, "Grow Edges");
                    changed = GrowEdges(data);
                    break;

                case IndieEditMeshSubMode.Polygon:
                    if (data.selectedFaces.Count == 0)
                        return;

                    IndieEditMeshUndo.Register(ep, "Grow Polygons");
                    changed = GrowFaces(data);
                    break;

                default:
                    return;
            }

            if (!changed)
                return;

            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static bool CanGrow(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || IndieEditMeshSession.Active != ep)
                return false;

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    return ep.Data.selectedVertices.Count > 0;
                case IndieEditMeshSubMode.Edge:
                    return ep.Data.selectedEdges.Count > 0;
                case IndieEditMeshSubMode.Polygon:
                    return ep.Data.selectedFaces.Count > 0;
                default:
                    return false;
            }
        }

        private static bool GrowVertices(IndieEditableMeshData data)
        {
            // Blender region_extend / Select More: one topological hop from the
            // frozen seed. Never mutate the seed set while scanning edges —
            // otherwise newly added verts keep expanding in the same pass.
            var seed = new HashSet<int>(data.selectedVertices);
            var toAdd = new HashSet<int>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                var edge = data.edges[i];
                bool a = seed.Contains(edge.v0);
                bool b = seed.Contains(edge.v1);

                if (a == b)
                    continue;

                toAdd.Add(a ? edge.v1 : edge.v0);
            }

            if (toAdd.Count == 0)
                return false;

            foreach (int v in toAdd)
                seed.Add(v);

            data.selectedVertices = new List<int>(seed);
            data.selectedEdges.Clear();
            data.selectedFaces.Clear();
            return true;
        }

        private static bool GrowEdges(IndieEditableMeshData data)
        {
            if (data.selectedEdges.Count == 0)
                return false;

            // Seed verts from the original selection only (one ring of incident edges).
            var selected = new HashSet<int>(data.selectedEdges);
            var seedVerts = new HashSet<int>();

            foreach (int edgeIndex in selected)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                seedVerts.Add(edge.v0);
                seedVerts.Add(edge.v1);
            }

            var toAdd = new HashSet<int>();

            for (int i = 0; i < data.edges.Count; i++)
            {
                if (selected.Contains(i))
                    continue;

                var edge = data.edges[i];

                if (!seedVerts.Contains(edge.v0) && !seedVerts.Contains(edge.v1))
                    continue;

                toAdd.Add(i);
            }

            if (toAdd.Count == 0)
                return false;

            foreach (int e in toAdd)
                selected.Add(e);

            data.selectedEdges = new List<int>(selected);
            data.selectedVertices.Clear();
            data.selectedFaces.Clear();
            return true;
        }

        private static bool GrowFaces(IndieEditableMeshData data)
        {
            var selected = new HashSet<int>(data.selectedFaces);
            int before = selected.Count;
            var edgeToFaces = new Dictionary<long, List<int>>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                var face = data.faces[i];
                AddFaceToEdge(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.a, face.b), i);
                AddFaceToEdge(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.b, face.c), i);
                AddFaceToEdge(edgeToFaces, IndieEditMeshMeshUtil.EdgeKey(face.c, face.a), i);
            }

            var toAdd = new List<int>();

            foreach (int faceIndex in selected)
            {
                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                TryCollectNeighborFaces(edgeToFaces, selected, face.a, face.b, toAdd);
                TryCollectNeighborFaces(edgeToFaces, selected, face.b, face.c, toAdd);
                TryCollectNeighborFaces(edgeToFaces, selected, face.c, face.a, toAdd);
            }

            for (int i = 0; i < toAdd.Count; i++)
                selected.Add(toAdd[i]);

            if (selected.Count == before)
                return false;

            data.selectedFaces = new List<int>(selected);
            data.selectedEdges.Clear();
            data.selectedVertices.Clear();
            return true;
        }

        private static void TryCollectNeighborFaces(
            Dictionary<long, List<int>> edgeToFaces,
            HashSet<int> selected,
            int v0,
            int v1,
            List<int> toAdd
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            if (!edgeToFaces.TryGetValue(key, out var faces))
                return;

            for (int i = 0; i < faces.Count; i++)
            {
                int other = faces[i];

                if (selected.Contains(other))
                    continue;

                toAdd.Add(other);
            }
        }

        private static void AddFaceToEdge(
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

        /// <summary>
        /// Flatten selection along one local axis: set that component of every
        /// affected vertex to the average of the selection. Needs ≥ 2 verts.
        /// axis: 0=X, 1=Y, 2=Z.
        /// </summary>
        public static void FlattenAxis(IndieEditMeshComponent ep, int axis)
        {
            if (ep == null || ep.Data == null)
                return;

            if (axis < 0 || axis > 2)
                return;

            int[] affected = IndieEditMeshSelection.GetAffectedVertices(ep.Data, ep.subMode);

            if (affected.Length < 2)
                return;

            IndieEditMeshUndo.Register(ep, "Flat");

            float sum = 0f;

            for (int i = 0; i < affected.Length; i++)
            {
                Vector3 p = ep.Data.vertices[affected[i]];
                sum += axis == 0 ? p.x : axis == 1 ? p.y : p.z;
            }

            float average = sum / affected.Length;

            for (int i = 0; i < affected.Length; i++)
            {
                int index = affected[i];
                Vector3 p = ep.Data.vertices[index];

                if (axis == 0)
                    p.x = average;
                else if (axis == 1)
                    p.y = average;
                else
                    p.z = average;

                ep.Data.vertices[index] = p;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        private static void DeleteBySelectedVertices(IndieEditableMeshData data)
        {
            var selectedVertices = new HashSet<int>(data.selectedVertices);

            data.faces.RemoveAll(face =>
                selectedVertices.Contains(face.a) ||
                selectedVertices.Contains(face.b) ||
                selectedVertices.Contains(face.c)
            );
        }

        private static void DeleteBySelectedEdges(IndieEditableMeshData data)
        {
            var edgeKeys = new HashSet<long>();

            foreach (int edgeIndex in data.selectedEdges)
            {
                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];
                edgeKeys.Add(IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1));
            }

            data.faces.RemoveAll(face =>
                edgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.a, face.b)) ||
                edgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.b, face.c)) ||
                edgeKeys.Contains(IndieEditMeshMeshUtil.EdgeKey(face.c, face.a))
            );
        }

        private static void DeleteBySelectedFaces(IndieEditableMeshData data)
        {
            var selectedFaces = new HashSet<int>(data.selectedFaces);

            if (selectedFaces.Count == 0)
                return;

            var newFaces = new List<IndieEditMeshFace>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                if (!selectedFaces.Contains(i))
                    newFaces.Add(data.faces[i]);
            }

            data.faces = newFaces;
        }
    }
}