using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    [CustomEditor(typeof(IndieEditMeshComponent))]
    public class IndieEditMeshInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            var ep = target as IndieEditMeshComponent;

            if (ep == null)
                return;

            if (!ep.isEditing)
            {
                EditorGUILayout.LabelField("Edit Mesh", EditorStyles.boldLabel);

                var meshFilter = ep.GetMeshFilter();

                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    EditorGUILayout.HelpBox(
                        "This object needs a MeshFilter with a mesh.",
                        MessageType.Warning
                    );
                }

                GUI.enabled = meshFilter != null && meshFilter.sharedMesh != null;

                if (GUILayout.Button("Edit", GUILayout.Height(30)))
                {
                    IndieEditMeshSession.BeginEditing(ep);
                    Repaint();
                }

                GUI.enabled = true;

                return;
            }

            if (IndieEditMeshSession.Active != ep || ep.Data == null)
            {
                EditorGUILayout.HelpBox(
                    "Edit mode state was lost. This can happen after script recompilation.",
                    MessageType.Warning
                );

                if (GUILayout.Button("Force Exit Edit Mode", GUILayout.Height(25)))
                {
                    ep.isEditing = false;
                    ep.SetData(null);

                    Tools.hidden = false;

                    IndieEditMeshUndo.Clear();
                    IndieEditMeshTransformTool.ResetDrag();

                    EditorUtility.SetDirty(ep);
                    SceneView.RepaintAll();
                    Repaint();
                }

                return;
            }

            EditorGUILayout.LabelField("Edit Mesh", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if ((int)ep.subMode > (int)IndieEditMeshSubMode.Polygon)
                ep.subMode = IndieEditMeshSubMode.Polygon;

            IndieEditMeshSubMode previousSubMode = ep.subMode;

            ep.subMode = (IndieEditMeshSubMode)GUILayout.Toolbar(
                (int)ep.subMode,
                new string[]
                {
                    "Vertex",
                    "Edge",
                    "Polygon"
                }
            );

            if (ep.subMode != previousSubMode)
            {
                if (ep.subMode != IndieEditMeshSubMode.Vertex)
                    IndieEditMeshCutTool.Deactivate();

                if (ep.subMode != IndieEditMeshSubMode.Edge)
                    IndieEditMeshPointToPoint.Deactivate();

                IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
            }

            EditorGUILayout.Space();

            Color togglePrev = GUI.backgroundColor;

            if (IndieEditMeshSelection.IgnoreBackFaces)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(
                    IndieEditMeshSelection.IgnoreBackFaces
                        ? "Ignore Back Faces (On)"
                        : "Ignore Back Faces",
                    GUILayout.Height(24)
                ))
            {
                IndieEditMeshSelection.IgnoreBackFaces = !IndieEditMeshSelection.IgnoreBackFaces;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = togglePrev;

            if (IndieEditMeshSelection.LinkCoincident)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(
                    IndieEditMeshSelection.LinkCoincident
                        ? "Link Coincident (On)"
                        : "Link Coincident",
                    GUILayout.Height(24)
                ))
            {
                IndieEditMeshSelection.LinkCoincident = !IndieEditMeshSelection.LinkCoincident;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = togglePrev;

            EditorGUILayout.Space(2f);

            if (IndieEditMeshAttachTool.IsActive)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(
                    IndieEditMeshAttachTool.IsActive ? "Attach (pick object…)" : "Attach",
                    GUILayout.Height(24)
                ))
            {
                IndieEditMeshAttachTool.SetActive(!IndieEditMeshAttachTool.IsActive);
            }

            GUI.backgroundColor = togglePrev;

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = IndieEditMeshUndo.UndoCount > 0;

            if (GUILayout.Button($"Undo ({IndieEditMeshUndo.UndoCount})", GUILayout.Height(22)))
            {
                IndieEditMeshUndo.Undo(ep);
                Repaint();
            }

            GUI.enabled = IndieEditMeshUndo.RedoCount > 0;

            if (GUILayout.Button($"Redo ({IndieEditMeshUndo.RedoCount})", GUILayout.Height(22)))
            {
                IndieEditMeshUndo.Redo(ep);
                Repaint();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (ep.subMode == IndieEditMeshSubMode.Vertex)
            {
                DrawEditVerticesBlock(ep);
                EditorGUILayout.Space();
            }
            else if (ep.subMode == IndieEditMeshSubMode.Edge)
            {
                DrawEditEdgesBlock(ep);
                EditorGUILayout.Space();
            }
            else if (ep.subMode == IndieEditMeshSubMode.Polygon)
            {
                DrawEditMeshgonsBlock(ep);
                EditorGUILayout.Space();
            }

            if (GUILayout.Button("Delete Selected", GUILayout.Height(25)))
            {
                IndieEditMeshOps.DeleteSelected(ep);
                Repaint();
            }

            if (GUILayout.Button("Flip Selected Normals", GUILayout.Height(25)))
            {
                IndieEditMeshOps.FlipNormals(ep);
                Repaint();
            }

            if (GUILayout.Button("Clear Selection", GUILayout.Height(22)))
            {
                IndieEditMeshUndo.Register(ep, "Clear Selection");
                ep.Data.ClearSelection();
                IndieEditMeshSession.RefreshMesh(ep);
                Repaint();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Exit Edit Mesh", GUILayout.Height(30)))
            {
                IndieEditMeshSession.EndEditing(ep, true);
                Repaint();
            }

            if (GUILayout.Button("Cancel Edit Mesh", GUILayout.Height(22)))
            {
                IndieEditMeshSession.EndEditing(ep, false);
                Repaint();
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Ctrl + Z = Undo\nCtrl + Y = Redo\nCtrl + A = Select All (current mode)\nShift/Ctrl + Click = Add/Remove\nDrag = Marquee select\nLink Coincident: select/move stacked verts together (keeps UV)\nPolygon + Shift + Move = Extrude\nEdge + Shift + Move = Extrude + Bridge\nAttach: pick MeshFilter object to merge\nVertex Cut: click path, RMB end/exit\nEdge Point-to-Point: click path, RMB exits, selection stays",
                MessageType.Info
            );

            if (GUI.changed)
            {
                EditorUtility.SetDirty(ep);
            }
        }

        private static void DrawEditVerticesBlock(IndieEditMeshComponent ep)
        {
            EditorGUILayout.LabelField("Edit Vertices", EditorStyles.boldLabel);

            bool hasSelection = ep.Data != null && ep.Data.selectedVertices.Count > 0;
            bool previewBusy = IndieEditMeshPreviewOp.IsActive;

            GUI.enabled = hasSelection && !previewBusy && !IndieEditMeshCutTool.IsActive;

            if (GUILayout.Button("Grow", GUILayout.Height(24)))
                IndieEditMeshOps.Grow(ep);

            GUI.enabled = !previewBusy;

            Color previous = GUI.backgroundColor;

            if (IndieEditMeshCutTool.IsActive)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(
                    IndieEditMeshCutTool.IsActive ? "Cut (Active)" : "Cut",
                    GUILayout.Height(24)
                ))
            {
                IndieEditMeshCutTool.SetActive(!IndieEditMeshCutTool.IsActive);
            }

            GUI.backgroundColor = previous;

            GUI.enabled = hasSelection && !previewBusy && !IndieEditMeshCutTool.IsActive;

            if (GUILayout.Button("Remove", GUILayout.Height(24)))
            {
                IndieEditMeshVertexOps.Remove(ep);
            }

            if (GUILayout.Button("Break", GUILayout.Height(24)))
            {
                IndieEditMeshVertexOps.Break(ep);
            }

            DrawActionWithSettings(
                "Weld",
                () => IndieEditMeshVertexOps.Weld(ep),
                () => IndieEditMeshVertexSettingsWindow.Open(ep, IndieEditMeshVertexSettingsKind.Weld)
            );

            DrawActionWithSettings(
                "Chamfer",
                () => IndieEditMeshVertexOps.Chamfer(ep),
                () => IndieEditMeshVertexSettingsWindow.Open(ep, IndieEditMeshVertexSettingsKind.Chamfer)
            );

            GUI.enabled = true;

            EditorGUILayout.Space();
            DrawFlatBlock(ep);
        }

        private static void DrawEditEdgesBlock(IndieEditMeshComponent ep)
        {
            EditorGUILayout.LabelField("Edit Edges", EditorStyles.boldLabel);

            bool hasSelection = ep.Data != null && ep.Data.selectedEdges.Count > 0;
            bool hasPairSelection = ep.Data != null && ep.Data.selectedEdges.Count >= 2;
            bool canLoop = IndieEditMeshEdgeOps.CanLoop(ep.Data);
            bool previewBusy = IndieEditMeshPreviewOp.IsActive;

            GUI.enabled = !previewBusy;

            Color pointToPointColor = GUI.backgroundColor;

            if (IndieEditMeshPointToPoint.IsActive)
                GUI.backgroundColor = new Color(1f, 0.7f, 0.35f, 1f);

            if (GUILayout.Button(
                    IndieEditMeshPointToPoint.IsActive
                        ? "Point-to-Point Selection (On)"
                        : "Point-to-Point Selection",
                    GUILayout.Height(24)
                ))
            {
                IndieEditMeshPointToPoint.SetActive(!IndieEditMeshPointToPoint.IsActive);
            }

            GUI.backgroundColor = pointToPointColor;

            GUI.enabled = hasSelection && !previewBusy;

            if (GUILayout.Button("Grow", GUILayout.Height(24)))
                IndieEditMeshOps.Grow(ep);

            DrawActionWithSettings(
                "Chamfer",
                () => IndieEditMeshEdgeOps.Chamfer(ep),
                () => IndieEditMeshEdgeSettingsWindow.Open(ep, IndieEditMeshEdgeSettingsKind.Chamfer)
            );

            GUI.enabled = hasPairSelection && !previewBusy;

            DrawActionWithSettings(
                "Bridge",
                () => IndieEditMeshEdgeOps.Bridge(ep),
                () => IndieEditMeshEdgeSettingsWindow.Open(ep, IndieEditMeshEdgeSettingsKind.Bridge)
            );

            DrawActionWithSettings(
                "Connect",
                () => IndieEditMeshEdgeOps.Connect(ep),
                () => IndieEditMeshEdgeSettingsWindow.Open(ep, IndieEditMeshEdgeSettingsKind.Connect)
            );

            DrawActionWithSettings(
                "Ring",
                () => IndieEditMeshEdgeOps.Ring(ep),
                () => IndieEditMeshEdgeSettingsWindow.Open(ep, IndieEditMeshEdgeSettingsKind.Ring)
            );

            GUI.enabled = canLoop && !previewBusy;

            if (GUILayout.Button("Loop", GUILayout.Height(24)))
                IndieEditMeshEdgeOps.Loop(ep);

            GUI.enabled = true;

            EditorGUILayout.Space();
            DrawFlatBlock(ep);
        }

        private static void DrawEditMeshgonsBlock(IndieEditMeshComponent ep)
        {
            EditorGUILayout.LabelField("Edit Polygons", EditorStyles.boldLabel);

            bool hasSelection = ep.Data != null && ep.Data.selectedFaces.Count > 0;
            bool hasPairSelection = ep.Data != null && ep.Data.selectedFaces.Count >= 2;
            bool previewBusy = IndieEditMeshPreviewOp.IsActive;

            GUI.enabled = hasSelection && !previewBusy;

            if (GUILayout.Button("Grow", GUILayout.Height(24)))
                IndieEditMeshOps.Grow(ep);

            DrawActionWithSettings(
                "Extrude",
                () => IndieEditMeshFaceOps.Extrude(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Extrude)
            );

            DrawActionWithSettings(
                "Outline",
                () => IndieEditMeshFaceOps.Outline(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Outline)
            );

            DrawActionWithSettings(
                "Bevel",
                () => IndieEditMeshFaceOps.Bevel(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Bevel)
            );

            DrawActionWithSettings(
                "Inset",
                () => IndieEditMeshFaceOps.Inset(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Inset)
            );

            GUI.enabled = hasPairSelection && !previewBusy;

            DrawActionWithSettings(
                "Bridge",
                () => IndieEditMeshFaceOps.Bridge(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Bridge)
            );

            bool canStrip = IndieEditMeshFaceOps.CanRing(ep.Data) && !previewBusy;
            GUI.enabled = canStrip;

            DrawActionWithSettings(
                "Ring",
                () => IndieEditMeshFaceOps.Ring(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Ring)
            );

            GUI.enabled = canStrip;

            DrawActionWithSettings(
                "Loop",
                () => IndieEditMeshFaceOps.Loop(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Loop)
            );

            GUI.enabled = canStrip;

            DrawActionWithSettings(
                "Row",
                () => IndieEditMeshFaceOps.Row(ep),
                () => IndieEditMeshFaceSettingsWindow.Open(ep, IndieEditMeshFaceSettingsKind.Row)
            );

            GUI.enabled = hasSelection && !previewBusy;

            if (GUILayout.Button("Flip", GUILayout.Height(24)))
                IndieEditMeshFaceOps.Flip(ep);

            GUI.enabled = true;

            EditorGUILayout.Space();
            DrawFlatBlock(ep);

            EditorGUILayout.Space();
            DrawPolygonSmoothingGroupsBlock(ep);
        }

        private static void DrawFlatBlock(IndieEditMeshComponent ep)
        {
            EditorGUILayout.LabelField("Flat", EditorStyles.boldLabel);

            bool previewBusy = IndieEditMeshPreviewOp.IsActive;
            int[] affected = ep.Data != null
                ? IndieEditMeshSelection.GetAffectedVertices(ep.Data, ep.subMode)
                : System.Array.Empty<int>();
            bool canFlat = !previewBusy && affected.Length >= 2;

            GUI.enabled = canFlat;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("X", GUILayout.Height(24)))
                IndieEditMeshOps.FlattenAxis(ep, 0);

            if (GUILayout.Button("Y", GUILayout.Height(24)))
                IndieEditMeshOps.FlattenAxis(ep, 1);

            if (GUILayout.Button("Z", GUILayout.Height(24)))
                IndieEditMeshOps.FlattenAxis(ep, 2);

            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
        }

        private static void DrawPolygonSmoothingGroupsBlock(IndieEditMeshComponent ep)
        {
            EditorGUILayout.LabelField("Polygon: Smoothing Groups", EditorStyles.boldLabel);

            bool hasSelection = ep.Data != null && ep.Data.selectedFaces.Count > 0;
            bool previewBusy = IndieEditMeshPreviewOp.IsActive;

            IndieEditMeshSmoothingGroups.GetSelectionMaskState(
                ep.Data,
                out int commonBits,
                out int mixedBits
            );

            const int columns = 8;
            float buttonWidth = (EditorGUIUtility.currentViewWidth - 40f) / columns;

            if (buttonWidth < 22f)
                buttonWidth = 22f;

            GUI.enabled = hasSelection && !previewBusy;

            for (int row = 0; row < 4; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    int group = row * columns + col + 1;
                    int bit = IndieEditMeshSmoothingGroups.BitForGroup(group);
                    bool isOn = (commonBits & bit) != 0;
                    bool isMixed = !isOn && (mixedBits & bit) != 0;

                    Color previous = GUI.backgroundColor;

                    if (isOn)
                        GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);
                    else if (isMixed)
                        GUI.backgroundColor = new Color(0.75f, 0.75f, 0.45f, 1f);

                    if (GUILayout.Button(
                            group.ToString(),
                            GUILayout.Width(buttonWidth),
                            GUILayout.Height(22f)
                        ))
                    {
                        IndieEditMeshSmoothingGroups.ToggleGroup(ep, group);
                    }

                    GUI.backgroundColor = previous;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !previewBusy;

            if (GUILayout.Button("Select By SG", GUILayout.Height(22f)))
                IndieEditMeshSelectBySGWindow.Open();

            GUI.enabled = hasSelection && !previewBusy;

            if (GUILayout.Button("Clear All", GUILayout.Height(22f)))
                IndieEditMeshSmoothingGroups.ClearAll(ep);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = hasSelection && !previewBusy;

            if (GUILayout.Button("Auto Smooth", GUILayout.Height(22f)))
                IndieEditMeshSmoothingGroups.AutoSmooth(ep);

            EditorGUI.BeginChangeCheck();
            float threshold = EditorGUILayout.FloatField(
                IndieEditMeshSmoothingGroups.AutoSmoothThreshold,
                GUILayout.Width(56f),
                GUILayout.Height(22f)
            );

            if (EditorGUI.EndChangeCheck())
                IndieEditMeshSmoothingGroups.AutoSmoothThreshold = threshold;

            EditorGUILayout.LabelField("Threshold", GUILayout.Width(64f));
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
        }

        private static void DrawActionWithSettings(
            string label,
            System.Action onAction,
            System.Action onSettings
        )
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(label, GUILayout.Height(24)))
                onAction?.Invoke();

            if (GUILayout.Button("⚙", GUILayout.Width(28), GUILayout.Height(24)))
                onSettings?.Invoke();

            EditorGUILayout.EndHorizontal();
        }
    }
}