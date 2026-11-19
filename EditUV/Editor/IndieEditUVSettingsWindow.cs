using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public class IndieEditUVSettingsWindow : EditorWindow
    {
        private static readonly Color AxisUColor = new Color(0.9f, 0.25f, 0.25f, 1f);
        private static readonly Color AxisVColor = new Color(0.25f, 0.85f, 0.35f, 1f);

        private IndieEditUVComponent _component;
        private readonly IndieEditUVView _view = new IndieEditUVView();
        private IndieEditUVCanvasGpu _gpu;
        private bool _framedOnce;

        private bool _panning;
        private Vector2 _panStartMouse;
        private Vector2 _panStartPan;

        private bool _marquee;
        private Vector2 _marqueeStart;
        private Vector2 _marqueeCurrent;
        private bool _marqueeModifiers;

        // Transform readout relative to current selection bound.
        private int _selectionSignature = int.MinValue;
        private float _uiRotation;
        private float _uiScale = 1f;

        public static void Open(IndieEditUVComponent uv)
        {
            if (uv == null)
                return;

            var window = GetWindow<IndieEditUVSettingsWindow>("Edit UV");
            window._component = uv;
            window._framedOnce = false;
            window._selectionSignature = int.MinValue;
            window._uiRotation = 0f;
            window._uiScale = 1f;
            window.minSize = new Vector2(420f, 420f);
            window.MarkCanvasGeometryDirty();
            window.Show();
            window.Focus();
        }

        /// <summary>Registered in OnEnable/OnDisable (survives domain reload via OnEnable).</summary>
        private static readonly List<IndieEditUVSettingsWindow> OpenWindows = new List<IndieEditUVSettingsWindow>();

        public static void MarkCanvasDirty()
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null || window._gpu == null)
                    continue;

                window._gpu.MarkGeometryDirty();
                window._gpu.MarkSeamsDirty();
                window._gpu.MarkSelectionDirty();
                window._gpu.MarkAppearanceDirty();
            }
        }

        public static void MarkCanvasSelectionDirty()
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null || window._gpu == null)
                    continue;

                window._gpu.MarkSelectionDirty();
            }
        }

        public static void MarkCanvasSeamsDirty()
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null || window._gpu == null)
                    continue;

                window._gpu.MarkSeamsDirty();
            }
        }

        public static void MarkCanvasDynamicUvDirty(
            IndieEditableMeshData data,
            IEnumerable<int> affectedVerts
        )
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null || window._gpu == null)
                    continue;

                window._gpu.MarkDynamicUvDirty(data, affectedVerts);
            }
        }

        private void MarkCanvasGeometryDirty()
        {
            if (_gpu == null)
                _gpu = new IndieEditUVCanvasGpu();

            _gpu.MarkGeometryDirty();
            _gpu.MarkSeamsDirty();
            _gpu.MarkSelectionDirty();
            _gpu.MarkAppearanceDirty();
        }

        /// <summary>
        /// After gizmo MouseUp: bake live rot/scale into UI fields.
        /// Bound rebuild (new selection) resets via SyncTransformUiFromSelection.
        /// </summary>
        public static void NotifyGizmoTransformCommitted(
            IndieEditUVGizmo.DragMode mode,
            float liveRotationDegrees,
            Vector2 liveScale
        )
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null)
                    continue;

                if (mode == IndieEditUVGizmo.DragMode.Rotate)
                    window._uiRotation += liveRotationDegrees;

                if (mode == IndieEditUVGizmo.DragMode.Scale)
                {
                    float factor = (Mathf.Abs(liveScale.x) + Mathf.Abs(liveScale.y)) * 0.5f;
                    window._uiScale *= factor;
                }

                window.Repaint();
            }
        }

        /// <summary>
        /// Undo/Redo restores mesh UVs but must NOT restore previous rot/scale UI values.
        /// </summary>
        public static void ResetTransformUiAfterUndo()
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                var window = OpenWindows[i];

                if (window == null)
                    continue;

                window._uiRotation = 0f;
                window._uiScale = 1f;
                // Keep signature so we don't treat this as a new selection rebuild.
                window.Repaint();
            }
        }

        public static void CloseIfOpen()
        {
            // Close() runs OnDisable, which mutates OpenWindows.
            var windows = OpenWindows.ToArray();

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        public static void RepaintIfOpen()
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                if (OpenWindows[i] != null)
                    OpenWindows[i].Repaint();
            }
        }

        private void OnEnable()
        {
            if (!OpenWindows.Contains(this))
                OpenWindows.Add(this);

            // No per-mousemove repaint — canvas RT is cached until dirty.
            wantsMouseMove = false;
            IndieEditUVGizmo.Reset();

            if (_gpu == null)
                _gpu = new IndieEditUVCanvasGpu();

            _gpu.MarkGeometryDirty();
        }

        private void OnDisable()
        {
            OpenWindows.Remove(this);
            IndieEditUVGizmo.Reset();
            IndieEditUVStretcherGizmo.Reset();

            if (_gpu != null)
            {
                _gpu.Dispose();
                _gpu = null;
            }
        }

        private void OnGUI()
        {
            if (_component == null || !_component.isEditing || IndieEditUVSession.Active != _component)
            {
                EditorGUILayout.HelpBox("Edit UV session is not active.", MessageType.Warning);

                if (GUILayout.Button("Close"))
                    Close();

                return;
            }

            if (_component.Data == null)
            {
                EditorGUILayout.HelpBox("No mesh data.", MessageType.Warning);
                return;
            }

            // Catch Ctrl+Z/Y while this window is focused (SceneView does not get them).
            if (IndieEditUVHotkeys.TryHandle(_component, Event.current))
            {
                Repaint();
                return;
            }

            SyncTransformUiFromSelection();

            DrawToolbar();

            // Canvas takes flexible space; the axis column sits beside it so
            // Select U / Select V clicks stay outside the canvas hit-test rect.
            // Transform bar is a fixed GUILayout strip below both.
            EditorGUILayout.BeginHorizontal();

            Rect canvasRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

            canvasRect = new Rect(
                canvasRect.x + 4f,
                canvasRect.y + 2f,
                canvasRect.width - 8f,
                Mathf.Max(8f, canvasRect.height - 4f)
            );

            DrawSelectAxisPanel();
            EditorGUILayout.EndHorizontal();

            _view.SetCanvasRect(canvasRect);

            if (!_framedOnce && canvasRect.width > 10f && canvasRect.height > 10f)
            {
                FrameAll();
                _framedOnce = true;
            }

            HandleCanvasEvents(canvasRect);

            if (Event.current.type == EventType.Repaint)
            {
                DrawCanvas(canvasRect);

                // Pelt stretchers live in IndieEditUVPeltWindow canvas; keep mesh gizmo off.
                if (!IndieEditUVPelt.IsActive)
                    IndieEditUVGizmo.Draw(_component, _view);
            }

            DrawTransformBar();
        }

        private void SyncTransformUiFromSelection()
        {
            int signature = IndieEditUVTransform.GetSelectionSignature(_component);

            if (signature == _selectionSignature)
                return;

            _selectionSignature = signature;
            _uiRotation = 0f;
            _uiScale = 1f;
        }

        private void DrawTransformBar()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(56f));

            bool hasSelection = IndieEditUVTransform.TryGetSelectionUvAverage(
                _component,
                out Vector2 average
            );

            EditorGUI.BeginDisabledGroup(!hasSelection);

            float displayRotation = _uiRotation;
            float displayScale = _uiScale;

            if (IndieEditUVGizmo.Mode == IndieEditUVGizmo.DragMode.Rotate)
                displayRotation = _uiRotation + IndieEditUVGizmo.LiveRotationDegrees;

            if (IndieEditUVGizmo.Mode == IndieEditUVGizmo.DragMode.Scale)
            {
                Vector2 live = IndieEditUVGizmo.LiveScale;
                displayScale = _uiScale * (Mathf.Abs(live.x) + Mathf.Abs(live.y)) * 0.5f;
            }

            if (!hasSelection)
            {
                average = Vector2.zero;
                displayRotation = 0f;
                displayScale = 1f;
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.MinWidth(160f));
            GUILayout.Label("Position (UV avg)", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            float newU = EditorGUILayout.DelayedFloatField(average.x, GUILayout.MinWidth(64f));
            float newV = EditorGUILayout.DelayedFloatField(average.y, GUILayout.MinWidth(64f));
            if (EditorGUI.EndChangeCheck() && hasSelection)
                ApplyPositionTypeIn(average, new Vector2(newU, newV));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.Width(100f));
            GUILayout.Label("Rotation", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            float newRot = EditorGUILayout.DelayedFloatField(displayRotation);
            if (EditorGUI.EndChangeCheck() && hasSelection && !IndieEditUVGizmo.IsDragging)
                ApplyRotationTypeIn(displayRotation, newRot);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.Width(100f));
            GUILayout.Label("Scale", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.DelayedFloatField(displayScale);
            if (EditorGUI.EndChangeCheck() && hasSelection && !IndieEditUVGizmo.IsDragging)
                ApplyScaleTypeIn(displayScale, newScale);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private void ApplyPositionTypeIn(Vector2 oldAverage, Vector2 newAverage)
        {
            Vector2 delta = newAverage - oldAverage;

            if (delta.sqrMagnitude < 1e-12f)
                return;

            int[] indices = IndieEditUVTransform.GetAffectedVertices(_component);

            if (indices == null || indices.Length == 0)
                return;

            var baseline = IndieEditUVTransform.CaptureUvs(_component, indices);
            IndieEditUVUndo.Register(_component, "Move UV");
            IndieEditUVTransform.ApplyMove(_component, baseline, delta);
        }

        private void ApplyRotationTypeIn(float oldDegrees, float newDegrees)
        {
            float deltaDeg = newDegrees - oldDegrees;

            if (Mathf.Abs(deltaDeg) < 1e-6f)
                return;

            int[] indices = IndieEditUVTransform.GetAffectedVertices(_component);

            if (indices == null || indices.Length == 0)
                return;

            Vector2 pivot = IndieEditUVTransform.GetSelectionUvPivot(_component);
            var baseline = IndieEditUVTransform.CaptureUvs(_component, indices);
            IndieEditUVUndo.Register(_component, "Rotate UV");
            IndieEditUVTransform.ApplyRotate(_component, baseline, pivot, deltaDeg * Mathf.Deg2Rad);
            _uiRotation = newDegrees;
        }

        private void ApplyScaleTypeIn(float oldScale, float newScale)
        {
            if (Mathf.Abs(oldScale) < 1e-6f)
                oldScale = 1e-6f;

            float factor = newScale / oldScale;

            if (Mathf.Abs(factor - 1f) < 1e-6f)
                return;

            int[] indices = IndieEditUVTransform.GetAffectedVertices(_component);

            if (indices == null || indices.Length == 0)
                return;

            Vector2 pivot = IndieEditUVTransform.GetSelectionUvPivot(_component);
            var baseline = IndieEditUVTransform.CaptureUvs(_component, indices);
            IndieEditUVUndo.Register(_component, "Scale UV");
            IndieEditUVTransform.ApplyScale(_component, baseline, pivot, new Vector2(factor, factor));
            _uiScale = newScale;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                FrameAll();

            GUILayout.Space(8f);
            GUILayout.Label("Texture", EditorStyles.miniLabel, GUILayout.Width(48f));

            EditorGUI.BeginChangeCheck();
            var texture = (Texture2D)EditorGUILayout.ObjectField(
                IndieEditUVMaterialUtil.PreviewTexture,
                typeof(Texture2D),
                false,
                GUILayout.Width(180f)
            );

            if (EditorGUI.EndChangeCheck())
            {
                IndieEditUVMaterialUtil.SetPreviewTexture(_component, texture);

                if (_gpu != null)
                {
                    _gpu.MarkAppearanceDirty();
                    _gpu.MarkGeometryDirty();
                }

                SceneView.RepaintAll();
                Repaint();
            }

            GUILayout.Space(8f);

            if (GUILayout.Button("Relax", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                IndieEditUVRelaxWindow.Open(_component);

            GUILayout.FlexibleSpace();
            GUILayout.Label("MMB pan · Wheel zoom · Shift move axis · Corners scale · Alt uniform · Shift+Alt axis", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectAxisPanel()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(132f),
                GUILayout.ExpandHeight(true)
            );

            GUILayout.Space(4f);
            EditorGUI.BeginDisabledGroup(!IndieEditUVSelectAxis.CanSelect(_component));

            if (GUILayout.Button("Select U", GUILayout.Height(22f)))
                IndieEditUVSelectAxis.Apply(_component, IndieEditUVSelectAxis.Axis.U);

            if (GUILayout.Button("Select V", GUILayout.Height(22f)))
                IndieEditUVSelectAxis.Apply(_component, IndieEditUVSelectAxis.Axis.V);

            if (GUILayout.Button("Flat U", GUILayout.Height(22f)))
                IndieEditUVSelectAxis.Flatten(_component, IndieEditUVSelectAxis.Axis.U);

            if (GUILayout.Button("Flat V", GUILayout.Height(22f)))
                IndieEditUVSelectAxis.Flatten(_component, IndieEditUVSelectAxis.Axis.V);

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(!IndieEditUVAssemble.CanAssemble(_component));

            if (GUILayout.Button("Assemble", GUILayout.Height(22f)))
                IndieEditUVAssemble.Apply(_component);

            GUILayout.Label("Padding %", EditorStyles.miniLabel);
            IndieEditUVAssemble.PaddingPercent = EditorGUILayout.Slider(
                IndieEditUVAssemble.PaddingPercent,
                0f,
                IndieEditUVAssemble.MaxPaddingPercent
            );

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private void FrameAll()
        {
            var data = _component.Data;

            if (data == null || data.uvs.Count == 0)
            {
                _view.FrameUvRect(new Rect(0f, 0f, 1f, 1f));
                return;
            }

            float minU = float.PositiveInfinity;
            float minV = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float maxV = float.NegativeInfinity;

            for (int i = 0; i < data.uvs.Count; i++)
            {
                Vector2 uv = data.uvs[i];
                minU = Mathf.Min(minU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxU = Mathf.Max(maxU, uv.x);
                maxV = Mathf.Max(maxV, uv.y);
            }

            minU = Mathf.Min(minU, 0f);
            minV = Mathf.Min(minV, 0f);
            maxU = Mathf.Max(maxU, 1f);
            maxV = Mathf.Max(maxV, 1f);

            _view.FrameUvRect(Rect.MinMaxRect(minU, minV, maxU, maxV));
            Repaint();
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event evt = Event.current;

            if (evt == null)
                return;

            // While typing in Position/Rotation/Scale, ignore canvas picks so focus
            // loss / click-through cannot clear the current UV selection.
            if (EditorGUIUtility.editingTextField &&
                (evt.type == EventType.MouseDown ||
                 evt.type == EventType.MouseDrag ||
                 evt.type == EventType.MouseUp))
            {
                return;
            }

            Vector2 mouse = evt.mousePosition;
            bool inCanvas = canvasRect.Contains(mouse);

            if (IndieEditUVPelt.IsActive)
            {
                // Proxy editing happens in Pelt Map window — block mesh UV edits here.
                if (evt.type == EventType.MouseDown ||
                    evt.type == EventType.MouseDrag ||
                    evt.type == EventType.MouseUp)
                {
                    return;
                }
            }
            else if (IndieEditUVGizmo.HandleInput(_component, _view, evt, canvasRect))
            {
                Repaint();
                return;
            }

            if (evt.type == EventType.ScrollWheel && inCanvas)
            {
                float delta = evt.delta.y > 0f ? 1.1f : 1f / 1.1f;
                _view.ZoomAt(mouse, delta);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.button == 2)
            {
                if (evt.type == EventType.MouseDown && inCanvas)
                {
                    _panning = true;
                    _panStartMouse = mouse;
                    _panStartPan = _view.Pan;
                    evt.Use();
                    return;
                }

                if (evt.type == EventType.MouseDrag && _panning)
                {
                    Vector2 deltaGui = mouse - _panStartMouse;
                    float pp = _view.PixelsPerUv;
                    _view.Pan = _panStartPan - new Vector2(deltaGui.x / pp, -deltaGui.y / pp);
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.type == EventType.MouseUp && _panning)
                {
                    _panning = false;
                    evt.Use();
                    return;
                }
            }

            if (evt.button != 0)
                return;

            if (evt.type == EventType.MouseDown && inCanvas)
            {
                _marquee = false;
                _marqueeStart = mouse;
                _marqueeCurrent = mouse;
                _marqueeModifiers = evt.shift || evt.control || evt.command;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDrag && GUIUtility.hotControl != 0)
            {
                _marqueeCurrent = mouse;

                if (!_marquee &&
                    Vector2.Distance(_marqueeStart, _marqueeCurrent) >= 6f)
                {
                    _marquee = true;
                }

                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && GUIUtility.hotControl != 0)
            {
                _marqueeCurrent = mouse;

                if (_marquee)
                {
                    IndieEditUVSelection.ApplyUvMarquee(
                        _component,
                        _view,
                        NormalizeRect(_marqueeStart, _marqueeCurrent),
                        _marqueeModifiers
                    );
                }
                else
                {
                    IndieEditUVSelection.HandleUvCanvasClick(
                        _component,
                        _view,
                        mouse,
                        _marqueeModifiers
                    );
                    EditorUtility.SetDirty(_component);
                    IndieEditUVNotify.SelectionChanged();
                }

                _marquee = false;
                GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
            }
        }

        private void DrawCanvas(Rect canvasRect)
        {
            if (_gpu == null)
                _gpu = new IndieEditUVCanvasGpu();

            // Mouse in window GUI space; view maps via canvas rect already set.
            Vector2 mouseGui = Event.current != null
                ? Event.current.mousePosition
                : canvasRect.center;

            Texture2D preview = IndieEditUVMaterialUtil.PreviewTexture;
            bool hasPreview = preview != null;

            // Align view canvas with integer RT size used by GPU.
            Rect gpuRect = new Rect(
                canvasRect.x,
                canvasRect.y,
                Mathf.Max(8f, Mathf.Ceil(canvasRect.width)),
                Mathf.Max(8f, Mathf.Ceil(canvasRect.height))
            );
            _view.SetCanvasRect(gpuRect);

            RenderTexture rt = _gpu.Render(
                _component.Data,
                _view,
                gpuRect,
                mouseGui,
                hasPreview,
                preview
            );

            if (rt != null)
                GUI.DrawTexture(gpuRect, rt, ScaleMode.StretchToFill, false);
            else
                EditorGUI.DrawRect(canvasRect, new Color(0.12f, 0.12f, 0.12f, 1f));

            DrawAxisLabels();

            if (_marquee)
            {
                Rect rect = NormalizeRect(_marqueeStart, _marqueeCurrent);
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 1f, 0.12f));
                DrawGuiBorder(rect, new Color(0.25f, 0.65f, 1f, 0.95f));
            }

            DrawGuiBorder(canvasRect, new Color(0.05f, 0.05f, 0.05f, 1f));
        }

        private void DrawAxisLabels()
        {
            Vector2 uLabel = _view.UvToGui(new Vector2(1.08f, 0.02f));
            Vector2 vLabel = _view.UvToGui(new Vector2(0.02f, 1.08f));
            GUI.color = AxisUColor;
            GUI.Label(new Rect(uLabel.x, uLabel.y - 8f, 20f, 16f), "U");
            GUI.color = AxisVColor;
            GUI.Label(new Rect(vLabel.x - 4f, vLabel.y - 16f, 20f, 16f), "V");
            GUI.color = Color.white;
        }

        private static Rect NormalizeRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y)
            );
        }

        private static void DrawGuiBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
        }
    }
}
