using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Indie3DEditor.Retopology
{
    [InitializeOnLoad]
    public static class IndieRetopologySetup
    {
        private static bool _moving;
        private static readonly List<IndieRetopologyComponent> Pending = new List<IndieRetopologyComponent>();

        static IndieRetopologySetup()
        {
            ObjectFactory.componentWasAdded -= OnComponentAdded;
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        public static void EnsureMoved(IndieRetopologyComponent retopo)
        {
            if (_moving || retopo == null || retopo.Initialized)
                return;

            MoveToChild(retopo);
        }

        private static void OnComponentAdded(Component component)
        {
            var retopo = component as IndieRetopologyComponent;

            if (_moving || retopo == null || retopo.Initialized)
                return;

            if (Pending.Contains(retopo))
                return;

            Pending.Add(retopo);

            EditorApplication.delayCall += () =>
            {
                Pending.Remove(retopo);
                EnsureMoved(retopo);
            };
        }

        private static void MoveToChild(IndieRetopologyComponent retopo)
        {
            if (_moving || retopo == null || retopo.Initialized)
                return;

            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Indie Retopology: Cannot start during Play Mode.");
                Object.DestroyImmediate(retopo);
                return;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(retopo.gameObject))
            {
                Debug.LogError("Indie Retopology: Add it to a scene object, not a prefab asset.");
                Object.DestroyImmediate(retopo);
                return;
            }

            var filter = retopo.GetComponent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError("Indie Retopology: Object does not have a mesh.");
                Object.DestroyImmediate(retopo);
                return;
            }

            Transform source = retopo.transform;
            var sourceRenderer = retopo.GetComponent<MeshRenderer>();

            _moving = true;

            try
            {
                string childName = string.IsNullOrEmpty(source.name)
                    ? "Retopo"
                    : source.name + " Retopo";

                var child = new GameObject(childName);
                child.transform.SetParent(source, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;
                child.layer = source.gameObject.layer;

                child.AddComponent<MeshFilter>();
                var childRenderer = child.AddComponent<MeshRenderer>();
                CopyMaterial(sourceRenderer, childRenderer);

                var moved = child.AddComponent<IndieRetopologyComponent>();
                moved.SetTarget(filter);
                moved.SetData(new IndieRetopologyMeshData());
                moved.MarkInitialized();

                Object.DestroyImmediate(retopo);
                IndieRetopologySession.BeginEditing(moved);
            }
            finally
            {
                _moving = false;
            }
        }

        private static void CopyMaterial(MeshRenderer source, MeshRenderer destination)
        {
            if (destination == null)
                return;

            if (source != null && source.sharedMaterial != null)
            {
                destination.sharedMaterials = source.sharedMaterials;
                return;
            }

            Material material = null;
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;

            if (pipeline != null)
                material = pipeline.defaultMaterial;

            if (material == null)
                material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            if (material != null)
                destination.sharedMaterial = material;
        }
    }
}
