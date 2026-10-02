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
            Field("combatAi","Боевое поведение");Field("hasWeapon","Отображать оружие");Field("bodyInventory","Инвентарь тела");Field("restoreHealthAfterHit","Восстановление здоровья мишени после удара");serializedObject.ApplyModifiedProperties();
        }
    }
    [CustomEditor(typeof(PropAsset))]
    public sealed class PropAssetEditor : EntityDefinitionEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();Field("id","Стабильный ID");Field("name","Название");
            Field("resource","Стабильный ключ графики (для модов)");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("texture"),new GUIContent("Текстура объекта"));
            Field("mass","Масса, кг");Field("width","Ширина, м");Field("height","Высота, м");serializedObject.ApplyModifiedProperties();
        }
    }
}
