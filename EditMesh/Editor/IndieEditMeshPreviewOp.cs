using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public enum IndieEditMeshPreviewKind
    {
        VertexExtrude,
        VertexWeld,
        VertexChamfer,
        EdgeExtrude,
        EdgeWeld,
        EdgeChamfer,
        EdgeBridge,
        EdgeConnect,
        EdgeRing,
        FaceExtrude,
        FaceOutline,
        FaceBevel,
        FaceInset,
        FaceBridge,
        FaceRing,
        FaceLoop,
        FaceRow
    }

    public static class IndieEditMeshPreviewOp
    {
        private static IndieEditMeshComponent _component;
        private static IndieEditableMeshData _baseline;
        private static IndieEditMeshPreviewKind _kind;
        private static bool _active;
        private static bool _useProxy;

        public static bool IsActive => _active;
        public static IndieEditMeshPreviewKind Kind => _kind;
        public static IndieEditMeshComponent Component => _component;

        public static bool Begin(IndieEditMeshComponent ep, IndieEditMeshPreviewKind kind)
        {
            if (ep == null || ep.Data == null || IndieEditMeshSession.Active != ep)
                return false;

            if (!HasRequiredSelection(ep, kind))
                return false;

            if (_active)
                Cancel();

            _component = ep;
            _kind = kind;
            _active = true;
            _useProxy = UsesProxy(kind) && IndieEditMeshEditProxy.Begin(ep, RegionSpan(kind));
            _baseline = _useProxy ? null : ep.Data.Clone();

            ApplyCurrentParams();
            return true;
        }

        public static bool BeginVertex(IndieEditMeshComponent ep, IndieEditMeshVertexSettingsKind kind)
        {
            return Begin(ep, ToPreviewKind(kind));
        }

        public static bool BeginEdge(IndieEditMeshComponent ep, IndieEditMeshEdgeSettingsKind kind)
        {
            return Begin(ep, ToPreviewKind(kind));
        }

        public static bool BeginFace(IndieEditMeshComponent ep, IndieEditMeshFaceSettingsKind kind)
        {
            return Begin(ep, ToPreviewKind(kind));
        }

        public static void RefreshPreview()
        {
            if (!HasState())
                return;

            ApplyCurrentParams();
        }

        public static void Accept()
        {
            if (!HasState())
                return;

            var ep = _component;

            if (_useProxy && !IndieEditMeshEditProxy.IsActiveFor(ep))
                ApplyCurrentParams();

            if (_useProxy)
            {
                IndieEditMeshUndo.Register(ep, GetActionName(_kind));
                IndieEditMeshEditProxy.Commit();
            }
            else
            {
                IndieEditMeshUndo.CommitBaseline(ep, _baseline, GetActionName(_kind));
            }

            ClearState();

            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();
        }

        public static void Cancel()
        {
            if (!HasState())
            {
                ClearState();
                return;
            }

            var ep = _component;

            if (_useProxy)
            {
                IndieEditMeshTransformTool.ResetDrag();
                IndieEditMeshEditProxy.Cancel(ep);
            }
            else
            {
                ep.SetData(_baseline.Clone());

                IndieEditMeshTransformTool.ResetDrag();
                IndieEditMeshSession.RefreshMesh(ep);
            }

            EditorUtility.SetDirty(ep);
            SceneView.RepaintAll();

            ClearState();
        }

        /// <summary>
        /// Outline slides neighbours that only meet the selection by position,
        /// so its proxy keeps two rings. Edge Chamfer rebuilds two rings around the edges.
        /// Other polygon ops stay on the shared-vertex piece.
        /// </summary>
        private static IndieEditMeshFaceRegionSpan RegionSpan(IndieEditMeshPreviewKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshPreviewKind.FaceOutline:
                    return IndieEditMeshFaceRegionSpan.OutlineDoubleBelt;
                case IndieEditMeshPreviewKind.EdgeChamfer:
                    return IndieEditMeshFaceRegionSpan.EdgeDoubleBelt;
                default:
                    return IndieEditMeshFaceRegionSpan.SharedVertex;
            }
        }

        /// <summary>
        /// Selection-local ops: they only change geometry around the selection,
        /// so the preview can run on <see cref="IndieEditMeshEditProxy"/>.
        /// </summary>
        private static bool UsesProxy(IndieEditMeshPreviewKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshPreviewKind.EdgeChamfer:
                case IndieEditMeshPreviewKind.FaceExtrude:
                case IndieEditMeshPreviewKind.FaceOutline:
                case IndieEditMeshPreviewKind.FaceBevel:
                case IndieEditMeshPreviewKind.FaceInset:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasState()
        {
            return _active && _component != null && (_useProxy || _baseline != null);
        }

        private static void ApplyCurrentParams()
        {
            if (_useProxy)
            {
                // Anything that rebuilt the full mesh dropped the proxy; ep.Data is untouched, so start over.
                if (!IndieEditMeshEditProxy.IsActiveFor(_component) &&
                    !IndieEditMeshEditProxy.Begin(_component, RegionSpan(_kind)))
                {
                    _useProxy = false;
                    _baseline = _component.Data.Clone();
                }
            }

            if (_useProxy)
            {
                IndieEditMeshEditProxy.RunOnLive(ApplyKind);
                IndieEditMeshEditProxy.RefreshMesh();
                SceneView.RepaintAll();
                return;
            }

            _component.SetData(_baseline.Clone());
            ApplyKind(_component.Data);

            IndieEditMeshSession.RefreshMesh(_component);
            EditorUtility.SetDirty(_component);
            SceneView.RepaintAll();
        }

        private static void ApplyKind(IndieEditableMeshData data)
        {
            switch (_kind)
            {
                case IndieEditMeshPreviewKind.VertexExtrude:
                    IndieEditMeshVertexOps.ApplyExtrude(
                        data,
                        IndieEditMeshVertexParams.ExtrudeHeight,
                        IndieEditMeshVertexParams.ExtrudeBaseWidth
                    );
                    break;

                case IndieEditMeshPreviewKind.VertexWeld:
                    IndieEditMeshVertexOps.ApplyWeld(
                        data,
                        IndieEditMeshVertexParams.WeldThreshold
                    );
                    break;

                case IndieEditMeshPreviewKind.VertexChamfer:
                    IndieEditMeshVertexOps.ApplyChamfer(
                        data,
                        IndieEditMeshVertexParams.ChamferAmount,
                        IndieEditMeshVertexParams.ChamferOpen
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeExtrude:
                    IndieEditMeshEdgeOps.ApplyExtrude(
                        data,
                        IndieEditMeshEdgeParams.ExtrudeHeight,
                        IndieEditMeshEdgeParams.ExtrudeBaseWidth
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeWeld:
                    IndieEditMeshEdgeOps.ApplyWeld(
                        data,
                        IndieEditMeshEdgeParams.WeldThreshold
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeChamfer:
                    IndieEditMeshEdgeOps.ApplyChamfer(
                        data,
                        IndieEditMeshEdgeParams.ChamferAmount,
                        IndieEditMeshEdgeParams.ChamferSegments,
                        IndieEditMeshEdgeParams.ChamferTension,
                        IndieEditMeshEdgeParams.ChamferInvert,
                        IndieEditMeshEdgeParams.ChamferOpen
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeBridge:
                    IndieEditMeshEdgeOps.ApplyBridge(
                        data,
                        IndieEditMeshEdgeParams.BridgeSegments,
                        IndieEditMeshEdgeParams.BridgeTwist,
                        IndieEditMeshEdgeParams.BridgeTaper,
                        IndieEditMeshEdgeParams.BridgeBias
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeConnect:
                    IndieEditMeshEdgeOps.ApplyConnect(
                        data,
                        IndieEditMeshEdgeParams.ConnectSegments,
                        IndieEditMeshEdgeParams.ConnectPinch,
                        IndieEditMeshEdgeParams.ConnectSlide
                    );
                    break;

                case IndieEditMeshPreviewKind.EdgeRing:
                    IndieEditMeshEdgeOps.ApplyRing(
                        data,
                        IndieEditMeshEdgeParams.RingAngle
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceExtrude:
                    IndieEditMeshFaceOps.ApplyExtrude(
                        data,
                        IndieEditMeshFaceParams.ExtrudeHeight,
                        IndieEditMeshFaceParams.ExtrudeType
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceOutline:
                    IndieEditMeshFaceOps.ApplyOutline(
                        data,
                        IndieEditMeshFaceParams.OutlineAmount
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceBevel:
                    IndieEditMeshFaceOps.ApplyBevel(
                        data,
                        IndieEditMeshFaceParams.BevelHeight,
                        IndieEditMeshFaceParams.BevelOutline,
                        IndieEditMeshFaceParams.BevelType
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceInset:
                    IndieEditMeshFaceOps.ApplyInset(
                        data,
                        IndieEditMeshFaceParams.InsetAmount,
                        IndieEditMeshFaceParams.InsetType
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceBridge:
                    IndieEditMeshFaceOps.ApplyBridge(
                        data,
                        IndieEditMeshFaceParams.BridgeSegments,
                        IndieEditMeshFaceParams.BridgeTwist,
                        IndieEditMeshFaceParams.BridgeTaper,
                        IndieEditMeshFaceParams.BridgeBias
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceRing:
                    IndieEditMeshFaceOps.ApplyRing(
                        data,
                        IndieEditMeshFaceParams.RingAngle
                    );
                    break;

                case IndieEditMeshPreviewKind.FaceLoop:
                    IndieEditMeshFaceOps.ApplyLoopSelectSpokes(data);
                    _component.subMode = IndieEditMeshSubMode.Polygon;
                    break;

                case IndieEditMeshPreviewKind.FaceRow:
                    IndieEditMeshFaceOps.ApplyRow(
                        data,
                        IndieEditMeshFaceParams.RowAngle
                    );
                    break;
            }
        }

        private static bool HasRequiredSelection(IndieEditMeshComponent ep, IndieEditMeshPreviewKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshPreviewKind.VertexExtrude:
                case IndieEditMeshPreviewKind.VertexWeld:
                case IndieEditMeshPreviewKind.VertexChamfer:
                    return ep.Data.selectedVertices.Count > 0;

                case IndieEditMeshPreviewKind.EdgeBridge:
                case IndieEditMeshPreviewKind.EdgeConnect:
                case IndieEditMeshPreviewKind.EdgeRing:
                    return ep.Data.selectedEdges.Count >= 2;

                case IndieEditMeshPreviewKind.EdgeExtrude:
                case IndieEditMeshPreviewKind.EdgeWeld:
                case IndieEditMeshPreviewKind.EdgeChamfer:
                    return ep.Data.selectedEdges.Count > 0;

                case IndieEditMeshPreviewKind.FaceBridge:
                    return ep.Data.selectedFaces.Count >= 2;

                case IndieEditMeshPreviewKind.FaceExtrude:
                case IndieEditMeshPreviewKind.FaceOutline:
                case IndieEditMeshPreviewKind.FaceBevel:
                case IndieEditMeshPreviewKind.FaceInset:
                    return ep.Data.selectedFaces.Count > 0;

                case IndieEditMeshPreviewKind.FaceRing:
                case IndieEditMeshPreviewKind.FaceLoop:
                case IndieEditMeshPreviewKind.FaceRow:
                    return IndieEditMeshFaceOps.CanRing(ep.Data);

                default:
                    return false;
            }
        }

        private static void ClearState()
        {
            _active = false;
            _component = null;
            _baseline = null;
            _useProxy = false;
        }

        private static IndieEditMeshPreviewKind ToPreviewKind(IndieEditMeshVertexSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshVertexSettingsKind.Extrude:
                    return IndieEditMeshPreviewKind.VertexExtrude;
                case IndieEditMeshVertexSettingsKind.Weld:
                    return IndieEditMeshPreviewKind.VertexWeld;
                default:
                    return IndieEditMeshPreviewKind.VertexChamfer;
            }
        }

        private static IndieEditMeshPreviewKind ToPreviewKind(IndieEditMeshEdgeSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshEdgeSettingsKind.Extrude:
                    return IndieEditMeshPreviewKind.EdgeExtrude;
                case IndieEditMeshEdgeSettingsKind.Weld:
                    return IndieEditMeshPreviewKind.EdgeWeld;
                case IndieEditMeshEdgeSettingsKind.Chamfer:
                    return IndieEditMeshPreviewKind.EdgeChamfer;
                case IndieEditMeshEdgeSettingsKind.Bridge:
                    return IndieEditMeshPreviewKind.EdgeBridge;
                case IndieEditMeshEdgeSettingsKind.Connect:
                    return IndieEditMeshPreviewKind.EdgeConnect;
                case IndieEditMeshEdgeSettingsKind.Ring:
                    return IndieEditMeshPreviewKind.EdgeRing;
                default:
                    return IndieEditMeshPreviewKind.EdgeConnect;
            }
        }

        private static IndieEditMeshPreviewKind ToPreviewKind(IndieEditMeshFaceSettingsKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshFaceSettingsKind.Extrude:
                    return IndieEditMeshPreviewKind.FaceExtrude;
                case IndieEditMeshFaceSettingsKind.Outline:
                    return IndieEditMeshPreviewKind.FaceOutline;
                case IndieEditMeshFaceSettingsKind.Bevel:
                    return IndieEditMeshPreviewKind.FaceBevel;
                case IndieEditMeshFaceSettingsKind.Inset:
                    return IndieEditMeshPreviewKind.FaceInset;
                case IndieEditMeshFaceSettingsKind.Ring:
                    return IndieEditMeshPreviewKind.FaceRing;
                case IndieEditMeshFaceSettingsKind.Loop:
                    return IndieEditMeshPreviewKind.FaceLoop;
                case IndieEditMeshFaceSettingsKind.Row:
                    return IndieEditMeshPreviewKind.FaceRow;
                default:
                    return IndieEditMeshPreviewKind.FaceBridge;
            }
        }

        private static string GetActionName(IndieEditMeshPreviewKind kind)
        {
            switch (kind)
            {
                case IndieEditMeshPreviewKind.VertexExtrude:
                    return "Extrude Vertices";
                case IndieEditMeshPreviewKind.VertexWeld:
                    return "Weld Vertices";
                case IndieEditMeshPreviewKind.VertexChamfer:
                    return "Chamfer Vertices";
                case IndieEditMeshPreviewKind.EdgeExtrude:
                    return "Extrude Edges";
                case IndieEditMeshPreviewKind.EdgeWeld:
                    return "Weld Edges";
                case IndieEditMeshPreviewKind.EdgeChamfer:
                    return "Chamfer Edges";
                case IndieEditMeshPreviewKind.EdgeBridge:
                    return "Bridge Edges";
                case IndieEditMeshPreviewKind.EdgeConnect:
                    return "Connect Edges";
                case IndieEditMeshPreviewKind.EdgeRing:
                    return "Ring Edges";
                case IndieEditMeshPreviewKind.FaceExtrude:
                    return "Extrude Polygons";
                case IndieEditMeshPreviewKind.FaceOutline:
                    return "Outline Polygons";
                case IndieEditMeshPreviewKind.FaceBevel:
                    return "Bevel Polygons";
                case IndieEditMeshPreviewKind.FaceInset:
                    return "Inset Polygons";
                case IndieEditMeshPreviewKind.FaceBridge:
                    return "Bridge Polygons";
                case IndieEditMeshPreviewKind.FaceRing:
                    return "Ring Polygons";
                case IndieEditMeshPreviewKind.FaceLoop:
                    return "Loop Polygons";
                case IndieEditMeshPreviewKind.FaceRow:
                    return "Row Polygons";
                default:
                    return "Edit Mesh";
            }
        }
    }
}
