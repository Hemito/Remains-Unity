using UnityEditor;

namespace Remains.Editor
{
    // Explicit authoring step; the normal build only validates these saved assets.
    public static class FactoryContainerMilestone
    {
        [MenuItem("Remains/Контент/Подготовить контейнеры и собрать игру")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            ContainerArtworkAuthoring.Install();
            FactoryOfficeChestLootAuthoring.Install();
            PrototypeBuild.Build();
        }
    }
}
