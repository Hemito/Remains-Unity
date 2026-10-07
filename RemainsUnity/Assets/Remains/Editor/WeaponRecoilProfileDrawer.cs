using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomPropertyDrawer(typeof(WeaponRecoilProfile))]
    public sealed class WeaponRecoilProfileDrawer : PropertyDrawer
    {
        private static readonly string[] values={"horizontalPeak","anglePeakDegrees","riseSeconds","positionReturnSeconds","angleReturnSeconds","maxHorizontal","maxAngleDegrees"};
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
            =>(property.isExpanded?3+values.Length:1)*(EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing);
        public override void OnGUI(Rect area,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(area,label,property);
            float step=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
            area.height=EditorGUIUtility.singleLineHeight;property.isExpanded=EditorGUI.Foldout(area,property.isExpanded,label,true);
            if(property.isExpanded)
            {
                EditorGUI.indentLevel++;area.y+=step;EditorGUI.PropertyField(area,property.FindPropertyRelative("version"));
                area.y+=step;var enabled=property.FindPropertyRelative("enabled");EditorGUI.PropertyField(area,enabled);
                using(new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    EditorGUI.indentLevel++;
                    foreach(string name in values){area.y+=step;EditorGUI.PropertyField(area,property.FindPropertyRelative(name));}
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
