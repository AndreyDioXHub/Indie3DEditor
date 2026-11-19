using Indie3DEditor.EditMesh;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
	[AddComponentMenu("Indie 3D Editor/Indie Edit UV Component")]
	public class IndieEditUVComponent : MonoBehaviour
    {
        public bool isEditing;
        public IndieEditMeshSubMode subMode = IndieEditMeshSubMode.Polygon;

        [SerializeField]
        private IndieEditableMeshData data;

        private Mesh originalMesh;
        private Material[] originalMaterials;

        public IndieEditableMeshData Data => data;
        public Mesh OriginalMesh => originalMesh;
        public Material[] OriginalMaterials => originalMaterials;

        public MeshFilter GetMeshFilter()
        {
            return GetComponent<MeshFilter>();
        }

        public MeshRenderer GetMeshRenderer()
        {
            return GetComponent<MeshRenderer>();
        }

        public void SetData(IndieEditableMeshData value)
        {
            data = value;
        }

        public void SetOriginalMesh(Mesh mesh)
        {
            originalMesh = mesh;
        }

        public void SetOriginalMaterials(Material[] materials)
        {
            originalMaterials = materials;
        }
    }
}
