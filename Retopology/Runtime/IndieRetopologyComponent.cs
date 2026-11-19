using UnityEngine;

namespace Indie3DEditor.Retopology
{
	[DisallowMultipleComponent]
	[AddComponentMenu("Indie 3D Editor/Indie Retopology Component")]
	public class IndieRetopologyComponent : MonoBehaviour
    {
        public bool isEditing;

        [SerializeField]
        private MeshFilter targetFilter;

        [SerializeField]
        private Mesh targetMesh;

        [SerializeField]
        private Transform targetTransform;

        [SerializeField]
        private bool initialized;

        [SerializeField]
        private Mesh outputMesh;

        [SerializeField]
        private IndieRetopologyMeshData data = new IndieRetopologyMeshData();

        public MeshFilter TargetFilter => targetFilter;
        public Mesh TargetMesh => targetFilter != null ? targetFilter.sharedMesh : targetMesh;
        public Transform TargetTransform => targetFilter != null ? targetFilter.transform : targetTransform;
        public Mesh OutputMesh => outputMesh;
        public bool Initialized => initialized;
        public IndieRetopologyMeshData Data => data;

        public bool HasFaces =>
            data != null && data.faces != null && data.faces.Count > 0;

        public MeshFilter GetMeshFilter()
        {
            return GetComponent<MeshFilter>();
        }

        public void SetTarget(MeshFilter filter)
        {
            if (filter != null && filter.gameObject == gameObject)
                filter = null;

            targetFilter = filter;
            targetMesh = filter != null ? filter.sharedMesh : null;
            targetTransform = filter != null ? filter.transform : null;
        }

        public void MarkInitialized()
        {
            initialized = true;
        }

        public void SetOutputMesh(Mesh mesh)
        {
            outputMesh = mesh;
        }

        public void SetData(IndieRetopologyMeshData value)
        {
            data = value ?? new IndieRetopologyMeshData();
            EnsureData();
        }

        public void EnsureData()
        {
            if (data == null)
                data = new IndieRetopologyMeshData();

            if (data.vertices == null)
                data.vertices = new System.Collections.Generic.List<Vector3>();

            if (data.faces == null)
                data.faces = new System.Collections.Generic.List<IndieRetopologyFace>();

            if (data.selectedEdges == null)
                data.selectedEdges = new System.Collections.Generic.List<IndieRetopologyEdge>();
        }

        private void OnEnable()
        {
            EnsureData();
        }
    }
}
