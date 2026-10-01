namespace Remains.Content
{
    public static class PonyAppearance
    {
        // The prototype wears one device on the near foreleg. pip2 is the spare view.
        public static bool IsVisible(PonyPart part)=>part.slot!="hairSecondary" && !part.path.StartsWith("pip2/");
    }
}
