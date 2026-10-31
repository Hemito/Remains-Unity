using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Remains.Editor
{
    [CustomEditor(typeof(PonyPoseDocument))]
    public sealed class PonyPoseDocumentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var document=(PonyPoseDocument)target;
            EditorGUILayout.LabelField("Анимация пони",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ID",document.clipId);
            EditorGUILayout.LabelField("Ключей",(document.keys?.Length??0).ToString());
            EditorGUILayout.LabelField("Суставов",(document.joints?.Length??0).ToString());
            EditorGUILayout.LabelField("Длительность",document.duration.ToString("0.###")+" с");
            EditorGUILayout.HelpBox("Редактируйте копию в окне анимаций. Исходный файл обновится при сохранении.",MessageType.Info);
            if(GUILayout.Button("Открыть редактор анимаций"))PonyAnimationWindow.Edit(document);
        }
        [OnOpenAsset]
        private static bool OpenAsset(int instanceId,int line)
        {
            if(EditorUtility.InstanceIDToObject(instanceId) is not PonyPoseDocument document)return false;
            PonyAnimationWindow.Edit(document);return true;
        }
    }
}
