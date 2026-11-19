using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    [InitializeOnLoad]
    public static class IndieRetopologyManager
    {
        private static double _lastUndoRedoTime;
        private static bool _editPointer;
        private static bool _restoringSelection;
        private static readonly int[] OrbitVertex = { -1 };

        static IndieRetopologyManager()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            EnsureActiveObjectSelected();
            PollHover();
        }

        private static void OnSelectionChanged()
        {
            if (IndieRetopologyAttach.ConsumeSelection())
            {
                EnsureActiveObjectSelected(true);
                return;
            }

            EnsureActiveObjectSelected();
        }

        private static void EnsureActiveObjectSelected(bool ignoreAttach = false)
        {
            if (_restoringSelection || (!ignoreAttach && IndieRetopologyAttach.IsActive))
                return;

            var active = IndieRetopologySession.Active;

            if (active == null)
                return;

            if (Selection.Contains(active.gameObject) && Selection.objects.Length == 1)
                return;

            _restoringSelection = true;
            Selection.objects = new Object[]
            {
                active.gameObject
            };
            IndieRetopologyUndo.SuppressUnityUndo();
            _restoringSelection = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            if (IndieRetopologySession.Active != null)
                IndieRetopologySession.EndEditing(IndieRetopologySession.Active, true);
        }

        public static void ClearEditPointer()
        {
            _editPointer = false;
        }

        private static void PollHover()
        {
            var active = IndieRetopologySession.Active;
            SceneView sceneView = SceneView.lastActiveSceneView;

            if (active == null || sceneView == null || IndieRetopologyDrag.IsDragging)
                return;

            if (!IndieRetopologyPick.TryRebuildSettledGrid(active, sceneView.camera))
                return;

            sceneView.Repaint();
        }

        private static bool SameHover(IndieRetopologyHit a, IndieRetopologyHit b)
        {
            if (a.valid != b.valid)
                return false;

            if (!a.valid)
                return true;

            return a.kind == b.kind && a.a == b.a && a.b == b.b;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Event evt = Event.current;

            if (evt == null)
                return;

            var active = IndieRetopologySession.Active;

            if (active == null)
                return;

            if (IsCameraControlEvent(evt))
                IndieRetopologyPick.NoteCameraEvent();

            if (HandleHotkeys(active, evt))
                return;

            int blockControl = GUIUtility.GetControlID(FocusType.Passive);

            if (evt.type == EventType.Layout && !IndieRetopologyAttach.IsActive)
                HandleUtility.AddDefaultControl(blockControl);

            if (evt.type == EventType.MouseUp &&
                evt.button == 0 &&
                (_editPointer || GUIUtility.hotControl == blockControl))
            {
                if (_editPointer)
                {
                    IndieRetopologyDrag.HandleMouseUp(
                        active,
                        sceneView,
                        evt.control || evt.command);
                }

                _editPointer = false;

                if (GUIUtility.hotControl == blockControl)
                    GUIUtility.hotControl = 0;

                if (!evt.alt && !IndieRetopologyAttach.IsActive)
                    evt.Use();

                sceneView.Repaint();
                return;
            }

            if (evt.type == EventType.Layout && !evt.alt && !IndieRetopologyDrag.IsDragging)
            {
                IndieRetopologyPick.TryUpdateCachedHover(
                    active,
                    sceneView.camera,
                    evt.mousePosition,
                    evt.shift || evt.control || evt.command);
            }

            bool altReleased = evt.type == EventType.KeyUp &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt);

            if (!evt.alt &&
                !IndieRetopologyDrag.IsDragging &&
                (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp))
            {
                bool highlightEdge = evt.shift || evt.control || evt.command;
                IndieRetopologyHit previous = IndieRetopologyPick.CurrentHover;

                if (IndieRetopologyPick.TryUpdateCachedHover(
                        active,
                        sceneView.camera,
                        evt.mousePosition,
                        highlightEdge) &&
                    !SameHover(previous, IndieRetopologyPick.CurrentHover))
                {
                    sceneView.Repaint();
                }
            }

            if (!evt.alt || altReleased)
            {
                if (IndieRetopologyDrag.IsDragging &&
                    (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp))
                {
                    IndieRetopologyDrag.RefreshMagnet(
                        active,
                        sceneView,
                        evt.control || evt.command);
                    sceneView.Repaint();
                }

                if (altReleased)
                    sceneView.Repaint();
            }

            HandleOrbit(active, sceneView, evt);

            IndieRetopologySceneView.Draw(active, sceneView);

            if (active.TargetMesh == null)
            {
                if (IndieRetopologyCreatePolygon.IsActive)
                    IndieRetopologyCreatePolygon.Cancel();

                bool attachWasActive = IndieRetopologyAttach.IsActive;

                if (attachWasActive)
                    IndieRetopologyAttach.Deactivate();

                IndieRetopologyConnectWindow.Dismiss();

                if (!attachWasActive &&
                    evt.button == 0 &&
                    !evt.alt &&
                    (evt.type == EventType.MouseDown ||
                     evt.type == EventType.MouseDrag ||
                     evt.type == EventType.MouseUp) &&
                    (HandleUtility.nearestControl == blockControl ||
                     GUIUtility.hotControl == blockControl ||
                     _editPointer))
                {
                    evt.Use();
                }

                return;
            }

            if (IndieRetopologyAttach.HandleSceneGUI(active, sceneView, evt))
                return;

            if (IndieRetopologyCreatePolygon.HandleSceneGUI(active, sceneView, evt))
                return;

            if (evt.type == EventType.MouseDown &&
                evt.button == 1 &&
                !evt.alt &&
                !evt.control &&
                !evt.command &&
                IndieRetopologyDrag.TryDeleteVertex(active, sceneView, evt.mousePosition))
            {
                evt.Use();
                sceneView.Repaint();
                return;
            }

            if (IsCameraControlEvent(evt))
                return;

            if (evt.type != EventType.MouseDown &&
                evt.type != EventType.MouseDrag &&
                evt.type != EventType.MouseUp)
            {
                return;
            }

            if (evt.button != 0 || evt.alt)
                return;

            bool ourControl = HandleUtility.nearestControl == blockControl ||
                GUIUtility.hotControl == blockControl ||
                _editPointer;

            if (!ourControl)
                return;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    IndieRetopologyDrag.HandleMouseDown(active, sceneView, evt);

                    if (IndieRetopologyDrag.IsDragging)
                        _editPointer = true;

                    GUIUtility.hotControl = blockControl;
                    evt.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseDrag:
                    if (_editPointer)
                        IndieRetopologyDrag.HandleMouseDrag(active, sceneView, evt);

                    evt.Use();
                    sceneView.Repaint();
                    break;
            }
        }

        private static void HandleOrbit(IndieRetopologyComponent retopo, SceneView sceneView, Event evt)
        {
            if (retopo == null || sceneView == null || evt == null || retopo.Data == null)
                return;

            if (evt.type != EventType.MouseDrag || Tools.viewTool != ViewTool.Orbit)
                return;

            if (sceneView.in2DMode || sceneView.isRotationLocked || sceneView.camera == null)
                return;

            IndieRetopologyHit hover = IndieRetopologyPick.CurrentHover;

            if (!hover.valid || hover.kind != IndieRetopologyPickKind.Vertex)
                return;

            var vertices = retopo.Data.vertices;

            if (vertices == null || hover.a < 0 || hover.a >= vertices.Count)
                return;

            Vector3 center = retopo.transform.TransformPoint(vertices[hover.a]);
            IndieEditMeshViewFrame.SnapPivotKeepingDistance(sceneView, center, true);
            OrbitVertex[0] = hover.a;
            IndieEditMeshViewFrame.MatchFlySpeed(sceneView, retopo.transform, vertices, OrbitVertex);
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

        private static bool HandleHotkeys(IndieRetopologyComponent active, Event evt)
        {
            if (evt.type == EventType.ValidateCommand)
            {
                if (evt.commandName == "Undo" || evt.commandName == "Redo")
                {
                    evt.Use();
                    return true;
                }

                return false;
            }

            if (evt.type == EventType.ExecuteCommand)
            {
                if (evt.commandName == "Undo")
                {
                    PerformUndo(active);
                    evt.Use();
                    return true;
                }

                if (evt.commandName == "Redo")
                {
                    PerformRedo(active);
                    evt.Use();
                    return true;
                }

                return false;
            }

            if (evt.type != EventType.KeyDown)
                return false;

            bool ctrl = evt.control || evt.command;

            if (!ctrl || evt.alt || EditorGUIUtility.editingTextField)
                return false;

            if (evt.keyCode == KeyCode.Z && !evt.shift)
            {
                PerformUndo(active);
                evt.Use();
                return true;
            }

            if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift))
            {
                PerformRedo(active);
                evt.Use();
                return true;
            }

            return false;
        }

        private static void PerformUndo(IndieRetopologyComponent active)
        {
            if (!TryConsumeUndoRedoGesture())
            {
                IndieRetopologyUndo.SuppressUnityUndo();
                return;
            }

            if (IndieRetopologyCreatePolygon.CancelIfPending())
                return;

            IndieRetopologyUndo.Undo(active);
            SceneView.RepaintAll();
        }

        private static void PerformRedo(IndieRetopologyComponent active)
        {
            if (!TryConsumeUndoRedoGesture())
            {
                IndieRetopologyUndo.SuppressUnityUndo();
                return;
            }

            if (IndieRetopologyCreatePolygon.CancelIfPending())
                return;

            IndieRetopologyUndo.Redo(active);
            SceneView.RepaintAll();
        }

        private static bool TryConsumeUndoRedoGesture()
        {
            double now = EditorApplication.timeSinceStartup;

            if (now - _lastUndoRedoTime < 0.05)
                return false;

            _lastUndoRedoTime = now;
            return true;
        }
    }
}
