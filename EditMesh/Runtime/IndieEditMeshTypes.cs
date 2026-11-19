using System;
using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    public enum IndieEditMeshSubMode
    {
        Vertex,
        Edge,
        Polygon
    }

    [Serializable]
    public struct IndieEditMeshFace
    {
        public int a;
        public int b;
        public int c;
        public int materialId;

        /// <summary>
        /// Bitmask of 3ds Max-style smoothing groups (bits 0..31 = groups 1..32).
        /// </summary>
        public int smoothingGroups;
    }

    [Serializable]
    public struct IndieEditMeshEdge
    {
        public int v0;
        public int v1;
    }

    [Serializable]
    public class IndieEditableMeshData
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector4> tangents = new List<Vector4>();
        public List<Color> colors = new List<Color>();

        public List<IndieEditMeshFace> faces = new List<IndieEditMeshFace>();
        public List<IndieEditMeshEdge> edges = new List<IndieEditMeshEdge>();

        public List<int> selectedVertices = new List<int>();
        public List<int> selectedEdges = new List<int>();
        public List<int> selectedFaces = new List<int>();

        public IndieEditableMeshData Clone()
        {
            return new IndieEditableMeshData
            {
                vertices = new List<Vector3>(vertices),
                uvs = new List<Vector2>(uvs),
                normals = new List<Vector3>(normals),
                tangents = new List<Vector4>(tangents),
                colors = new List<Color>(colors),

                faces = new List<IndieEditMeshFace>(faces),
                edges = new List<IndieEditMeshEdge>(edges),

                selectedVertices = new List<int>(selectedVertices),
                selectedEdges = new List<int>(selectedEdges),
                selectedFaces = new List<int>(selectedFaces)
            };
        }

        public void ClearSelection()
        {
            selectedVertices.Clear();
            selectedEdges.Clear();
            selectedFaces.Clear();
        }

        public void SetVertexSelection(int index)
        {
            selectedVertices.Clear();

            if (index >= 0)
                selectedVertices.Add(index);
        }

        public void ToggleVertexSelection(int index)
        {
            if (selectedVertices.Contains(index))
                selectedVertices.Remove(index);
            else
                selectedVertices.Add(index);
        }

        public void SetEdgeSelection(int index)
        {
            selectedEdges.Clear();

            if (index >= 0)
                selectedEdges.Add(index);
        }

        public void ToggleEdgeSelection(int index)
        {
            if (selectedEdges.Contains(index))
                selectedEdges.Remove(index);
            else
                selectedEdges.Add(index);
        }

        public void SetFaceSelection(int index)
        {
            selectedFaces.Clear();

            if (index >= 0)
                selectedFaces.Add(index);
        }

        public void ToggleFaceSelection(int index)
        {
            if (selectedFaces.Contains(index))
                selectedFaces.Remove(index);
            else
                selectedFaces.Add(index);
        }

        public void RebuildEdges()
        {
            edges.Clear();

            var dict = new Dictionary<long, int>();

            foreach (var face in faces)
            {
                GetOrAddEdge(dict, face.a, face.b);
                GetOrAddEdge(dict, face.b, face.c);
                GetOrAddEdge(dict, face.c, face.a);
            }
        }

        private void GetOrAddEdge(Dictionary<long, int> dict, int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);

            long key = (((long)min) << 32) | (uint)max;

            if (!dict.ContainsKey(key))
            {
                int index = edges.Count;
                edges.Add(new IndieEditMeshEdge { v0 = min, v1 = max });
                dict.Add(key, index);
            }
        }
    }
}