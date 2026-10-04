using Remains.Content;
using UnityEditor;

namespace Remains.Editor
{
    [CustomEditor(typeof(RoomSurfaceAuthoring)),CanEditMultipleObjects]
    public sealed class RoomSurfaceAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var slope=serializedObject.FindProperty("slopeDirection");
            foreach(string name in new[]{"texture","movementSound","slopeDirection"}) EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            bool sloped=!slope.hasMultipleDifferentValues && slope.intValue!=0;
            using(new EditorGUI.DisabledScope(sloped))
                foreach(string name in new[]{"oneWay","shotsPassThrough"}) EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            if(sloped) EditorGUILayout.HelpBox("Наклонная опора проходима снизу и пропускает выстрелы.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
