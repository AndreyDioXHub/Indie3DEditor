using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVUndo
    {
        private static readonly Stack<IndieEditableMeshData> UndoStack = new Stack<IndieEditableMeshData>();
        private static readonly Stack<IndieEditableMeshData> RedoStack = new Stack<IndieEditableMeshData>();

        public static int UndoCount => UndoStack.Count;
        public static int RedoCount => RedoStack.Count;

        public static void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
        }

        public static void Register(IndieEditUVComponent uv, string actionName)
        {
            if (uv == null || uv.Data == null)
                return;

            if (IndieEditUVSession.Active != uv)
                return;

            UndoStack.Push(uv.Data.Clone());
            RedoStack.Clear();

            EditorUtility.SetDirty(uv);
            SuppressUnityUndo();
        }

        public static void CommitBaseline(IndieEditUVComponent uv, IndieEditableMeshData baseline, string actionName)
        {
            if (uv == null || uv.Data == null || baseline == null)
                return;

            if (IndieEditUVSession.Active != uv)
                return;

            UndoStack.Push(baseline.Clone());
            RedoStack.Clear();

            EditorUtility.SetDirty(uv);
            SuppressUnityUndo();
        }

        public static void Undo(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null || UndoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            RedoStack.Push(uv.Data.Clone());
            uv.SetData(UndoStack.Pop());

            IndieEditUVGizmo.Reset();
            IndieEditUVSession.RefreshMesh(uv);
            IndieEditUVSceneView.InvalidateCache(uv);
            // Mesh restores; rot/scale UI must NOT restore — reset to 0 / 1.
            IndieEditUVSettingsWindow.ResetTransformUiAfterUndo();

            EditorUtility.SetDirty(uv);
            SuppressUnityUndo();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        public static void Redo(IndieEditUVComponent uv)
        {
            if (uv == null || uv.Data == null || RedoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            UndoStack.Push(uv.Data.Clone());
            uv.SetData(RedoStack.Pop());

            IndieEditUVGizmo.Reset();
            IndieEditUVSession.RefreshMesh(uv);
            IndieEditUVSceneView.InvalidateCache(uv);
            IndieEditUVSettingsWindow.ResetTransformUiAfterUndo();

            EditorUtility.SetDirty(uv);
            SuppressUnityUndo();
            IndieEditUVNotify.SelectionOrMeshChanged();
        }

        private const double SuppressSafetyInterval = 0.5;

        private static int _lastSuppressedGroup = -1;
        private static double _lastSuppressTime;

        public static void SuppressUnityUndo()
        {
            if (IndieEditUVSession.Active == null)
                return;

            UnityEditor.Undo.ClearAll();
            _lastSuppressedGroup = UnityEditor.Undo.GetCurrentGroup();
            _lastSuppressTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Per-tick variant: clears only when Unity opened a new undo group
        /// (plus a slow safety sweep for records added to the same group).
        /// </summary>
        public static void SuppressUnityUndoIfChanged()
        {
            if (IndieEditUVSession.Active == null)
                return;

            bool groupChanged = UnityEditor.Undo.GetCurrentGroup() != _lastSuppressedGroup;
            bool safetyDue = EditorApplication.timeSinceStartup - _lastSuppressTime >= SuppressSafetyInterval;

            if (groupChanged || safetyDue)
                SuppressUnityUndo();
        }
    }
}
