using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVGizmo
    {
        public enum DragMode
        {
            None,
            Move,
            Rotate,
            Scale
        }

        private const float HandleSize = 8f;
        private const float RotateRingPadding = 18f;
        private const float RotateHitThickness = 10f;

        private static DragMode _mode;
        private static Vector2 _dragStartGui;
        private static Vector2 _pivotUv;
        private static float _startAngle;
        private static Dictionary<int, Vector2> _baseline;
        private static bool _registeredUndo;

        /// <summary>Live rotation delta in degrees for the current rotate drag.</summary>
        public static float LiveRotationDegrees { get; private set; }

        /// <summary>Live scale factors for the current scale drag (1,1 when idle).</summary>
        public static Vector2 LiveScale { get; private set; } = Vector2.one;

        public static DragMode Mode => _mode;
        public static bool IsDragging => _mode != DragMode.None;

        public static void Reset()
        {
            _mode = DragMode.None;
            _baseline = null;
            _registeredUndo = false;
            LiveRotationDegrees = 0f;
            LiveScale = Vector2.one;
        }

        public static void Draw(IndieEditUVComponent uv, IndieEditUVView view)
        {
            if (uv == null || uv.Data == null || view == null)
                return;

            if (!IndieEditUVTransform.TryGetSelectionUvBounds(uv, out Rect uvBounds))
                return;

            GetGuiBound(view, uvBounds, out Rect guiBound, out Vector2 center, out Vector2[] corners);
            DrawGizmo(guiBound, center, corners);
        }

        public static bool HandleInput(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Event evt,
            Rect canvasRect
        )
        {
            if (uv == null || uv.Data == null || view == null)
                return false;

            if (!IndieEditUVTransform.TryGetSelectionUvBounds(uv, out Rect uvBounds))
                return false;

            GetGuiBound(view, uvBounds, out Rect guiBound, out Vector2 center, out Vector2[] corners);

            if (!canvasRect.Contains(evt.mousePosition) && _mode == DragMode.None)
                return false;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0)
                        return false;

                    return BeginDrag(uv, view, evt, guiBound, center, corners, uvBounds);

                case EventType.MouseDrag:
                    if (_mode == DragMode.None || evt.button != 0)
                        return false;

                    UpdateDrag(uv, view, evt);
                    evt.Use();
                    return true;

                case EventType.MouseUp:
                    if (_mode == DragMode.None || evt.button != 0)
                        return false;

                    DragMode finished = _mode;
                    float rot = LiveRotationDegrees;
                    Vector2 scale = LiveScale;
                    bool changed = _registeredUndo;
                    Reset();

                    if (changed)
                        EditorUtility.SetDirty(uv);

                    IndieEditUVSettingsWindow.NotifyGizmoTransformCommitted(finished, rot, scale);
                    evt.Use();
                    return true;
            }

            return _mode != DragMode.None;
        }

        private static void GetGuiBound(
            IndieEditUVView view,
            Rect uvBounds,
            out Rect guiBound,
            out Vector2 center,
            out Vector2[] corners
        )
        {
            Vector2 guiMin = view.UvToGui(new Vector2(uvBounds.xMin, uvBounds.yMax));
            Vector2 guiMax = view.UvToGui(new Vector2(uvBounds.xMax, uvBounds.yMin));
            guiBound = Rect.MinMaxRect(
                Mathf.Min(guiMin.x, guiMax.x),
                Mathf.Min(guiMin.y, guiMax.y),
                Mathf.Max(guiMin.x, guiMax.x),
                Mathf.Max(guiMin.y, guiMax.y)
            );
            center = guiBound.center;
            corners = new[]
            {
                new Vector2(guiBound.xMin, guiBound.yMin),
                new Vector2(guiBound.xMax, guiBound.yMin),
                new Vector2(guiBound.xMax, guiBound.yMax),
                new Vector2(guiBound.xMin, guiBound.yMax)
            };
        }

        private static bool BeginDrag(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Event evt,
            Rect guiBound,
            Vector2 center,
            Vector2[] corners,
            Rect uvBounds
        )
        {
            float distCenter = Vector2.Distance(evt.mousePosition, center);

            if (distCenter <= HandleSize)
            {
                StartDrag(uv, DragMode.Move, evt, view, uvBounds);
                evt.Use();
                return true;
            }

            for (int i = 0; i < corners.Length; i++)
            {
                if (Vector2.Distance(evt.mousePosition, corners[i]) <= HandleSize)
                {
                    StartDrag(uv, DragMode.Scale, evt, view, uvBounds);
                    evt.Use();
                    return true;
                }
            }

            float ringRadius = GetRingRadius(guiBound);
            float distRing = Mathf.Abs(distCenter - ringRadius);

            if (distRing <= RotateHitThickness)
            {
                StartDrag(uv, DragMode.Rotate, evt, view, uvBounds);
                evt.Use();
                return true;
            }

            return false;
        }

        private static void StartDrag(
            IndieEditUVComponent uv,
            DragMode mode,
            Event evt,
            IndieEditUVView view,
            Rect uvBounds
        )
        {
            _mode = mode;
            _dragStartGui = evt.mousePosition;
            _pivotUv = uvBounds.center;
            Vector2 pivotGui = view.UvToGui(_pivotUv);
            _startAngle = Mathf.Atan2(
                evt.mousePosition.y - pivotGui.y,
                evt.mousePosition.x - pivotGui.x
            );

            int[] indices = IndieEditUVTransform.GetAffectedVertices(uv);
            _baseline = IndieEditUVTransform.CaptureUvs(uv, indices);
            _registeredUndo = false;
            LiveRotationDegrees = 0f;
            LiveScale = Vector2.one;
        }

        private static void EnsureUndo(IndieEditUVComponent uv, string name)
        {
            if (_registeredUndo)
                return;

            IndieEditUVUndo.Register(uv, name);
            _registeredUndo = true;
        }

        private static void UpdateDrag(IndieEditUVComponent uv, IndieEditUVView view, Event evt)
        {
            Vector2 startUv = view.GuiToUv(_dragStartGui);
            Vector2 currentUv = view.GuiToUv(evt.mousePosition);

            switch (_mode)
            {
                case DragMode.Move:
                    EnsureUndo(uv, "Move UV");
                    IndieEditUVTransform.ApplyMove(uv, _baseline, ConstrainMove(evt, currentUv - startUv));
                    break;

                case DragMode.Rotate:
                {
                    EnsureUndo(uv, "Rotate UV");
                    Vector2 pivotGui = view.UvToGui(_pivotUv);
                    float angle = Mathf.Atan2(
                        evt.mousePosition.y - pivotGui.y,
                        evt.mousePosition.x - pivotGui.x
                    );
                    float delta = -(angle - _startAngle);
                    LiveRotationDegrees = delta * Mathf.Rad2Deg;
                    IndieEditUVTransform.ApplyRotate(uv, _baseline, _pivotUv, delta);
                    break;
                }

                case DragMode.Scale:
                    EnsureUndo(uv, "Scale UV");
                    LiveScale = ComputeScale(view, evt);
                    IndieEditUVTransform.ApplyScale(uv, _baseline, _pivotUv, LiveScale);
                    break;
            }
        }

        /// <summary>
        /// Shift locks the move to the screen axis with the larger drag from the press point.
        /// Horizontal keeps U, vertical keeps V. Releasing Shift restores both axes.
        /// </summary>
        private static Vector2 ConstrainMove(Event evt, Vector2 deltaUv)
        {
            if (!evt.shift)
                return deltaUv;

            Vector2 guiDelta = evt.mousePosition - _dragStartGui;

            if (Mathf.Abs(guiDelta.x) >= Mathf.Abs(guiDelta.y))
                deltaUv.y = 0f;
            else
                deltaUv.x = 0f;

            return deltaUv;
        }

        private static Vector2 ComputeScale(IndieEditUVView view, Event evt)
        {
            Vector2 startLocal = view.GuiToUv(_dragStartGui) - _pivotUv;
            Vector2 currentLocal = view.GuiToUv(evt.mousePosition) - _pivotUv;

            float sx = Mathf.Abs(startLocal.x) > 1e-6f
                ? currentLocal.x / startLocal.x
                : 1f;
            float sy = Mathf.Abs(startLocal.y) > 1e-6f
                ? currentLocal.y / startLocal.y
                : 1f;

            if (Mathf.Abs(sx) < 1e-4f)
                sx = sx < 0f ? -1e-4f : 1e-4f;

            if (Mathf.Abs(sy) < 1e-4f)
                sy = sy < 0f ? -1e-4f : 1e-4f;

            bool alt = evt.alt;
            bool shift = evt.shift;

            if (alt && shift)
            {
                Vector2 guiDelta = evt.mousePosition - _dragStartGui;

                if (Mathf.Abs(guiDelta.x) >= Mathf.Abs(guiDelta.y))
                    return new Vector2(sx, 1f);

                return new Vector2(1f, sy);
            }

            if (alt)
            {
                float uniform = Mathf.Abs(sx) >= Mathf.Abs(sy) ? sx : sy;
                return new Vector2(uniform, uniform);
            }

            return new Vector2(sx, sy);
        }

        private static float GetRingRadius(Rect guiBound)
        {
            float half = Mathf.Max(guiBound.width, guiBound.height) * 0.5f;
            return half + RotateRingPadding;
        }

        private static void DrawGizmo(Rect guiBound, Vector2 center, Vector2[] corners)
        {
            Handles.BeginGUI();

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            DrawRectOutline(guiBound);

            float radius = GetRingRadius(guiBound);
            Handles.color = new Color(0.35f, 0.85f, 1f, 0.9f);
            Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);

            DrawHandleSquare(center, HandleSize, new Color(1f, 0.9f, 0.2f, 1f));

            for (int i = 0; i < corners.Length; i++)
                DrawHandleSquare(corners[i], HandleSize, new Color(1f, 0.55f, 0.15f, 1f));

            Handles.EndGUI();
        }

        private static void DrawRectOutline(Rect rect)
        {
            Vector3 a = new Vector3(rect.xMin, rect.yMin, 0f);
            Vector3 b = new Vector3(rect.xMax, rect.yMin, 0f);
            Vector3 c = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 d = new Vector3(rect.xMin, rect.yMax, 0f);
            Handles.DrawLine(a, b);
            Handles.DrawLine(b, c);
            Handles.DrawLine(c, d);
            Handles.DrawLine(d, a);
        }

        private static void DrawHandleSquare(Vector2 center, float size, Color color)
        {
            float h = size * 0.5f;
            Rect r = new Rect(center.x - h, center.y - h, size, size);
            EditorGUI.DrawRect(r, color);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 1f, r.height), Color.black);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), Color.black);
        }
    }
}
