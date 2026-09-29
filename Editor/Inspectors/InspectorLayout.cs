using System;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    internal static class InspectorLayout
    {
        internal static IDisposable Section(string title, string description = null)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.VerticalScope scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            EditorGUILayout.LabelField(new GUIContent(title, description), EditorStyles.boldLabel);
            return scope;
        }

        internal static void Header(string title, string description)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(new GUIContent(title, description), EditorStyles.largeLabel);
        }

        internal static void Field(SerializedObject owner, string property, string label)
        {
            SerializedProperty value = owner.FindProperty(property);
            EditorGUILayout.PropertyField(value, new GUIContent(label, value.tooltip), true);
        }

        internal static void ReadOnly(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value ?? "?", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        internal static void Object(string label, UnityEngine.Object value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(label, value, typeof(UnityEngine.Object), true);
            }
        }

        internal static void Rotation(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(new GUIContent(label + " (degrees)", property.tooltip));
            Rect rect = EditorGUILayout.GetControlRect();
            using (new EditorGUI.PropertyScope(rect, GUIContent.none, property))
            {
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                Vector3 angles = EditorGUI.Vector3Field(rect, GUIContent.none, property.quaternionValue.eulerAngles);
                if (EditorGUI.EndChangeCheck())
                {
                    property.quaternionValue = Quaternion.Euler(angles);
                }
                EditorGUI.showMixedValue = mixed;
            }
        }

        internal static void Position(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(new GUIContent(label, property.tooltip));
            using (new EditorGUILayout.HorizontalScope())
            {
                float width = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 14f;
                EditorGUILayout.PropertyField(property.FindPropertyRelative("_x"), new GUIContent("X"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("_y"), new GUIContent("Y"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("_z"), new GUIContent("Z"));
                EditorGUIUtility.labelWidth = width;
            }
        }

        internal static bool IsOn(SerializedProperty property)
        {
            return !property.hasMultipleDifferentValues && property.boolValue;
        }

        internal static void Help(ref bool expanded, string text)
        {
            expanded = EditorGUILayout.Foldout(expanded, "Setup help", true);
            if (expanded)
            {
                EditorGUILayout.HelpBox(text, MessageType.None);
            }
        }

        internal static void DiagnosticsButton()
        {
            if (GUILayout.Button("Open Emas diagnostics"))
            {
                DiagnosticsWindow.ShowWindow();
            }
        }
    }
}
