using System;

namespace Remains.Networking
{
    // Wall-clock schedule for replaceable state. At most one current sample per
    // frame; missed slots are skipped instead of replaying obsolete state.
    public sealed class ReplicationCadence
    {
        private readonly double interval;
        private double next;
        private bool started;
        public ReplicationCadence(double intervalSeconds)
        {
            if(!double.IsFinite(intervalSeconds) || intervalSeconds<=0)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds),"Интервал репликации должен быть положительным конечным числом.");
            interval=intervalSeconds;
        }
        public bool TryAdvance(double now)
        {
            if(!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now),"Время репликации должно быть конечным числом.");
            if(!started) {started=true;next=now;}
            const double tolerance=1e-9;
            if(now+tolerance<next) return false;
            next+=(Math.Floor((now+tolerance-next)/interval)+1)*interval;
            return true;
        }
    }
}
