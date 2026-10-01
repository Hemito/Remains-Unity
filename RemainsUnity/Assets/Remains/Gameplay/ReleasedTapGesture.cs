namespace Remains.Gameplay
{
    // A second press shortly after a release. Holding a button never repeats it.
    public sealed class ReleasedTapGesture
    {
        private readonly float window;
        private bool held;
        private float sinceRelease=float.PositiveInfinity;
        public ReleasedTapGesture(float window) => this.window=window;
        public bool Step(bool pressed,float dt)
        {
            sinceRelease+=dt;
            bool triggered=pressed && !held && sinceRelease<window;
            if(!pressed && held) sinceRelease=0;
            if(pressed && !held) sinceRelease=float.PositiveInfinity;
            held=pressed;
            return triggered;
        }
    }
}
