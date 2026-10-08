namespace Remains.Editor
{
    public static class PonyMagicAcceptance
    {
        public static void VerifyAndBuild()=>PrototypeBuild.BuildTo("Builds/PonyMagicPresentation/Remains.exe");
        public static void VerifyFocused()
        {
            Remains.Content.ActorTypes.Load();Remains.Content.WeaponCatalog.Load();
            Remains.Domain.Anatomy.Load();PonyMagicActivityChecks.Run();
        }
    }
}
