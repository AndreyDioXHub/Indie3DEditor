using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
	[AddComponentMenu("Indie 3D Editor/Indie Edit Mesh Component")]
	public class IndieEditMeshComponent : MonoBehaviour
    {
        public bool isEditing;
        public IndieEditMeshSubMode subMode = IndieEditMeshSubMode.Polygon;

        [SerializeField]
        private IndieEditableMeshData data;

        private Mesh originalMesh;

        public IndieEditableMeshData Data => data;
        public Mesh OriginalMesh => originalMesh;

        public MeshFilter GetMeshFilter()
        {
            return GetComponent<MeshFilter>();
        }

        public void SetData(IndieEditableMeshData value)
        {
            data = value;
        }

        public void SetOriginalMesh(Mesh mesh)
        {
            originalMesh = mesh;
        }
    }
}