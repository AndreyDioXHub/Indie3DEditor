using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.CreatePrimitive
{
    public static class IndieCreatePrimitiveMenu
    {
        [MenuItem("GameObject/Create Primitive", false, 0)]
        private static void Create(MenuCommand command)
        {
            var go = new GameObject("Primitive");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.transform.position = Vector3.zero;

            Undo.RegisterCreatedObjectUndo(go, "Create Primitive");
            go.AddComponent<IndieCreatePrimitiveComponent>();
            Selection.activeGameObject = go;
        }
    }
}
