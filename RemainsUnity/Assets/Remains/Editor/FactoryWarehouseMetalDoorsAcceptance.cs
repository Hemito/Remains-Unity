using UnityEngine;

namespace Remains.Editor
{
    // Explicit saved-content preparation and acceptance, never a runtime fallback.
    public static class FactoryWarehouseMetalDoorsAcceptance
    {
        public static void RebuildPlayer()=>PrototypeBuild.BuildTo("Builds/WarehouseMetalDoors/Remains.exe");

        public static void Run()
        {
            InventoryDescriptionAuthoring.Run();
            PonyHeadStyleAcceptance.Prepare();
            PonyHeadSlopeChecks.Run();
            CharacterInterfaceChecks.Render();
            FactoryWarehouseMetalDoorsPlacement.Install();
            InventoryDescriptionChecks.Run();
            InventoryDescriptionChecks.Render();
            CosmeticInventoryUiChecks.Render();
            PrototypeBuild.BuildTo("Builds/WarehouseMetalDoors/Remains.exe");
            Debug.Log("Двери склада и полный текст инвентаря: авторинг, native контракты, макеты и Windows сборка завершены.");
        }
    }
}
