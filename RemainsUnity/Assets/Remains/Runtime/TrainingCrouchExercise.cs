using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class TrainingCrouchExercise
    {
        private int stage;
        private float elapsed;
        public bool Complete=>stage==5;
        public PlayerCommand Step(ActorState actor,bool host)
        {
            if(Complete) return default;
            elapsed+=Time.deltaTime;
            switch(stage)
            {
                case 0:
                    if(elapsed>=(host?1.5f:0)) Next();
                    return default;
                case 1:
                    if(actor.crouched && elapsed>=.4f) Next();
                    return new PlayerCommand {movement=Vector2.down};
                case 2:
                    if(actor.crouched && actor.pose=="crawl" && Mathf.Abs(actor.vx+2.1f)<.05f) {Next();return default;}
                    return new PlayerCommand {movement=Vector2.left,sprint=true,hasAim=true,aim=actor.Position+new Vector2(4,.8f)};
                case 3:
                    if(elapsed>=.3f && actor.crouched && Mathf.Abs(actor.vx)<.01f) Next();
                    return default;
                case 4:
                    if(!actor.crouched) Next();
                    return new PlayerCommand {movement=Vector2.up};
                default:return default;
            }
        }
        private void Next() {stage++;elapsed=0;}
    }
}
