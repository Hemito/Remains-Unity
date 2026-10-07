namespace Remains.Editor
{
    public static class FactoryWarehouseFurnitureAcceptance
    {
        public static void Run()
        {
            FactoryWarehouseFurnitureAuthoring.Install();
            FactoryWarehouseFurnitureAuthoring.Verify();
            FactoryWarehouseFurnitureAuthoring.Install();
            FactoryWarehouseConnectionAuthoring.Connect();
            PrototypeBuild.BuildTo("Builds/WarehouseFurniture/Remains.exe");
        }
    }
}
