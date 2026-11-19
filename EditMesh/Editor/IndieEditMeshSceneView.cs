using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshSceneView
    {
        public static void Draw(IndieEditMeshComponent ep, SceneView sceneView)
        {
            if (ep == null || ep.Data == null)
                return;

            IndieEditMeshEditProxy.DrawGhost(ep);
            DrawSelection(ep);

            if (!IndieEditMeshCutTool.IsActive && !IndieEditMeshPointToPoint.IsActive)
                IndieEditMeshTransformTool.Draw(ep);

            IndieEditMeshPointToPoint.DrawOverlay(ep);
            IndieEditMeshEdgeOps.DrawConnectPreview(ep);
            DrawMarquee();
        }

        private static void DrawMarquee()
        {
            if (!IndieEditMeshSelection.IsMarqueeActive)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            Rect rect = IndieEditMeshSelection.GetMarqueeGuiRect();
            Color fill = new Color(0.2f, 0.55f, 1f, 0.15f);
            Color border = new Color(0.25f, 0.65f, 1f, 0.95f);

            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, fill);
            DrawGuiRectBorder(rect, border, 1f);
            Handles.EndGUI();
        }

        private static void DrawGuiRectBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private static void DrawSelection(IndieEditMeshComponent ep)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var data = IndieEditMeshEditProxy.DataFor(ep);
            var transform = ep.transform;

            if (data.selectedFaces.Count > 0)
            {
                Handles.color = new Color(1f, 0.25f, 0.25f, 0.95f);

                foreach (int faceIndex in data.selectedFaces)
                {
                    if (faceIndex < 0 || faceIndex >= data.faces.Count)
                        continue;

                    var face = data.faces[faceIndex];

                    if (!IsValidVertex(data, face.a) ||
                        !IsValidVertex(data, face.b) ||
                        !IsValidVertex(data, face.c))
                    {
                        continue;
                    }

                    Vector3 a = transform.TransformPoint(data.vertices[face.a]);
                    Vector3 b = transform.TransformPoint(data.vertices[face.b]);
                    Vector3 c = transform.TransformPoint(data.vertices[face.c]);

                    Handles.DrawLine(a, b);
                    Handles.DrawLine(b, c);
                    Handles.DrawLine(c, a);
                }
            }

            if (data.selectedEdges.Count > 0)
            {
                Handles.color = Color.cyan;

                foreach (int edgeIndex in data.selectedEdges)
                {
                    if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                        continue;

                    var edge = data.edges[edgeIndex];

                    if (!IsValidVertex(data, edge.v0) || !IsValidVertex(data, edge.v1))
                        continue;

                    Vector3 a = transform.TransformPoint(data.vertices[edge.v0]);
                    Vector3 b = transform.TransformPoint(data.vertices[edge.v1]);

                    Handles.DrawLine(a, b);
                }
            }

            if (data.selectedVertices.Count > 0)
            {
                Handles.color = Color.yellow;

                foreach (int vertexIndex in data.selectedVertices)
                {
                    if (!IsValidVertex(data, vertexIndex))
                        continue;

                    Vector3 worldPosition = transform.TransformPoint(data.vertices[vertexIndex]);
                    float size = HandleUtility.GetHandleSize(worldPosition) * 0.05f;

                    Handles.DotHandleCap(
                        0,
                        worldPosition,
                        Quaternion.identity,
                        size,
                        EventType.Repaint
                    );
                }
            }
        }

        private static bool IsValidVertex(IndieEditableMeshData data, int index)
        {
            return index >= 0 && index < data.vertices.Count;
        }
    }
}