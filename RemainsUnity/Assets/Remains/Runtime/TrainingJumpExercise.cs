using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Normal commands only: short jump, held jump onto the shelf, double-S back down.
    public sealed class TrainingJumpExercise
    {
        private int stage;
        private float started,baseY;
        public float ShortHeight {get;private set;}
        public float LongHeight {get;private set;}
        public bool Complete=>stage==7;
        public PlayerCommand Step(ActorState actor)
        {
            float now=Time.unscaledTime;
            if(stage==0) {baseY=actor.y;started=now;stage=1;return new PlayerCommand {jump=true};}
            if(stage==1)
            {
                ShortHeight=Mathf.Max(ShortHeight,actor.y-baseY);
                if(now-started>.2f && actor.y<=baseY+.01f && actor.vy==0) stage=2;
                return default;
            }
            if(stage==2) {started=now;stage=3;return new PlayerCommand {jump=true,jumpHeld=true};}
            if(stage==3)
            {
                LongHeight=Mathf.Max(LongHeight,actor.y-baseY);
                if(now-started>1.4f && Mathf.Abs(actor.y-4)<.01f && actor.vy==0) stage=4;
                return new PlayerCommand {jumpHeld=true};
            }
            if(stage==4) {stage=5;return new PlayerCommand {movement=Vector2.down};}
            if(stage==5) {stage=6;return default;}
            if(stage==6)
            {
                if(actor.y<=baseY+.01f && actor.vy==0) stage=7;
                return new PlayerCommand {movement=Vector2.down};
            }
            return default;
        }
    }
}
