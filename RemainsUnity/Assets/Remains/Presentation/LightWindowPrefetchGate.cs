using System;
using UnityEngine;
namespace Remains.Presentation
{
    // Camera noise may change a proposal; it never owns/cancels already useful prepared GPU work.
    public sealed class LightWindowPrefetchGate
    {
        private RectInt proposed;
        private float since;
        private bool hasProposal;
        public bool ShouldRequest(RectInt active,RectInt predicted,float now,float hysteresis)
        {
            if(!float.IsFinite(now) || hysteresis<0 || !float.IsFinite(hysteresis)) throw new ArgumentException("Прогноз света: неверное время/гистерезис.");
            if(predicted==active) {hasProposal=false;return false;}
            if(!hasProposal || proposed!=predicted) {proposed=predicted;since=now;hasProposal=true;}
            return now-since>=hysteresis;
        }
        public void Reset()=>hasProposal=false;
    }
}
