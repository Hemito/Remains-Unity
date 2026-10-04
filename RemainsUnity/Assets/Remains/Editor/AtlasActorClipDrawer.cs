using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomPropertyDrawer(typeof(AtlasActorClip))]
    public sealed class AtlasActorClipDrawer : PropertyDrawer
    {
        private static readonly string[] fields={"pose","row","count","framesPerSecond","loop","loopStartFrame"};
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
            =>(property.isExpanded?7:1)*(EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing);
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            var line=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
            property.isExpanded=EditorGUI.Foldout(line,property.isExpanded,label,true);
            if(property.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach(string name in fields)
                {
                    line.y+=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
                    using(new EditorGUI.DisabledScope(name=="loopStartFrame" && !property.FindPropertyRelative("loop").boolValue))
                        EditorGUI.PropertyField(line,property.FindPropertyRelative(name));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
