using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// While Edit Mesh has a sub-object selection, Scene View orbit
    /// (Alt + left drag in 3D) circles that selection and sets WASD speed
    /// from its bounds. Pan, zoom and an empty selection keep Unity's own camera.
    /// </summary>
    public static class IndieEditMeshCameraOrbit
    {
        public static void Handle(IndieEditMeshComponent ep, SceneView sceneView, Event evt)
        {
            if (ep == null || sceneView == null || evt == null)
                return;

            if (evt.type != EventType.MouseDrag)
                return;

            if (Tools.viewTool != ViewTool.Orbit)
                return;

            if (sceneView.in2DMode || sceneView.isRotationLocked)
                return;

            if (!IndieEditMeshTransformTypeIn.HasSelection(ep))
                return;

            if (sceneView.camera == null)
                return;

            IndieEditMeshViewFrame.SnapPivotKeepingDistance(
                sceneView,
                IndieEditMeshTransformTypeIn.GetWorldPivot(ep),
                true);

            if (ep.Data == null)
                return;

            IndieEditMeshViewFrame.MatchFlySpeed(
                sceneView,
                ep.transform,
                ep.Data.vertices,
                IndieEditMeshSelection.GetAffectedVertices(ep.Data, ep.subMode));
        }
    }
}
