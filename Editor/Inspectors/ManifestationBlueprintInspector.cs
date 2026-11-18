using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Edits a blueprint's named appearances as an inline table.</summary>
    [CustomEditor(typeof(ManifestationBlueprint))]
    [CanEditMultipleObjects]
    public sealed class ManifestationBlueprintInspector : UnityEditor.Editor
    {
        private ReorderableList _variants;

        /// <summary>Draws blueprint settings, the Name/Prefab table and configuration errors.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_variants", "_fallbackViewPrefab");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            _variants.DoLayoutList();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_fallbackViewPrefab"));
            serializedObject.ApplyModifiedProperties();

            // Save edits before checking them so errors describe the values shown in the Inspector.
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((ManifestationBlueprint)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }

        private void OnEnable()
        {
            // Set up list callbacks each time Unity creates this editor for the selected assets.
            _variants = new ReorderableList(serializedObject, serializedObject.FindProperty("_variants"),
                true, true, true, true);
            _variants.elementHeight = EditorGUIUtility.singleLineHeight + 6f;
            _variants.drawHeaderCallback = DrawHeader;
            _variants.drawElementCallback = DrawVariant;
            _variants.onAddCallback = AddVariant;
        }

        private static void Columns(Rect rect, out Rect name, out Rect prefab)
        {
            float nameWidth = rect.width * 0.4f;
            name = new Rect(rect.x, rect.y, nameWidth - 6f, EditorGUIUtility.singleLineHeight);
            prefab = new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth,
                EditorGUIUtility.singleLineHeight);
        }

        private static void DrawHeader(Rect rect)
        {
            rect.xMin += 14f;
            Rect name;
            Rect prefab;
            Columns(rect, out name, out prefab);
            EditorGUI.LabelField(name, new GUIContent("Name", "Case-sensitive Variant identifier used in code."));
            EditorGUI.LabelField(prefab, new GUIContent("View Prefab", "Visual child instantiated for this named Variant."));
        }

        private void DrawVariant(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty row = _variants.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 3f;
            Rect name;
            Rect prefab;
            Columns(rect, out name, out prefab);
            EditorGUI.PropertyField(name, row.FindPropertyRelative("_name"), GUIContent.none);
            EditorGUI.PropertyField(prefab, row.FindPropertyRelative("_prefab"), GUIContent.none);
        }

        private void AddVariant(ReorderableList list)
        {
            SerializedProperty rows = list.serializedProperty;
            HashSet<string> names = new HashSet<string>(System.StringComparer.Ordinal);
            // Choose a name that is unused in every selected blueprint.
            foreach (UnityEngine.Object value in targets)
            {
                using (SerializedObject blueprint = new SerializedObject(value))
                {
                    SerializedProperty variants = blueprint.FindProperty("_variants");
                    for (int index = 0; index < variants.arraySize; index++)
                    {
                        names.Add(variants.GetArrayElementAtIndex(index).FindPropertyRelative("_name").stringValue);
                    }
                }
            }

            string name = "variant";
            // Find one unused name across all selected blueprints. Uppercase and lowercase names differ.
            int suffix = 2;
            while (names.Contains(name))
            {
                name = "variant_" + suffix++;
            }

            int newIndex = rows.arraySize;
            // Unity may copy the previous row when adding one. Reset both fields to avoid copying its prefab.
            rows.InsertArrayElementAtIndex(newIndex);
            SerializedProperty row = rows.GetArrayElementAtIndex(newIndex);
            row.FindPropertyRelative("_name").stringValue = name;
            row.FindPropertyRelative("_prefab").objectReferenceValue = null;
            list.index = newIndex;
        }
    }
}
