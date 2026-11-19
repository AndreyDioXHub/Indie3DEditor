using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public class IndieEditMeshVertexSettingsWindow : EditorWindow
    {
        private IndieEditMeshVertexSettingsKind _kind;
        private IndieEditMeshPreviewKind _previewKind;
        private bool _closingByButton;

        public static void Open(IndieEditMeshComponent ep, IndieEditMeshVertexSettingsKind kind)
        {
            if (ep == null || ep.Data == null || ep.Data.selectedVertices.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Edit Vertices",
                    "Select one or more vertices first.",
                    "OK"
                );
                return;
            }

            if (IndieEditMeshPreviewOp.IsActive)
                IndieEditMeshPreviewOp.Cancel();

            if (!IndieEditMeshPreviewOp.BeginVertex(ep, kind))
                return;

            var window = GetWindow<IndieEditMeshVertexSettingsWindow>(true, GetTitle(kind), true);
            window._kind = kind;
            window._previewKind = IndieEditMeshPreviewOp.Kind;
            window._closingByButton = false;
            window.minSize = new Vector2(280f, 140f);
            window.maxSize = new Vector2(420f, 220f);
            window.ShowUtility();
            window.Focus();
        }

        private void OnDisable()
        {
            if (_closingByButton)
                return;

            if (IndieEditMeshPreviewOp.IsActive && IndieEditMeshPreviewOp.Kind == _previewKind)
                IndieEditMeshPreviewOp.Cancel();
        }

        private void OnGUI()
        {
            if (!IndieEditMeshPreviewOp.IsActive || IndieEditMeshPreviewOp.Kind != _previewKind)
            {
                EditorGUILayout.HelpBox("Preview session is not active.", MessageType.Warning);

                if (GUILayout.Button("Close"))
                    Close();

                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(GetTitle(_kind), EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();

            switch (_kind)
            {
                case IndieEditMeshVertexSettingsKind.Extrude:
                    IndieEditMeshVertexParams.ExtrudeHeight = EditorGUILayout.FloatField(
                        "Extrusion Height",
                        IndieEditMeshVertexParams.ExtrudeHeight
                    );
                    IndieEditMeshVertexParams.ExtrudeBaseWidth = EditorGUILayout.FloatField(
                        "Extrusion Base Width",
                        Mathf.Max(0f, IndieEditMeshVertexParams.ExtrudeBaseWidth)
                    );
                    break;

                case IndieEditMeshVertexSettingsKind.Weld:
                    IndieEditMeshVertexParams.WeldThreshold = EditorGUILayout.FloatField(
                        "Weld Threshold",
                        Mathf.Max(0f, IndieEditMeshVertexParams.WeldThreshold)
                    );
                    break;

                case IndieEditMeshVertexSettingsKind.Chamfer:
                    IndieEditMeshVertexParams.ChamferAmount = EditorGUILayout.FloatField(
                        "Chamfer Amount",
                        Mathf.Max(0f, IndieEditMeshVertexParams.ChamferAmount)
                    );
                    IndieEditMeshVertexParams.ChamferOpen = EditorGUILayout.Toggle(
                        "Open",
                        IndieEditMeshVertexParams.ChamferOpen
                    );
                    break;
            }

            if (EditorGUI.EndChangeCheck())
                IndieEditMeshPreviewOp.RefreshPreview();

            EditorGUILayout.Space(12f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Accept", GUILayout.Height(28f)))
            {
                _closingByButton = true;
                IndieEditMeshPreviewOp.Accept();
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(28f)))
            {
                _closingByButton = true;
                IndieEditMeshPreviewOp.Cancel();
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string GetTitle(IndieEditMeshVertexSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshVertexSettingsKind.Extrude:
                    return "Extrude Vertices";
                case IndieEditMeshVertexSettingsKind.Weld:
                    return "Weld Vertices";
                case IndieEditMeshVertexSettingsKind.Chamfer:
                    return "Chamfer Vertices";
                default:
                    return "Vertex Settings";
            }
        }
    }
}
