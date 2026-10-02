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
            Field("id","ID комнаты");
            if(((RoomAuthoring)target).GetComponent<RoomLightingAuthoring>()==null) Field("lightingProfile","Профиль света");
            Field("movementSound","Покрытие для звуков движения");
            EditorGUILayout.LabelField("Границы и появление",EditorStyles.boldLabel);
            Field("left","Левая граница");Field("right","Правая граница");Field("bottom","Нижняя граница");Field("top","Верхняя граница");
            Field("spawnX","Появление X");Field("spawnY","Появление Y");
            if(!((RoomAuthoring)target).HasAuthoredArtwork) Field("artwork","Слои оформления");
            else EditorGUILayout.HelpBox("Видимые слои находятся в дочерней группе «Оформление». Материалы и геометрия сохранены как ассеты.",MessageType.Info);
            EditorGUILayout.HelpBox("Геометрия берётся из дочерних поверхностей и лестниц. Их положение и размеры задаются Transform. Поворот и отрицательный масштаб не поддерживаются.",MessageType.Info);
            if(PrefabUtility.IsPartOfPrefabInstance(target))
                EditorGUILayout.HelpBox("Игра загружает исходный префаб. После редактирования экземпляра примените нужные Overrides к префабу. Для игрового запуска откройте Prototype.unity.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if(GUILayout.Button("Обновить предпросмотр освещения")) RoomLightingPreview.Refresh((RoomAuthoring)target);
        }
    }
}
