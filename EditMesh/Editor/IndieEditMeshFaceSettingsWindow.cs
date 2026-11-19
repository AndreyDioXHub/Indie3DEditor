using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public class IndieEditMeshFaceSettingsWindow : EditorWindow
    {
        private IndieEditMeshFaceSettingsKind _kind;
        private IndieEditMeshPreviewKind _previewKind;
        private bool _closingByButton;

        public static void Open(IndieEditMeshComponent ep, IndieEditMeshFaceSettingsKind kind)
        {
            int minSelection = kind == IndieEditMeshFaceSettingsKind.Bridge
                ? 2
                : kind == IndieEditMeshFaceSettingsKind.Ring ||
                  kind == IndieEditMeshFaceSettingsKind.Loop ||
                  kind == IndieEditMeshFaceSettingsKind.Row
                    ? IndieEditMeshFaceOps.RingMinFaces
                    : 1;

            if (ep == null || ep.Data == null || ep.Data.selectedFaces.Count < minSelection)
            {
                EditorUtility.DisplayDialog(
                    "Edit Polygons",
                    minSelection >= IndieEditMeshFaceOps.RingMinFaces
                        ? $"Select at least {IndieEditMeshFaceOps.RingMinFaces} polygons first."
                        : minSelection > 1
                            ? "Select at least two polygons first."
                            : "Select one or more polygons first.",
                    "OK"
                );
                return;
            }

            if (IndieEditMeshPreviewOp.IsActive)
                IndieEditMeshPreviewOp.Cancel();

            if (!IndieEditMeshPreviewOp.BeginFace(ep, kind))
                return;

            var window = GetWindow<IndieEditMeshFaceSettingsWindow>(true, GetTitle(kind), true);
            window._kind = kind;
            window._previewKind = IndieEditMeshPreviewOp.Kind;
            window._closingByButton = false;
            window.minSize = new Vector2(300f, 170f);
            window.maxSize = new Vector2(480f, 300f);
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
                case IndieEditMeshFaceSettingsKind.Extrude:
                    IndieEditMeshFaceParams.ExtrudeType = (IndieEditMeshFaceExtrudeType)EditorGUILayout.EnumPopup(
                        "Extrusion Type",
                        IndieEditMeshFaceParams.ExtrudeType
                    );
                    IndieEditMeshFaceParams.ExtrudeHeight = EditorGUILayout.FloatField(
                        "Extrusion Height",
                        IndieEditMeshFaceParams.ExtrudeHeight
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Outline:
                    IndieEditMeshFaceParams.OutlineAmount = EditorGUILayout.FloatField(
                        "Outline Amount",
                        IndieEditMeshFaceParams.OutlineAmount
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Bevel:
                    IndieEditMeshFaceParams.BevelType = (IndieEditMeshFaceExtrudeType)EditorGUILayout.EnumPopup(
                        "Bevel Type",
                        IndieEditMeshFaceParams.BevelType
                    );
                    IndieEditMeshFaceParams.BevelHeight = EditorGUILayout.FloatField(
                        "Height",
                        IndieEditMeshFaceParams.BevelHeight
                    );
                    IndieEditMeshFaceParams.BevelOutline = EditorGUILayout.FloatField(
                        "Outline Amount",
                        IndieEditMeshFaceParams.BevelOutline
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Inset:
                {
                    int insetTypeIndex =
                        IndieEditMeshFaceParams.InsetType == IndieEditMeshFaceExtrudeType.ByPolygon ? 1 : 0;
                    insetTypeIndex = EditorGUILayout.Popup(
                        "Inset Type",
                        insetTypeIndex,
                        new[] { "Group", "By Polygon" }
                    );
                    IndieEditMeshFaceParams.InsetType = insetTypeIndex == 1
                        ? IndieEditMeshFaceExtrudeType.ByPolygon
                        : IndieEditMeshFaceExtrudeType.Group;
                    IndieEditMeshFaceParams.InsetAmount = Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField("Inset Amount", IndieEditMeshFaceParams.InsetAmount)
                    );
                    break;
                }

                case IndieEditMeshFaceSettingsKind.Bridge:
                    IndieEditMeshFaceParams.BridgeSegments = Mathf.Max(
                        1,
                        EditorGUILayout.IntField("Segments", IndieEditMeshFaceParams.BridgeSegments)
                    );
                    IndieEditMeshFaceParams.BridgeTwist = EditorGUILayout.IntField(
                        "Twist",
                        IndieEditMeshFaceParams.BridgeTwist
                    );
                    IndieEditMeshFaceParams.BridgeTaper = EditorGUILayout.FloatField(
                        "Taper",
                        IndieEditMeshFaceParams.BridgeTaper
                    );
                    IndieEditMeshFaceParams.BridgeBias = EditorGUILayout.Slider(
                        "Bias",
                        IndieEditMeshFaceParams.BridgeBias,
                        -99f,
                        99f
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Ring:
                    IndieEditMeshFaceParams.RingAngle = EditorGUILayout.Slider(
                        "Angle Threshold",
                        IndieEditMeshFaceParams.RingAngle,
                        0.1f,
                        45f
                    );
                    EditorGUILayout.HelpBox(
                        "Max angle (°) between the current edge and the next ring candidate.",
                        MessageType.None
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Loop:
                    IndieEditMeshFaceParams.LoopAngle = EditorGUILayout.Slider(
                        "Angle Threshold",
                        IndieEditMeshFaceParams.LoopAngle,
                        0.1f,
                        90f
                    );
                    EditorGUILayout.HelpBox(
                        "Seeds → Edge Loop both rings → select polygons that " +
                        "touch both rings (belt between them).",
                        MessageType.None
                    );
                    break;

                case IndieEditMeshFaceSettingsKind.Row:
                    IndieEditMeshFaceParams.RowAngle = EditorGUILayout.Slider(
                        "Angle Threshold",
                        IndieEditMeshFaceParams.RowAngle,
                        0.1f,
                        90f
                    );
                    EditorGUILayout.HelpBox(
                        "Grow a row of quads left/right across width edges (1→2). " +
                        "Stops when the turn between faces exceeds this angle (°).",
                        MessageType.None
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

        private static string GetTitle(IndieEditMeshFaceSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshFaceSettingsKind.Extrude:
                    return "Extrude Polygons";
                case IndieEditMeshFaceSettingsKind.Outline:
                    return "Outline Polygons";
                case IndieEditMeshFaceSettingsKind.Bevel:
                    return "Bevel Polygons";
                case IndieEditMeshFaceSettingsKind.Inset:
                    return "Inset Polygons";
                case IndieEditMeshFaceSettingsKind.Bridge:
                    return "Bridge Polygons";
                case IndieEditMeshFaceSettingsKind.Ring:
                    return "Ring Polygons";
                case IndieEditMeshFaceSettingsKind.Loop:
                    return "Loop Polygons";
                case IndieEditMeshFaceSettingsKind.Row:
                    return "Row Polygons";
                default:
                    return "Polygon Settings";
            }
        }
    }
}
