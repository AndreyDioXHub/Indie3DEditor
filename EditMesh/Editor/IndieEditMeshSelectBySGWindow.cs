using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public class IndieEditMeshSelectBySGWindow : EditorWindow
    {
        private bool _clearSelection = true;

        public static void Open()
        {
            var window = GetWindow<IndieEditMeshSelectBySGWindow>(true, "Select By SG", true);
            window.minSize = new Vector2(260f, 160f);
            window.maxSize = new Vector2(320f, 200f);
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Select By Smoothing Group", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            _clearSelection = EditorGUILayout.Toggle("Clear Selection", _clearSelection);
            EditorGUILayout.Space(6f);

            DrawGroupGrid();
        }

        private void DrawGroupGrid()
        {
            const int columns = 8;
            float buttonWidth = (position.width - 24f) / columns;

            if (buttonWidth < 22f)
                buttonWidth = 22f;

            for (int row = 0; row < 4; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    int group = row * columns + col + 1;

                    if (GUILayout.Button(
                            group.ToString(),
                            GUILayout.Width(buttonWidth),
                            GUILayout.Height(22f)
                        ))
                    {
                        Apply(group);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void Apply(int groupNumber)
        {
            var ep = IndieEditMeshSession.Active;

            if (ep == null || !ep.isEditing || ep.Data == null)
            {
                Close();
                return;
            }

            IndieEditMeshSmoothingGroups.SelectByGroup(ep, groupNumber, _clearSelection);
            Close();
        }
    }
}
