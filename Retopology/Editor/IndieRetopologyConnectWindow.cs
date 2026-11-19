using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public class IndieRetopologyConnectWindow : EditorWindow
    {
        private static IndieRetopologyConnectWindow _window;

        public static bool IsOpen => _window != null;

        public static void Open()
        {
            IndieRetopologyComponent retopo = IndieRetopologySession.Active;

            if (retopo == null || retopo.Data == null || retopo.Data.selectedEdges == null)
                return;

            if (retopo.Data.selectedEdges.Count <= 2)
                return;

            var window = GetWindow<IndieRetopologyConnectWindow>(true, "Connect Edges", true);
            window.minSize = new Vector2(300f, 160f);
            window.maxSize = new Vector2(460f, 220f);
            _window = window;
            window.ShowUtility();
            window.Focus();
            SceneView.RepaintAll();
        }

        public static void Dismiss()
        {
            if (_window == null)
                return;

            IndieRetopologyConnectWindow window = _window;
            _window = null;
            window.Close();
            SceneView.RepaintAll();
        }

        private void OnDisable()
        {
            if (_window == this)
                _window = null;

            SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            IndieRetopologyComponent retopo = IndieRetopologySession.Active;

            if (retopo == null)
            {
                EditorGUILayout.HelpBox("Edit session is not active.", MessageType.Warning);

                if (GUILayout.Button("Close"))
                    Close();

                return;
            }

            EditorGUI.BeginChangeCheck();

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

            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUILayout.Space(12f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Accept", GUILayout.Height(28f)))
            {
                IndieRetopologyConnect.Connect(retopo);
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(28f)))
                Close();

            EditorGUILayout.EndHorizontal();
        }
    }
}
