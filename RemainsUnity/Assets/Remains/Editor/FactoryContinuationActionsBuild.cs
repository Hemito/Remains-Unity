using Remains.Content;
using Remains.Domain;

namespace Remains.Editor
{
    // Builds the already installed, validated content. No fixture is authored here.
    public static class FactoryContinuationActionsBuild
    {
        public static void Build()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            WeaponCatalog.Load();Anatomy.Load();PropCatalog.Load();ActorTypes.Load();
            FactoryContinuationObjectsArtworkAuthoring.Check();
            FactoryContinuationChecks.Run();FactoryContinuationObjectsChecks.Run();FactoryContinuationNpcChecks.Run();
            FactoryContinuationConnectionChecks.Run();
            PrototypeBuild.BuildTo("Builds/FactoryContinuationActions/Remains.exe");
        }
    }
}
