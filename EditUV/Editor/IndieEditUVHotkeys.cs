using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Modal Edit UV hotkeys: consume Unity Undo/Redo/SelectAll commands and
    /// route them to our stack — same idea as IndieEditMeshManager.HandleEditHotkeys.
    /// </summary>
    public static class IndieEditUVHotkeys
    {
        private static double _lastUndoRedoTime;

        public static bool TryHandle(IndieEditUVComponent active, Event evt, SceneView sceneView = null)
        {
            if (active == null || evt == null)
                return false;

            if (evt.type == EventType.ValidateCommand)
            {
                switch (evt.commandName)
                {
                    case "SelectAll":
                    case "Undo":
                    case "Redo":
                        evt.Use();
                        return true;
                }

                return false;
            }

            if (evt.type == EventType.ExecuteCommand)
            {
                switch (evt.commandName)
                {
                    case "SelectAll":
                        IndieEditUVSelection.SelectAll(active);
                        evt.Use();
                        return true;

                    case "Undo":
                        PerformUndo(active);
                        evt.Use();
                        return true;

                    case "Redo":
                        PerformRedo(active);
                        evt.Use();
                        return true;
                }

                return false;
            }

            if (evt.type != EventType.KeyDown)
                return false;

            bool ctrl = evt.control || evt.command;

            if (!ctrl &&
                evt.keyCode == KeyCode.Z &&
                !evt.shift &&
                !evt.alt &&
                !EditorGUIUtility.editingTextField)
            {
                if (active.Data != null)
                {
                    IndieEditMeshViewFrame.Focus(
                        sceneView,
                        active.transform,
                        active.Data.vertices,
                        IndieEditUVTransform.GetAffectedVertices(active));
                }

                evt.Use();
                return true;
            }

            if (!ctrl)
                return false;

            if (evt.keyCode == KeyCode.A && !evt.shift && !evt.alt)
            {
                IndieEditUVSelection.SelectAll(active);
                evt.Use();
                return true;
            }

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

        public static void PerformUndo(IndieEditUVComponent active)
        {
            if (!TryConsumeUndoRedoGesture())
            {
                IndieEditUVUndo.SuppressUnityUndo();
                return;
            }

            IndieEditUVUndo.Undo(active);
            IndieEditUVUndo.SuppressUnityUndo();
            SceneView.RepaintAll();
            IndieEditUVSettingsWindow.RepaintIfOpen();
        }

        public static void PerformRedo(IndieEditUVComponent active)
        {
            if (!TryConsumeUndoRedoGesture())
            {
                IndieEditUVUndo.SuppressUnityUndo();
                return;
            }

            IndieEditUVUndo.Redo(active);
            IndieEditUVUndo.SuppressUnityUndo();
            SceneView.RepaintAll();
            IndieEditUVSettingsWindow.RepaintIfOpen();
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
