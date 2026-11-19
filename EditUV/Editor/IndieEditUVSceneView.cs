using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    public static class IndieEditUVSceneView
    {
        private static IndieEditUVComponent _cacheComponent;
        private static int _cacheMeshId;
        private static List<Vector3> _cacheEndpoints = new List<Vector3>();

        public static void Draw(IndieEditUVComponent uv, SceneView sceneView)
        {
            if (uv == null)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            Camera camera = sceneView != null ? sceneView.camera : null;

            if (uv.isEditing && uv.Data != null)
            {
                IndieEditUVSceneOverlay.DrawSession(uv, camera);
                DrawMarquee();
                IndieEditUVSeamTools.DrawOverlay(uv);
            }
            else
            {
                IndieEditUVSceneOverlay.DrawLegacySeams(uv, GetSeamEndpoints(uv), camera);
            }
        }

        public static void InvalidateCache(IndieEditUVComponent uv)
        {
            IndieEditUVSceneOverlay.MarkDirty();

            if (_cacheComponent == uv)
            {
                _cacheComponent = null;
                _cacheMeshId = 0;
                _cacheEndpoints = new List<Vector3>();
            }
        }

        private static void DrawMarquee()
        {
            if (!IndieEditUVSelection.IsMarqueeActive)
                return;

            Rect rect = IndieEditUVSelection.GetMarqueeGuiRect();
            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 1f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), new Color(0.25f, 0.65f, 1f, 0.95f));
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), new Color(0.25f, 0.65f, 1f, 0.95f));
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), new Color(0.25f, 0.65f, 1f, 0.95f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), new Color(0.25f, 0.65f, 1f, 0.95f));
            Handles.EndGUI();
        }

        private static List<Vector3> GetSeamEndpoints(IndieEditUVComponent uv)
        {
            var meshFilter = uv.GetMeshFilter();

            if (meshFilter == null || meshFilter.sharedMesh == null)
                return null;

            Mesh mesh = meshFilter.sharedMesh;
            int meshId = mesh.GetInstanceID();

            if (_cacheComponent == uv && _cacheMeshId == meshId && _cacheEndpoints != null)
                return _cacheEndpoints;

            _cacheComponent = uv;
            _cacheMeshId = meshId;
            _cacheEndpoints = IndieEditUVSeamUtil.CollectSeamLocalEndpoints(mesh);
            return _cacheEndpoints;
        }
    }
}
