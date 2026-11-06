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
                var transforms=serializedObject.FindProperty("emissionTransforms");
                EditorGUILayout.PropertyField(transforms,true);
                using(new EditorGUI.DisabledScope(transforms.arraySize>0))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("emissionOffset"));
                if(transforms.arraySize>0)
                    EditorGUILayout.HelpBox("Положение вспышки задают исходные матрицы каждого кадра; общее смещение не применяется.",MessageType.Info);
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
