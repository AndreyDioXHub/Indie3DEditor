using System.Collections.Generic;
using Indie3DEditor.EditMesh;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Selection + 1-ring neighborhood for Edit UV canvas dynamic layer.
    /// </summary>
    public sealed class IndieEditUVCanvasNeighborhood
    {
        private List<int>[] _vertEdges;
        private List<int>[] _vertFaces;
        private int _vertCapacity = -1;
        private int _edgeCount = -1;
        private int _faceCount = -1;

        public readonly HashSet<int> DynVerts = new HashSet<int>();
        public readonly HashSet<int> DynEdges = new HashSet<int>();
        public readonly HashSet<int> DynFaces = new HashSet<int>();

        public void InvalidateAdjacency()
        {
            _vertCapacity = -1;
            _edgeCount = -1;
            _faceCount = -1;
            _vertEdges = null;
            _vertFaces = null;
        }

        public void EnsureAdjacency(IndieEditableMeshData data)
        {
            if (data == null)
            {
                InvalidateAdjacency();
                return;
            }

            if (_vertEdges != null &&
                _vertCapacity == data.vertices.Count &&
                _edgeCount == data.edges.Count &&
                _faceCount == data.faces.Count)
            {
                return;
            }

            int vertCount = data.vertices.Count;
            _vertCapacity = vertCount;
            _edgeCount = data.edges.Count;
            _faceCount = data.faces.Count;
            _vertEdges = new List<int>[vertCount];
            _vertFaces = new List<int>[vertCount];

            for (int i = 0; i < vertCount; i++)
            {
                _vertEdges[i] = new List<int>(4);
                _vertFaces[i] = new List<int>(4);
            }

            for (int e = 0; e < data.edges.Count; e++)
            {
                var edge = data.edges[e];

                if (edge.v0 >= 0 && edge.v0 < vertCount)
                    _vertEdges[edge.v0].Add(e);

                if (edge.v1 >= 0 && edge.v1 < vertCount)
                    _vertEdges[edge.v1].Add(e);
            }

            for (int f = 0; f < data.faces.Count; f++)
            {
                var face = data.faces[f];

                if (face.a >= 0 && face.a < vertCount)
                    _vertFaces[face.a].Add(f);

                if (face.b >= 0 && face.b < vertCount)
                    _vertFaces[face.b].Add(f);

                if (face.c >= 0 && face.c < vertCount)
                    _vertFaces[face.c].Add(f);
            }
        }

        public void RebuildFromSelection(IndieEditableMeshData data)
        {
            DynVerts.Clear();
            DynEdges.Clear();
            DynFaces.Clear();

            if (data == null)
                return;

            EnsureAdjacency(data);

            // Seed from selection.
            for (int i = 0; i < data.selectedVertices.Count; i++)
            {
                int v = data.selectedVertices[i];

                if (v >= 0 && v < data.vertices.Count)
                    DynVerts.Add(v);
            }

            for (int i = 0; i < data.selectedEdges.Count; i++)
            {
                int e = data.selectedEdges[i];

                if (e < 0 || e >= data.edges.Count)
                    continue;

                DynEdges.Add(e);
                var edge = data.edges[e];
                DynVerts.Add(edge.v0);
                DynVerts.Add(edge.v1);
            }

            for (int i = 0; i < data.selectedFaces.Count; i++)
            {
                int f = data.selectedFaces[i];

                if (f < 0 || f >= data.faces.Count)
                    continue;

                DynFaces.Add(f);
                var face = data.faces[f];
                DynVerts.Add(face.a);
                DynVerts.Add(face.b);
                DynVerts.Add(face.c);
            }

            if (DynVerts.Count == 0)
                return;

            // 1-ring: all edges/faces incident to seed verts, then expand verts.
            var seed = new List<int>(DynVerts);

            for (int s = 0; s < seed.Count; s++)
            {
                int v = seed[s];

                if (v < 0 || v >= _vertCapacity)
                    continue;

                List<int> edges = _vertEdges[v];

                for (int i = 0; i < edges.Count; i++)
                {
                    int e = edges[i];
                    DynEdges.Add(e);
                    var edge = data.edges[e];
                    DynVerts.Add(edge.v0);
                    DynVerts.Add(edge.v1);
                }

                List<int> faces = _vertFaces[v];

                for (int i = 0; i < faces.Count; i++)
                {
                    int f = faces[i];
                    DynFaces.Add(f);
                    var face = data.faces[f];
                    DynVerts.Add(face.a);
                    DynVerts.Add(face.b);
                    DynVerts.Add(face.c);
                }
            }
        }

        public bool ContainsAllVerts(IEnumerable<int> indices)
        {
            if (indices == null)
                return true;

            foreach (int v in indices)
            {
                if (!DynVerts.Contains(v))
                    return false;
            }

            return true;
        }
    }
}
