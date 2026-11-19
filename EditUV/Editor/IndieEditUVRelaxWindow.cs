using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Non-modal Relax Tool dialog (3ds Max layout). Settings live in <see cref="IndieEditUVRelax"/>
    /// so a running Start Relax picks up changes on the next frame.
    /// </summary>
    public class IndieEditUVRelaxWindow : EditorWindow
    {
        private static readonly string[] MethodLabels =
        {
            "Relax By Polygon Angles",
            "Relax By Edge Angles"
        };

        private static IndieEditUVRelaxWindow _instance;

        private IndieEditUVComponent _component;

        public static void Open(IndieEditUVComponent uv)
        {
            if (uv == null)
                return;

            var window = GetWindow<IndieEditUVRelaxWindow>(true, "Relax Tool", true);
            window._component = uv;
            window.minSize = new Vector2(330f, 150f);
            window.maxSize = new Vector2(480f, 190f);
            window.Show();
        }

        public static void CloseIfOpen()
        {
            if (_instance != null)
                _instance.Close();
        }

        public static void RepaintIfOpen()
        {
            if (_instance != null)
                _instance.Repaint();
        }

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
                _instance = null;

            IndieEditUVRelax.Stop();
        }

        private void OnGUI()
        {
            Event evt = Event.current;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && IndieEditUVRelax.IsRunning)
            {
                IndieEditUVRelax.Stop();
                evt.Use();
            }

            if (IndieEditUVSession.Active != _component)
                _component = IndieEditUVSession.Active;

            if (_component == null)
            {
                EditorGUILayout.HelpBox("Edit UV session is not active.", MessageType.Info);
                return;
            }

            bool running = IndieEditUVRelax.IsRunning;
            bool continuous = running && IndieEditUVRelax.IsContinuous;

            GUILayout.Space(6f);
            IndieEditUVRelax.Method = (IndieEditUVRelaxMethod)EditorGUILayout.Popup(
                (int)IndieEditUVRelax.Method,
                MethodLabels,
                GUILayout.Width(190f)
            );
            GUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();
            DrawSettings();
            GUILayout.Space(10f);
            DrawButtons(running, continuous);
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(GetStatus(running, continuous), EditorStyles.miniLabel);
        }

        private static void DrawSettings()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 66f;

            EditorGUILayout.BeginVertical(GUILayout.Width(190f));

            IndieEditUVRelax.Iterations = Mathf.Clamp(
                EditorGUILayout.IntField("Iterations:", IndieEditUVRelax.Iterations),
                0,
                IndieEditUVRelax.MaxIterations
            );
            IndieEditUVRelax.Amount = EditorGUILayout.Slider("Amount:", IndieEditUVRelax.Amount, 0f, 1f);
            IndieEditUVRelax.Stretch = EditorGUILayout.Slider("Stretch:", IndieEditUVRelax.Stretch, 0f, 1f);

            GUILayout.Space(4f);
            IndieEditUVRelax.KeepBoundary = EditorGUILayout.ToggleLeft("Keep Boundary Points Fixed", IndieEditUVRelax.KeepBoundary);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ToggleLeft("Save Outer Corners", false);

            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private void DrawButtons(bool running, bool continuous)
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Space(14f);

            using (new EditorGUI.DisabledScope(running && !continuous))
            {
                if (GUILayout.Button(continuous ? "Stop Relax" : "Start Relax", GUILayout.Height(22f)))
                {
                    if (continuous)
                        IndieEditUVRelax.Stop();
                    else if (!IndieEditUVRelax.StartContinuous(_component))
                        ShowNothingToRelax();
                }
            }

            GUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(running))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(22f)) && !IndieEditUVRelax.StartApply(_component))
                    ShowNothingToRelax();
            }

            EditorGUILayout.EndVertical();
        }

        private void ShowNothingToRelax()
        {
            ShowNotification(new GUIContent(IndieEditUVPelt.IsActive ? "Close Pelt first" : "Nothing to relax"));
        }

        private static string GetStatus(bool running, bool continuous)
        {
            if (!running)
                return string.Empty;

            return continuous
                ? $"Relaxing... frame {IndieEditUVRelax.FramesDone}  (Esc to stop)"
                : $"Process frame {IndieEditUVRelax.FramesDone} / {IndieEditUVRelax.FramesTotal}  (Esc to abort)";
        }
    }
}
