namespace Remains.Editor
{
    public static class ExistingActionsAcceptance
    {
        public static void VerifyAndBuild()
        {
            FactoryLegacyDoorAuthoring.Install();
            ContainerOpenAudioAuthoring.Install();
            ActorDeathAudioAuthoring.Install();
            HealingItemAudioAuthoring.Install();
            FactoryLegacyDoorChecks.Run();
            ActorDeathAudioChecks.Run();
            HealingItemAudioChecks.Run();
            PrototypeBuild.BuildTo("Builds/ExistingActions68/Remains.exe");
            FactoryLegacyDoorAuthoring.Check();
            ContainerOpenAudioAuthoring.Check();
            ActorDeathAudioAuthoring.Check();
            HealingItemAudioAuthoring.Check();
        }
    }
}
