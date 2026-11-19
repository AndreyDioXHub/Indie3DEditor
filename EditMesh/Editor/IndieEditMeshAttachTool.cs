using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Pick another MeshFilter object in the Scene and merge it into the
    /// currently edited mesh. Source object is deleted; renderer keeps one
    /// mother material.
    /// </summary>
    public static class IndieEditMeshAttachTool
    {
        private static bool _active;

        public static bool IsActive => _active;

        public static void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;

            if (_active)
            {
                IndieEditMeshCutTool.Deactivate();
                IndieEditMeshPointToPoint.Deactivate();
            }

            SceneView.RepaintAll();
        }

        public static void Deactivate()
        {
            SetActive(false);
        }

        public static bool HandleSceneGUI(IndieEditMeshComponent ep, SceneView sceneView, Event evt)
        {
            if (!_active || ep == null || ep.Data == null || evt == null)
                return false;

            Handles.BeginGUI();
            var rect = new Rect(12f, 12f, 320f, 40f);
            GUI.Box(rect, "Attach: click a MeshFilter object  |  Esc / RMB = cancel");
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

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                GameObject picked = HandleUtility.PickGameObject(evt.mousePosition, false);

                if (TryAttach(ep, picked))
                {
                    Deactivate();
                    evt.Use();
                    sceneView.Repaint();
                    return true;
                }

                evt.Use();
                sceneView.Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp)
            {
                if (evt.button == 0 && !evt.alt)
                {
                    evt.Use();
                    return true;
                }
            }

            return false;
        }

        public static bool TryAttach(IndieEditMeshComponent ep, GameObject sourceObject)
        {
            if (ep == null || ep.Data == null || sourceObject == null)
                return false;

            if (sourceObject == ep.gameObject || sourceObject.transform.IsChildOf(ep.transform))
            {
                Debug.LogWarning("Indie EditMesh Attach: cannot attach the edited object to itself.");
                return false;
            }

            var sourceFilter = sourceObject.GetComponent<MeshFilter>();

            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                Debug.LogWarning("Indie EditMesh Attach: picked object has no MeshFilter with a mesh.");
                return false;
            }

            IndieEditableMeshData imported;

            try
            {
                imported = IndieEditableMeshImporter.Import(sourceFilter.sharedMesh);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Indie EditMesh Attach: cannot import mesh. {ex.Message}");
                return false;
            }

            IndieEditMeshUndo.Register(ep, "Attach");

            Matrix4x4 toMother = ep.transform.worldToLocalMatrix * sourceObject.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = toMother.inverse.transpose;

            int vertexOffset = ep.Data.vertices.Count;

            for (int i = 0; i < imported.vertices.Count; i++)
            {
                Vector3 local = toMother.MultiplyPoint3x4(imported.vertices[i]);
                ep.Data.vertices.Add(local);

                if (i < imported.uvs.Count)
                    ep.Data.uvs.Add(imported.uvs[i]);
                else if (ep.Data.uvs.Count == vertexOffset + i)
                    ep.Data.uvs.Add(Vector2.zero);

                if (i < imported.normals.Count)
                {
                    Vector3 n = normalMatrix.MultiplyVector(imported.normals[i]);
                    ep.Data.normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
                }

                if (i < imported.colors.Count)
                    ep.Data.colors.Add(imported.colors[i]);

                if (i < imported.tangents.Count)
                {
                    Vector4 t = imported.tangents[i];
                    Vector3 td = normalMatrix.MultiplyVector(new Vector3(t.x, t.y, t.z));
                    ep.Data.tangents.Add(new Vector4(td.x, td.y, td.z, t.w));
                }
            }

            // Keep UV/color/normal arrays aligned with vertex count when mother had none.
            while (ep.Data.uvs.Count > 0 && ep.Data.uvs.Count < ep.Data.vertices.Count)
                ep.Data.uvs.Add(Vector2.zero);

            for (int i = 0; i < imported.faces.Count; i++)
            {
                var face = imported.faces[i];
                face.a += vertexOffset;
                face.b += vertexOffset;
                face.c += vertexOffset;
                face.materialId = 0;
                ep.Data.faces.Add(face);
            }

            // Single material slot for the whole editable mesh.
            for (int i = 0; i < ep.Data.faces.Count; i++)
            {
                var face = ep.Data.faces[i];
                face.materialId = 0;
                ep.Data.faces[i] = face;
            }

            ep.Data.RebuildEdges();
            ApplyMotherSingleMaterial(ep);
            IndieEditMeshSession.RefreshMesh(ep);

            Object.DestroyImmediate(sourceObject);

            IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
            SceneView.RepaintAll();
            return true;
        }

        private static void ApplyMotherSingleMaterial(IndieEditMeshComponent ep)
        {
            var renderer = ep.GetComponent<MeshRenderer>();

            if (renderer == null)
                return;

            Material mother = null;
            var current = renderer.sharedMaterials;

            if (current != null && current.Length > 0)
                mother = current[0];

            if (mother == null)
                mother = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            renderer.sharedMaterials = new[]
            {
                mother
            };

            EditorUtility.SetDirty(renderer);
        }
    }
}
