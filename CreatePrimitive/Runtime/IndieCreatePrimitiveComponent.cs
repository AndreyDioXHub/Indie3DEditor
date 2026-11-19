using UnityEngine;

namespace Indie3DEditor.CreatePrimitive
{
    public enum IndieCreatePrimitiveType
    {
        Cube,
        Sphere,
        Cylinder
    }

    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("Indie 3D Editor/Indie Create Primitive")]
    public class IndieCreatePrimitiveComponent : MonoBehaviour
    {
        public IndieCreatePrimitiveType type = IndieCreatePrimitiveType.Cube;

        [Min(0.001f)]
        public float width = 1f;

        [Min(0.001f)]
        public float height = 1f;

        [Min(0.001f)]
        public float depth = 1f;

        [Tooltip("Face divisions along X.")]
        [Range(1, 128)]
        public int widthSegments = 1;

        [Tooltip("Face divisions along Y.")]
        [Range(1, 128)]
        public int heightSegments = 1;

        [Tooltip("Face divisions along Z.")]
        [Range(1, 128)]
        public int depthSegments = 1;

        [Min(0.001f)]
        public float radius = 0.5f;

        [Tooltip("Divisions around the sphere.")]
        [Range(3, 128)]
        public int sphereSegments = 24;

        [Tooltip("Divisions from the top pole to the bottom pole.")]
        [Range(2, 128)]
        public int sphereRings = 16;

        [Min(0.001f)]
        public float cylinderRadius = 0.5f;

        [Min(0.001f)]
        public float cylinderHeight = 2f;

        [Tooltip("Vertical faces around the cylinder.")]
        [Range(3, 128)]
        public int cylinderSides = 16;

        [SerializeField]
        private bool _capturedOriginal;

        [SerializeField]
        private Mesh _originalMesh;

        [SerializeField]
        private int _appliedHash;

        [System.NonSerialized]
        private bool _committed;

        [System.NonSerialized]
        private bool _previewQueued;

        public bool IsCommitted => _committed;

        internal bool CapturedOriginal => _capturedOriginal;

        internal Mesh OriginalMesh => _originalMesh;

        internal int AppliedHash
        {
            get => _appliedHash;
            set => _appliedHash = value;
        }

        public void MarkCommitted()
        {
            _committed = true;
        }

        internal void CaptureOriginalIfNeeded(Mesh currentMesh)
        {
            if (_capturedOriginal)
                return;

            if (IndieCreatePrimitivePreview.IsPreviewMesh(currentMesh))
                _originalMesh = null;
            else
                _originalMesh = currentMesh;

            _capturedOriginal = true;
        }

        private void OnEnable()
        {
            QueuePreview();
        }

        private void OnValidate()
        {
            IndieCreatePrimitiveMeshBuilder.Clamp(this);
            QueuePreview();
        }

        private void OnDestroy()
        {
            if (_committed)
                return;

            IndieCreatePrimitivePreview.RestoreOriginal(this);
        }

        private void QueuePreview()
        {
            if (_committed)
                return;

#if UNITY_EDITOR
            if (_previewQueued)
                return;

            _previewQueued = true;
            UnityEditor.EditorApplication.delayCall += ApplyQueuedPreview;
#else
            IndieCreatePrimitivePreview.Apply(this);
#endif
        }

#if UNITY_EDITOR
        private void ApplyQueuedPreview()
        {
            if (this == null)
                return;

            _previewQueued = false;

            if (_committed)
                return;

            IndieCreatePrimitivePreview.Apply(this);
        }
#endif
    }
}
