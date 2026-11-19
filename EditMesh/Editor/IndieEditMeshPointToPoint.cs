using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Edge-mode point-to-point selection. Same input as Edit UV Point-to-Point Seams:
    /// click an edge endpoint, preview the shortest path, click again to add those edges.
    /// </summary>
    public static class IndieEditMeshPointToPoint
    {
        private static readonly Color PreviewCommitted = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color PreviewHover = new Color(0.3f, 0.9f, 1f, 0.9f);

        private static bool _active;
        private static bool _swallowRightClick;
        private static bool _rightClickMoved;
        private static int _anchorVertex = -1;
        private static readonly List<long> _committedPath = new List<long>();
        private static readonly List<long> _previewPath = new List<long>();

        public static bool IsActive => _active;

        /// <summary>
        /// Right click leaves the mode and eats the Scene View context menu.
        /// Selection is left untouched. Alt+RMB stays with the camera.
        /// MouseUp keeps the swallow armed so the following ContextClick is eaten too;
        /// a drag clears it, because Unity does not send ContextClick after a drag.
        /// </summary>
        public static bool TryConsumeRightClick(Event evt)
        {
            if (evt == null || evt.alt)
                return false;

            if (!_active && !_swallowRightClick)
                return false;

            bool rmb =
                evt.button == 1 &&
                (evt.type == EventType.MouseDown ||
                 evt.type == EventType.MouseDrag ||
                 evt.type == EventType.MouseUp);
            bool context = evt.type == EventType.ContextClick;

            if (!rmb && !context)
                return false;

            if (evt.type == EventType.MouseDown)
            {
                _swallowRightClick = true;
                _rightClickMoved = false;

                if (_active)
                    Deactivate();
            }
            else if (evt.type == EventType.MouseDrag)
            {
                _rightClickMoved = true;
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (_rightClickMoved)
                    _swallowRightClick = false;
            }
            else if (context)
            {
                _swallowRightClick = false;

                if (_active)
                    Deactivate();
            }

            evt.Use();
            return true;
        }

        public static void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;
            ResetTransient();

            if (_active)
            {
                IndieEditMeshCutTool.Deactivate();
                IndieEditMeshAttachTool.Deactivate();
                IndieEditMeshSelection.ResetMarquee();
            }

            SceneView.RepaintAll();
        }

        public static void Deactivate()
        {
            SetActive(false);
        }

        public static void ResetTransient()
        {
            _anchorVertex = -1;
            _committedPath.Clear();
            _previewPath.Clear();
        }

        public static bool HandleSceneGUI(
            IndieEditMeshComponent ep,
            SceneView sceneView,
            Event evt,
            int blockControl = -1
        )
        {
            if (!_active || ep == null || ep.Data == null || evt == null)
                return false;

            if (ep.subMode != IndieEditMeshSubMode.Edge)
            {
                Deactivate();
                return false;
            }

            if (IndieEditMeshPreviewOp.IsActive)
                return false;

            if (evt.alt)
                return false;

            if (blockControl < 0)
                blockControl = GUIUtility.GetControlID(FocusType.Passive);

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                UpdatePreview(ep, sceneView, evt.mousePosition);

                if (sceneView != null)
                    sceneView.Repaint();

                return false;
            }

            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                (HandleUtility.nearestControl == blockControl || GUIUtility.hotControl == 0))
            {
                GUIUtility.hotControl = blockControl;
                int vertex = IndieEditMeshSelection.PickEdgeVertex(ep, sceneView, evt.mousePosition);

                if (vertex < 0)
                {
                    evt.Use();
                    return true;
                }

                if (_anchorVertex < 0)
                {
                    _anchorVertex = vertex;
                    _committedPath.Clear();
                    _previewPath.Clear();
                }
                else
                {
                    List<long> path = FindPath(ep, sceneView, vertex);

                    if (path != null && path.Count > 0)
                    {
                        AddPathToSelection(ep, path);
                        _committedPath.AddRange(path);
                    }

                    _anchorVertex = vertex;
                    _previewPath.Clear();
                }

                evt.Use();

                if (sceneView != null)
                    sceneView.Repaint();

                return true;
            }

            if (evt.type == EventType.MouseUp &&
                evt.button == 0 &&
                GUIUtility.hotControl == blockControl)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
                return true;
            }

            return false;
        }

        public static void DrawOverlay(IndieEditMeshComponent ep)
        {
            if (!_active || ep == null || ep.Data == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var data = ep.Data;
            var transform = ep.transform;
            var previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            if (_committedPath.Count > 0)
            {
                Handles.color = PreviewCommitted;

                for (int i = 0; i < _committedPath.Count; i++)
                    DrawEdgeKey(data, transform, _committedPath[i], 4f);
            }

            if (_previewPath.Count > 0)
            {
                Handles.color = PreviewHover;

                for (int i = 0; i < _previewPath.Count; i++)
                    DrawEdgeKey(data, transform, _previewPath[i], 3.5f);
            }

            if (_anchorVertex >= 0 && _anchorVertex < data.vertices.Count)
            {
                Vector3 world = transform.TransformPoint(data.vertices[_anchorVertex]);
                float size = HandleUtility.GetHandleSize(world) * 0.1f;
                Handles.color = PreviewCommitted;
                Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
            }

            Handles.zTest = previousZTest;
        }

        private static void UpdatePreview(IndieEditMeshComponent ep, SceneView sceneView, Vector2 mousePosition)
        {
            _previewPath.Clear();

            if (_anchorVertex < 0)
                return;

            int hover = IndieEditMeshSelection.PickEdgeVertex(ep, sceneView, mousePosition);

            if (hover < 0 || hover == _anchorVertex)
                return;

            List<long> path = FindPath(ep, sceneView, hover);

            if (path != null)
                _previewPath.AddRange(path);
        }

        private static List<long> FindPath(IndieEditMeshComponent ep, SceneView sceneView, int toVertex)
        {
            Camera camera = IndieEditMeshSelection.IgnoreBackFaces && sceneView != null
                ? sceneView.camera
                : null;

            return IndieEditMeshShortestPath.FindShortestEdgePath(
                ep.Data,
                _anchorVertex,
                toVertex,
                ep.transform,
                camera
            );
        }

        private static void AddPathToSelection(IndieEditMeshComponent ep, List<long> path)
        {
            var data = ep.Data;
            bool changed = false;

            for (int i = 0; i < path.Count; i++)
            {
                IndieEditMeshMeshUtil.DecodeEdgeKey(path[i], out int v0, out int v1);
                int edgeIndex = IndieEditMeshSelection.FindEdgeIndex(data, v0, v1);

                if (edgeIndex < 0 || data.selectedEdges.Contains(edgeIndex))
                    continue;

                data.selectedEdges.Add(edgeIndex);
                changed = true;
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(ep);
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        private static void DrawEdgeKey(
            IndieEditableMeshData data,
            Transform transform,
            long key,
            float thickness
        )
        {
            IndieEditMeshMeshUtil.DecodeEdgeKey(key, out int v0, out int v1);

            if (v0 < 0 || v1 < 0 || v0 >= data.vertices.Count || v1 >= data.vertices.Count)
                return;

            Handles.DrawLine(
                transform.TransformPoint(data.vertices[v0]),
                transform.TransformPoint(data.vertices[v1]),
                thickness
            );
        }
    }
}
