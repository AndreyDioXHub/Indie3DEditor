using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyCreatePolygon
    {
        private static readonly Color LineColor = new Color(0.45f, 0.95f, 0.55f, 1f);
        private static readonly List<int> Points = new List<int>(3);

        private static IndieRetopologyMeshData _baseline;
        private static IndieRetopologyComponent _owner;

        public static bool IsActive { get; private set; }
        public static int PointCount => Points.Count;

        public static void Toggle()
        {
            if (IsActive)
                Cancel();
            else
                Activate();
        }

        public static void Activate()
        {
            IndieRetopologyAttach.Deactivate();
            Points.Clear();
            _baseline = null;
            _owner = IndieRetopologySession.Active;
            IsActive = true;
            Repaint();
        }

        public static void CaptureBaseline(IndieRetopologyComponent retopo)
        {
            if (_baseline != null || retopo == null || retopo.Data == null)
                return;

            _baseline = retopo.Data.Clone();
            _owner = retopo;
        }

        public static void TrackPoint(int index)
        {
            if (index < 0 || Points.Count >= 3)
                return;

            Points.Add(index);
        }

        public static void OnPointReleased(IndieRetopologyComponent retopo, Camera camera)
        {
            if (!IsActive || Points.Count < 3)
                return;

            int a = Points[0];
            int b = Points[1];
            int c = Points[2];
            IndieRetopologyMeshData baseline = _baseline;
            ClearAttempt();
            IsActive = false;

            if (retopo != null && retopo.Data != null)
            {
                IndieRetopologyDrag.AddOrientedFace(retopo, camera, a, b, c);
                retopo.Data.MarkChanged();
                IndieRetopologyUndo.CommitBaseline(retopo, baseline);
                IndieRetopologyPick.ClearHover();
                IndieRetopologyPick.InvalidateCache();
                IndieRetopologySession.RefreshMesh(retopo);
                EditorUtility.SetDirty(retopo);
            }

            Repaint();
        }

        public static bool CancelIfPending()
        {
            if (!IsActive || Points.Count == 0)
                return false;

            Cancel();
            return true;
        }

        public static void Cancel()
        {
            if (!IsActive)
                return;

            IndieRetopologyComponent owner = _owner != null ? _owner : IndieRetopologySession.Active;
            IndieRetopologyMeshData baseline = _baseline;
            ClearAttempt();
            IsActive = false;
            IndieRetopologyDrag.Reset();
            IndieRetopologyManager.ClearEditPointer();

            if (owner != null && baseline != null)
            {
                owner.SetData(baseline);
                owner.Data.MarkChanged();
                IndieRetopologyPick.ClearHover();
                IndieRetopologyPick.InvalidateCache();
                IndieRetopologySession.RefreshMesh(owner);
                EditorUtility.SetDirty(owner);
            }

            Repaint();
        }

        public static bool HandleSceneGUI(IndieRetopologyComponent retopo, SceneView sceneView, Event evt)
        {
            if (!IsActive || retopo == null || evt == null)
                return false;

            Handles.BeginGUI();
            GUI.Box(new Rect(12f, 12f, 460f, 40f), "Create Polygon: click 3 points  |  Esc / RMB = cancel");
            Handles.EndGUI();

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Cancel();
                evt.Use();

                if (sceneView != null)
                    sceneView.Repaint();

                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && !evt.alt)
            {
                Cancel();
                evt.Use();

                if (sceneView != null)
                    sceneView.Repaint();

                return true;
            }

            return false;
        }

        public static void DrawPending(IndieRetopologyComponent retopo)
        {
            if (!IsActive || Points.Count < 2 || retopo == null || retopo.Data == null)
                return;

            var vertices = retopo.Data.vertices;

            if (vertices == null)
                return;

            Transform transform = retopo.transform;
            Handles.color = LineColor;
            Vector3 previous = default;
            bool hasPrevious = false;

            for (int i = 0; i < Points.Count; i++)
            {
                int index = Points[i];

                if (index < 0 || index >= vertices.Count)
                {
                    hasPrevious = false;
                    continue;
                }

                Vector3 world = transform.TransformPoint(vertices[index]);

                if (hasPrevious)
                    Handles.DrawAAPolyLine(3f, previous, world);

                previous = world;
                hasPrevious = true;
            }

            if (Points.Count < 3)
                return;

            int first = Points[0];
            int last = Points[Points.Count - 1];

            if (first < 0 || last < 0 || first >= vertices.Count || last >= vertices.Count)
                return;

            Handles.DrawAAPolyLine(
                3f,
                transform.TransformPoint(vertices[last]),
                transform.TransformPoint(vertices[first]));
        }

        private static void ClearAttempt()
        {
            Points.Clear();
            _baseline = null;
            _owner = null;
        }

        private static void Repaint()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
