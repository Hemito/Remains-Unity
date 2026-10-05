using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomPropertyDrawer(typeof(AtlasActorClip))]
    public sealed class AtlasActorClipDrawer : PropertyDrawer
    {
        private static readonly string[] fields={"pose","row","count","framesPerSecond","loop","loopStartFrame","verticalFrames","weaponSockets"};
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
        {
            float height=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
            if(property.isExpanded)
                foreach(string name in fields) height+=EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name),true)+EditorGUIUtility.standardVerticalSpacing;
            return height;
        }
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            var line=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
            property.isExpanded=EditorGUI.Foldout(line,property.isExpanded,label,true);
            if(property.isExpanded)
            {
                EditorGUI.indentLevel++;
                bool motion=property.FindPropertyRelative("verticalFrames").arraySize>0;
                line.y+=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
                foreach(string name in fields)
                {
                    var child=property.FindPropertyRelative(name);line.height=EditorGUI.GetPropertyHeight(child,true);
                    bool disabled=motion && (name=="framesPerSecond" || name=="loop" || name=="loopStartFrame") ||
                        name=="loopStartFrame" && !property.FindPropertyRelative("loop").boolValue;
                    using(new EditorGUI.DisabledScope(disabled)) EditorGUI.PropertyField(line,child,true);
                    line.y+=line.height+EditorGUIUtility.standardVerticalSpacing;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
