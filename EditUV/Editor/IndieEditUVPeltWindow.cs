using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Pelt control panel + canvas for the proxy skin (original UV untouched).
    /// </summary>
    public class IndieEditUVPeltWindow : EditorWindow
    {
        private static readonly Color BgColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color FaceFill = new Color(0.75f, 0.25f, 0.2f, 0.35f);
        private static readonly Color FaceEdge = new Color(0.9f, 0.35f, 0.3f, 0.9f);
        private static readonly Color CutColor = new Color(0.15f, 0.95f, 1f, 1f);
        private static readonly Color VertColor = new Color(0.2f, 0.95f, 0.35f, 1f);
        private static readonly Color StretcherColor = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color SpringColor = new Color(1f, 0.35f, 0.2f, 0.75f);

        private IndieEditUVComponent _component;
        private int _simulationSamples = IndieEditUVPelt.DefaultSimulationSamples;
        private readonly IndieEditUVView _view = new IndieEditUVView();
        private bool _framed;
        private bool _panning;
        private Vector2 _panStartGui;
        private Vector2 _panStartUv;

        public static void Open(IndieEditUVComponent uv)
        {
            if (uv == null)
                return;

            if (!IndieEditUVPelt.TryBegin(uv, out string error))
            {
                EditorUtility.DisplayDialog("Pelt Map", error, "OK");
                return;
            }

            var window = GetWindow<IndieEditUVPeltWindow>(true, "Pelt Map", true);
            window._component = uv;
            window._simulationSamples = IndieEditUVPelt.DefaultSimulationSamples;
            window._framed = false;
            window.minSize = new Vector2(480, 420);
            window.Show();
            IndieEditUVNotify.SelectionOrMeshChanged();
            SceneView.RepaintAll();
        }

        public static void CloseIfOpen()
        {
            var windows = Resources.FindObjectsOfTypeAll<IndieEditUVPeltWindow>();

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        public static void RepaintIfOpen()
        {
            var windows = Resources.FindObjectsOfTypeAll<IndieEditUVPeltWindow>();

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Repaint();
            }
        }

        private void OnDisable()
        {
            if (IndieEditUVPelt.IsActive && _component != null)
                IndieEditUVPelt.RestoreSnapshotAndEnd(_component);
        }

        private void OnGUI()
        {
            if (_component == null || !IndieEditUVPelt.IsActive || IndieEditUVPelt.Proxy == null)
            {
                EditorGUILayout.HelpBox("Pelt session is not active.", MessageType.Info);

                if (GUILayout.Button("Close"))
                    Close();

                return;
            }

            DrawToolbar();

            Rect canvasRect = GUILayoutUtility.GetRect(
                10f,
                10f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

            // Leave a bit of padding from window edges.
            canvasRect = new Rect(
                canvasRect.x + 4f,
                canvasRect.y + 2f,
                Mathf.Max(32f, canvasRect.width - 8f),
                Mathf.Max(32f, canvasRect.height - 6f)
            );

            _view.SetCanvasRect(canvasRect);

            if (!_framed && Event.current.type == EventType.Repaint)
            {
                _view.FrameUvRect(IndieEditUVPelt.Proxy.GetUvBounds(), 0.2f);
                _framed = true;
            }

            HandleCanvasEvents(canvasRect);

            if (Event.current.type == EventType.Repaint)
            {
                DrawCanvas(canvasRect);

                if (IndieEditUVPelt.SelectStretchersMode)
                    IndieEditUVStretcherGizmo.Draw(_view);
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Pelt Map (proxy)", EditorStyles.boldLabel, GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Samples", GUILayout.Width(52));
            _simulationSamples = EditorGUILayout.IntSlider(
                _simulationSamples,
                1,
                40,
                GUILayout.Width(140)
            );

            if (GUILayout.Button("Start Pelt", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                IndieEditUVPelt.RunSimulation(_component, _simulationSamples);
                Repaint();
            }

            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                IndieEditUVPelt.ResetToSnapshot(_component);
                _framed = false;
                Repaint();
            }

            Color prev = GUI.backgroundColor;

            if (IndieEditUVPelt.SelectStretchersMode)
                GUI.backgroundColor = new Color(1f, 0.85f, 0.35f, 1f);

            if (GUILayout.Button(
                    IndieEditUVPelt.SelectStretchersMode ? "Stretchers On" : "Stretchers",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(90)
                ))
            {
                IndieEditUVPelt.SelectStretchersMode = !IndieEditUVPelt.SelectStretchersMode;
                IndieEditUVPelt.SelectedStretcherIndices.Clear();
                IndieEditUVStretcherGizmo.Reset();
                Repaint();
            }

            GUI.backgroundColor = prev;

            bool closeAfter = false;

            if (GUILayout.Button("Commit", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                var comp = _component;
                IndieEditUVPelt.Commit(comp);
                _component = null;
                closeAfter = true;
            }

            if (!closeAfter &&
                GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(55)))
            {
                IndieEditUVPelt.RestoreSnapshotAndEnd(_component);
                _component = null;
                closeAfter = true;
            }

            EditorGUILayout.EndHorizontal();

            if (closeAfter)
            {
                Close();
                IndieEditUVSettingsWindow.RepaintIfOpen();
                SceneView.RepaintAll();
                return;
            }

            if (IndieEditUVPelt.SelectStretchersMode)
            {
                EditorGUILayout.HelpBox(
                    "Proxy only — original UV untouched until Commit. " +
                    "Click stretcher · Double-click = all · Center = move · Ring = rotate",
                    MessageType.Info
                );
            }
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event evt = Event.current;
            Vector2 mouse = evt.mousePosition;
            bool inCanvas = canvasRect.Contains(mouse);

            if (IndieEditUVPelt.SelectStretchersMode &&
                IndieEditUVStretcherGizmo.HandleInput(_component, _view, evt, canvasRect))
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

            if (evt.type == EventType.MouseDown && inCanvas && evt.button == 2)
            {
                _panning = true;
                _panStartGui = mouse;
                _panStartUv = _view.Pan;
                evt.Use();
                return;
            }

            if (_panning)
            {
                if (evt.type == EventType.MouseDrag && evt.button == 2)
                {
                    Vector2 deltaGui = mouse - _panStartGui;
                    float pp = Mathf.Max(1e-4f, _view.PixelsPerUv);
                    _view.Pan = _panStartUv + new Vector2(-deltaGui.x / pp, deltaGui.y / pp);
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.type == EventType.MouseUp && evt.button == 2)
                {
                    _panning = false;
                    evt.Use();
                }
            }
        }

        private void DrawCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, BgColor);
            DrawChecker(canvasRect);

            IndieEditUVPeltProxy proxy = IndieEditUVPelt.Proxy;

            if (proxy == null)
                return;

            Handles.BeginGUI();

            // Faces
            for (int i = 0; i < proxy.Faces.Count; i++)
            {
                var f = proxy.Faces[i];
                Vector3 a = _view.UvToGui(proxy.Verts[f.A].Pos);
                Vector3 b = _view.UvToGui(proxy.Verts[f.B].Pos);
                Vector3 c = _view.UvToGui(proxy.Verts[f.C].Pos);
                Handles.color = FaceFill;
                Handles.DrawAAConvexPolygon(a, b, c);
                Handles.color = FaceEdge;
                Handles.DrawLine(a, b, 1.2f);
                Handles.DrawLine(b, c, 1.2f);
                Handles.DrawLine(c, a, 1.2f);
            }

            // Cyan cuts
            Handles.color = CutColor;

            for (int i = 0; i < proxy.Faces.Count; i++)
            {
                var f = proxy.Faces[i];
                DrawCutEdge(proxy, f.A, f.B);
                DrawCutEdge(proxy, f.B, f.C);
                DrawCutEdge(proxy, f.C, f.A);
            }

            // Boundary verts
            for (int i = 0; i < proxy.BoundaryOrdered.Count; i++)
            {
                int v = proxy.BoundaryOrdered[i];
                Vector2 gui = _view.UvToGui(proxy.Verts[v].Pos);
                EditorGUI.DrawRect(new Rect(gui.x - 2.5f, gui.y - 2.5f, 5f, 5f), VertColor);
            }

            // Springs + stretcher ring
            var stretchers = proxy.Stretchers;
            var boundary = proxy.BoundaryOrdered;
            int n = Mathf.Min(stretchers.Count, boundary.Count);

            Handles.color = SpringColor;

            for (int i = 0; i < n; i++)
            {
                Handles.DrawLine(
                    _view.UvToGui(proxy.Verts[boundary[i]].Pos),
                    _view.UvToGui(stretchers[i]),
                    1.5f
                );
            }

            Handles.color = StretcherColor;

            for (int i = 0; i < stretchers.Count; i++)
            {
                int next = (i + 1) % stretchers.Count;
                Handles.DrawLine(
                    _view.UvToGui(stretchers[i]),
                    _view.UvToGui(stretchers[next]),
                    2f
                );
            }

            for (int i = 0; i < stretchers.Count; i++)
            {
                Vector2 gui = _view.UvToGui(stretchers[i]);
                float size = IndieEditUVPelt.SelectedStretcherIndices.Contains(i) ? 8f : 5f;
                Color c = IndieEditUVPelt.SelectedStretcherIndices.Contains(i)
                    ? new Color(1f, 0.55f, 0.1f, 1f)
                    : StretcherColor;
                EditorGUI.DrawRect(
                    new Rect(gui.x - size * 0.5f, gui.y - size * 0.5f, size, size),
                    c
                );
            }

            Handles.EndGUI();

            DrawGuiBorder(canvasRect, new Color(0.05f, 0.05f, 0.05f, 1f));
        }

        private void DrawCutEdge(IndieEditUVPeltProxy proxy, int a, int b)
        {
            long key = IndieEditMeshMeshUtil.EdgeKey(a, b);

            if (!proxy.CutProxyEdges.Contains(key))
                return;

            // Only draw interior cuts (boundary perimeter is also in CutGeos —
            // still fine to show cyan on perimeter of the skin).
            Handles.DrawLine(
                _view.UvToGui(proxy.Verts[a].Pos),
                _view.UvToGui(proxy.Verts[b].Pos),
                2.5f
            );
        }

        private static void DrawChecker(Rect canvasRect)
        {
            const int cells = 16;
            float cell = Mathf.Min(canvasRect.width, canvasRect.height) / cells;
            Color a = new Color(0.22f, 0.22f, 0.22f, 1f);
            Color b = new Color(0.26f, 0.26f, 0.26f, 1f);

            for (int y = 0; y < cells + 2; y++)
            {
                for (int x = 0; x < cells + 2; x++)
                {
                    Rect r = new Rect(
                        canvasRect.x + x * cell,
                        canvasRect.y + y * cell,
                        cell,
                        cell
                    );
                    r = Rect.MinMaxRect(
                        Mathf.Max(r.xMin, canvasRect.xMin),
                        Mathf.Max(r.yMin, canvasRect.yMin),
                        Mathf.Min(r.xMax, canvasRect.xMax),
                        Mathf.Min(r.yMax, canvasRect.yMax)
                    );

                    if (r.width <= 0f || r.height <= 0f)
                        continue;

                    EditorGUI.DrawRect(r, ((x + y) & 1) == 0 ? a : b);
                }
            }
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
