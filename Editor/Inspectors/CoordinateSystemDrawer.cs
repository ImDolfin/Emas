using System;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>
    /// Offers coordinate presets and inline signed-axis selectors for custom mappings.
    /// </summary>
    [CustomPropertyDrawer(typeof(CoordinateSystem))]
    public sealed class CoordinateSystemDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] _presets =
        {
            new GUIContent("Unity", "X right, Y up, Z forward."),
            new GUIContent("East North Up (ENU)", "X east, Y north, Z up."),
            new GUIContent("North East Down (NED)", "X north, Y east, Z down."),
            new GUIContent("Custom", "Choose the source axis mapped to each Unity direction.")
        };
        private static readonly CoordinateSystem[] _coordinates =
        {
            CoordinateSystem.Unity, CoordinateSystem.EastNorthUp, CoordinateSystem.NorthEastDown
        };
        private static readonly Axis[] _axes =
        {
            Axis.PositiveX, Axis.NegativeX, Axis.PositiveY, Axis.NegativeY, Axis.PositiveZ, Axis.NegativeZ
        };
        private static readonly GUIContent[] _axisLabels =
        {
            new GUIContent("+X"), new GUIContent("-X"), new GUIContent("+Y"),
            new GUIContent("-Y"), new GUIContent("+Z"), new GUIContent("-Z")
        };

        /// <summary>Reserves additional rows only while custom axes are being edited.</summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight
                + (ShowCustom(property) ? 3 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) : 0);
        }

        /// <summary>Draws the preset and, for Custom, its source axes using Unity serialized editing.</summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.PropertyScope(position, label, property))
            {
                Rect row = position;
                row.height = EditorGUIUtility.singleLineHeight;
                int preset = property.isExpanded ? 3 : PresetIndex(property);
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(row, label, preset, _presets);
                if (EditorGUI.EndChangeCheck())
                {
                    property.isExpanded = selected == 3;
                    if (selected >= 0 && selected < _coordinates.Length)
                    {
                        CoordinateSystem value = _coordinates[selected];
                        property.FindPropertyRelative("_right").intValue = (int)value.Right;
                        property.FindPropertyRelative("_up").intValue = (int)value.Up;
                        property.FindPropertyRelative("_forward").intValue = (int)value.Forward;
                    }
                }
                EditorGUI.showMixedValue = mixed;

                if (ShowCustom(property))
                {
                    EditorGUI.indentLevel++;
                    DrawAxis(ref row, property.FindPropertyRelative("_right"), "Unity right", "Source axis mapped to Unity positive X.");
                    DrawAxis(ref row, property.FindPropertyRelative("_up"), "Unity up", "Source axis mapped to Unity positive Y.");
                    DrawAxis(ref row, property.FindPropertyRelative("_forward"), "Unity forward", "Source axis mapped to Unity positive Z.");
                    EditorGUI.indentLevel--;
                }
            }
        }

        private static void DrawAxis(ref Rect row, SerializedProperty property, string label, string tooltip)
        {
            row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            GUIContent content = new GUIContent(label, tooltip);
            using (new EditorGUI.PropertyScope(row, content, property))
            {
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(row, content, Array.IndexOf(_axes, (Axis)property.intValue), _axisLabels);
                if (EditorGUI.EndChangeCheck() && selected >= 0)
                {
                    property.intValue = (int)_axes[selected];
                }
                EditorGUI.showMixedValue = mixed;
            }
        }

        private static bool ShowCustom(SerializedProperty property)
        {
            return property.isExpanded || (!property.hasMultipleDifferentValues && PresetIndex(property) == 3);
        }

        private static int PresetIndex(SerializedProperty property)
        {
            if (property.hasMultipleDifferentValues)
            {
                return -1;
            }

            int right = property.FindPropertyRelative("_right").intValue;
            int up = property.FindPropertyRelative("_up").intValue;
            int forward = property.FindPropertyRelative("_forward").intValue;
            for (int index = 0; index < _coordinates.Length; index++)
            {
                CoordinateSystem value = _coordinates[index];
                if (right == (int)value.Right && up == (int)value.Up && forward == (int)value.Forward)
                {
                    return index;
                }
            }

            return 3;
        }
    }
}
