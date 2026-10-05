using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderPoseContactChecks
    {
        public static void Run()
        {
            Anatomy.Load();PonyVisual.Prepare();
            var settings=MovementSettings.Load();
            var room=new RoomLayout {id="ladder-pose-contact",left=-5,right=5,bottom=-1,top=6,
                ladders=new[]{new RoomLadder {x=0,bottom=0,top=3}},
                platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1}}};
            var counterexample=new ActorState {id="climb-visible-floor",y=.1f,vy=-settings.climbSpeed*settings.climbDownMultiplier,
                climbing=true,pose="climb",poseTime=.1f};
            Require(PonyMotionFrames.Sample(counterexample,counterexample.poseTime)==4,"Контрпример должен использовать настоящий кадрclimb4");
            Require(LadderPoseContact.ShouldRelease(counterexample,room,counterexample.Position+Vector2.up*(counterexample.vy/60)),
                "Нижняя видимая нога не обнаружила твёрдый пол раньше габарита тела");
            new MovementController(room).Step(counterexample,Vector2.down,false,1f/60);
            Require(!counterexample.climbing && counterexample.y>0 && counterexample.vy<0,"Контакт ноги не отпустил хват с сохранением спуска до приземления тела");
            var idle=new ActorState {id="unchanged-idle",pose="idle"};
            Require(Anatomy.TrySupportContacts(idle,out var idleContacts) && MinimumY(idleContacts)<0,
                "Исходные малые пикселиidle неожиданно исправлены вместе с лестницей");
            room.platforms[0].oneWay=true;
            var beam=new ActorState {id="climb-one-way",y=.1f,vy=-4.725f,climbing=true,pose="climb",poseTime=.1f};
            Require(!LadderPoseContact.ShouldRelease(beam,room,beam.Position+Vector2.down*.08f),"Хват преждевременно отпущен у проходимой балки");
            room.platforms[0].oneWay=false;
            var trace=new StringBuilder("rate,facing,startPhase,tick,y,vy,poseTime,sampledClimbFrame,drawnClip,drawnFrame,heldFootMinimumY,climbing\n");
            foreach(int rate in new[]{30,60,144})
            foreach(int facing in new[]{-1,1})
            foreach(float phase in new[]{0f,.1f,.2f,.3f})
            {
                float dt=1f/rate;
                room.ladders[0].facing=(LadderFacing)facing;
                var actor=new ActorState {id=$"ladder-contact-{rate}-{facing}-{phase:R}",x=0,y=2.25f,climbing=true,
                    facing=facing,pose="climb",poseTime=phase,vy=-settings.climbSpeed*settings.climbDownMultiplier};
                var movement=new MovementController(room,settings);
                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
                try
                {
                    visual.Bind(actor);
                    bool released=false;int heldSamples=0;
                    for(int tick=0;tick<rate*2;tick++)
                    {
                        ActorPose.Advance(actor,dt);
                        int heldFrame=actor.climbing?PonyMotionFrames.Sample(actor,actor.poseTime):-1;
                        float localMinimum=0;
                        if(actor.climbing)
                        {
                            Require(Anatomy.TrySupportContacts(actor,out var contacts),"Нет контактов выбранного кадра");
                            localMinimum=MinimumY(contacts);
                        }
                        bool heldBefore=actor.climbing;
                        movement.Step(actor,Vector2.down,false,dt);
                        visual.DrawPose(actor.poseTime);
                        if(actor.climbing)
                        {
                            heldSamples++;
                            Require(visual.DrawnClip=="climb" && visual.DrawnFrame==heldFrame,"Контакты и PonyVisual выбрали разные кадры хвата");
                            Require(actor.y+localMinimum>=-.0001f,$"Видимая нога прошла пол до отпускания: rate={rate}, phase={phase:R}, frame={heldFrame}, y={actor.y:R}, footY={actor.y+localMinimum:R}");
                        }
                        if(heldBefore && !actor.climbing) released=true;
                        Require(actor.y>=-.0001f,"Тело прошло твёрдый нижний пол");
                        trace.AppendLine(string.Join(",",rate,facing,F(phase),tick,F(actor.y),F(actor.vy),F(actor.poseTime),heldFrame,
                            visual.DrawnClip,visual.DrawnFrame,F(heldFrame<0?float.NaN:actor.y+localMinimum),actor.climbing));
                    }
                    Require(released && heldSamples>0 && !actor.climbing && Mathf.Abs(actor.y)<.0001f && actor.vy==0,
                        $"Спуск не завершился устойчивой опорой: rate={rate}, facing={facing}, phase={phase:R}, position={actor.Position}");
                }
                finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
            }
            var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/ladder-pose-contact"));
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"descent.csv"),trace.ToString());
            Debug.Log("Контакт ног при спуске: PASS — настоящие PonyVisual кадры, твёрдый пол, прежняя скорость/гравитация, проход балки, оба направления и30/60/144Гц. Trace: "+directory);
        }
        private static float MinimumY(IReadOnlyList<Vector2> contacts)
        {
            float result=float.PositiveInfinity;foreach(var point in contacts) result=Mathf.Min(result,point.y);return result;
        }
        private static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        private static void Require(bool valid,string context)
        {
            if(!valid) throw new InvalidOperationException("Контакт ног лестницы: "+context);
        }
    }
}
