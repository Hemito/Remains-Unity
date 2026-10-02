using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomEditor(typeof(RoomLightAuthoring))]
    public sealed class RoomLightAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Field(string key,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(key),new GUIContent(label));
            Field("id","ID источника");Field("intensity","Яркость");Field("useMask","Исходная маска света");
            bool masked=serializedObject.FindProperty("useMask").boolValue;
            using(new EditorGUI.DisabledScope(!masked)) {Field("mask","Маска света");Field("maskOffset","Смещение от источника");Field("maskSize","Размер маски, м");}
            using(new EditorGUI.DisabledScope(masked)) {Field("radius","Радиус, м");Field("color","Цвет");}
            EditorGUILayout.HelpBox("Положение задаётся Transform. Радиус/размер изменяйте здесь; масштаб и поворот Transform источника должны оставаться исходными.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
