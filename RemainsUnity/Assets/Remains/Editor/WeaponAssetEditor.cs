using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomEditor(typeof(WeaponAsset))]
    public sealed class WeaponAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var definition=serializedObject.FindProperty("definition");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("presentation"),new GUIContent("Префаб оружия"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shotAnimation"),new GUIContent("Анимация выстрела (необязательно)"));
            void Field(string id,string label)=>EditorGUILayout.PropertyField(definition.FindPropertyRelative(id),new GUIContent(label),true);
            Field("id","Стабильный ID");Field("damage","Базовый урон");Field("range","Дальность, м");Field("cooldown","Интервал атак, с");Field("manaCost","Расход маны");
            EditorGUILayout.LabelField("Боеприпасы",EditorStyles.boldLabel);Field("ammo","ID боеприпаса");
            using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(definition.FindPropertyRelative("ammo").stringValue)))
            {Field("magazineSize","Ёмкость магазина");Field("ammoPerShot","Расход за выстрел");Field("reloadSeconds","Перезарядка, с");}
            EditorGUILayout.LabelField("Снаряд",EditorStyles.boldLabel);Field("projectileSpeed","Скорость, м/с");
            using(new EditorGUI.DisabledScope(definition.FindPropertyRelative("projectileSpeed").floatValue<=0))
            {Field("projectileSpeedVariation","Разброс скорости, доля");Field("projectileLifetime","Время жизни, с");}
            Field("stopAtFirstActor","Остановка на первом актёре");Field("stopAtTerrain","Остановка на геометрии");
            EditorGUILayout.LabelField("Ближний бой",EditorStyles.boldLabel);
            Field("meleeMotion","Ключи движения");
            using(new EditorGUI.DisabledScope(definition.FindPropertyRelative("meleeMotion").arraySize==0))
            {Field("meleeContactStart","Начало контакта, доля цикла");Field("meleeContactEnd","Конец контакта, доля цикла");Field("speedDamage","Урон от скорости");Field("maxSpeedDamage","Максимальная прибавка урона");}
            serializedObject.ApplyModifiedProperties();
        }
    }
}
