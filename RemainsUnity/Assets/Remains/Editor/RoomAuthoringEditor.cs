using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomEditor(typeof(RoomAuthoring))]
    public sealed class RoomAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var settings=serializedObject.FindProperty("settings");
            void Field(string key,string label)=>EditorGUILayout.PropertyField(settings.FindPropertyRelative(key),new GUIContent(label),true);
            Field("id","ID комнаты");Field("lightingProfile","Профиль света");Field("movementSound","Покрытие для звуков движения");
            EditorGUILayout.LabelField("Границы и появление",EditorStyles.boldLabel);
            Field("left","Левая граница");Field("right","Правая граница");Field("bottom","Нижняя граница");Field("top","Верхняя граница");
            Field("spawnX","Появление X");Field("spawnY","Появление Y");Field("artwork","Слои оформления");
            EditorGUILayout.HelpBox("Геометрия берётся из дочерних поверхностей и лестниц. Их положение и размеры задаются Transform. Поворот и отрицательный масштаб не поддерживаются.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
