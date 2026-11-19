using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// 3ds Max-style polygon smoothing groups (32 bits).
    /// </summary>
    public static class IndieEditMeshSmoothingGroups
    {
        public const int GroupCount = 32;
        public const float DefaultAutoSmoothThreshold = 45f;

        private static float _autoSmoothThreshold = DefaultAutoSmoothThreshold;

        public static float AutoSmoothThreshold
        {
            get => _autoSmoothThreshold;
            set => _autoSmoothThreshold = Mathf.Clamp(value, 0f, 180f);
        }

        public static int BitForGroup(int groupNumber)
        {
            if (groupNumber < 1 || groupNumber > GroupCount)
                return 0;

            return 1 << (groupNumber - 1);
        }

        public static void GetSelectionMaskState(
            IndieEditableMeshData data,
            out int commonBits,
            out int mixedBits
        )
        {
            commonBits = 0;
            mixedBits = 0;

            if (data == null || data.selectedFaces.Count == 0)
                return;

            bool first = true;
            int andMask = ~0;
            int orMask = 0;

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int faceIndex = data.selectedFaces[i];

                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                int sg = data.faces[faceIndex].smoothingGroups;

                if (first)
                {
                    andMask = sg;
                    orMask = sg;
                    first = false;
                }
                else
                {
                    andMask &= sg;
                    orMask |= sg;
                }
            }

            if (first)
                return;

            commonBits = andMask;
            mixedBits = orMask & ~andMask;
        }

        public static void ToggleGroup(IndieEditMeshComponent ep, int groupNumber)
        {
            if (ep == null || ep.Data == null || ep.Data.selectedFaces.Count == 0)
                return;

            int bit = BitForGroup(groupNumber);

            if (bit == 0)
                return;

            GetSelectionMaskState(ep.Data, out int commonBits, out _);
            bool clear = (commonBits & bit) != 0;

            IndieEditMeshUndo.Register(
                ep,
                clear ? "Clear Smoothing Group" : "Set Smoothing Group"
            );

            var data = ep.Data;

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int faceIndex = data.selectedFaces[i];

                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];

                if (clear)
                    face.smoothingGroups &= ~bit;
                else
                    face.smoothingGroups |= bit;

                data.faces[faceIndex] = face;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        public static void ClearAll(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || ep.Data.selectedFaces.Count == 0)
                return;

            IndieEditMeshUndo.Register(ep, "Clear Smoothing Groups");

            var data = ep.Data;

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int faceIndex = data.selectedFaces[i];

                if (faceIndex < 0 || faceIndex >= data.faces.Count)
                    continue;

                var face = data.faces[faceIndex];
                face.smoothingGroups = 0;
                data.faces[faceIndex] = face;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        public static void SelectByGroup(
            IndieEditMeshComponent ep,
            int groupNumber,
            bool clearSelection
        )
        {
            if (ep == null || ep.Data == null)
                return;

            int bit = BitForGroup(groupNumber);

            if (bit == 0)
                return;

            IndieEditMeshUndo.Register(ep, "Select By Smoothing Group");

            var data = ep.Data;

            if (clearSelection)
                data.ClearSelection();

            data.selectedVertices.Clear();
            data.selectedEdges.Clear();

            if (clearSelection)
                data.selectedFaces.Clear();

            var seen = new HashSet<int>(data.selectedFaces);

            for (int i = 0; i < data.faces.Count; i++)
            {
                if ((data.faces[i].smoothingGroups & bit) == 0)
                    continue;

                if (seen.Add(i))
                    data.selectedFaces.Add(i);
            }

            IndieEditMeshSession.RefreshMesh(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
            SceneView.RepaintAll();
        }

        public static void AutoSmooth(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || ep.Data.selectedFaces.Count == 0)
                return;

            IndieEditMeshUndo.Register(ep, "Auto Smooth");

            var data = ep.Data;
            var selected = new List<int>();

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int faceIndex = data.selectedFaces[i];

                if (faceIndex >= 0 && faceIndex < data.faces.Count)
                    selected.Add(faceIndex);
            }

            if (selected.Count == 0)
                return;

            for (int i = 0; i < selected.Count; i++)
            {
                var face = data.faces[selected[i]];
                face.smoothingGroups = 0;
                data.faces[selected[i]] = face;
            }

            var normals = new Vector3[selected.Count];

            for (int i = 0; i < selected.Count; i++)
                normals[i] = FaceNormal(data, selected[i]);

            var selectedSet = new HashSet<int>(selected);
            var edgeFaces = BuildEdgeFaceMap(data, selectedSet);
            float cosThreshold = Mathf.Cos(_autoSmoothThreshold * Mathf.Deg2Rad);

            var parent = new int[selected.Count];
            var indexOf = new Dictionary<int, int>(selected.Count);

            for (int i = 0; i < selected.Count; i++)
            {
                parent[i] = i;
                indexOf[selected[i]] = i;
            }

            foreach (var pair in edgeFaces)
            {
                var list = pair.Value;

                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        int fa = list[i];
                        int fb = list[j];

                        if (!indexOf.TryGetValue(fa, out int ia) ||
                            !indexOf.TryGetValue(fb, out int ib))
                        {
                            continue;
                        }

                        float dot = Vector3.Dot(normals[ia], normals[ib]);

                        if (dot >= cosThreshold - 1e-5f)
                            Union(parent, ia, ib);
                    }
                }
            }

            var componentToGroup = new Dictionary<int, int>();
            int nextGroup = 0;

            for (int i = 0; i < selected.Count; i++)
            {
                int root = Find(parent, i);

                if (!componentToGroup.TryGetValue(root, out int groupIndex))
                {
                    groupIndex = nextGroup % GroupCount;
                    componentToGroup[root] = groupIndex;
                    nextGroup++;
                }

                var face = data.faces[selected[i]];
                face.smoothingGroups = 1 << groupIndex;
                data.faces[selected[i]] = face;
            }

            IndieEditMeshSession.RefreshMesh(ep);
            SceneView.RepaintAll();
        }

        private static Dictionary<long, List<int>> BuildEdgeFaceMap(
            IndieEditableMeshData data,
            HashSet<int> faceFilter
        )
        {
            var map = new Dictionary<long, List<int>>();

            foreach (int faceIndex in faceFilter)
            {
                var face = data.faces[faceIndex];
                AddEdgeFace(map, face.a, face.b, faceIndex);
                AddEdgeFace(map, face.b, face.c, faceIndex);
                AddEdgeFace(map, face.c, face.a, faceIndex);
            }

            return map;
        }

        private static void AddEdgeFace(
            Dictionary<long, List<int>> map,
            int v0,
            int v1,
            int faceIndex
        )
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(v0, v1);

            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map.Add(key, list);
            }

            list.Add(faceIndex);
        }

        private static Vector3 FaceNormal(IndieEditableMeshData data, int faceIndex)
        {
            var face = data.faces[faceIndex];
            Vector3 a = data.vertices[face.a];
            Vector3 b = data.vertices[face.b];
            Vector3 c = data.vertices[face.c];
            Vector3 n = Vector3.Cross(b - a, c - a);

            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        private static int Find(int[] parent, int i)
        {
            if (parent[i] != i)
                parent[i] = Find(parent, parent[i]);

            return parent[i];
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra != rb)
                parent[rb] = ra;
        }
    }
}
