using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    [CustomEditor(typeof(IndieRetopologyComponent))]
    public class IndieRetopologyInspector : Editor
    {
        private void OnEnable()
        {
            var retopo = target as IndieRetopologyComponent;

            if (retopo == null || retopo.Initialized)
                return;

            EditorApplication.delayCall += () =>
            {
                if (retopo != null)
                    IndieRetopologySetup.EnsureMoved(retopo);
            };
        }

        public override void OnInspectorGUI()
        {
            var retopo = target as IndieRetopologyComponent;

            if (retopo == null)
                return;

            if (!retopo.Initialized)
            {
                EditorGUILayout.LabelField("Retopology", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Retopology is moving onto a child object.",
                    MessageType.Info
                );
                return;
            }

            if (!retopo.isEditing)
            {
                DrawIdle(retopo);
                return;
            }

            if (IndieRetopologySession.Active != retopo || retopo.Data == null)
            {
                EditorGUILayout.HelpBox(
                    "Edit mode state was lost. This can happen after script recompilation.",
                    MessageType.Warning
                );

                if (GUILayout.Button("Force Exit Retopology", GUILayout.Height(25)))
                    IndieRetopologySession.RequestRecover(retopo);

                return;
            }

            DrawEditing(retopo);
        }

        private void DrawIdle(IndieRetopologyComponent retopo)
        {
            EditorGUILayout.LabelField("Retopology", EditorStyles.boldLabel);
            DrawTargetField(retopo);
            DrawMissingTargetWarning(retopo);

            GUI.enabled = retopo.TargetMesh != null;

            if (GUILayout.Button("Edit Retopology", GUILayout.Height(30)))
            {
                IndieRetopologySession.BeginEditing(retopo);
                Repaint();
            }

            GUI.enabled = true;
        }

        private void DrawEditing(IndieRetopologyComponent retopo)
        {
            EditorGUILayout.LabelField("Retopology", EditorStyles.boldLabel);
            DrawTargetField(retopo);
            DrawMissingTargetWarning(retopo);
            EditorGUILayout.Space();

            bool hasTarget = retopo.TargetMesh != null;
            Color previousBackground = GUI.backgroundColor;

            if (IndieRetopologyAttach.IsActive)
                GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);

            GUI.enabled = hasTarget;

            if (GUILayout.Button(IndieRetopologyAttach.IsActive ? "Attach: pick object" : "Attach", GUILayout.Height(24)))
                IndieRetopologyAttach.Toggle();

            GUI.backgroundColor = previousBackground;

            if (IndieRetopologyCreatePolygon.IsActive)
                GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);

            if (GUILayout.Button("Create Polygon", GUILayout.Height(24)))
                IndieRetopologyCreatePolygon.Toggle();

            GUI.backgroundColor = previousBackground;
            GUI.enabled = true;
            EditorGUILayout.Space();

            int selected = retopo.Data != null && retopo.Data.selectedEdges != null
                ? retopo.Data.selectedEdges.Count
                : 0;

            GUI.enabled = hasTarget && selected > 2;
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Connect", GUILayout.Height(24)))
            {
                IndieRetopologyConnect.Connect(retopo);
                Repaint();
            }

            if (GUILayout.Button("⚙", GUILayout.Width(28), GUILayout.Height(24)))
                IndieRetopologyConnectWindow.Open();

            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            EditorGUILayout.Space();

            if (GUILayout.Button("Exit Retopology", GUILayout.Height(30)))
                IndieRetopologySession.RequestEndEditing(retopo, true);

            EditorGUILayout.Space();
            DrawHelp(retopo);

            if (GUI.changed)
                EditorUtility.SetDirty(retopo);
        }

        private static void DrawHelp(IndieRetopologyComponent retopo)
        {
            IndieRetopologyMeshData data = retopo.Data;
            int vertices = data != null && data.vertices != null ? data.vertices.Count : 0;
            int polygons = data != null && data.faces != null ? data.faces.Count : 0;
            int edges = IndieRetopologyConnect.CountEdges(data);

            EditorGUILayout.HelpBox(
                $"Vertices: {vertices}\nEdges: {edges}\nPolygons: {polygons}\n\n" +
                "Click the surface to place a point until the first polygon exists. Drag a point to slide it.\n" +
                "Create Polygon: click three points on the surface. The polygon stays editable in this retopology. Esc or right-click cancels and deletes those points.\n" +
                "Attach: click a MeshFilter object to merge it in and remove the source. Points that share a position move and delete together.\n" +
                "While dragging, hold Ctrl and move the point onto another point to weld them.\n" +
                "After the first polygon, hold Shift to highlight an edge, then click the mesh to grow a polygon.\n" +
                "Release that new point on another point within the weld radius to merge them.\n" +
                "Ctrl + click an edge selects it. Ctrl + click empty space clears the edge selection.\n" +
                "Right-click a point to delete it and the edges connected to it.\n" +
                "The Connect gear sets Segments, Pinch and Slide. The dashed preview shows the cut until Accept.\n" +
                "Connect turns on when more than two edges are selected.\n" +
                "Ctrl + Z = Undo\nCtrl + Y = Redo",
                MessageType.Info
            );
        }

        private static void DrawTargetField(IndieRetopologyComponent retopo)
        {
            AdoptLegacyFilter(retopo);

            EditorGUI.BeginChangeCheck();

            MeshFilter filter = (MeshFilter)EditorGUILayout.ObjectField(
                "Target Mesh",
                retopo.TargetFilter,
                typeof(MeshFilter),
                true
            );

            if (!EditorGUI.EndChangeCheck())
                return;

            retopo.SetTarget(filter);

            if (retopo.TargetFilter == null)
            {
                IndieRetopologyCreatePolygon.Cancel();
                IndieRetopologyAttach.Deactivate();
                IndieRetopologyConnectWindow.Dismiss();
            }

            IndieRetopologyPick.InvalidateCache();
            EditorUtility.SetDirty(retopo);
        }

        private static void AdoptLegacyFilter(IndieRetopologyComponent retopo)
        {
            if (retopo.TargetFilter != null || retopo.TargetTransform == null)
                return;

            MeshFilter filter = retopo.TargetTransform.GetComponent<MeshFilter>();

            if (filter == null || filter.gameObject == retopo.gameObject)
                return;

            retopo.SetTarget(filter);
            EditorUtility.SetDirty(retopo);
        }

        private static void DrawMissingTargetWarning(IndieRetopologyComponent retopo)
        {
            if (retopo.TargetMesh != null)
                return;

            EditorGUILayout.HelpBox(
                "No mesh is assigned to edit the retopology against. Tools stay locked until a Target Mesh is set.",
                MessageType.Warning
            );
        }
    }
}
