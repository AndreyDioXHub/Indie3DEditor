using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologySceneView
    {
        private static readonly Color SelectedEdgeColor = new Color(0.2f, 0.85f, 1f, 1f);
        private static readonly Color VertexColor = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color HoverColor = new Color(0.25f, 1f, 0.45f, 1f);

        public static void Draw(IndieRetopologyComponent retopo, SceneView sceneView)
        {
            if (retopo == null || retopo.Data == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var data = retopo.Data;
            Transform transform = retopo.transform;
            Camera camera = sceneView != null ? sceneView.camera : null;
            CompareFunction previous = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            DrawSelectedEdges(data, transform, camera);
            DrawLooseVertices(data, transform);
            IndieRetopologyCreatePolygon.DrawPending(retopo);
            DrawHover(data, transform);
            DrawMagnet(data, transform);
            Handles.zTest = previous;

            IndieRetopologyConnect.DrawPreview(retopo);
        }

        private static void DrawLooseVertices(IndieRetopologyMeshData data, Transform transform)
        {
            if (!IndieRetopologyPick.HasLooseVertices(data))
                return;

            Handles.color = VertexColor;
            int count = IndieRetopologyPick.LooseCount(data);

            for (int i = 0; i < count; i++)
            {
                int index = IndieRetopologyPick.LooseAt(data, i);
                Vector3 world = transform.TransformPoint(data.vertices[index]);
                float size = HandleUtility.GetHandleSize(world) * 0.045f;

                Handles.DotHandleCap(
                    0,
                    world,
                    Quaternion.identity,
                    size,
                    EventType.Repaint
                );
            }
        }

        private static void DrawSelectedEdges(IndieRetopologyMeshData data, Transform transform, Camera camera)
        {
            if (data.selectedEdges == null)
                return;

            Handles.color = SelectedEdgeColor;

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                IndieRetopologyEdge edge = data.selectedEdges[i];

                if (!TryWorld(data, transform, edge.v0, out Vector3 a) ||
                    !TryWorld(data, transform, edge.v1, out Vector3 b))
                {
                    continue;
                }

                if (!EdgeIsFront(data, transform, camera, edge.v0, edge.v1))
                    continue;

                Handles.DrawAAPolyLine(3f, a, b);
            }
        }

        private static void DrawHover(IndieRetopologyMeshData data, Transform transform)
        {
            IndieRetopologyHit hover = IndieRetopologyPick.CurrentHover;

            if (!hover.valid)
                return;

            Handles.color = HoverColor;

            if (hover.kind == IndieRetopologyPickKind.Vertex)
            {
                if (!TryWorld(data, transform, hover.a, out Vector3 world))
                    return;

                float size = HandleUtility.GetHandleSize(world) * 0.07f;

                Handles.DotHandleCap(
                    0,
                    world,
                    Quaternion.identity,
                    size,
                    EventType.Repaint
                );
                return;
            }

            if (!TryWorld(data, transform, hover.a, out Vector3 a) ||
                !TryWorld(data, transform, hover.b, out Vector3 b))
            {
                return;
            }

            Handles.DrawAAPolyLine(3f, a, b);
        }

        private static void DrawMagnet(IndieRetopologyMeshData data, Transform transform)
        {
            int index = IndieRetopologyDrag.MagnetVertex;

            if (!TryWorld(data, transform, index, out Vector3 world))
                return;

            Handles.color = HoverColor;
            float size = HandleUtility.GetHandleSize(world) * 0.07f;

            Handles.DotHandleCap(
                0,
                world,
                Quaternion.identity,
                size,
                EventType.Repaint
            );
        }

        private static bool IsFront(Camera camera, Vector3 a, Vector3 b, Vector3 c)
        {
            return camera == null || IndieEditMeshPickMath.IsFrontFacingTriangle(camera, a, b, c);
        }

        private static bool EdgeIsFront(
            IndieRetopologyMeshData data,
            Transform transform,
            Camera camera,
            int v0,
            int v1)
        {
            if (data.faces == null || data.faces.Count == 0)
                return true;

            bool onFace = false;

            for (int i = 0; i < data.faces.Count; i++)
            {
                IndieRetopologyFace face = data.faces[i];
                bool uses =
                    (face.a == v0 && face.b == v1) ||
                    (face.b == v0 && face.c == v1) ||
                    (face.c == v0 && face.a == v1) ||
                    (face.a == v1 && face.b == v0) ||
                    (face.b == v1 && face.c == v0) ||
                    (face.c == v1 && face.a == v0);

                if (!uses)
                    continue;

                onFace = true;

                if (!TryWorld(data, transform, face.a, out Vector3 a) ||
                    !TryWorld(data, transform, face.b, out Vector3 b) ||
                    !TryWorld(data, transform, face.c, out Vector3 c))
                {
                    continue;
                }

                if (IsFront(camera, a, b, c))
                    return true;
            }

            return !onFace;
        }

        private static bool TryWorld(
            IndieRetopologyMeshData data,
            Transform transform,
            int index,
            out Vector3 world)
        {
            world = default;

            if (data.vertices == null || index < 0 || index >= data.vertices.Count)
                return false;

            world = transform.TransformPoint(data.vertices[index]);
            return true;
        }
    }
}
