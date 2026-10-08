namespace Remains.Editor
{
    public static class SelectedItemHudAcceptance
    {
        // Capture the actual saved 1x button after the explicitly changed HUD.
        // The export tool binds this create-only baseline before Install.
        public static void Prepare()
        {
            SelectedItemHudAuthoring.Apply();
            MenuButtonVectorReserveAuthoring.CaptureBaseline();
        }

        public static void VerifyAndBuild()
        {
            MenuButtonVectorReserveAuthoring.Install();
            SelectedItemHudChecks.Run();
            SelectedItemHudChecks.Render();
            PrototypeBuild.BuildTo("Builds/SelectedItemHud/Remains.exe");
            MenuButtonVectorReserveAuthoring.Check();
            VectorArtworkReserveChecks.Run();
            HudVectorReserveAuthoring.Check();
        }
    }
}
