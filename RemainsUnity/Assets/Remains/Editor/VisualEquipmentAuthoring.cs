using Remains.Content;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    // Explicit preparation only. Runtime never rebuilds source data or creates graphics assets.
    public static class VisualEquipmentAuthoring
    {
        [MenuItem("Remains/Снаряжение/Подготовить визуальную экипировку")]
        public static void Prepare()
        {
            InventoryIconRegistration.Register("Original/Pony/Wearables/inventory-icons","wearable-");
            WearableRenderCompiler.Build();
            WeaponCatalog.Load();
            PonyAppearanceAuthoring.PrepareCompiled();
            CharacterInterfaceAuthoring.RefreshSavedPortraits();
            InventoryIconRegistration.CheckAuthored("Original/Pony/Wearables/inventory-icons","wearable-");
            CosmeticEquipmentChecks.Run();CosmeticContentModChecks.Run();
            CosmeticInventoryUiChecks.Run();WearablePresentationChecks.Run();
            AssetDatabase.SaveAssets();
            Debug.Log("VisualEquipmentAuthoring PASS: определения, отдельная графика, иконки, префаб пони и оба портрета подготовлены. Защита и физика не добавлены.");
        }
        public static void BuildAndVerify()
        {Prepare();WearablePresentationChecks.Render();CosmeticInventoryUiChecks.Render();PrototypeBuild.BuildTo("Builds/VisualEquipment/Remains.exe");}
    }
}
