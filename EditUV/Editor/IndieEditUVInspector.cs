using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    [CustomEditor(typeof(IndieEditUVComponent))]
    public class IndieEditUVInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            var uv = target as IndieEditUVComponent;

            if (uv == null)
                return;

            if (!uv.isEditing)
            {
                EditorGUILayout.LabelField("Edit UV", EditorStyles.boldLabel);

                var meshFilter = uv.GetMeshFilter();

                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    EditorGUILayout.HelpBox(
                        "This object needs a MeshFilter with a mesh.",
                        MessageType.Warning
                    );
                }

                GUI.enabled = meshFilter != null && meshFilter.sharedMesh != null;

                if (GUILayout.Button("Edit UV", GUILayout.Height(30)))
                {
                    IndieEditUVSession.BeginEditing(uv);
                    Repaint();
                }

                GUI.enabled = true;
                return;
            }

            if (IndieEditUVSession.Active != uv || uv.Data == null)
            {
                EditorGUILayout.HelpBox(
                    "Edit mode state was lost. This can happen after script recompilation.",
                    MessageType.Warning
                );

                if (GUILayout.Button("Force Exit Edit Mode", GUILayout.Height(25)))
                {
                    uv.isEditing = false;
                    uv.SetData(null);
                    IndieEditUVMaterialUtil.RestoreMaterials(uv);
                    Tools.hidden = false;
                    IndieEditUVUndo.Clear();
                    IndieEditUVGizmo.Reset();
                    IndieEditUVSettingsWindow.CloseIfOpen();
                    EditorUtility.SetDirty(uv);
                    SceneView.RepaintAll();
                    Repaint();
                }

                return;
            }

            // When Inspector has focus, still route Undo/Redo to our stack.
            if (IndieEditUVHotkeys.TryHandle(uv, Event.current))
            {
                Repaint();
                return;
            }

            EditorGUILayout.LabelField("Edit UV", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            IndieEditMeshSubMode previous = uv.subMode;

            uv.subMode = (IndieEditMeshSubMode)GUILayout.Toolbar(
                (int)uv.subMode,
                new[]
                {
                    "Vertex",
                    "Edge",
                    "Polygon"
                }
            );

            if (uv.subMode != previous)
            {
                uv.Data.ClearSelection();
                IndieEditUVGizmo.Reset();
                IndieEditUVNotify.SelectionOrMeshChanged();
            }

            EditorGUILayout.Space();

            Color togglePrev = GUI.backgroundColor;

            if (IndieEditUVSelection.IgnoreBackFaces)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(
                    IndieEditUVSelection.IgnoreBackFaces
                        ? "Ignore Back Faces (On)"
                        : "Ignore Back Faces",
                    GUILayout.Height(24)
                ))
            {
                IndieEditUVSelection.IgnoreBackFaces = !IndieEditUVSelection.IgnoreBackFaces;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = togglePrev;

            EditorGUILayout.Space();
            DrawSeamsBlock(uv);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = IndieEditUVUndo.UndoCount > 0;

            if (GUILayout.Button($"Undo ({IndieEditUVUndo.UndoCount})", GUILayout.Height(22)))
            {
                IndieEditUVUndo.Undo(uv);
                Repaint();
            }

            GUI.enabled = IndieEditUVUndo.RedoCount > 0;

            if (GUILayout.Button($"Redo ({IndieEditUVUndo.RedoCount})", GUILayout.Height(22)))
            {
                IndieEditUVUndo.Redo(uv);
                Repaint();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button("Clear Selection", GUILayout.Height(22)))
            {
                if (IndieEditUVSeamState.HasActiveSeamTool)
                {
                    IndieEditUVSeamState.ClearWorking();
                    SceneView.RepaintAll();
                    Repaint();
                }
                else
                {
                    IndieEditUVUndo.Register(uv, "Clear Selection");
                    uv.Data.ClearSelection();
                    IndieEditUVGizmo.Reset();
                    IndieEditUVNotify.SelectionOrMeshChanged();
                    Repaint();
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Exit Edit UV", GUILayout.Height(30)))
            {
                IndieEditUVSession.EndEditing(uv, true);
                Repaint();
            }

            if (GUILayout.Button("Cancel Edit UV", GUILayout.Height(22)))
            {
                IndieEditUVSession.EndEditing(uv, false);
                Repaint();
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Ctrl + Z = Undo\nCtrl + Y = Redo\nCtrl + A = Select All\nShift/Ctrl + Click = Add/Remove\nDrag = Marquee\nIgnore Back Faces: only front-facing picks\nUV window: move / rotate ring / corner scale\nShift while moving = one axis (sideways keeps U, up/down keeps V)\nAlt = uniform scale · Shift+Alt = axis scale\nSeams: Edit Seams / Point-to-Point · Ctrl+Click removes working seams",
                MessageType.Info
            );

            if (GUI.changed)
                EditorUtility.SetDirty(uv);
        }

        private static void DrawSeamsBlock(IndieEditUVComponent uv)
        {
            EditorGUILayout.LabelField("Seams", EditorStyles.boldLabel);

            Color prev = GUI.backgroundColor;

            DrawSeamModeToggle(
                "Edit Seams",
                IndieEditUVSeamMode.EditSeams,
                new Color(0.35f, 0.85f, 1f, 1f)
            );

            DrawSeamModeToggle(
                "Point-to-Point Seams",
                IndieEditUVSeamMode.PointToPoint,
                new Color(1f, 0.7f, 0.35f, 1f)
            );

            GUI.backgroundColor = prev;

            EditorGUILayout.Space(4f);

            bool canConvert =
                uv.subMode == IndieEditMeshSubMode.Edge &&
                uv.Data.selectedEdges.Count > 0;

            GUI.enabled = canConvert;

            if (GUILayout.Button("Convert Selected Edges to Seams", GUILayout.Height(22)))
            {
                IndieEditUVSeamOps.ConvertSelectedEdgesToSeams(uv);
                SceneView.RepaintAll();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Expand Polygon Selection to Seams", GUILayout.Height(22)))
            {
                IndieEditUVSeamOps.ExpandPolygonSelectionToSeams(uv);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Pelt Map…", GUILayout.Height(26)))
            {
                IndieEditUVPeltWindow.Open(uv);
            }

            if (IndieEditUVSeamState.WorkingSeams.Count > 0)
            {
                EditorGUILayout.LabelField(
                    $"Working seams: {IndieEditUVSeamState.WorkingSeams.Count}",
                    EditorStyles.miniLabel
                );

                if (GUILayout.Button("Clear Working Seams", GUILayout.Height(20)))
                {
                    IndieEditUVSeamState.ClearWorking();
                    SceneView.RepaintAll();
                }
            }
        }

        private static void DrawSeamModeToggle(
            string label,
            IndieEditUVSeamMode mode,
            Color activeColor
        )
        {
            Color prev = GUI.backgroundColor;
            bool on = IndieEditUVSeamState.Mode == mode;

            if (on)
                GUI.backgroundColor = activeColor;

            if (GUILayout.Button(on ? $"{label} (On)" : label, GUILayout.Height(24)))
            {
                IndieEditUVSeamState.SetMode(mode);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = prev;
        }
    }
}
