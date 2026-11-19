using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.CreatePrimitive
{
    [CustomEditor(typeof(IndieCreatePrimitiveComponent))]
    [CanEditMultipleObjects]
    public class IndieCreatePrimitiveInspector : Editor
    {
        private SerializedProperty _type;
        private SerializedProperty _width;
        private SerializedProperty _height;
        private SerializedProperty _depth;
        private SerializedProperty _widthSegments;
        private SerializedProperty _heightSegments;
        private SerializedProperty _depthSegments;
        private SerializedProperty _radius;
        private SerializedProperty _sphereSegments;
        private SerializedProperty _sphereRings;
        private SerializedProperty _cylinderRadius;
        private SerializedProperty _cylinderHeight;
        private SerializedProperty _cylinderSides;

        private void OnEnable()
        {
            _type = serializedObject.FindProperty("type");
            _width = serializedObject.FindProperty("width");
            _height = serializedObject.FindProperty("height");
            _depth = serializedObject.FindProperty("depth");
            _widthSegments = serializedObject.FindProperty("widthSegments");
            _heightSegments = serializedObject.FindProperty("heightSegments");
            _depthSegments = serializedObject.FindProperty("depthSegments");
            _radius = serializedObject.FindProperty("radius");
            _sphereSegments = serializedObject.FindProperty("sphereSegments");
            _sphereRings = serializedObject.FindProperty("sphereRings");
            _cylinderRadius = serializedObject.FindProperty("cylinderRadius");
            _cylinderHeight = serializedObject.FindProperty("cylinderHeight");
            _cylinderSides = serializedObject.FindProperty("cylinderSides");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Create Primitive", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawTypeButtons();
            EditorGUILayout.Space();

            if (_type.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Selected objects use different primitive types.",
                    MessageType.Info
                );
            }
            else
            {
                DrawParameters((IndieCreatePrimitiveType)_type.intValue);
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "The mesh is centered on the local origin. Create Primitive saves it into Indie 3D Editor/Generated and removes this component.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Exit Play Mode to save the primitive.",
                    MessageType.Warning
                );
            }

            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            bool create = GUILayout.Button("Create Primitive", GUILayout.Height(30));
            EditorGUI.EndDisabledGroup();

            serializedObject.ApplyModifiedProperties();

            if (!create)
                return;

            var selected = new Object[targets.Length];
            System.Array.Copy(targets, selected, targets.Length);
            EditorApplication.delayCall += () => BakeSelection(selected);
        }

        private void DrawTypeButtons()
        {
            int current = _type.hasMultipleDifferentValues ? -1 : _type.intValue;

            EditorGUILayout.BeginHorizontal();
            DrawTypeButton("Cube", IndieCreatePrimitiveType.Cube, current);
            DrawTypeButton("Sphere", IndieCreatePrimitiveType.Sphere, current);
            DrawTypeButton("Cylinder", IndieCreatePrimitiveType.Cylinder, current);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeButton(string label, IndieCreatePrimitiveType type, int current)
        {
            Color previous = GUI.backgroundColor;

            if (current == (int)type)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);

            if (GUILayout.Button(label, GUILayout.Height(28)))
                _type.intValue = (int)type;

            GUI.backgroundColor = previous;
        }

        private void DrawParameters(IndieCreatePrimitiveType type)
        {
            switch (type)
            {
                case IndieCreatePrimitiveType.Sphere:
                    EditorGUILayout.PropertyField(_radius, new GUIContent("Radius"));
                    EditorGUILayout.PropertyField(
                        _sphereSegments,
                        new GUIContent("Segments", "Divisions around the sphere.")
                    );
                    EditorGUILayout.PropertyField(
                        _sphereRings,
                        new GUIContent("Rings", "Divisions from the top pole to the bottom pole.")
                    );
                    EditorGUILayout.LabelField(
                        "Segments go around. Rings go from top to bottom.",
                        EditorStyles.wordWrappedMiniLabel
                    );
                    DrawTriangleCount(
                        IndieCreatePrimitiveMeshBuilder.CountSphereTriangles(
                            _sphereSegments.intValue,
                            _sphereRings.intValue
                        )
                    );
                    break;

                case IndieCreatePrimitiveType.Cylinder:
                    EditorGUILayout.PropertyField(_cylinderRadius, new GUIContent("Radius"));
                    EditorGUILayout.PropertyField(_cylinderHeight, new GUIContent("Height"));
                    EditorGUILayout.PropertyField(
                        _cylinderSides,
                        new GUIContent("Sides", "Vertical faces around the cylinder.")
                    );
                    DrawTriangleCount(
                        IndieCreatePrimitiveMeshBuilder.CountCylinderTriangles(_cylinderSides.intValue)
                    );
                    break;

                default:
                    EditorGUILayout.PropertyField(_width, new GUIContent("Width", "Size along X."));
                    EditorGUILayout.PropertyField(_height, new GUIContent("Height", "Size along Y."));
                    EditorGUILayout.PropertyField(_depth, new GUIContent("Depth", "Size along Z."));
                    EditorGUILayout.PropertyField(
                        _widthSegments,
                        new GUIContent("Width Segments", "Face divisions along X.")
                    );
                    EditorGUILayout.PropertyField(
                        _heightSegments,
                        new GUIContent("Height Segments", "Face divisions along Y.")
                    );
                    EditorGUILayout.PropertyField(
                        _depthSegments,
                        new GUIContent("Depth Segments", "Face divisions along Z.")
                    );
                    DrawTriangleCount(
                        IndieCreatePrimitiveMeshBuilder.CountCubeTriangles(
                            _widthSegments.intValue,
                            _heightSegments.intValue,
                            _depthSegments.intValue
                        )
                    );
                    break;
            }
        }

        private static void DrawTriangleCount(int count)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Triangles", count);
            }
        }

        private static void BakeSelection(Object[] selected)
        {
            var components = new IndieCreatePrimitiveComponent[selected.Length];

            for (int i = 0; i < selected.Length; i++)
                components[i] = selected[i] as IndieCreatePrimitiveComponent;

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    IndieCreatePrimitiveAssetStore.Bake(components[i]);
            }
        }
    }
}
