using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    public static class IndieRetopologyDrag
    {
        private static bool _dragging;
        private static bool _grow;
        private static int _index = -1;
        private static int _magnet = -1;
        private static IndieRetopologyMeshData _baseline;
        private static readonly List<int> Cluster = new List<int>();
        private static readonly HashSet<int> ClusterSkip = new HashSet<int>();

        public static bool IsDragging => _dragging;
        public static int MagnetVertex => _magnet;

        public static void Reset()
        {
            ClearSlide();
            IndieRetopologyDragProxy.End(false, null);
        }

        public static bool HandleMouseDown(IndieRetopologyComponent retopo, SceneView sceneView, Event evt)
        {
            Reset();

            if (retopo == null || retopo.Data == null || sceneView == null || evt == null)
                return false;

            retopo.EnsureData();

            bool ctrl = evt.control || evt.command;
            bool shift = evt.shift && !ctrl;
            Camera camera = sceneView.camera;
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);

            if (IndieRetopologyCreatePolygon.IsActive)
                return PlaceCreatePoint(retopo, camera, ray);

            if (ctrl)
                return ToggleEdge(retopo, sceneView, evt.mousePosition);

            if (shift && retopo.HasFaces)
                return GrowFromEdge(retopo, sceneView, camera, ray, evt.mousePosition);

            IndieRetopologyHit hit = IndieRetopologyPick.PickVertex(retopo, sceneView, evt.mousePosition);

            if (hit.valid)
            {
                BeginSlide(retopo, hit.a);
                return _dragging;
            }

            if (retopo.HasFaces)
                return false;

            if (!IndieRetopologyPick.TrySurfaceLocal(retopo, ray, camera, out Vector3 localPoint))
                return false;

            _baseline = retopo.Data.Clone();
            int index = retopo.Data.vertices.Count;
            retopo.Data.vertices.Add(localPoint);
            retopo.Data.MarkChanged();
            TryAddFirstFace(retopo, camera);
            BeginSlide(retopo, index);
            EditorUtility.SetDirty(retopo);
            return _dragging;
        }

        public static void HandleMouseDrag(IndieRetopologyComponent retopo, SceneView sceneView, Event evt)
        {
            if (!_dragging || retopo == null || retopo.Data == null || sceneView == null || evt == null)
                return;

            if (_index < 0 || _index >= retopo.Data.vertices.Count)
                return;

            Camera camera = sceneView.camera;
            Vector2 mousePosition = evt.mousePosition;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            if (IndieRetopologyPick.TrySurfaceLocal(retopo, ray, camera, out Vector3 localPoint))
            {
                Vector3 previous = retopo.Data.vertices[_index];
                retopo.Data.vertices[_index] = localPoint;
                Vector3 delta = localPoint - previous;
                MoveCluster(retopo.Data.vertices, delta);
            }

            bool snap = !IndieRetopologyCreatePolygon.IsActive &&
                (_grow || evt.control || evt.command);
            UpdateMagnet(retopo, sceneView, snap);
            OrientCluster(retopo, camera);
            IndieRetopologySession.RefreshPreviewPositions(retopo, IndieRetopologyDragProxy.MovedFaces);
        }

        public static void RefreshMagnet(IndieRetopologyComponent retopo, SceneView sceneView, bool ctrl)
        {
            if (!_dragging || retopo == null || sceneView == null)
                return;

            if (IndieRetopologyCreatePolygon.IsActive)
            {
                _magnet = -1;
                return;
            }

            UpdateMagnet(retopo, sceneView, _grow || ctrl);
        }

        private static void WeldMagnet(IndieRetopologyComponent retopo, SceneView sceneView)
        {
            if (_magnet < 0)
                UpdateMagnet(retopo, sceneView, true);

            if (_magnet < 0 || ClusterSkip.Contains(_magnet))
                return;

            int survivor = IndieRetopologyWeld.MergeCluster(retopo.Data, Cluster, _magnet);

            if (survivor < 0)
                return;

            IndieRetopologyDragProxy.End(true, retopo);
            IndieRetopologyUndo.CommitBaseline(retopo, _baseline);
            EditorUtility.SetDirty(retopo);
            ClearSlide();
        }

        public static bool TryDeleteVertex(IndieRetopologyComponent retopo, SceneView sceneView, Vector2 mousePosition)
        {
            if (retopo == null || retopo.Data == null || sceneView == null)
                return false;

            retopo.EnsureData();

            IndieRetopologyHit hit = IndieRetopologyPick.PickVertex(retopo, sceneView, mousePosition);

            if (!hit.valid)
                return false;

            if (_dragging)
                HandleMouseUp(retopo);

            IndieRetopologyMeshData baseline = retopo.Data.Clone();
            var cluster = new List<int>();
            IndieRetopologyWeld.CollectCoincident(retopo.Data, hit.a, cluster);

            if (!IndieRetopologyWeld.DeleteCluster(retopo.Data, cluster))
                return false;

            IndieRetopologyPick.ClearHover();
            IndieRetopologySession.RefreshMesh(retopo);
            IndieRetopologyUndo.CommitBaseline(retopo, baseline);
            EditorUtility.SetDirty(retopo);
            return true;
        }

        public static void HandleMouseUp(IndieRetopologyComponent retopo, SceneView sceneView = null, bool ctrl = false)
        {
            if (_dragging && retopo != null)
            {
                bool create = IndieRetopologyCreatePolygon.IsActive;

                if (!create && (_grow || ctrl) && sceneView != null)
                    WeldMagnet(retopo, sceneView);

                if (_dragging && retopo.Data != null)
                {
                    retopo.Data.MarkChanged();

                    if (create)
                    {
                        Camera camera = sceneView != null ? sceneView.camera : null;
                        IndieRetopologyCreatePolygon.OnPointReleased(retopo, camera);
                    }
                    else
                    {
                        IndieRetopologyUndo.CommitBaseline(retopo, _baseline);
                    }

                    EditorUtility.SetDirty(retopo);
                }
            }

            bool refresh = _dragging && retopo != null;
            ClearSlide();
            IndieRetopologyDragProxy.End(refresh, retopo);
        }

        private static void ClearSlide()
        {
            _dragging = false;
            _grow = false;
            _index = -1;
            _magnet = -1;
            _baseline = null;
            Cluster.Clear();
            ClusterSkip.Clear();
        }

        private static bool ToggleEdge(IndieRetopologyComponent retopo, SceneView sceneView, Vector2 mousePosition)
        {
            IndieRetopologyHit edge = IndieRetopologyPick.PickFrontEdge(retopo, sceneView, mousePosition);

            if (!edge.valid)
                return ClearEdgeSelection(retopo);

            _baseline = retopo.Data.Clone();
            retopo.Data.ToggleEdge(edge.a, edge.b);
            IndieRetopologyUndo.CommitBaseline(retopo, _baseline);
            EditorUtility.SetDirty(retopo);
            _baseline = null;
            return true;
        }

        private static bool ClearEdgeSelection(IndieRetopologyComponent retopo)
        {
            if (retopo.Data.selectedEdges == null || retopo.Data.selectedEdges.Count == 0)
                return false;

            _baseline = retopo.Data.Clone();
            retopo.Data.selectedEdges.Clear();
            IndieRetopologyUndo.CommitBaseline(retopo, _baseline);
            EditorUtility.SetDirty(retopo);
            _baseline = null;
            return true;
        }

        private static bool GrowFromEdge(
            IndieRetopologyComponent retopo,
            SceneView sceneView,
            Camera camera,
            Ray ray,
            Vector2 mousePosition)
        {
            IndieRetopologyHit edge = IndieRetopologyPick.PickFrontEdge(retopo, sceneView, mousePosition);

            if (!edge.valid)
                return false;

            if (!IndieRetopologyPick.TrySurfaceLocal(retopo, ray, camera, out Vector3 localPoint))
                return false;

            _baseline = retopo.Data.Clone();
            int index = retopo.Data.vertices.Count;
            retopo.Data.vertices.Add(localPoint);
            retopo.Data.MarkChanged();
            AddFace(retopo, camera, edge.a, edge.b, index);
            BeginSlide(retopo, index, edge.a, edge.b);
            _grow = _dragging;
            UpdateMagnet(retopo, sceneView, true);
            EditorUtility.SetDirty(retopo);
            return _dragging;
        }

        private static void BeginSlide(IndieRetopologyComponent retopo, int index, int edgeA = -1, int edgeB = -1)
        {
            if (index < 0 || index >= retopo.Data.vertices.Count)
                return;

            if (_baseline == null)
                _baseline = retopo.Data.Clone();

            _dragging = true;
            _index = index;
            CaptureCluster(retopo);
            IndieRetopologyPick.ClearHover();
            IndieRetopologyDragProxy.Begin(retopo, Cluster, edgeA, edgeB);
        }

        private static void CaptureCluster(IndieRetopologyComponent retopo)
        {
            Cluster.Clear();
            ClusterSkip.Clear();
            IndieRetopologyWeld.CollectCoincident(retopo.Data, _index, Cluster);

            for (int i = 0; i < Cluster.Count; i++)
                ClusterSkip.Add(Cluster[i]);

            if (ClusterSkip.Count == 0)
            {
                Cluster.Add(_index);
                ClusterSkip.Add(_index);
            }
        }

        private static void MoveCluster(List<Vector3> vertices, Vector3 delta)
        {
            if (delta.sqrMagnitude <= 0f)
                return;

            for (int i = 0; i < Cluster.Count; i++)
            {
                int index = Cluster[i];

                if (index == _index || index < 0 || index >= vertices.Count)
                    continue;

                vertices[index] += delta;
            }
        }

        private static void OrientCluster(IndieRetopologyComponent retopo, Camera camera)
        {
            for (int i = 0; i < Cluster.Count; i++)
                IndieRetopologyDragProxy.OrientVertex(retopo, camera, Cluster[i]);
        }

        private static void UpdateMagnet(IndieRetopologyComponent retopo, SceneView sceneView, bool snap)
        {
            _magnet = -1;

            if (!snap || sceneView == null || retopo.Data == null || retopo.Data.vertices == null)
                return;

            if (_index < 0 || _index >= retopo.Data.vertices.Count)
                return;

            Vector2 point = HandleUtility.WorldToGUIPoint(
                retopo.transform.TransformPoint(retopo.Data.vertices[_index]));

            IndieRetopologyHit hit = IndieRetopologyPick.PickVertex(
                retopo,
                sceneView,
                point,
                _index,
                ClusterSkip);

            if (hit.valid)
                _magnet = hit.a;
        }

        public static void AddOrientedFace(IndieRetopologyComponent retopo, Camera camera, int a, int b, int c)
        {
            if (retopo == null || retopo.Data == null || retopo.Data.vertices == null)
                return;

            int count = retopo.Data.vertices.Count;

            if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
                return;

            AddFace(retopo, camera, a, b, c);
        }

        private static bool PlaceCreatePoint(IndieRetopologyComponent retopo, Camera camera, Ray ray)
        {
            if (IndieRetopologyCreatePolygon.PointCount >= 3)
                return false;

            if (!IndieRetopologyPick.TrySurfaceLocal(retopo, ray, camera, out Vector3 localPoint))
                return false;

            IndieRetopologyCreatePolygon.CaptureBaseline(retopo);
            int index = retopo.Data.vertices.Count;
            retopo.Data.vertices.Add(localPoint);
            retopo.Data.MarkChanged();
            IndieRetopologyCreatePolygon.TrackPoint(index);
            BeginSlide(retopo, index);
            EditorUtility.SetDirty(retopo);
            return _dragging;
        }

        private static void TryAddFirstFace(IndieRetopologyComponent retopo, Camera camera)
        {
            var data = retopo.Data;

            if (data.vertices.Count != 3 || data.faces.Count != 0)
                return;

            AddFace(retopo, camera, 0, 1, 2);
        }

        private static void AddFace(IndieRetopologyComponent retopo, Camera camera, int a, int b, int c)
        {
            Vector3 wa = retopo.transform.TransformPoint(retopo.Data.vertices[a]);
            Vector3 wb = retopo.transform.TransformPoint(retopo.Data.vertices[b]);
            Vector3 wc = retopo.transform.TransformPoint(retopo.Data.vertices[c]);

            if (camera != null && !IndieEditMeshPickMath.IsFrontFacingTriangle(camera, wa, wb, wc))
            {
                int swap = b;
                b = c;
                c = swap;
            }

            retopo.Data.faces.Add(new IndieRetopologyFace
            {
                a = a,
                b = b,
                c = c
            });
        }
    }
}
