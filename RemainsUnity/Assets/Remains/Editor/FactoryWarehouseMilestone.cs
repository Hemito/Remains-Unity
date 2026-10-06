using UnityEditor;

namespace Remains.Editor
{
    public static class FactoryWarehouseMilestone
    {
        public static void Build()
        {
            AssetDatabase.Refresh();
            if(AssetDatabase.LoadMainAssetAtPath(FactoryWarehouseDraft.Path)==null) FactoryWarehouseDraft.Import();
            else FactoryWarehouseChecks.Check(AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(FactoryWarehouseDraft.Path).GetComponent<Remains.Content.RoomAuthoring>());
            FactoryWarehouseNpcAuthoring.Install();
            FactoryWarehouseConnectionAuthoring.Connect();
            PrototypeBuild.Build();
        }
    }
}
