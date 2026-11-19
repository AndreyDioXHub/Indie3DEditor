using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public class IndieEditMeshEdgeSettingsWindow : EditorWindow
    {
        private IndieEditMeshEdgeSettingsKind _kind;
        private IndieEditMeshPreviewKind _previewKind;
        private IndieEditMeshComponent _target;
        private bool _closingByButton;
        private static IndieEditMeshEdgeSettingsWindow _connectWindow;

        public static bool IsConnectPreviewFor(IndieEditMeshComponent ep)
        {
            return _connectWindow != null && _connectWindow._target == ep;
        }

        /// <summary>
        /// Hides the dashed preview and closes the Connect window without rebuilding the mesh.
        /// </summary>
        public static void DismissConnectPreview()
        {
            if (_connectWindow == null)
                return;

            var window = _connectWindow;
            _connectWindow = null;
            window._closingByButton = true;
            window.Close();
            SceneView.RepaintAll();
        }

        public static void Open(IndieEditMeshComponent ep, IndieEditMeshEdgeSettingsKind kind)
        {
            int minSelection = kind == IndieEditMeshEdgeSettingsKind.Bridge ||
                               kind == IndieEditMeshEdgeSettingsKind.Connect ||
                               kind == IndieEditMeshEdgeSettingsKind.Ring
                ? 2
                : 1;

            if (ep == null || ep.Data == null || ep.Data.selectedEdges.Count < minSelection)
            {
                EditorUtility.DisplayDialog(
                    "Edit Edges",
                    minSelection > 1
                        ? "Select at least two edges first."
                        : "Select one or more edges first.",
                    "OK"
                );
                return;
            }

            if (IndieEditMeshPreviewOp.IsActive)
                IndieEditMeshPreviewOp.Cancel();

            bool connect = kind == IndieEditMeshEdgeSettingsKind.Connect;

            if (!connect && !IndieEditMeshPreviewOp.BeginEdge(ep, kind))
                return;

            var window = GetWindow<IndieEditMeshEdgeSettingsWindow>(true, GetTitle(kind), true);
            window._kind = kind;
            window._target = ep;
            window._previewKind = connect ? IndieEditMeshPreviewKind.EdgeConnect : IndieEditMeshPreviewOp.Kind;
            window._closingByButton = false;
            window.minSize = new Vector2(300f, 180f);
            window.maxSize = new Vector2(460f, 280f);

            if (connect)
            {
                _connectWindow = window;
                SceneView.RepaintAll();
            }
            else if (_connectWindow == window)
            {
                _connectWindow = null;
                SceneView.RepaintAll();
            }

            window.ShowUtility();
            window.Focus();
        }

        private bool IsConnect => _kind == IndieEditMeshEdgeSettingsKind.Connect;

        private void OnDisable()
        {
            if (_connectWindow == this)
            {
                _connectWindow = null;
                SceneView.RepaintAll();
            }

            if (_closingByButton || IsConnect)
                return;

            if (IndieEditMeshPreviewOp.IsActive && IndieEditMeshPreviewOp.Kind == _previewKind)
                IndieEditMeshPreviewOp.Cancel();
        }

        private void OnGUI()
        {
            bool sessionAlive = IsConnect
                ? _target != null && IndieEditMeshSession.Active == _target
                : IndieEditMeshPreviewOp.IsActive && IndieEditMeshPreviewOp.Kind == _previewKind;

            if (!sessionAlive)
            {
                EditorGUILayout.HelpBox(
                    IsConnect ? "Edit session is not active." : "Preview session is not active.",
                    MessageType.Warning
                );

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
                case IndieEditMeshEdgeSettingsKind.Extrude:
                    IndieEditMeshEdgeParams.ExtrudeHeight = EditorGUILayout.FloatField(
                        "Extrusion Height",
                        IndieEditMeshEdgeParams.ExtrudeHeight
                    );
                    IndieEditMeshEdgeParams.ExtrudeBaseWidth = EditorGUILayout.FloatField(
                        "Extrusion Base Width",
                        Mathf.Max(0f, IndieEditMeshEdgeParams.ExtrudeBaseWidth)
                    );
                    break;

                case IndieEditMeshEdgeSettingsKind.Weld:
                    IndieEditMeshEdgeParams.WeldThreshold = EditorGUILayout.FloatField(
                        "Weld Threshold",
                        Mathf.Max(0f, IndieEditMeshEdgeParams.WeldThreshold)
                    );
                    break;

                case IndieEditMeshEdgeSettingsKind.Chamfer:
                    IndieEditMeshEdgeParams.ChamferAmountInput = Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField("Amount", IndieEditMeshEdgeParams.ChamferAmountInput)
                    );
                    IndieEditMeshEdgeParams.ChamferAmountDivisor = Mathf.Max(
                        1e-6f,
                        EditorGUILayout.FloatField("Divide", IndieEditMeshEdgeParams.ChamferAmountDivisor)
                    );
                    IndieEditMeshEdgeParams.ChamferSegments = Mathf.Max(
                        1,
                        EditorGUILayout.IntField("Segments", IndieEditMeshEdgeParams.ChamferSegments)
                    );
                    IndieEditMeshEdgeParams.ChamferTension = EditorGUILayout.Slider(
                        "Tension (0=round, 1=flat)",
                        IndieEditMeshEdgeParams.ChamferTension,
                        0f,
                        1f
                    );
                    IndieEditMeshEdgeParams.ChamferInvert = EditorGUILayout.Toggle(
                        "Invert (concave)",
                        IndieEditMeshEdgeParams.ChamferInvert
                    );
                    IndieEditMeshEdgeParams.ChamferOpen = EditorGUILayout.Toggle(
                        "Open",
                        IndieEditMeshEdgeParams.ChamferOpen
                    );
                    break;

                case IndieEditMeshEdgeSettingsKind.Bridge:
                    IndieEditMeshEdgeParams.BridgeSegments = Mathf.Max(
                        1,
                        EditorGUILayout.IntField("Segments", IndieEditMeshEdgeParams.BridgeSegments)
                    );
                    IndieEditMeshEdgeParams.BridgeTwist = EditorGUILayout.IntField(
                        "Twist",
                        IndieEditMeshEdgeParams.BridgeTwist
                    );
                    IndieEditMeshEdgeParams.BridgeTaper = EditorGUILayout.FloatField(
                        "Taper",
                        IndieEditMeshEdgeParams.BridgeTaper
                    );
                    IndieEditMeshEdgeParams.BridgeBias = EditorGUILayout.Slider(
                        "Bias",
                        IndieEditMeshEdgeParams.BridgeBias,
                        -99f,
                        99f
                    );
                    break;

                case IndieEditMeshEdgeSettingsKind.Connect:
                    IndieEditMeshEdgeParams.ConnectSegments = Mathf.Max(
                        1,
                        EditorGUILayout.IntField("Segments", IndieEditMeshEdgeParams.ConnectSegments)
                    );
                    IndieEditMeshEdgeParams.ConnectPinch = EditorGUILayout.Slider(
                        "Pinch",
                        IndieEditMeshEdgeParams.ConnectPinch,
                        -1f,
                        1f
                    );
                    IndieEditMeshEdgeParams.ConnectSlide = EditorGUILayout.Slider(
                        "Slide",
                        IndieEditMeshEdgeParams.ConnectSlide,
                        -1f,
                        1f
                    );
                    break;

                case IndieEditMeshEdgeSettingsKind.Ring:
                    IndieEditMeshEdgeParams.RingAngle = EditorGUILayout.Slider(
                        "Angle Threshold",
                        IndieEditMeshEdgeParams.RingAngle,
                        0.1f,
                        45f
                    );
                    EditorGUILayout.HelpBox(
                        "Max angle (°) between the current edge and the next ring candidate.",
                        MessageType.None
                    );
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (IsConnect)
                    SceneView.RepaintAll();
                else
                    IndieEditMeshPreviewOp.RefreshPreview();
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Accept", GUILayout.Height(28f)))
            {
                _closingByButton = true;

                if (IsConnect)
                {
                    var ep = _target;
                    IndieEditMeshEdgeOps.Connect(ep);
                }
                else
                {
                    IndieEditMeshPreviewOp.Accept();
                }

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

        private static string GetTitle(IndieEditMeshEdgeSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshEdgeSettingsKind.Extrude:
                    return "Extrude Edges";
                case IndieEditMeshEdgeSettingsKind.Weld:
                    return "Weld Edges";
                case IndieEditMeshEdgeSettingsKind.Chamfer:
                    return "Chamfer Edges";
                case IndieEditMeshEdgeSettingsKind.Bridge:
                    return "Bridge Edges";
                case IndieEditMeshEdgeSettingsKind.Connect:
                    return "Connect Edges";
                case IndieEditMeshEdgeSettingsKind.Ring:
                    return "Ring Edges";
                default:
                    return "Edge Settings";
            }
        }
    }
}
