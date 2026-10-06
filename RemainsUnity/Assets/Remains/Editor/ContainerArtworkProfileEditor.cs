using Remains.Content;
using UnityEditor;

namespace Remains.Editor
{
    [CustomEditor(typeof(ContainerArtworkProfileAsset))]
    public sealed class ContainerArtworkProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("version"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("definition"));
            var mode=serializedObject.FindProperty("mode");EditorGUILayout.PropertyField(mode);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("closed"));
            if(mode.enumValueIndex==(int)ContainerArtworkMode.ClosedOpened)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("opened"));
            else EditorGUILayout.HelpBox("Один исходный кадр: просмотр и опустошение не меняют рисунок.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
