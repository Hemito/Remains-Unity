using Remains.Content;
using UnityEditor;
namespace Remains.Editor
{
    [CustomEditor(typeof(RoomActorPlacement))]
    public sealed class RoomActorPlacementEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var property=serializedObject.GetIterator();bool children=true;
            while(property.NextVisible(children))
            {
                children=false;
                using(new EditorGUI.DisabledScope(property.name=="m_Script" || property.name=="spawn" && serializedObject.FindProperty("fixedPlacement").boolValue))
                    EditorGUILayout.PropertyField(property,true);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
