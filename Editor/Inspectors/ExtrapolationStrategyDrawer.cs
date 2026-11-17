using System;
using System.Collections.Generic;
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
            using (new EditorGUI.PropertyScope(position, label, property))
            {
                Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                Rect selector = EditorGUI.PrefixLabel(row, label);
                object strategy = property.managedReferenceValue;
                bool mixedStrategies = HaveMixedStrategies(property);
                string name = mixedStrategies ? "Mixed strategies"
                    : strategy == null ? "Constant Acceleration (default)" : StrategyName(strategy.GetType());
                if (EditorGUI.DropdownButton(selector, new GUIContent(name, label.tooltip), FocusType.Keyboard))
                {
                    ShowMenu(property);
                }

                if (mixedStrategies)
                {
                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    SerializedProperty child = property.Copy();
                    SerializedProperty end = property.GetEndProperty();
                    bool next = child.NextVisible(true);
                    while (next && !SerializedProperty.EqualContents(child, end))
                    {
                        row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                        row.height = EditorGUI.GetPropertyHeight(child, true);
                        EditorGUI.PropertyField(row, child, SettingLabel(strategy, child), true);
                        next = child.NextVisible(false);
                    }
                }
            }
        }

        /// <summary>Measures the selector and the selected strategy's serialized fields.</summary>
        /// <param name="property">Managed-reference strategy field.</param>
        /// <param name="label">Field label.</param>
        /// <returns>Required Inspector height in pixels.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (HaveMixedStrategies(property))
            {
                return height;
            }

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
            // GhostInspector disposes its per-trait SerializedObject when the draw ends,
            // before a menu selection runs. Retain only targets and the property path.
            UnityEngine.Object[] targets = property.serializedObject.targetObjects;
            string path = property.propertyPath;
            bool mixedStrategies = HaveMixedStrategies(property);
            GenericMenu menu = new GenericMenu();
            List<Type> types = new List<Type>(TypeCache.GetTypesDerivedFrom<IExtrapolationStrategy>());
            types.Sort((left, right) => StringComparer.Ordinal.Compare(StrategyName(left), StrategyName(right)));
            foreach (Type type in types)
            {
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !type.IsSerializable
                    || type.GetConstructor(Type.EmptyTypes) == null || typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    continue;
                }

                Type selectedType = type;
                bool selected = !mixedStrategies
                    && (property.managedReferenceValue == null ? type == typeof(ConstantAccelerationExtrapolation)
                        : property.managedReferenceValue.GetType() == type);
                menu.AddItem(new GUIContent(StrategyName(type)), selected, () =>
                {
                    foreach (UnityEngine.Object target in targets)
                    {
                        if (target == null)
                        {
                            continue;
                        }

                        using (SerializedObject owner = new SerializedObject(target))
                        {
                            owner.Update();
                            SerializedProperty strategy = owner.FindProperty(path);
                            if (strategy == null)
                            {
                                continue;
                            }

                            strategy.managedReferenceValue = Activator.CreateInstance(selectedType);
                            owner.ApplyModifiedProperties();
                        }
                    }
                });
            }

            menu.ShowAsContext();
        }

        private static string StrategyName(Type type)
        {
            string name = ObjectNames.NicifyVariableName(type.Name);
            const string suffix = " Extrapolation";
            return type == typeof(NoExtrapolation) || !name.EndsWith(suffix, StringComparison.Ordinal)
                ? name : name.Substring(0, name.Length - suffix.Length);
        }

        private static GUIContent SettingLabel(object strategy, SerializedProperty property)
        {
            string label = property.displayName;
            if (strategy is SingerExtrapolation && property.name == "_maneuverTimeConstant")
            {
                label = "Maneuver decay (s)";
            }
            else if (strategy is CoordinatedTurnExtrapolation)
            {
                switch (property.name)
                {
                    case "_packetSmoothingDuration":
                        label = "Packet correction (s)";
                        break;
                    case "_useBankAngleWhenRatesUnavailable":
                        label = "Infer turn from bank";
                        break;
                    case "_gravity":
                        label = "Gravity (units/s²)";
                        break;
                }
            }

            return new GUIContent(label, property.tooltip);
        }

        private static bool HaveMixedStrategies(SerializedProperty property)
        {
            if (!property.hasMultipleDifferentValues)
            {
                return false;
            }

            // Different settings within one model must remain editable; only differing types hide the child fields.
            string type = property.managedReferenceFullTypename;
            foreach (UnityEngine.Object target in property.serializedObject.targetObjects)
            {
                using (SerializedObject owner = new SerializedObject(target))
                {
                    if (owner.FindProperty(property.propertyPath).managedReferenceFullTypename != type)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
