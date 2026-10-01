using System;

namespace Remains.Domain
{
    // Gameplay time only: transport/recovery deadlines must continue using real time.
    public sealed class SimulationTimer
    {
        public float Remaining {get;private set;}
        public bool Ready=>Remaining<=0;
        public void Restart(float duration)
        {
            Validate(duration,nameof(duration));Remaining=duration;
        }
        public void Advance(float delta)
        {
            Validate(delta,nameof(delta));Remaining=Math.Max(0,Remaining-delta);
        }
        private static void Validate(float value,string parameter)
        {
            if(float.IsNaN(value) || float.IsInfinity(value) || value<0)
                throw new ArgumentOutOfRangeException(parameter,value,"Игровое время должно быть конечным и неотрицательным.");
        }
    }
}
