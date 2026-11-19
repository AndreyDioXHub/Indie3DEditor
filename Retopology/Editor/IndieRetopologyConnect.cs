using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyConnect
    {
        public static void Connect(IndieRetopologyComponent retopo)
        {
            IndieRetopologyConnectWindow.Dismiss();

            if (retopo == null || IndieRetopologySession.Active != retopo)
                return;

            retopo.EnsureData();
            IndieRetopologyMeshData data = retopo.Data;

            if (data.selectedEdges == null || data.selectedEdges.Count <= 2)
                return;

            IndieRetopologyMeshData baseline = data.Clone();
            IndieEditableMeshData editable = ToEditable(data);
            IndieEditMeshEdgeOps.ApplyConnect(
                editable,
                IndieEditMeshEdgeParams.ConnectSegments,
                IndieEditMeshEdgeParams.ConnectPinch,
                IndieEditMeshEdgeParams.ConnectSlide
            );
            CopyFromEditable(data, editable);

            IndieRetopologyUndo.CommitBaseline(retopo, baseline);
            IndieRetopologySession.RefreshMesh(retopo);
            EditorUtility.SetDirty(retopo);
            SceneView.RepaintAll();
        }

        public static void DrawPreview(IndieRetopologyComponent retopo)
        {
            if (!IndieRetopologyConnectWindow.IsOpen)
                return;

            if (retopo == null || IndieRetopologySession.Active != retopo || retopo.Data == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            retopo.EnsureData();
            IndieEditableMeshData editable = ToEditable(retopo.Data);
            IndieEditMeshEdgeOps.DrawConnectPreview(editable, retopo.transform, retopo.GetEntityId().GetHashCode());
        }

        public static int CountEdges(IndieRetopologyMeshData data)
        {
            if (data == null || data.faces == null)
                return 0;

            var keys = new HashSet<long>();

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];
                keys.Add(IndieEditMeshMeshUtil.EdgeKey(face.a, face.b));
                keys.Add(IndieEditMeshMeshUtil.EdgeKey(face.b, face.c));
                keys.Add(IndieEditMeshMeshUtil.EdgeKey(face.c, face.a));
            }

            return keys.Count;
        }

        private static IndieEditableMeshData ToEditable(IndieRetopologyMeshData data)
        {
            var editable = new IndieEditableMeshData();
            editable.vertices.AddRange(data.vertices);

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];
                editable.faces.Add(new IndieEditMeshFace
                {
                    a = face.a,
                    b = face.b,
                    c = face.c
                });
            }

            editable.RebuildEdges();

            if (data.selectedEdges == null)
                return editable;

            for (int s = 0; s < data.selectedEdges.Count; s++)
            {
                IndieRetopologyEdge selected = data.selectedEdges[s];
                long key = IndieEditMeshMeshUtil.EdgeKey(selected.v0, selected.v1);

                for (int i = 0; i < editable.edges.Count; i++)
                {
                    IndieEditMeshEdge edge = editable.edges[i];

                    if (IndieEditMeshMeshUtil.EdgeKey(edge.v0, edge.v1) != key)
                        continue;

                    editable.selectedEdges.Add(i);
                    break;
                }
            }

            return editable;
        }

        private static void CopyFromEditable(IndieRetopologyMeshData data, IndieEditableMeshData editable)
        {
            data.vertices.Clear();
            data.faces.Clear();
            data.selectedEdges.Clear();
            data.MarkChanged();

            if (editable == null)
                return;

            if (editable.vertices != null)
                data.vertices.AddRange(editable.vertices);

            if (editable.faces != null)
            {
                for (int i = 0; i < editable.faces.Count; i++)
                {
                    IndieEditMeshFace face = editable.faces[i];
                    data.faces.Add(new IndieRetopologyFace
                    {
                        a = face.a,
                        b = face.b,
                        c = face.c
                    });
                }
            }

            if (editable.edges == null || editable.selectedEdges == null)
                return;

            for (int i = 0; i < editable.selectedEdges.Count; i++)
            {
                int index = editable.selectedEdges[i];

                if (index < 0 || index >= editable.edges.Count)
                    continue;

                IndieEditMeshEdge edge = editable.edges[index];
                data.selectedEdges.Add(IndieRetopologyMeshData.Ordered(edge.v0, edge.v1));
            }
        }
    }
}
