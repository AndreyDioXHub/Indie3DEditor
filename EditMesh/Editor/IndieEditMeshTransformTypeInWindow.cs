using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public class IndieEditMeshTransformTypeInWindow : EditorWindow
    {
        private const string WindowTitle = "Selection Transform";
        private const string PrefsPositionSpace = "Indie3DEditor.EditMesh.TypeIn.LocalPosition";

        private static readonly string[] PositionSpaceLabels = { "World", "Local" };

        private int _selectionSignature;
        private int[] _baselineIndices;
        private Vector3[] _baselineLocal;
        private Vector3 _baselineLocalPivot;
        private Quaternion _worldRotation = Quaternion.identity;
        private Vector3 _rotationEuler;
        private Tool _lastTool = Tool.None;
        private bool _useLocalPosition;

        public static void NotifySelectionOrMeshChanged()
        {
            var ep = IndieEditMeshSession.Active;

            if (ep == null || !ep.isEditing || !IndieEditMeshTransformTypeIn.HasSelection(ep))
            {
                CloseIfOpen();
                return;
            }

            var window = GetOpen();

            if (window == null)
            {
                window = CreateInstance<IndieEditMeshTransformTypeInWindow>();
                window.titleContent = new GUIContent(WindowTitle);
                window.minSize = new Vector2(260f, 120f);
                window.maxSize = new Vector2(420f, 200f);
                window.ShowUtility();
                PositionNearSceneView(window);
            }

            window.CaptureBaseline(resetRotation: true);
            window.Repaint();
        }

        public static void NotifyMeshTransformed(
            bool commitBaseline,
            Quaternion committedRotationDelta = default
        )
        {
            var window = GetOpen();

            if (window == null)
                return;

            if (commitBaseline)
            {
                if (committedRotationDelta != default)
                    window.CommitRotationDelta(committedRotationDelta);
                else
                    window.CaptureBaseline(resetRotation: false);
            }

            window.Repaint();
        }

        public static void CloseIfOpen()
        {
            var window = GetOpen();

            if (window != null)
                window.Close();
        }

        private static IndieEditMeshTransformTypeInWindow GetOpen()
        {
            var windows = Resources.FindObjectsOfTypeAll<IndieEditMeshTransformTypeInWindow>();

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    return windows[i];
            }

            return null;
        }

        private static void PositionNearSceneView(EditorWindow window)
        {
            var sceneView = SceneView.lastActiveSceneView;

            if (sceneView == null)
                return;

            Rect scene = sceneView.position;
            float width = 300f;
            float height = 120f;

            window.position = new Rect(
                scene.xMax - width - 16f,
                scene.yMin + 40f,
                width,
                height
            );
        }

        private void OnEnable()
        {
            _useLocalPosition = EditorPrefs.GetBool(PrefsPositionSpace, false);
            EditorApplication.update += OnEditorUpdate;
            CaptureBaseline(resetRotation: true);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            var ep = IndieEditMeshSession.Active;

            if (ep == null || !ep.isEditing || !IndieEditMeshTransformTypeIn.HasSelection(ep))
            {
                Close();
                return;
            }

            int signature = IndieEditMeshTransformTypeIn.GetSelectionSignature(ep);

            if (signature != _selectionSignature)
                CaptureBaseline(resetRotation: true);

            if (Tools.current != _lastTool)
            {
                _lastTool = Tools.current;
                // Keep accumulated world rotation when switching tools.
                CaptureBaseline(resetRotation: false);
                Repaint();
            }
            else if (
                Tools.current == Tool.Rotate &&
                IndieEditMeshTransformTool.IsDragging
            )
            {
                Repaint();
            }
        }

        private void CaptureBaseline(bool resetRotation)
        {
            var ep = IndieEditMeshSession.Active;

            if (ep == null || ep.Data == null)
            {
                _selectionSignature = 0;
                _baselineIndices = new int[0];
                _baselineLocal = new Vector3[0];
                _baselineLocalPivot = Vector3.zero;
                _worldRotation = Quaternion.identity;
                _rotationEuler = Vector3.zero;
                return;
            }

            _selectionSignature = IndieEditMeshTransformTypeIn.GetSelectionSignature(ep);
            IndieEditMeshTransformTypeIn.CaptureLocalPositions(
                ep,
                out _baselineIndices,
                out _baselineLocal,
                out _baselineLocalPivot
            );

            if (resetRotation)
            {
                _worldRotation = ep.transform.rotation;
                _rotationEuler = IndieEditMeshTransformTypeIn.SanitizeEuler(
                    _worldRotation.eulerAngles
                );
            }
            else
            {
                _rotationEuler = IndieEditMeshTransformTypeIn.SanitizeEuler(
                    _worldRotation.eulerAngles
                );
            }

            _lastTool = Tools.current;
        }

        private void CommitRotationDelta(Quaternion worldDelta)
        {
            if (Mathf.Abs(Quaternion.Dot(worldDelta, Quaternion.identity)) < 0.999999f)
                _worldRotation = worldDelta * _worldRotation;

            CaptureBaseline(resetRotation: false);
        }

        private void OnGUI()
        {
            var ep = IndieEditMeshSession.Active;

            if (ep == null || !ep.isEditing || !IndieEditMeshTransformTypeIn.HasSelection(ep))
            {
                EditorGUILayout.HelpBox("No Edit Mesh selection.", MessageType.Info);
                return;
            }

            if (IndieEditMeshTransformTypeIn.GetSelectionSignature(ep) != _selectionSignature)
                CaptureBaseline(resetRotation: true);

            EditorGUILayout.Space(4f);

            string modeLabel;

            switch (Tools.current)
            {
                case Tool.Move:
                    modeLabel = _useLocalPosition
                        ? "Absolute Position (Local)"
                        : "Absolute Position (World)";
                    break;

                case Tool.Rotate:
                    modeLabel = "Absolute Rotation (World)";
                    break;

                case Tool.Scale:
                    modeLabel = "Absolute Size (World)";
                    break;

                default:
                    modeLabel = "Select Move / Rotate / Scale tool";
                    break;
            }

            EditorGUILayout.LabelField(modeLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                SelectionCountLabel(ep),
                EditorStyles.miniLabel
            );
            EditorGUILayout.Space(2f);

            switch (Tools.current)
            {
                case Tool.Move:
                    DrawPositionSpaceToggle();
                    DrawPositionFields(ep);
                    break;

                case Tool.Rotate:
                    DrawRotationFields(ep);
                    break;

                case Tool.Scale:
                    DrawSizeFields(ep);
                    break;

                default:
                    EditorGUILayout.HelpBox(
                        "Choose the Move, Rotate, or Scale tool to edit values.",
                        MessageType.None
                    );
                    break;
            }
        }

        private void DrawPositionSpaceToggle()
        {
            int space = _useLocalPosition ? 1 : 0;
            int next = GUILayout.Toolbar(space, PositionSpaceLabels, GUILayout.Height(22));

            if (next == space)
                return;

            _useLocalPosition = next == 1;
            EditorPrefs.SetBool(PrefsPositionSpace, _useLocalPosition);
            GUI.FocusControl(null);
            Repaint();
        }

        private void DrawPositionFields(IndieEditMeshComponent ep)
        {
            Vector3 pivot = _useLocalPosition
                ? IndieEditMeshTransformTypeIn.GetLocalPivot(ep)
                : IndieEditMeshTransformTypeIn.GetWorldPivot(ep);
            Vector3 edited = DrawDelayedVector3(pivot);

            if (edited == pivot)
                return;

            if (_useLocalPosition)
                IndieEditMeshTransformTypeIn.ApplyLocalPosition(ep, edited);
            else
                IndieEditMeshTransformTypeIn.ApplyWorldPosition(ep, edited);

            CaptureBaseline(resetRotation: false);
        }

        private void DrawRotationFields(IndieEditMeshComponent ep)
        {
            bool draggingRotate =
                IndieEditMeshTransformTool.IsDragging && Tools.current == Tool.Rotate;

            Vector3 displayEuler = draggingRotate
                ? IndieEditMeshTransformTypeIn.SanitizeEuler(
                    (IndieEditMeshTransformTool.HandleRotation * _worldRotation).eulerAngles
                )
                : _rotationEuler;

            if (draggingRotate)
            {
                EditorGUI.BeginDisabledGroup(true);
                DrawDelayedVector3(displayEuler);
                EditorGUI.EndDisabledGroup();
                return;
            }

            Vector3 edited = DrawDelayedVector3(_rotationEuler);

            if (edited == _rotationEuler)
                return;

            IndieEditMeshTransformTypeIn.ApplyWorldRotationFromBaseline(
                ep,
                _baselineIndices,
                _baselineLocal,
                _baselineLocalPivot,
                _worldRotation,
                edited
            );

            _worldRotation = Quaternion.Euler(edited);
            _rotationEuler = IndieEditMeshTransformTypeIn.SanitizeEuler(edited);
            CaptureBaseline(resetRotation: false);
        }

        private void DrawSizeFields(IndieEditMeshComponent ep)
        {
            Vector3 worldSize = IndieEditMeshTransformTypeIn.GetWorldSize(ep);

            if (worldSize.sqrMagnitude < 1e-12f)
            {
                EditorGUILayout.HelpBox(
                    "Size needs at least two distinct points in the selection.",
                    MessageType.Info
                );
                return;
            }

            Vector3 edited = DrawDelayedVector3(worldSize);
            edited.x = Mathf.Max(0f, edited.x);
            edited.y = Mathf.Max(0f, edited.y);
            edited.z = Mathf.Max(0f, edited.z);

            if (edited != worldSize)
            {
                IndieEditMeshTransformTypeIn.ApplyWorldSize(ep, edited);
                CaptureBaseline(resetRotation: false);
            }
        }

        private static Vector3 DrawDelayedVector3(Vector3 value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("X");
            value.x = EditorGUILayout.DelayedFloatField(value.x);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Y");
            value.y = EditorGUILayout.DelayedFloatField(value.y);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Z");
            value.z = EditorGUILayout.DelayedFloatField(value.z);
            EditorGUILayout.EndHorizontal();

            return value;
        }

        private static string SelectionCountLabel(IndieEditMeshComponent ep)
        {
            var data = ep.Data;

            switch (ep.subMode)
            {
                case IndieEditMeshSubMode.Vertex:
                    return $"Vertices: {data.selectedVertices.Count}";

                case IndieEditMeshSubMode.Edge:
                    return $"Edges: {data.selectedEdges.Count}";

                case IndieEditMeshSubMode.Polygon:
                    return $"Polygons: {data.selectedFaces.Count}";

                default:
                    return string.Empty;
            }
        }
    }
}
