using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>
    /// Edits a blueprint's named appearances as an inline table.
    /// </summary>
    [CustomEditor(typeof(ManifestationBlueprint))]
    [CanEditMultipleObjects]
    public sealed class ManifestationBlueprintInspector : UnityEditor.Editor
    {
        private ReorderableList _variants;

        private void OnEnable()
        {
            _variants = new ReorderableList(serializedObject, serializedObject.FindProperty("_variants"),
                true, true, true, true);
            _variants.elementHeight = EditorGUIUtility.singleLineHeight + 6f;
            _variants.drawHeaderCallback = DrawHeader;
            _variants.drawElementCallback = DrawVariant;
            _variants.onAddCallback = AddVariant;
        }

        /// <summary>
        /// Draws blueprint settings, the Name/Prefab table and configuration errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_variants", "_fallbackViewPrefab");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            _variants.DoLayoutList();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_fallbackViewPrefab"));
            serializedObject.ApplyModifiedProperties();

            foreach (UnityEngine.Object value in targets)
            {
                string error = ((ManifestationBlueprint)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
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
            EditorGUI.LabelField(prefab, "View Prefab");
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
            // The inserted name must be unused on every selected blueprint during multi-object editing.
            foreach (UnityEngine.Object value in targets)
            {
                SerializedProperty variants = new SerializedObject(value).FindProperty("_variants");
                for (int index = 0; index < variants.arraySize; index++)
                {
                    names.Add(variants.GetArrayElementAtIndex(index).FindPropertyRelative("_name").stringValue);
                }
            }

            string name = "variant";
            int suffix = 2;
            while (names.Contains(name))
            {
                name = "variant_" + suffix++;
            }

            int newIndex = rows.arraySize;
            // Unity can copy the preceding row on insertion; overwrite both fields to avoid duplicating its prefab.
            rows.InsertArrayElementAtIndex(newIndex);
            SerializedProperty row = rows.GetArrayElementAtIndex(newIndex);
            row.FindPropertyRelative("_name").stringValue = name;
            row.FindPropertyRelative("_prefab").objectReferenceValue = null;
            list.index = newIndex;
        }
    }
}
