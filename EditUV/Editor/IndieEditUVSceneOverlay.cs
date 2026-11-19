using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Scene View overlay (seams + selection) as cached GPU meshes.
    /// Meshes are rebuilt only when topology, seams or selection change;
    /// every repaint is a handful of DrawMeshNow calls.
    /// </summary>
    [InitializeOnLoad]
    public static class IndieEditUVSceneOverlay
    {
        private const string ShaderName = "Hidden/Indie3DEditor/EditUVSceneOverlay";
        private const int PassFill = 0;
        private const int PassLines = 1;
        private const int PassPoints = 2;

        private const float BakedSeamThickness = 3f;
        private const float WorkingSeamThickness = 5f;
        private const float LegacySeamThickness = 1f;
        private const float SelectFaceEdgeThickness = 3f;
        private const float SelectEdgeThickness = 4f;
        private const float SelectVertSize = 12f;

        private static readonly Color BakedSeamColor = new Color(0f, 1f, 0f, 0.55f);
        private static readonly Color WorkingSeamColor = new Color(0.15f, 0.85f, 1f, 0.95f);
        private static readonly Color SelectFaceFill = new Color(1f, 0.2f, 0.15f, 0.45f);
        private static readonly Color SelectFaceEdge = new Color(1f, 0.35f, 0.2f, 1f);
        private static readonly Color SelectEdgeColor = new Color(1f, 0.55f, 0.15f, 1f);
        private static readonly Color SelectVertColor = new Color(1f, 0.9f, 0.2f, 1f);

        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int ViewportId = Shader.PropertyToID("_ViewportPx");

        private static readonly IndieEditUVOverlayBuffer Buffer = new IndieEditUVOverlayBuffer();

        private static Material _material;

        private static Mesh _bakedSeams;
        private static Mesh _workingSeams;
        private static Mesh _selectFill;
        private static Mesh _selectLines;
        private static Mesh _selectPoints;
        private static Mesh _legacySeams;

        private static bool _dirty = true;
        private static IndieEditableMeshData _boundData;
        private static int _boundVertexCount;
        private static int _boundFaceCount;
        private static int _boundEdgeCount;
        private static int _seamVersion = -1;
        private static int _selectionSignature;
        private static List<Vector3> _legacyEndpoints;

        static IndieEditUVSceneOverlay()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
        }

        /// <summary>Topology / data swap: rebuild every layer on next draw.</summary>
        public static void MarkDirty()
        {
            _dirty = true;
            _legacyEndpoints = null;
        }

        public static void DrawSession(IndieEditUVComponent uv, Camera camera)
        {
            var data = uv.Data;

            if (data == null)
                return;

            bool topologyChanged =
                _dirty ||
                data != _boundData ||
                data.vertices.Count != _boundVertexCount ||
                data.faces.Count != _boundFaceCount ||
                data.edges.Count != _boundEdgeCount;

            if (topologyChanged)
            {
                _dirty = false;
                _boundData = data;
                _boundVertexCount = data.vertices.Count;
                _boundFaceCount = data.faces.Count;
                _boundEdgeCount = data.edges.Count;
                _seamVersion = -1;
                _selectionSignature = 0;
                RebuildSelection(data);
            }
            else
            {
                int signature = ComputeSelectionSignature(data);

                if (signature != _selectionSignature)
                    RebuildSelection(data);
            }

            if (_seamVersion != IndieEditUVSeamState.Version)
                RebuildSeams(data);

            if (!BeginDraw(camera))
                return;

            Matrix4x4 matrix = uv.transform.localToWorldMatrix;
            DrawMesh(_selectFill, PassFill, matrix, CompareFunction.Always);
            DrawMesh(_selectLines, PassLines, matrix, CompareFunction.LessEqual);
            DrawMesh(_selectPoints, PassPoints, matrix, CompareFunction.LessEqual);
            DrawMesh(_bakedSeams, PassLines, matrix, CompareFunction.LessEqual);
            DrawMesh(_workingSeams, PassLines, matrix, CompareFunction.LessEqual);
        }

        /// <summary>Seams of a non-edited object; rebuilt when the endpoint list instance changes.</summary>
        public static void DrawLegacySeams(IndieEditUVComponent uv, List<Vector3> endpoints, Camera camera)
        {
            if (endpoints == null || endpoints.Count < 2)
                return;

            if (!ReferenceEquals(endpoints, _legacyEndpoints) || _legacySeams == null)
            {
                _legacyEndpoints = endpoints;
                Buffer.Clear();

                for (int i = 0; i + 1 < endpoints.Count; i += 2)
                    IndieEditUVOverlayGeometry.AppendSegment(Buffer, endpoints[i], endpoints[i + 1], LegacySeamThickness, BakedSeamColor);

                _legacySeams = IndieEditUVOverlayGeometry.Apply(Buffer, _legacySeams, "EditUV Legacy Seams");
            }

            if (!BeginDraw(camera))
                return;

            DrawMesh(_legacySeams, PassLines, uv.transform.localToWorldMatrix, CompareFunction.LessEqual);
        }

        private static void RebuildSeams(IndieEditableMeshData data)
        {
            _seamVersion = IndieEditUVSeamState.Version;
            _bakedSeams = BuildSeamMesh(data, IndieEditUVSeamState.BakedSeams, BakedSeamThickness, BakedSeamColor, _bakedSeams, "EditUV Baked Seams");
            _workingSeams = BuildSeamMesh(data, IndieEditUVSeamState.WorkingSeams, WorkingSeamThickness, WorkingSeamColor, _workingSeams, "EditUV Working Seams");
        }

        private static Mesh BuildSeamMesh(
            IndieEditableMeshData data,
            IReadOnlyCollection<long> keys,
            float thickness,
            Color color,
            Mesh mesh,
            string name
        )
        {
            Buffer.Clear();

            foreach (long key in keys)
            {
                if (!IndieEditUVSeamState.TryGetEdgeEndpoints(data, key, out int v0, out int v1))
                    continue;

                IndieEditUVOverlayGeometry.AppendSegment(Buffer, data.vertices[v0], data.vertices[v1], thickness, color);
            }

            return IndieEditUVOverlayGeometry.Apply(Buffer, mesh, name);
        }

        private static void RebuildSelection(IndieEditableMeshData data)
        {
            _selectionSignature = ComputeSelectionSignature(data);
            int vertexCount = data.vertices.Count;

            Buffer.Clear();

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                if (!TryGetFace(data, data.selectedFaces[i], vertexCount, out var face))
                    continue;

                IndieEditUVOverlayGeometry.AppendTriangle(
                    Buffer,
                    data.vertices[face.a],
                    data.vertices[face.b],
                    data.vertices[face.c],
                    SelectFaceFill
                );
            }

            _selectFill = IndieEditUVOverlayGeometry.Apply(Buffer, _selectFill, "EditUV Select Fill");

            Buffer.Clear();

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                if (!TryGetFace(data, data.selectedFaces[i], vertexCount, out var face))
                    continue;

                Vector3 a = data.vertices[face.a];
                Vector3 b = data.vertices[face.b];
                Vector3 c = data.vertices[face.c];
                IndieEditUVOverlayGeometry.AppendSegment(Buffer, a, b, SelectFaceEdgeThickness, SelectFaceEdge);
                IndieEditUVOverlayGeometry.AppendSegment(Buffer, b, c, SelectFaceEdgeThickness, SelectFaceEdge);
                IndieEditUVOverlayGeometry.AppendSegment(Buffer, c, a, SelectFaceEdgeThickness, SelectFaceEdge);
            }

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                int edgeIndex = data.selectedEdges[i];

                if (edgeIndex < 0 || edgeIndex >= data.edges.Count)
                    continue;

                var edge = data.edges[edgeIndex];

                if (!IsValid(edge.v0, vertexCount) || !IsValid(edge.v1, vertexCount))
                    continue;

                IndieEditUVOverlayGeometry.AppendSegment(
                    Buffer,
                    data.vertices[edge.v0],
                    data.vertices[edge.v1],
                    SelectEdgeThickness,
                    SelectEdgeColor
                );
            }

            _selectLines = IndieEditUVOverlayGeometry.Apply(Buffer, _selectLines, "EditUV Select Lines");

            Buffer.Clear();

            for (int i = 0; i < data.selectedVertices.Count; i++)
            {
                int index = data.selectedVertices[i];

                if (!IsValid(index, vertexCount))
                    continue;

                IndieEditUVOverlayGeometry.AppendPoint(Buffer, data.vertices[index], SelectVertSize, SelectVertColor);
            }

            _selectPoints = IndieEditUVOverlayGeometry.Apply(Buffer, _selectPoints, "EditUV Select Points");
        }

        private static bool TryGetFace(IndieEditableMeshData data, int faceIndex, int vertexCount, out IndieEditMeshFace face)
        {
            face = default;

            if (faceIndex < 0 || faceIndex >= data.faces.Count)
                return false;

            face = data.faces[faceIndex];
            return IsValid(face.a, vertexCount) && IsValid(face.b, vertexCount) && IsValid(face.c, vertexCount);
        }

        private static bool IsValid(int index, int vertexCount) => index >= 0 && index < vertexCount;

        private static int ComputeSelectionSignature(IndieEditableMeshData data)
        {
            unchecked
            {
                int hash = 17;
                hash = HashList(hash, data.selectedVertices);
                hash = HashList(hash, data.selectedEdges);
                hash = HashList(hash, data.selectedFaces);
                return hash == 0 ? 1 : hash;
            }
        }

        private static int HashList(int hash, List<int> list)
        {
            unchecked
            {
                hash = hash * 31 + list.Count;

                for (int i = 0; i < list.Count; i++)
                    hash = hash * 31 + list[i];

                return hash;
            }
        }

        private static bool BeginDraw(Camera camera)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return false;

            if (camera == null)
                camera = Camera.current;

            if (camera == null)
                return false;

            if (_material == null)
            {
                Shader shader = Shader.Find(ShaderName);

                if (shader == null)
                    return false;

                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _material.SetVector(
                ViewportId,
                new Vector4(camera.pixelWidth, camera.pixelHeight, EditorGUIUtility.pixelsPerPoint, 0f)
            );
            return true;
        }

        private static void DrawMesh(Mesh mesh, int pass, Matrix4x4 matrix, CompareFunction zTest)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return;

            _material.SetFloat(ZTestId, (float)zTest);

            if (!_material.SetPass(pass))
                return;

            Graphics.DrawMeshNow(mesh, matrix);
        }

        private static void Release()
        {
            IndieEditUVOverlayGeometry.Destroy(ref _bakedSeams);
            IndieEditUVOverlayGeometry.Destroy(ref _workingSeams);
            IndieEditUVOverlayGeometry.Destroy(ref _selectFill);
            IndieEditUVOverlayGeometry.Destroy(ref _selectLines);
            IndieEditUVOverlayGeometry.Destroy(ref _selectPoints);
            IndieEditUVOverlayGeometry.Destroy(ref _legacySeams);

            if (_material != null)
                Object.DestroyImmediate(_material);

            _material = null;
            _boundData = null;
            _legacyEndpoints = null;
            _dirty = true;
        }
    }
}
