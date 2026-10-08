namespace Remains.Editor
{
    public static class PendingBodyAdmissionAcceptance
    {
        public static void VerifyAndBuild()
        {
            ItemHubVectorReserveAuthoring.VerifyPreservation();
            PrototypeBuild.BuildTo("Builds/PendingBodyAdmission/Remains.exe");
            ItemHubVectorReserveAuthoring.Validate();
        }
    }
}
