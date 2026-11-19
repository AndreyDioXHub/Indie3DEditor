using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyAttach
    {
        public static bool IsActive { get; private set; }

        private static bool _consumingSelection;

        public static void Toggle()
        {
            if (!IsActive)
                IndieRetopologyCreatePolygon.Cancel();

            IsActive = !IsActive;
            SceneView.RepaintAll();
        }

        public static void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            SceneView.RepaintAll();
        }

        public static bool ConsumeSelection()
        {
            if (!IsActive || _consumingSelection)
                return false;

            var retopo = IndieRetopologySession.Active;
            GameObject picked = Selection.activeGameObject;

            if (retopo == null || picked == null || picked == retopo.gameObject)
                return false;

            _consumingSelection = true;
            bool attached = TryAttach(retopo, picked);
            _consumingSelection = false;

            if (attached)
                Deactivate();

            return true;
        }

        public static bool HandleSceneGUI(IndieRetopologyComponent retopo, SceneView sceneView, Event evt)
        {
            if (!IsActive || retopo == null || evt == null)
                return false;

            Handles.BeginGUI();
            GUI.Box(new Rect(12f, 12f, 360f, 40f), "Attach: click a MeshFilter object  |  Esc / RMB = cancel");
            Handles.EndGUI();

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Deactivate();
                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && !evt.alt)
            {
                Deactivate();
                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if ((evt.type == EventType.MouseDown ||
                 evt.type == EventType.MouseDrag ||
                 evt.type == EventType.MouseUp) &&
                evt.button == 0 &&
                !evt.alt)
            {
                return true;
            }

            return false;
        }

        private static bool TryAttach(IndieRetopologyComponent retopo, GameObject sourceObject)
        {
            if (retopo == null || retopo.Data == null || sourceObject == null)
                return false;

            if (!CanAttach(retopo, sourceObject))
                return false;

            var sourceFilter = sourceObject.GetComponent<MeshFilter>();

            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                Debug.LogWarning("Retopology Attach: picked object has no MeshFilter with a mesh.");
                return false;
            }

            IndieEditableMeshData imported;

            try
            {
                imported = IndieEditableMeshImporter.Import(sourceFilter.sharedMesh);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Retopology Attach: cannot import mesh. {ex.Message}");
                return false;
            }

            if (imported.vertices == null || imported.vertices.Count == 0)
                return false;

            retopo.EnsureData();
            IndieRetopologyMeshData baseline = retopo.Data.Clone();
            Matrix4x4 toLocal = retopo.transform.worldToLocalMatrix * sourceObject.transform.localToWorldMatrix;
            bool flipWinding = toLocal.determinant < 0f;
            int offset = retopo.Data.vertices.Count;

            for (int i = 0; i < imported.vertices.Count; i++)
                retopo.Data.vertices.Add(toLocal.MultiplyPoint3x4(imported.vertices[i]));

            if (imported.faces != null)
            {
                for (int i = 0; i < imported.faces.Count; i++)
                {
                    IndieEditMeshFace face = imported.faces[i];
                    int b = face.b + offset;
                    int c = face.c + offset;

                    if (flipWinding)
                    {
                        int swap = b;
                        b = c;
                        c = swap;
                    }

                    retopo.Data.faces.Add(new IndieRetopologyFace
                    {
                        a = face.a + offset,
                        b = b,
                        c = c
                    });
                }
            }

            Object.DestroyImmediate(sourceObject);
            retopo.Data.MarkChanged();
            IndieRetopologySession.RefreshMesh(retopo);
            IndieRetopologyUndo.CommitBaseline(retopo, baseline);
            EditorUtility.SetDirty(retopo);
            SceneView.RepaintAll();
            return true;
        }

        private static bool CanAttach(IndieRetopologyComponent retopo, GameObject sourceObject)
        {
            if (sourceObject == retopo.gameObject)
                return false;

            if (retopo.transform.IsChildOf(sourceObject.transform) ||
                sourceObject.transform.IsChildOf(retopo.transform))
            {
                return false;
            }

            Transform target = retopo.TargetTransform;

            if (target != null &&
                (sourceObject == target.gameObject || sourceObject.transform.IsChildOf(target)))
            {
                return false;
            }

            return true;
        }
    }
}
