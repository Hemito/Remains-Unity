using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public abstract class EntityDefinitionEditor : UnityEditor.Editor
    {
        protected void Field(string id,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty("definition").FindPropertyRelative(id),new GUIContent(label));
    }
    [CustomEditor(typeof(ActorTypeAsset))]
    public sealed class ActorTypeAssetEditor : EntityDefinitionEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();Field("id","Стабильный ID");Field("presentation","ID способа отображения");Field("artwork","Путь описания графики");
            var source=serializedObject.FindProperty("presentationSource");
            source.enumValueIndex=EditorGUILayout.Popup("Источник представления",source.enumValueIndex,new[]{"Зарегистрированный сборщик","Префаб"});
            using(new EditorGUI.DisabledScope(source.enumValueIndex!=(int)ActorPresentationSource.Prefab))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("presentationPrefab"),new GUIContent("Префаб персонажа"));
            Field("combatAi","Боевое поведение");
            using(new EditorGUI.DisabledScope(!serializedObject.FindProperty("definition").FindPropertyRelative("combatAi").boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("definition").FindPropertyRelative("perception"),new GUIContent("Восприятие NPC"),true);
                EditorGUI.indentLevel--;
            }
            Field("hasWeapon","Отображать оружие");
            var definition=serializedObject.FindProperty("definition");
            using(new EditorGUI.DisabledScope(!definition.FindPropertyRelative("hasWeapon").boolValue ||
                definition.FindPropertyRelative("presentation").stringValue!="atlas" || string.IsNullOrWhiteSpace(definition.FindPropertyRelative("artwork").stringValue)))
            {
                EditorGUI.indentLevel++;Field("atlasWeaponSocket","Оружие на точке кадра атласа");EditorGUI.indentLevel--;
            }
            Field("bodyInventory","Инвентарь тела");Field("canUseDoors","Может пользоваться дверями");
            Field("telekinesisMovable","Разрешён перенос телекинезом");Field("bodyMassKg","Масса тела, кг");
            Field("hitProtection","Поглощение урона от попадания");
            Field("restoreHealthAfterHit","Восстановление здоровья мишени после удара");serializedObject.ApplyModifiedProperties();
        }
    }
    [CustomEditor(typeof(PropAsset))]
    public sealed class PropAssetEditor : EntityDefinitionEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();Field("id","Стабильный ID");Field("name","Название");
            Field("resource","Стабильный ключ графики (для модов)");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prefab"),new GUIContent("Префаб объекта"));
            Field("hasPhysicalBody","Физическое тело");
            bool physical=serializedObject.FindProperty("definition").FindPropertyRelative("hasPhysicalBody").boolValue;
            using(new EditorGUI.DisabledScope(!physical))
            {
                if(!physical)
                {
                    serializedObject.FindProperty("definition").FindPropertyRelative("movable").boolValue=false;
                    serializedObject.FindProperty("definition").FindPropertyRelative("mass").floatValue=0;
                }
                Field("movable","Разрешён перенос");Field("mass","Масса физического тела, кг");
            }
            Field("width",physical?"Ширина тела и взаимодействия, м":"Ширина области взаимодействия, м");
            Field("height",physical?"Высота тела и взаимодействия, м":"Высота области взаимодействия, м");serializedObject.ApplyModifiedProperties();
        }
    }
}
