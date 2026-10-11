using Remains.Content;
using UnityEditor;

namespace Remains.Editor
{
    [CustomEditor(typeof(WeaponShotClip))]
    public sealed class WeaponShotClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("frames"),true);
            var masks=serializedObject.FindProperty("emissionFrames");
            EditorGUILayout.PropertyField(masks,true);
            using(new EditorGUI.DisabledScope(masks.arraySize==0))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("emissionOffset"));
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
