using System.Collections.Generic;
using UnityEditor;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVNotify
    {
        public static void SelectionOrMeshChanged()
        {
            IndieEditUVSettingsWindow.MarkCanvasDirty();
            IndieEditUVSettingsWindow.RepaintIfOpen();
            SceneView.RepaintAll();
        }

        public static void SelectionChanged()
        {
            IndieEditUVSettingsWindow.MarkCanvasSelectionDirty();
            IndieEditUVSettingsWindow.RepaintIfOpen();
            SceneView.RepaintAll();
        }

        public static void SeamsChanged()
        {
            IndieEditUVSettingsWindow.MarkCanvasSeamsDirty();
            IndieEditUVSettingsWindow.RepaintIfOpen();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// UV gizmo/type-in drag: refresh dynamic neighborhood only.
        /// </summary>
        public static void UvDragChanged(IndieEditUVComponent uv)
        {
            UvsChanged(uv, uv != null && uv.Data != null ? IndieEditUVTransform.GetAffectedVertices(uv) : null);
        }

        /// <summary>UVs of <paramref name="affected"/> moved; canvas falls back to a full rebuild if they leave the neighborhood.</summary>
        public static void UvsChanged(IndieEditUVComponent uv, IEnumerable<int> affected)
        {
            if (uv == null || uv.Data == null || affected == null)
            {
                SelectionOrMeshChanged();
                return;
            }

            IndieEditUVSettingsWindow.MarkCanvasDynamicUvDirty(uv.Data, affected);
            IndieEditUVSettingsWindow.RepaintIfOpen();
            SceneView.RepaintAll();
        }
    }
}
