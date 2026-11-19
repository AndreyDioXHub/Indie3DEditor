using System.Collections.Generic;
using UnityEditor;

namespace Indie3DEditor.EditMesh
{
    public static class IndieEditMeshUndo
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

        public static void Register(IndieEditMeshComponent ep, string actionName)
        {
            if (ep == null || ep.Data == null)
                return;

            if (IndieEditMeshSession.Active != ep)
                return;

            UndoStack.Push(ep.Data.Clone());
            RedoStack.Clear();

            EditorUtility.SetDirty(ep);
            // Keep Unity's global undo empty so Ctrl+Z cannot remove the component.
            SuppressUnityUndo();
        }

        public static void CommitBaseline(IndieEditMeshComponent ep, IndieEditableMeshData baseline, string actionName)
        {
            if (ep == null || ep.Data == null || baseline == null)
                return;

            if (IndieEditMeshSession.Active != ep)
                return;

            UndoStack.Push(baseline.Clone());
            RedoStack.Clear();

            EditorUtility.SetDirty(ep);
            SuppressUnityUndo();
        }

        public static void Undo(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || UndoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            RedoStack.Push(ep.Data.Clone());
            ep.SetData(UndoStack.Pop());

            IndieEditMeshTransformTool.ResetDrag();
            IndieEditMeshSession.RefreshMesh(ep);

            EditorUtility.SetDirty(ep);
            SuppressUnityUndo();
            SceneView.RepaintAll();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void Redo(IndieEditMeshComponent ep)
        {
            if (ep == null || ep.Data == null || RedoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            UndoStack.Push(ep.Data.Clone());
            ep.SetData(RedoStack.Pop());

            IndieEditMeshTransformTool.ResetDrag();
            IndieEditMeshSession.RefreshMesh(ep);

            EditorUtility.SetDirty(ep);
            SuppressUnityUndo();
            SceneView.RepaintAll();
            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
        }

        public static void SuppressUnityUndo()
        {
            if (IndieEditMeshSession.Active == null)
                return;

            // Fully qualify — this class also has an Undo() method.
            UnityEditor.Undo.ClearAll();
        }
    }
}