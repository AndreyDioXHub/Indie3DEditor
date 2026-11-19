using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Same bound gizmo as <see cref="IndieEditUVGizmo"/>, but transforms pelt stretcher
    /// points only (not mesh UVs).
    /// </summary>
    public static class IndieEditUVStretcherGizmo
    {
        private const float HandleSize = 8f;
        private const float RotateRingPadding = 18f;
        private const float RotateHitThickness = 10f;
        private const float PickPixels = 10f;
        private const float DoubleClickSeconds = 0.35f;
        private const float DoubleClickPixels = 6f;

        private static IndieEditUVGizmo.DragMode _mode;
        private static Vector2 _dragStartGui;
        private static Vector2 _pivotUv;
        private static float _startAngle;
        private static Dictionary<int, Vector2> _baseline;

        private static double _lastClickTime = -1d;
        private static Vector2 _lastClickGui;

        public static bool IsDragging => _mode != IndieEditUVGizmo.DragMode.None;

        public static void Reset()
        {
            _mode = IndieEditUVGizmo.DragMode.None;
            _baseline = null;
            _lastClickTime = -1d;
        }

        public static void Draw(IndieEditUVView view)
        {
            if (view == null ||
                !IndieEditUVPelt.IsActive ||
                !IndieEditUVPelt.SelectStretchersMode ||
                IndieEditUVPelt.SelectedStretcherIndices.Count == 0)
            {
                return;
            }

            if (!IndieEditUVPelt.TryGetSelectedStretcherBounds(out Rect uvBounds))
                return;

            GetGuiBound(view, uvBounds, out Rect guiBound, out Vector2 center, out Vector2[] corners);

            // When all stretchers are selected, do not draw the 4 AABB corners —
            // they look like "only 4 stretchers". Ring points come from DrawPeltOverlay.
            bool allSelected =
                IndieEditUVPelt.Stretchers != null &&
                IndieEditUVPelt.SelectedStretcherIndices.Count >= IndieEditUVPelt.Stretchers.Count;

            DrawGizmo(guiBound, center, corners, drawCorners: !allSelected);
        }

        public static bool HandleInput(
            IndieEditUVComponent uv,
            IndieEditUVView view,
            Event evt,
            Rect canvasRect
        )
        {
            if (uv == null ||
                view == null ||
                !IndieEditUVPelt.IsActive ||
                !IndieEditUVPelt.SelectStretchersMode ||
                IndieEditUVPelt.Stretchers == null)
            {
                return false;
            }

            Vector2 mouse = evt.mousePosition;
            bool inCanvas = canvasRect.Contains(mouse);

            if (_mode != IndieEditUVGizmo.DragMode.None)
            {
                switch (evt.type)
                {
                    case EventType.MouseDrag:
                        if (evt.button != 0)
                            return false;

                        UpdateDrag(view, evt);
                        evt.Use();
                        return true;

                    case EventType.MouseUp:
                        if (evt.button != 0)
                            return false;

                        ResetDrag();
                        evt.Use();
                        return true;
                }

                return true;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0 || !inCanvas)
                return false;

            // Double-click → select all stretchers
            if (IsDoubleClick(mouse))
            {
                IndieEditUVPelt.SelectAllStretchers();
                _lastClickTime = -1d;
                evt.Use();
                return true;
            }

            _lastClickTime = EditorApplication.timeSinceStartup;
            _lastClickGui = mouse;

            if (IndieEditUVPelt.SelectedStretcherIndices.Count > 0 &&
                IndieEditUVPelt.TryGetSelectedStretcherBounds(out Rect uvBounds))
            {
                GetGuiBound(view, uvBounds, out Rect guiBound, out Vector2 center, out Vector2[] corners);

                if (TryBeginGizmoDrag(view, evt, guiBound, center, corners, uvBounds))
                    return true;
            }

            int hit = PickStretcher(view, mouse);

            if (hit >= 0)
            {
                bool additive = evt.shift || evt.control || evt.command;

                if (!additive)
                    IndieEditUVPelt.SelectedStretcherIndices.Clear();

                if (!IndieEditUVPelt.SelectedStretcherIndices.Add(hit) && additive)
                    IndieEditUVPelt.SelectedStretcherIndices.Remove(hit);

                evt.Use();
                return true;
            }

            if (!(evt.shift || evt.control || evt.command))
                IndieEditUVPelt.SelectedStretcherIndices.Clear();

            evt.Use();
            return true;
        }

        private static bool IsDoubleClick(Vector2 mouse)
        {
            if (_lastClickTime < 0d)
                return false;

            double dt = EditorApplication.timeSinceStartup - _lastClickTime;

            if (dt > DoubleClickSeconds)
                return false;

            return Vector2.Distance(mouse, _lastClickGui) <= DoubleClickPixels;
        }

        private static bool TryBeginGizmoDrag(
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
                StartDrag(IndieEditUVGizmo.DragMode.Move, evt, view, uvBounds);
                evt.Use();
                return true;
            }

            for (int i = 0; i < corners.Length; i++)
            {
                if (Vector2.Distance(evt.mousePosition, corners[i]) <= HandleSize)
                {
                    StartDrag(IndieEditUVGizmo.DragMode.Scale, evt, view, uvBounds);
                    evt.Use();
                    return true;
                }
            }

            float ringRadius = GetRingRadius(guiBound);
            float distRing = Mathf.Abs(distCenter - ringRadius);

            if (distRing <= RotateHitThickness)
            {
                StartDrag(IndieEditUVGizmo.DragMode.Rotate, evt, view, uvBounds);
                evt.Use();
                return true;
            }

            return false;
        }

        private static void StartDrag(
            IndieEditUVGizmo.DragMode mode,
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

            _baseline = new Dictionary<int, Vector2>();

            foreach (int index in IndieEditUVPelt.SelectedStretcherIndices)
            {
                if (index >= 0 && index < IndieEditUVPelt.Stretchers.Count)
                    _baseline[index] = IndieEditUVPelt.Stretchers[index];
            }
        }

        private static void ResetDrag()
        {
            _mode = IndieEditUVGizmo.DragMode.None;
            _baseline = null;
        }

        private static void UpdateDrag(IndieEditUVView view, Event evt)
        {
            if (_baseline == null || _baseline.Count == 0)
                return;

            Vector2 startUv = view.GuiToUv(_dragStartGui);
            Vector2 currentUv = view.GuiToUv(evt.mousePosition);

            switch (_mode)
            {
                case IndieEditUVGizmo.DragMode.Move:
                {
                    Vector2 delta = currentUv - startUv;

                    foreach (var pair in _baseline)
                        IndieEditUVPelt.SetStretcher(pair.Key, pair.Value + delta);

                    break;
                }

                case IndieEditUVGizmo.DragMode.Rotate:
                {
                    Vector2 pivotGui = view.UvToGui(_pivotUv);
                    float angle = Mathf.Atan2(
                        evt.mousePosition.y - pivotGui.y,
                        evt.mousePosition.x - pivotGui.x
                    );
                    float delta = -(angle - _startAngle);
                    float cos = Mathf.Cos(delta);
                    float sin = Mathf.Sin(delta);

                    foreach (var pair in _baseline)
                    {
                        Vector2 p = pair.Value - _pivotUv;
                        IndieEditUVPelt.SetStretcher(
                            pair.Key,
                            _pivotUv + new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos)
                        );
                    }

                    break;
                }

                case IndieEditUVGizmo.DragMode.Scale:
                {
                    Vector2 scale = ComputeScale(view, evt);

                    foreach (var pair in _baseline)
                    {
                        Vector2 p = pair.Value - _pivotUv;
                        IndieEditUVPelt.SetStretcher(
                            pair.Key,
                            _pivotUv + new Vector2(p.x * scale.x, p.y * scale.y)
                        );
                    }

                    break;
                }
            }
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

        private static int PickStretcher(IndieEditUVView view, Vector2 mouseGui)
        {
            var stretchers = IndieEditUVPelt.Stretchers;

            if (stretchers == null)
                return -1;

            float best = PickPixels;
            int bestIndex = -1;

            for (int i = 0; i < stretchers.Count; i++)
            {
                float d = Vector2.Distance(mouseGui, view.UvToGui(stretchers[i]));

                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }

            return bestIndex;
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

            // Degenerate selection (single point): give handles a usable size.
            if (guiBound.width < 4f)
            {
                float c = guiBound.center.x;
                guiBound.xMin = c - 20f;
                guiBound.xMax = c + 20f;
            }

            if (guiBound.height < 4f)
            {
                float c = guiBound.center.y;
                guiBound.yMin = c - 20f;
                guiBound.yMax = c + 20f;
            }

            center = guiBound.center;
            corners = new[]
            {
                new Vector2(guiBound.xMin, guiBound.yMin),
                new Vector2(guiBound.xMax, guiBound.yMin),
                new Vector2(guiBound.xMax, guiBound.yMax),
                new Vector2(guiBound.xMin, guiBound.yMax)
            };
        }

        private static float GetRingRadius(Rect guiBound)
        {
            float half = Mathf.Max(guiBound.width, guiBound.height) * 0.5f;
            return half + RotateRingPadding;
        }

        private static void DrawGizmo(
            Rect guiBound,
            Vector2 center,
            Vector2[] corners,
            bool drawCorners
        )
        {
            Handles.BeginGUI();

            if (drawCorners)
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
                DrawRectOutline(guiBound);
            }

            float radius = GetRingRadius(guiBound);
            Handles.color = new Color(0.35f, 0.85f, 1f, 0.9f);
            Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);

            DrawHandleSquare(center, HandleSize, new Color(1f, 0.9f, 0.2f, 1f));

            if (drawCorners)
            {
                for (int i = 0; i < corners.Length; i++)
                    DrawHandleSquare(corners[i], HandleSize, new Color(1f, 0.55f, 0.15f, 1f));
            }

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
