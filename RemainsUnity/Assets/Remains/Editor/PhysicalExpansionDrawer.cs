using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomPropertyDrawer(typeof(PhysicalExpansion))]
    public sealed class PhysicalExpansionDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight*2+EditorGUIUtility.standardVerticalSpacing*2+
                EditorGUI.GetPropertyHeight(property.FindPropertyRelative("additions"),true);
        }
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            var row=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(row,label,EditorStyles.boldLabel);EditorGUI.indentLevel++;
            row.y+=row.height+EditorGUIUtility.standardVerticalSpacing;
            var previous=property.FindPropertyRelative("previousPatch");EditorGUI.PropertyField(row,previous);
            row.y+=row.height+EditorGUIUtility.standardVerticalSpacing;
            var additions=property.FindPropertyRelative("additions");row.height=EditorGUI.GetPropertyHeight(additions,true);
            using(new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(previous.stringValue))) EditorGUI.PropertyField(row,additions,true);
            EditorGUI.indentLevel--;EditorGUI.EndProperty();
        }
    }
}
