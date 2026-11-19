using System;
using System.Collections.Generic;
using UnityEngine;

namespace Indie3DEditor.Retopology
{
    [Serializable]
    public struct IndieRetopologyFace
    {
        public int a;
        public int b;
        public int c;
    }

    [Serializable]
    public struct IndieRetopologyEdge
    {
        public int v0;
        public int v1;
    }

    [Serializable]
    public class IndieRetopologyMeshData
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<IndieRetopologyFace> faces = new List<IndieRetopologyFace>();
        public List<IndieRetopologyEdge> selectedEdges = new List<IndieRetopologyEdge>();

        [NonSerialized]
        public int revision;

        public void MarkChanged()
        {
            revision++;
        }

        public IndieRetopologyMeshData Clone()
        {
            return new IndieRetopologyMeshData
            {
                vertices = vertices != null ? new List<Vector3>(vertices) : new List<Vector3>(),
                faces = faces != null ? new List<IndieRetopologyFace>(faces) : new List<IndieRetopologyFace>(),
                selectedEdges = selectedEdges != null
                    ? new List<IndieRetopologyEdge>(selectedEdges)
                    : new List<IndieRetopologyEdge>()
            };
        }

        public void ToggleEdge(int a, int b)
        {
            if (selectedEdges == null)
                selectedEdges = new List<IndieRetopologyEdge>();

            IndieRetopologyEdge edge = Ordered(a, b);

            for (int i = 0; i < selectedEdges.Count; i++)
            {
                IndieRetopologyEdge selected = selectedEdges[i];

                if (selected.v0 == edge.v0 && selected.v1 == edge.v1)
                {
                    selectedEdges.RemoveAt(i);
                    return;
                }
            }

            selectedEdges.Add(edge);
        }

        public static IndieRetopologyEdge Ordered(int a, int b)
        {
            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }

            return new IndieRetopologyEdge
            {
                v0 = a,
                v1 = b
            };
        }
    }
}
