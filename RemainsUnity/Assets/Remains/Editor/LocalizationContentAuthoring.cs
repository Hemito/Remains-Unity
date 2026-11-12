using System;
using Remains.Content;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

namespace Remains.Editor
{
    public static class LocalizationContentAuthoring
    {
        // Source values belong to the serialized content catalog, never the translation JSON.
        public static void Register()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryProfileCatalog>("Assets/Remains/Resources/Content/FactoryProfiles.asset");
            if(catalog==null)throw new InvalidOperationException("Нет авторского каталога профилей заводов для локализации.");
            catalog.Validate();
            var table=(StringTable)LocalizationEditorSettings.GetStringTableCollection("Remains.UI").GetTable("ru");
            foreach(var profile in catalog.profiles)
            {
                table.AddEntry("factory.profile."+profile.id+".title",profile.title).IsSmart=false;
                table.AddEntry("factory.profile."+profile.id+".description",profile.description).IsSmart=false;
            }
            EditorUtility.SetDirty(table);
        }
    }
}
