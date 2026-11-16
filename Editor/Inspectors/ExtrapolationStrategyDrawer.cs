using System;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Exposes serializable extrapolation strategies and their authored settings in the Ghost Inspector.</summary>
    [CustomPropertyDrawer(typeof(IExtrapolationStrategy), true)]
    public sealed class ExtrapolationStrategyDrawer : PropertyDrawer
    {
        /// <summary>Draws the strategy selector and any fields belonging to the selected strategy.</summary>
        /// <param name="position">Available Inspector rectangle in pixels.</param>
        /// <param name="property">Managed-reference strategy field.</param>
        /// <param name="label">Field label and tooltip.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect selector = EditorGUI.PrefixLabel(row, label);
            object strategy = property.managedReferenceValue;
            string name = strategy == null ? "Constant Acceleration Extrapolation (default)"
                : ObjectNames.NicifyVariableName(strategy.GetType().Name);
            if (EditorGUI.DropdownButton(selector, new GUIContent(name), FocusType.Keyboard))
            {
                ShowMenu(property);
            }

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool next = child.NextVisible(true);
            while (next && !SerializedProperty.EqualContents(child, end))
            {
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, true);
                next = child.NextVisible(false);
            }

            EditorGUI.EndProperty();
        }

        /// <summary>Measures the selector and the selected strategy's serialized fields.</summary>
        /// <param name="property">Managed-reference strategy field.</param>
        /// <param name="label">Field label.</param>
        /// <returns>Required Inspector height in pixels.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool next = child.NextVisible(true);
            while (next && !SerializedProperty.EqualContents(child, end))
            {
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);
                next = child.NextVisible(false);
            }

            return height;
        }

        private static void ShowMenu(SerializedProperty property)
        {
            GenericMenu menu = new GenericMenu();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IExtrapolationStrategy>())
            {
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !type.IsSerializable
                    || type.GetConstructor(Type.EmptyTypes) == null || typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    continue;
                }

                Type selectedType = type;
                SerializedObject owner = property.serializedObject;
                string path = property.propertyPath;
                bool selected = property.managedReferenceValue != null && property.managedReferenceValue.GetType() == type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), selected, () =>
                {
                    owner.Update();
                    owner.FindProperty(path).managedReferenceValue = Activator.CreateInstance(selectedType);
                    owner.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }
    }
}
