using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class RemoteActorMotionChecks
    {
        public static void Run()
        {
            foreach(int fps in new[]{30,60,144}) {CheckJitter(fps);CheckFallingBody(fps);}
            var movementSettings=MovementSettings.Load();
            foreach(int fps in new[]{30,60,144})
            {CheckLastClimbPacket(fps);CheckConfirmedLadderPackets(fps,movementSettings);CheckLadderTransitions(fps);}
            var motion=new RemoteActorMotion();
            var world=new WorldState();
            var actor=new ActorState {id="remote",owner="remote",player=true,connected=true,vx=8.4f,pose="run"};
            var local=new ActorState {id="local",owner="local",player=true,connected=true,x=-2};
            world.actors.Add(actor);world.actors.Add(local);
            double now=0;
            void Step(float dt,bool paused=false) {motion.Step(world,"local",dt,_=>paused,now);now+=dt;}
            float previous=0;
            for(int frame=0;frame<60;frame++)
            {
                if(frame%3==0) {actor.x=frame/60f*8.4f;Stamp(actor,now);}
                Step(1f/60);
                Require(Mathf.Abs(actor.x-previous-.14f)<.001f,"Равномерный поток 20 Гц не дал равномерного движения 60 Гц");
                Require(local.x==-2,"Изменено локально управляемое тело");previous=actor.x;
            }
            float lostAt=actor.x;
            for(int i=0;i<120;i++) Step(1f/60);
            Require(actor.x-lostAt<=8.4f*RemoteActorMotion.PredictionSeconds+.001f,"Потеря пакетов вызывает неограниченный прогноз");
            float stopped=actor.x;Step(1);
            Require(Mathf.Abs(actor.x-stopped)<.001f,"Прогноз не остановился после горизонта");
            Stamp(actor,now);actor.x=20;actor.vx=0;Step(1f/60);
            Require(actor.x==20,"Телепорт воспроизведён проходом через комнату");
            Stamp(actor,now);actor.vx=4;Step(1,true);
            Require(actor.x==20,"Пауза двигает чужое тело");
            actor.owner="local";actor.x=3;Step(.1f);
            Require(actor.x==3,"После передачи владения осталась чужая проекция");
            actor.owner="remote";actor.level="hub";actor.x=4;actor.vx=0;actor.generation++;
            Step(.1f);Require(actor.x==4,"Смена локации/поколения сохранила старую траекторию");
            var canonical=world.Copy();var replica=canonical.Copy();replica.actors[0].vx=5;
            Stamp(replica.actors[0],now);motion.Clear();motion.Step(replica,"local",.05f,_=>false,now);
            Require(canonical.actors[0].x==4 && replica.actors[0].x>4,"Проекция изменила независимый канонический снимок");
            Debug.Log("Удалённое движение: PASS — 20/60 Гц, предел прогноза, пауза, телепорт, владение, независимый снимок.");
        }
        private static void Stamp(ActorState actor,double now)
        {actor.motionSequence++;actor.motionSentAt=now;actor.motionClockGeneration=actor.generation;}

        private static void StepReplica(RemoteActorMotion motion,WorldState world,ActorState actor,float dt,double now,bool paused=false)
        {
            motion.Step(world,"local",dt,_=>paused,now);
            // Same required integration as the ordinary runtime animation loop.
            if(!paused && !motion.ControlsPosePhase(actor)) ActorPose.Advance(actor,dt);
        }
        private static void RequireClimbContact(ActorState actor,float floor,string context)
        {
            if(!actor.climbing) return;
            int frame=PonyMotionFrames.Sample(actor,actor.poseTime);
            Require(Anatomy.TrySupportContacts(actor,out var contacts),"Нет контактов фактически выбранного кадра " + context);
            float minimum=float.PositiveInfinity;
            foreach(var point in contacts) minimum=Mathf.Min(minimum,point.y+actor.y);
            Require(minimum>=floor-RoomSupport.ContactTolerance-.00001f,
                $"Реплика пересекла пол: {context}, frame={frame}, bodyY={actor.y:R}, poseTime={actor.poseTime:R}, footY={minimum:R}, floor={floor:R}");
        }
        private static void CheckLastClimbPacket(int fps)
        {
            // Confirmed owner sample from the 144 Hz native failure trace. The
            // following release is missing; the replica must not invent it or
            // keep descending/advancing its climb cycle beyond this pair.
            var actor=new ActorState {id="last-climb",owner="remote",player=true,connected=true,level="hub",
                climbing=true,pose="climb",poseTime=-2.47676826f,y=1.38551939f,vy=-4.72500038f};
            var world=new WorldState();world.actors.Add(actor);Stamp(actor,604800);
            var motion=new RemoteActorMotion();
            float confirmedY=actor.y,confirmedPhase=actor.poseTime;
            for(int frame=0;frame<fps;frame++)
            {
                StepReplica(motion,world,actor,1f/fps,1000d+(double)frame/fps);
                RequireClimbContact(actor,.8f,$"last packet/fps={fps}/tick={frame}");
            }
            Require(actor.y==confirmedY && actor.poseTime==confirmedPhase && motion.ControlsPosePhase(actor),
                "Потерянный release экстраполировал подтверждённую лестничную пару");
        }
        private static void CheckConfirmedLadderPackets(int fps,MovementSettings settings)
        {
            float dt=1f/fps;
            var room=new RoomLayout {id="confirmed-ladder",left=-5,right=5,bottom=-1,top=16,
                ladders=new[]{new RoomLadder {x=0,bottom=.8f,top=13.6f}},
                platforms=new[]{new RoomSurface {x=-5,y=0,width=10,height=.8f}}};
            var source=new ActorState {id="confirmed-climb",owner="remote",player=true,connected=true,level="hub",
                climbing=true,pose="climb",y=13.1f,vy=-settings.climbSpeed*settings.climbDownMultiplier};
            var controller=new MovementController(room,settings);
            var cadence=new Remains.Networking.ReplicationCadence(.05);
            var packets=new System.Collections.Generic.List<(double delivery,ActorState actor)>();
            double lastClimb=0;
            for(int frame=0;frame<fps*4;frame++)
            {
                double now=(double)frame/fps;
                ActorPose.Advance(source,dt);controller.Step(source,Vector2.down,false,dt);
                RequireClimbContact(source,.8f,$"owner/fps={fps}/tick={frame}");
                if(!cadence.TryAdvance(now)) continue;
                Stamp(source,604800+now);
                if(source.climbing) lastClimb=now;
                packets.Add((now+(source.motionSequence%2==0?.09:.025),source.Copy()));
            }
            // Delay all release/landing publications, including reliable-state
            // equivalents, so several displayed frames follow the last climb.
            for(int index=0;index<packets.Count;index++)
                if(!packets[index].actor.climbing)
                    packets[index]=(System.Math.Max(packets[index].delivery,lastClimb+.65),packets[index].actor);
            packets.Sort((first,second)=>first.delivery.CompareTo(second.delivery));
            var replica=new ActorState {id=source.id,owner="remote",player=true,connected=true,level="hub",y=13.1f};
            var world=new WorldState();world.actors.Add(replica);
            var motion=new RemoteActorMotion();int next=0,movingBetweenPackets=0,climbSamples=0;
            bool release=false;float previous=replica.y;
            for(int frame=0;frame<fps*5;frame++)
            {
                double now=(double)frame/fps;bool publication=false;
                while(next<packets.Count && packets[next].delivery<=now)
                {
                    var incoming=packets[next++].actor;
                    if(!ActorMotionReplication.IsNewer(incoming.motionSequence,replica.motionSequence)) continue;
                    ActorPose.CopyMotion(incoming,replica);publication=true;
                }
                StepReplica(motion,world,replica,dt,1000+now);
                if(replica.climbing)
                {
                    climbSamples++;RequireClimbContact(replica,.8f,$"intermediate/fps={fps}/tick={frame}");
                    if(!publication && previous-replica.y>.00001f) movingBetweenPackets++;
                }
                else if(climbSamples>0) {release=true;Require(!motion.ControlsPosePhase(replica),"Release сохранил владение лестничной фазой");}
                previous=replica.y;
            }
            Require(climbSamples>0 && movingBetweenPackets>0 && release,"Нет сглаживания подтверждённых climb-пар или обычного выхода в падение");
        }
        private static void CheckLadderTransitions(int fps)
        {
            float dt=1f/fps;double now=1000;
            var actor=new ActorState {id="ladder-transition",owner="remote",player=true,connected=true,level="hub",
                climbing=true,pose="climb",poseTime=-2.47676826f,y=1.38551939f,vy=-4.72500038f};
            var world=new WorldState();world.actors.Add(actor);var motion=new RemoteActorMotion();
            void Publish(float y,float vy,float phase)
            {actor.y=y;actor.vy=vy;actor.poseTime=phase;Stamp(actor,604800+now-1000);StepReplica(motion,world,actor,dt,now);now+=dt;RequireClimbContact(actor,.8f,"transition/fps="+fps);}
            Publish(actor.y,actor.vy,actor.poseTime);
            Publish(1.05f,0,0);
            // Interpolating the numeric reset would visit frame10/4 with a much
            // lower hoof although both confirmed endpoints are above the floor.
            Require(actor.y==1.05f && actor.poseTime==0 && PonyMotionFrames.Sample(actor,actor.poseTime)==0,
                "Переход moving→hold прошёл цикл сброса вместо подтверждённого stationary кадра");
            Publish(1.38551939f,-4.72500038f,-2.47676826f);
            Require(actor.y==1.38551939f && actor.poseTime==-2.47676826f,"Hold→moving использовал прежнюю фазовую коррекцию");
            Publish(1.4f,3.15f,.05f);
            Require(actor.y==1.4f && actor.poseTime==.05f,"Разворот лестницы смешал разные направления подтверждённого движения");
            float frozenY=actor.y,frozenPhase=actor.poseTime;
            StepReplica(motion,world,actor,.5f,now,true);
            Require(actor.y==frozenY && actor.poseTime==frozenPhase,"Пауза разделила body и phase лестничной пары");
            actor.climbing=false;actor.pose="fall";actor.poseSequence++;actor.y=1.28664768f;actor.vy=-4.849998f;Stamp(actor,604801);
            StepReplica(motion,world,actor,dt,now+.5);
            Require(!motion.ControlsPosePhase(actor) && actor.pose=="fall","Лестничная проекция не освободила обычную анимацию падения");
            actor.owner="local";actor.climbing=true;actor.pose="climb";actor.y=2;actor.poseTime=.2f;
            motion.Step(world,"local",dt,_=>false,now+1);
            Require(!motion.ControlsPosePhase(actor) && actor.y==2 && actor.poseTime==.2f,"Лестничная пара вмешалась после получения локального владения");
            actor.owner="remote";actor.generation++;actor.y=3;actor.poseTime=.1f;actor.motionClockGeneration=-1;
            motion.Step(world,"local",dt,_=>false,now+2);
            Require(actor.y==3 && actor.poseTime==.1f,"Новое поколение унаследовало старую лестничную пару/часы");
            actor.level="factory:0";actor.y=4;actor.poseTime=.3f;Stamp(actor,1);
            motion.Step(world,"local",dt,_=>false,now+3);
            Require(actor.y==4 && actor.poseTime==.3f,"Новый уровень унаследовал старую лестничную коррекцию");
            actor.lifecycleSequence++;actor.y=5;actor.poseTime=.4f;
            motion.Step(world,"local",dt,_=>false,now+4);
            Require(actor.y==5 && actor.poseTime==.4f,"Новое состояние жизни унаследовало старую лестничную пару");
            actor.down=true;motion.Step(world,"local",dt,_=>false,now+5);
            Require(!motion.ControlsPosePhase(actor),"Пассивное тело сохраняет управление фазой хвата");
        }
        private static void CheckFallingBody(int fps)
        {
            var world=new WorldState();
            var actor=new ActorState {id="falling-body",owner="remote",player=true,connected=true,down=true,y=3,vy=-4};
            world.actors.Add(actor);
            var motion=new RemoteActorMotion();var cadence=new Remains.Networking.ReplicationCadence(.05);
            float previous=3,confirmed=3;int betweenPackets=0;
            for(int frame=0;frame<fps*2;frame++)
            {
                double now=(double)frame/fps;bool packet=cadence.TryAdvance(now);
                if(packet) {confirmed=Mathf.Max(.8f,3-4*(float)now);actor.y=confirmed;actor.vy=confirmed>.8f?-4:0;Stamp(actor,now);}
                motion.Step(world,"local",1f/fps,_=>false,now);
                Require(actor.y>=confirmed-.00001f && actor.y<=previous+.00001f,"Изображение падающего тела перескочило подтверждённую позицию");
                if(!packet && previous-actor.y>.00001f) betweenPackets++;
                previous=actor.y;
            }
            Require(betweenPackets>0 && Mathf.Abs(actor.y-.8f)<.001f,"Тело не движется между пакетами или не остановилось на полу");
            actor.y=1;Stamp(actor,2);motion.Step(world,"local",1f/fps,_=>false,2);
            float pausedY=actor.y;motion.Step(world,"local",.5f,_=>true,2.1);
            Require(actor.y==pausedY,"Пауза продолжает сглаживать тело");
            actor.owner="local";actor.y=1.5f;motion.Step(world,"local",.1f,_=>false,2.6);
            Require(actor.y==1.5f,"Сглаживание тела вмешалось после передачи владения");
        }
        private static void CheckJitter(int fps)
        {
            var world=new WorldState();
            var actor=new ActorState {id="jitter",owner="remote",player=true,connected=true,vx=8.4f,pose="run"};
            world.actors.Add(actor);
            var packets=new System.Collections.Generic.List<(int delivery,int source,uint sequence)>();
            var schedule=new Remains.Networking.ReplicationCadence(.05);
            uint sequence=0;
            for(int frame=0;frame<fps*4;frame++)
                if(schedule.TryAdvance((double)frame/fps))
                {sequence++;packets.Add((frame+(sequence%2==0?(int)Math.Ceiling(fps*.03):0),frame,sequence));}
            var motion=new RemoteActorMotion();
            float maximumError=0;
            for(int frame=0;frame<fps*4;frame++)
            {
                foreach(var packet in packets)
                {
                    if(packet.delivery!=frame || !ActorMotionReplication.IsNewer(packet.sequence,actor.motionSequence)) continue;
                    actor.x=8.4f*packet.source/fps;actor.motionSequence=packet.sequence;
                    // Deliberately unrelated sender/receiver clock origins.
                    actor.motionSentAt=604800d+(double)packet.source/fps;actor.motionClockGeneration=actor.generation;
                }
                motion.Step(world,"local",1f/fps,_=>false,1000d+(double)frame/fps);
                maximumError=Mathf.Max(maximumError,Mathf.Abs(actor.x-8.4f*(frame+1)/fps));
            }
            Require(maximumError<.002f,$"Запоздавший пакет тормозит ровный бег: fps={fps}, error={maximumError}");
            // Stop and reverse are genuine source changes, not delivery jitter.
            double now=1004;
            actor.x=33.6f;actor.vx=0;Stamp(actor,604804);
            for(int i=0;i<fps;i++) motion.Step(world,"local",1f/fps,_=>false,now+(double)i/fps);
            Require(Mathf.Abs(actor.x-33.6f)<.002f,"Остановка источника не остановила проекцию");
            actor.generation++;actor.motionSequence++;actor.x=2;actor.vx=-8.4f;
            motion.Step(world,"local",1f/fps,_=>false,1005);
            Require(actor.x==2,"До первой публикации новый владелец использовал чужие часы");
            Stamp(actor,5);motion.Step(world,"local",1f/fps,_=>false,1005+1d/fps);
            Require(actor.x<2 && actor.x>=2-8.4f/fps-.002f,"Новый владелец не начал движение от собственного времени");
            Debug.Log($"Временные отметки движения: PASS — fps={fps}, maxError={maximumError}, задержка чередующихся пакетов, остановка, новые часы владельца.");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Удалённое движение: "+message);}
    }
}
