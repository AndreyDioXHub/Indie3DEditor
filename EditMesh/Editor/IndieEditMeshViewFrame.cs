using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Centers the Scene View on an edit target the same way Alt-orbit snaps
    /// the pivot, and scales WASD fly speed to that world bounds.
    /// </summary>
    public static class IndieEditMeshViewFrame
    {
        private const float DistanceEpsilon = 0.0001f;
        private const float RatioEpsilon = 0.001f;
        private const float DegenerateDiagonal = 1e-4f;
        private const float DegenerateObjectFraction = 0.05f;
        private const float MinimumFlySpeed = 0.01f;

        private static bool _speedCaptured;
        private static float _savedSpeed;
        private static float _savedSpeedMin;
        private static float _savedSpeedMax;
        private static SceneView _speedSceneView;

        public static bool TryWorldCentroid(
            Transform transform,
            IList<Vector3> vertices,
            int[] indices,
            out Vector3 worldCenter)
        {
            worldCenter = Vector3.zero;

            if (transform == null || vertices == null || indices == null || indices.Length == 0)
                return false;

            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];

                if (index < 0 || index >= vertices.Count)
                    continue;

                sum += vertices[index];
                count++;
            }

            if (count == 0)
                return false;

            worldCenter = transform.TransformPoint(sum / count);
            return true;
        }

        public static void SnapPivotKeepingDistance(
            SceneView sceneView,
            Vector3 worldCenter,
            bool adjustDistance)
        {
            if (sceneView == null)
                return;

            if (adjustDistance && !sceneView.in2DMode)
            {
                Camera camera = sceneView.camera;

                if (camera != null)
                {
                    float stick = Vector3.Distance(camera.transform.position, worldCenter);
                    float currentDistance = sceneView.cameraDistance;

                    if (stick > DistanceEpsilon && Mathf.Abs(currentDistance) > DistanceEpsilon)
                    {
                        float ratio = stick / Mathf.Abs(currentDistance);

                        if (Mathf.Abs(ratio - 1f) > RatioEpsilon)
                            sceneView.size *= ratio;
                    }
                }
            }

            if ((sceneView.pivot - worldCenter).sqrMagnitude > DistanceEpsilon * DistanceEpsilon)
                sceneView.pivot = worldCenter;
        }

        /// <summary>
        /// Pivot to the selection bounds, or the whole mesh when <paramref name="selected"/> is empty.
        /// Fly speed becomes the bounds diagonal until <see cref="RestoreFlySpeed"/>.
        /// </summary>
        public static void Focus(
            SceneView sceneView,
            Transform transform,
            IList<Vector3> vertices,
            int[] selected)
        {
            if (sceneView == null)
                sceneView = SceneView.lastActiveSceneView;

            if (sceneView == null || transform == null || vertices == null || vertices.Count == 0)
                return;

            bool useSelection = selected != null && selected.Length > 0;

            if (!TryWorldBounds(transform, vertices, useSelection ? selected : null, out Bounds bounds))
                return;

            SnapPivotKeepingDistance(sceneView, bounds.center, !sceneView.in2DMode);
            ApplyFlySpeed(sceneView, FlySpeedFor(transform, vertices, bounds));
            sceneView.Repaint();
        }

        /// <summary>
        /// WASD speed for an Alt-orbit. Uses the selection bounds, or the whole mesh
        /// when <paramref name="selected"/> is empty. The first call in a session
        /// remembers the previous speed for <see cref="RestoreFlySpeed"/>.
        /// </summary>
        public static void MatchFlySpeed(
            SceneView sceneView,
            Transform transform,
            IList<Vector3> vertices,
            int[] selected)
        {
            if (sceneView == null || transform == null || vertices == null || vertices.Count == 0)
                return;

            bool useSelection = selected != null && selected.Length > 0;

            if (!TryWorldBounds(transform, vertices, useSelection ? selected : null, out Bounds bounds))
                return;

            ApplyFlySpeed(sceneView, FlySpeedFor(transform, vertices, bounds));
        }

        public static void RestoreFlySpeed()
        {
            if (!_speedCaptured)
                return;

            SceneView sceneView = _speedSceneView != null
                ? _speedSceneView
                : SceneView.lastActiveSceneView;

            _speedCaptured = false;
            _speedSceneView = null;

            if (sceneView == null)
                return;

            SceneView.CameraSettings settings = sceneView.cameraSettings;
            float speed = _savedSpeed;
            float speedMin = _savedSpeedMin;
            float speedMax = _savedSpeedMax;

            if (speedMin > speedMax)
            {
                float swap = speedMin;
                speedMin = speedMax;
                speedMax = swap;
            }

            settings.speedMin = Mathf.Min(settings.speedMin, speedMin);
            settings.speedMax = Mathf.Max(settings.speedMax, speedMax);
            settings.speed = Mathf.Clamp(speed, settings.speedMin, settings.speedMax);
            settings.speedMin = speedMin;
            settings.speedMax = speedMax;
            settings.speed = Mathf.Clamp(speed, speedMin, speedMax);
        }

        private static float FlySpeedFor(
            Transform transform,
            IList<Vector3> vertices,
            Bounds framed)
        {
            float diagonal = framed.size.magnitude;

            if (diagonal >= DegenerateDiagonal)
                return diagonal;

            if (TryWorldBounds(transform, vertices, null, out Bounds whole))
                diagonal = whole.size.magnitude * DegenerateObjectFraction;

            return Mathf.Max(diagonal, MinimumFlySpeed);
        }

        private static void ApplyFlySpeed(SceneView sceneView, float speed)
        {
            SceneView.CameraSettings settings = sceneView.cameraSettings;

            if (!_speedCaptured)
            {
                _savedSpeed = settings.speed;
                _savedSpeedMin = settings.speedMin;
                _savedSpeedMax = settings.speedMax;
                _speedSceneView = sceneView;
                _speedCaptured = true;
            }

            if (speed < settings.speedMin)
                settings.speedMin = speed;

            if (speed > settings.speedMax)
                settings.speedMax = speed;

            settings.speed = speed;
        }

        /// <summary>
        /// World AABB. A null <paramref name="indices"/> list uses every vertex.
        /// </summary>
        private static bool TryWorldBounds(
            Transform transform,
            IList<Vector3> vertices,
            int[] indices,
            out Bounds bounds)
        {
            bounds = new Bounds();

            if (transform == null || vertices == null || vertices.Count == 0)
                return false;

            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            int count = indices != null ? indices.Length : vertices.Count;

            for (int i = 0; i < count; i++)
            {
                int index = indices != null ? indices[i] : i;

                if (index < 0 || index >= vertices.Count)
                    continue;

                Vector3 world = transform.TransformPoint(vertices[index]);

                if (!any)
                {
                    min = world;
                    max = world;
                    any = true;
                    continue;
                }

                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            if (!any)
                return false;

            bounds.SetMinMax(min, max);
            return true;
        }
    }
}
