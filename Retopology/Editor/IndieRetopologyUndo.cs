using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyUndo
    {
        private static readonly Stack<IndieRetopologyMeshData> UndoStack = new Stack<IndieRetopologyMeshData>();
        private static readonly Stack<IndieRetopologyMeshData> RedoStack = new Stack<IndieRetopologyMeshData>();

        public static int UndoCount => UndoStack.Count;
        public static int RedoCount => RedoStack.Count;

        public static void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
        }

        public static void CommitBaseline(IndieRetopologyComponent retopo, IndieRetopologyMeshData baseline)
        {
            if (retopo == null || retopo.Data == null || baseline == null)
                return;

            if (IndieRetopologySession.Active != retopo)
                return;

            if (Same(retopo.Data, baseline))
                return;

            UndoStack.Push(baseline.Clone());
            RedoStack.Clear();

            EditorUtility.SetDirty(retopo);
            SuppressUnityUndo();
        }

        public static void Undo(IndieRetopologyComponent retopo)
        {
            if (retopo == null || retopo.Data == null || UndoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            RedoStack.Push(retopo.Data.Clone());
            retopo.SetData(UndoStack.Pop());

            IndieRetopologyDrag.Reset();
            IndieRetopologyPick.ClearHover();
            IndieRetopologySession.RefreshMesh(retopo);

            EditorUtility.SetDirty(retopo);
            SuppressUnityUndo();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public static void Redo(IndieRetopologyComponent retopo)
        {
            if (retopo == null || retopo.Data == null || RedoStack.Count == 0)
            {
                SuppressUnityUndo();
                return;
            }

            UndoStack.Push(retopo.Data.Clone());
            retopo.SetData(RedoStack.Pop());

            IndieRetopologyDrag.Reset();
            IndieRetopologyPick.ClearHover();
            IndieRetopologySession.RefreshMesh(retopo);

            EditorUtility.SetDirty(retopo);
            SuppressUnityUndo();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public static void SuppressUnityUndo()
        {
            if (IndieRetopologySession.Active == null)
                return;

            UnityEditor.Undo.ClearAll();
        }

        private static bool Same(IndieRetopologyMeshData current, IndieRetopologyMeshData baseline)
        {
            int currentSelected = current.selectedEdges != null ? current.selectedEdges.Count : 0;
            int baselineSelected = baseline.selectedEdges != null ? baseline.selectedEdges.Count : 0;

            if (current.vertices.Count != baseline.vertices.Count ||
                current.faces.Count != baseline.faces.Count ||
                currentSelected != baselineSelected)
            {
                return false;
            }

            for (int i = 0; i < current.vertices.Count; i++)
            {
                if (current.vertices[i] != baseline.vertices[i])
                    return false;
            }

            for (int i = 0; i < current.faces.Count; i++)
            {
                IndieRetopologyFace a = current.faces[i];
                IndieRetopologyFace b = baseline.faces[i];

                if (a.a != b.a || a.b != b.b || a.c != b.c)
                    return false;
            }

            for (int i = 0; i < currentSelected; i++)
            {
                IndieRetopologyEdge a = current.selectedEdges[i];
                IndieRetopologyEdge b = baseline.selectedEdges[i];

                if (a.v0 != b.v0 || a.v1 != b.v1)
                    return false;
            }

            return true;
        }
    }
}
