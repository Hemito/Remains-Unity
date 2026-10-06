using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    // Drives the real pause protocol and observes the actor; never assigns its mana.
    public sealed class TrainingMagicRecoveryExercise
    {
        private int stage;
        private float started;
        public float PausedMana {get;private set;}
        public float RecoveredMana {get;private set;}
        public bool ConfirmedPause=>stage==2;
        public bool Complete=>stage==4;
        public bool Step(CoopSession session,ActorState actor)
        {
            switch(stage)
            {
                case 0:
                    if(actor.mana>=99.9f) throw new InvalidOperationException("Проверке восстановления нужен израсходованный подъёмом запас.");
                    session.Pause.SetManual(true);stage=1;break;
                case 1:
                    if(!session.Pause.IsPaused(actor.level)) break;
                    PausedMana=actor.mana;started=Time.unscaledTime;stage=2;break;
                case 2:
                    if(Mathf.Abs(actor.mana-PausedMana)>.0001f)
                        throw new InvalidOperationException("Магия восстанавливается на паузе уровня.");
                    if(Time.unscaledTime-started<.5f) break;
                    session.Pause.SetManual(false);stage=3;started=Time.unscaledTime;break;
                case 3:
                    if(session.Pause.IsPaused(actor.level) || Time.unscaledTime-started<1) break;
                    RecoveredMana=actor.mana;
                    if(RecoveredMana<=PausedMana+.1f || RecoveredMana>100)
                        throw new InvalidOperationException("После снятия паузы запас магии не восстановился в допустимых пределах.");
                    stage=4;break;
            }
            return Complete;
        }
    }
}
