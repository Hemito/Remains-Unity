using System;
using Remains.Diagnostics;
using UnityEngine;

namespace Remains.Editor
{
    public static class TransitionTimingChecks
    {
        public static void Run()
        {
            if(TransitionTiming.Enabled) throw new InvalidOperationException("Проверка замеров перехода требует выключенного рабочего журнала.");
            TransitionTiming.Reset();
            try
            {
                for(int index=0;index<1000;index++) using(TransitionTiming.Measure("disabled","check-room")) { }
                Require(TransitionTiming.ToArray().Length==0,"выключенный журнал оставил записи");
                TransitionTiming.Enabled=true;
                int frame=Time.frameCount;
                using(TransitionTiming.Measure("outer","check-room"))
                {
                    using(TransitionTiming.Measure("child","check-room")) { }
                    using(TransitionTiming.Measure("sibling","check-room")) { }
                }
                var nested=TransitionTiming.ToArray();
                Require(nested.Length==3 && nested[0].sequence==1 && nested[0].parentSequence==0 &&
                    nested[1].sequence==2 && nested[1].parentSequence==1 && nested[2].sequence==3 && nested[2].parentSequence==1,
                    "sequence/parent не сохраняют вложенность и соседство");
                foreach(var sample in nested) Require(sample.frame==frame && Finite(sample.milliseconds),"нет actual frame/конечного elapsed");
                Require(nested[0].milliseconds>=nested[1].milliseconds && nested[0].milliseconds>=nested[2].milliseconds,
                    "вложенная работа длиннее включающей её области");
                try
                {
                    using(TransitionTiming.Measure("body-exception","check-room"))
                    using(TransitionTiming.Measure("inner-exception","check-room"))
                        throw new BodyException();
                }
                catch(BodyException) { }
                var unwound=TransitionTiming.ToArray();
                Require(unwound.Length==5 && unwound[3].parentSequence==0 && unwound[4].parentSequence==4 &&
                    Finite(unwound[3].milliseconds) && Finite(unwound[4].milliseconds),"body exception не завершил using scopes");
                var active=TransitionTiming.Measure("active","check-room");var copied=active;
                Rejected(()=>TransitionTiming.ToArray(),"snapshot незавершённой области");
                active.Dispose();active.Dispose();
                Require(TransitionTiming.ToArray().Length==6,"повторный Dispose того же value добавил запись");
                Rejected(()=>copied.Dispose(),"двойное закрытие копии scope");
                Require(TransitionTiming.ToArray().Length==6,"ошибка копии повредила завершённый журнал");
                Debug.Log("Замеры перехода: PASS — disabled, actual frame/sequence/parent, using exception unwinding, active snapshot отказ, value Dispose идемпотентен, copied double-close отказ.");
            }
            finally {TransitionTiming.Enabled=false;TransitionTiming.Reset();}
        }
        private sealed class BodyException : Exception { }
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value) && value>=0;
        private static void Rejected(Action action,string name)
        {
            try {action();}
            catch(InvalidOperationException) {return;}
            throw new InvalidOperationException("Диагностика перехода приняла ошибочную операцию: "+name);
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Проверка замеров перехода: "+reason);}
    }
}
