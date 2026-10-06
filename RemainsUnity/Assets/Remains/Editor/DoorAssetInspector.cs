using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [CustomEditor(typeof(DoorAsset))]
    public sealed class DoorAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Field(string name)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            Field("id");Field("size");Field("closed");Field("opened");
            var version=serializedObject.FindProperty("capabilityVersion");
            if(version.intValue==0)
            {
                EditorGUILayout.HelpBox("Прежнее определение: обычная неразрушаемая дверь. Область взаимодействия равна проёму.",MessageType.Info);
                Field("openSound");Field("closeSound");
                if(GUILayout.Button("Явно включить расширенный контракт"))
                {
                    version.intValue=1;
                    serializedObject.FindProperty("interactive").boolValue=true;
                    serializedObject.FindProperty("maxHealth").floatValue=0;
                    serializedObject.FindProperty("interactionSize").vector2Value=serializedObject.FindProperty("size").vector2Value;
                }
            }
            else
            {
                Field("interactive");
                using(new EditorGUI.DisabledScope(!serializedObject.FindProperty("interactive").boolValue))
                {Field("interactionSize");Field("openSound");Field("closeSound");}
                Field("maxHealth");
                using(new EditorGUI.DisabledScope(serializedObject.FindProperty("maxHealth").floatValue<=0))
                {Field("destroyed");Field("destroySound");}
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
