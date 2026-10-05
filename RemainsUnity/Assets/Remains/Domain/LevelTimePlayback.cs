using System;

namespace Remains.Domain
{
    // Confirmed network epochs remain exact. Local physics follows them continuously:
    // a correction changes the rate, never replays elapsed history as a displacement.
    public sealed class LevelTimePlayback
    {
        public const double MaximumRateCorrection=.25;
        public bool Initialized {get;private set;}
        public double ElapsedSeconds {get;private set;}
        public void Advance(double target,bool running,double delta)
        {
            if(!double.IsFinite(target) || target<0 || !double.IsFinite(delta) || delta<0)
                throw new InvalidOperationException($"Некорректный шаг локальных часов: target={target}, delta={delta}.");
            if(!Initialized) {ElapsedSeconds=target;Initialized=true;return;}
            if(!running || delta==0) return;
            double nominal=ElapsedSeconds+delta;
            double correction=Math.Clamp(target-nominal,-delta*MaximumRateCorrection,delta*MaximumRateCorrection);
            double next=nominal+correction;
            if(!double.IsFinite(next)) throw new InvalidOperationException("Переполнение локальных часов уровня.");
            ElapsedSeconds=next;
        }
        // All dynamic support consumers use this one declared bound. Actor input and
        // gravity still receive the real frame delta, not this corrected clock interval.
        public static bool IsContinuous(double previous,double current,double delta)
            =>double.IsFinite(previous) && double.IsFinite(current) && double.IsFinite(delta) && delta>=0 &&
                current>=previous && current-previous<=delta*(1+MaximumRateCorrection)+.001;
    }
}
