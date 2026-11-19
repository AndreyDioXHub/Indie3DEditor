using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    [InitializeOnLoad]
    public static class IndieEditUVManager
    {
        private static bool _restoringSelection;

        static IndieEditUVManager()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            EnsureActiveObjectSelected();

            // Modal edit: keep Unity's undo stack empty so Ctrl+Z cannot unwind
            // Add Component / Selection / Inspector — only IndieEditUVUndo applies.
            if (IndieEditUVSession.Active != null)
            {
                IndieEditUVUndo.SuppressUnityUndoIfChanged();

                // Survive domain reload / accidental Q (View tool).
                if (Tools.current == Tool.View)
                    Tools.current = Tool.None;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (IndieEditUVSession.Active != null)
                    IndieEditUVSession.EndEditing(IndieEditUVSession.Active, true);
            }
        }

        private static void OnSelectionChanged()
        {
            EnsureActiveObjectSelected();
            SceneView.RepaintAll();
        }

        private static void EnsureActiveObjectSelected()
        {
            if (_restoringSelection)
                return;

            var active = IndieEditUVSession.Active;

            if (active == null)
                return;

            if (Selection.Contains(active.gameObject) && Selection.objects.Length == 1)
                return;

            _restoringSelection = true;
            Selection.objects = new Object[]
            {
                active.gameObject
            };
            IndieEditUVUndo.SuppressUnityUndo();
            _restoringSelection = false;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Event evt = Event.current;

            if (evt == null)
                return;

            DrawSeamsForSelection();

            var active = IndieEditUVSession.Active;

            if (active == null)
                return;

            HandleOrbit(active, sceneView, evt);

            if (IndieEditUVHotkeys.TryHandle(active, evt, sceneView))
                return;

            // Keep arrow cursor / pick tool: View (hand) steals LMB for pan.
            EnforcePickToolAndCursor(sceneView);

            int blockControl = GUIUtility.GetControlID(FocusType.Passive);

            if (evt.type == EventType.Layout)
                HandleUtility.AddDefaultControl(blockControl);

            IndieEditUVSceneView.Draw(active, sceneView);

            if (IndieEditUVSeamTools.TryConsumeRightClick(evt))
            {
                sceneView.Repaint();
                return;
            }

            if (IsCameraControlEvent(evt))
                return;

            if (IndieEditUVSeamState.HasActiveSeamTool)
            {
                if (IndieEditUVSeamTools.HandleSceneGui(active, sceneView, evt, blockControl))
                    sceneView.Repaint();

                return;
            }

            if (evt.type == EventType.MouseDown ||
                evt.type == EventType.MouseDrag ||
                evt.type == EventType.MouseUp ||
                evt.type == EventType.ContextClick)
            {
                bool ownsHotControl = GUIUtility.hotControl == blockControl;
                bool canTakeClick =
                    ownsHotControl ||
                    IndieEditUVSelection.IsMarqueeActive ||
                    (evt.type == EventType.MouseDown &&
                     evt.button == 0 &&
                     !evt.alt &&
                     (HandleUtility.nearestControl == blockControl ||
                      GUIUtility.hotControl == 0));

                if (canTakeClick && evt.button == 0 && !evt.alt)
                {
                    switch (evt.type)
                    {
                        case EventType.MouseDown:
                            GUIUtility.hotControl = blockControl;
                            IndieEditUVSelection.HandleMouseDown(active, evt.mousePosition, evt);
                            evt.Use();
                            sceneView.Repaint();
                            break;

                        case EventType.MouseDrag:
                            if (ownsHotControl)
                            {
                                IndieEditUVSelection.HandleMouseDrag(active, evt.mousePosition, evt);
                                evt.Use();
                                sceneView.Repaint();
                            }
                            break;

                        case EventType.MouseUp:
                            if (ownsHotControl)
                            {
                                IndieEditUVSelection.HandleMouseUp(
                                    active,
                                    sceneView,
                                    evt.mousePosition,
                                    evt
                                );
                                GUIUtility.hotControl = 0;
                                evt.Use();
                                sceneView.Repaint();
                            }
                            break;
                    }
                }
                else if (evt.type == EventType.ContextClick ||
                         (evt.type == EventType.MouseDown && evt.button != 1 && evt.button != 2))
                {
                    if (HandleUtility.nearestControl == blockControl || GUIUtility.hotControl == 0)
                    {
                        evt.Use();
                        sceneView.Repaint();
                    }
                }
            }
        }

        private static void EnforcePickToolAndCursor(SceneView sceneView)
        {
            if (Tools.current == Tool.View)
                Tools.current = Tool.None;

            if (sceneView == null)
                return;

            Handles.BeginGUI();
            EditorGUIUtility.AddCursorRect(
                new Rect(0f, 0f, sceneView.position.width, sceneView.position.height),
                MouseCursor.Arrow
            );
            Handles.EndGUI();
        }

        private static void DrawSeamsForSelection()
        {
            Object[] selected = Selection.objects;

            if (selected == null)
                return;

            var active = IndieEditUVSession.Active;

            for (int i = 0; i < selected.Length; i++)
            {
                var go = selected[i] as GameObject;

                if (go == null)
                    continue;

                var uv = go.GetComponent<IndieEditUVComponent>();

                if (uv == null)
                    continue;

                if (active != null && uv == active)
                    continue;

                IndieEditUVSceneView.Draw(uv, null);
            }
        }

        private static void HandleOrbit(IndieEditUVComponent uv, SceneView sceneView, Event evt)
        {
            if (uv == null || sceneView == null || evt == null || uv.Data == null)
                return;

            if (evt.type != EventType.MouseDrag || Tools.viewTool != ViewTool.Orbit)
                return;

            if (sceneView.in2DMode || sceneView.isRotationLocked || sceneView.camera == null)
                return;

            int[] selected = IndieEditUVTransform.GetAffectedVertices(uv);

            if (!IndieEditMeshViewFrame.TryWorldCentroid(uv.transform, uv.Data.vertices, selected, out Vector3 center))
                return;

            IndieEditMeshViewFrame.SnapPivotKeepingDistance(sceneView, center, true);
            IndieEditMeshViewFrame.MatchFlySpeed(sceneView, uv.transform, uv.Data.vertices, selected);
        }

        private static bool IsCameraControlEvent(Event evt)
        {
            if (evt.alt)
                return true;

            switch (evt.type)
            {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                    return evt.button == 1 || evt.button == 2;

                case EventType.ScrollWheel:
                    return true;

                default:
                    return false;
            }
        }

    }
}
