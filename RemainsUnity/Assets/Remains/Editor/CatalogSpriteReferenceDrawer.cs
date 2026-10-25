using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The key and geometry are one authored record. Editing one field independently would invalidate registration.
    [CustomPropertyDrawer(typeof(CatalogSpriteReference))]
    public sealed class CatalogSpriteReferenceDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
            =>(property.isExpanded?7:1)*(EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing);
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            var row=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
            property.isExpanded=EditorGUI.Foldout(row,property.isExpanded,label,true);
            if(property.isExpanded)
            {
                EditorGUI.indentLevel++;
                using(new EditorGUI.DisabledScope(true))
                {
                    string key=property.FindPropertyRelative("key").stringValue;
                    Draw(string.IsNullOrEmpty(key)?"direct":"key",string.IsNullOrEmpty(key)?"Спрайт вне каталога":"Ключ каталога");
                    Draw("rect","Исходная область");Draw("pivot","Исходная привязка");
                    Draw("pixelsPerUnit","Исходных пикселей на метр");Draw("sourceCanvas","Исходный холст");
                    Draw("rasterReason","Причина сохранения растра");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
            void Draw(string field,string title)
            {row.y+=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;EditorGUI.PropertyField(row,property.FindPropertyRelative(field),new GUIContent(title,"Подготовлено авторингом вместе с геометрией. Для замены запустите авторинг потребителя."));}
        }
    }
}
