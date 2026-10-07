using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class SlopeLocomotionPoseChecks
    {
        public static void Run()
        {
            Anatomy.Load();SlopePoseCatalog.Load();
            Require(SlopePoseCatalog.TryGet("pony",out var profile),"Нет явного профиля pony.");
            Require(!SlopePoseCatalog.TryGet("raider",out _) && !SlopePoseCatalog.TryGet("radroach",out _),"Атласная анатомия получила чужой профиль.");
            CheckClips(profile);CheckMalformed();
            foreach(int fps in new[]{30,60,144}) foreach(int direction in new[]{-1,1})
            {
                foreach(int travel in new[]{-1,1}) foreach(bool sprint in new[]{false,true}) Traverse(fps,direction,travel,sprint,profile);
                foreach(int facing in new[]{-1,1}) {Standing(fps,direction,facing,profile);Backwards(fps,direction,facing,profile);}
                Endpoints(fps,direction);Priorities(fps,direction,profile);
            }
            CheckTies();
            Debug.Log("Наклонные позы: PASS — явная capability pony, четыре проверенных клипа, реальный MovementController при 30/60/144 Гц, оба наклона/направления/Shift, обратный шаг с lookAtX, неизменная физика относительно атласной анатомии, остановка/нулевое время, flat endpoints и shuffle, landing/crouch/ladder/airborne/passive. Original runtime и сетевая пауза этой проверкой не подтверждаются.");
        }
        private static RoomLayout Room(int direction)=>new()
        {
            id="slope-locomotion-check",left=-8,right=14,bottom=-4,top=12,ladders=Array.Empty<RoomLadder>(),
            platforms=new[]{new RoomSurface {x=-8,y=(direction>0?0:3)-.4f,width=8,height=.4f},
                new RoomSurface {x=0,y=0,width=6,height=3,slopeDirection=direction,oneWay=true,shotsPassThrough=true},
                new RoomSurface {x=6,y=(direction>0?3:0)-.4f,width=8,height=.4f}}
        };
        private static float Height(int direction,float x,float width=1)=>.5f*Mathf.Clamp(direction>0?x+width*.5f:6-x+width*.5f,0,6);
        private static ActorState Actor(int direction,float x,string anatomy="pony")=>new()
        {id="slope-actor-"+anatomy,anatomy=anatomy,player=true,connected=true,x=x,y=Height(direction,x,Anatomy.Movement(anatomy).width)};
        private static void Traverse(int fps,int direction,int travel,bool sprint,SlopePoseProfile profile)
        {
            float dt=1f/fps;var settings=MovementSettings.Load();var room=Room(direction);var motion=new MovementController(room,settings);var referenceMotion=new MovementController(room,settings);
            var actor=Actor(direction,travel>0?-2:9);var reference=Actor(direction,actor.x,"raider");int witnessed=0;
            for(int frame=0;frame<fps*5 && (travel>0?actor.x<9:actor.x>-2);frame++)
            {
                ActorPose.Advance(actor,dt);ActorPose.Advance(reference,dt);float priorTime=actor.poseTime;uint priorSequence=actor.poseSequence;string priorPose=actor.pose;
                motion.Step(actor,new Vector2(travel,0),false,dt,sprint);referenceMotion.Step(reference,new Vector2(travel,0),false,dt,sprint);
                Require(actor.Position==reference.Position && actor.Velocity==reference.Velocity && actor.facing==reference.facing,
                    $"Поза изменила физику: fps={fps}, direction={direction}, travel={travel}, sprint={sprint}, x={actor.x}.");
                Require(ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position) && Mathf.Abs(actor.y-Height(direction,actor.x))<.0001f,"Реальный проход потерял опору.");
                if(actor.x>.6f && actor.x<5.4f && Mathf.Abs(actor.vx)>.1f)
                {
                    witnessed++;Require(actor.pose==profile.Select(true,actor.facing*direction>0),"Проход использует flat/run или противоположную наклонную категорию.");
                    if(priorPose==actor.pose) Require(actor.poseSequence==priorSequence && actor.poseTime==priorTime,"Удерживаемая поза выбирается заново каждый кадр.");
                    if(sprint && Mathf.Abs(actor.vx-Mathf.Sign(actor.vx)*settings.runSpeed)<.001f) Require(actor.pose==profile.Select(true,actor.facing*direction>0),"Shift заменил slopeWalk чужим циклом.");
                }
            }
            Require(witnessed>0 && (travel>0?actor.x>=9:actor.x<=-2),"Проход не пересёк весь наклон и обе плоские опоры.");
            Require(!profile.IsMoving(actor.pose) && !profile.IsStanding(actor.pose),"За наклоном осталась наклонная поза.");
        }
        private static void Standing(int fps,int direction,int facing,SlopePoseProfile profile)
        {
            var actor=Actor(direction,3);actor.facing=facing;var motion=new MovementController(Room(direction));float dt=1f/fps;
            motion.Step(actor,Vector2.zero,false,dt);string pose=profile.Select(false,facing*direction>0);uint sequence=actor.poseSequence;var position=actor.Position;
            Require(actor.pose==pose,"Неподвижный контакт не выбрал slopeStand.");
            for(int i=0;i<fps;i++)
            {
                ActorPose.Advance(actor,dt);motion.Step(actor,Vector2.zero,false,dt);
                Require(actor.Position==position && Mathf.Abs(actor.y-Height(direction,3))<.0001f && actor.vx==0 && actor.vy==0 && actor.pose==pose && actor.poseSequence==sequence,
                    "Стоячая поза сдвигает тело или перезапускается.");
                Require(PonyAnimationData.FrameIndex(pose,actor.poseTime,false)==0,"Стойка перестала быть одним исходным кадром.");
            }
        }
        private static void Backwards(int fps,int direction,int facing,SlopePoseProfile profile)
        {
            var actor=Actor(direction,3);var motion=new MovementController(Room(direction));float dt=1f/fps;float lookAt=3+facing*20;
            for(int i=0;i<Mathf.Max(2,fps/8);i++)
            {
                ActorPose.Advance(actor,dt);motion.Step(actor,new Vector2(-facing,0),false,dt,lookAtX:lookAt);
                Require(actor.facing==facing && Mathf.Sign(actor.vx)==-facing && actor.pose==profile.Select(true,facing*direction>0),
                    "Обычный обратный шаг/lookAtX сменил категорию по знаку скорости.");
            }
            uint sequence=actor.poseSequence;float phase=actor.poseTime;var position=actor.Position;
            for(int i=0;i<8;i++) {ActorPose.Advance(actor,0);motion.Step(actor,Vector2.zero,false,0,lookAtX:lookAt);}
            Require(actor.Position==position && actor.poseTime==phase && actor.poseSequence==sequence,"Нулевое время/пауза продвинуло тело или фазу.");
            motion.Step(actor,Vector2.zero,false,dt,lookAtX:3-facing*20);
            Require(actor.facing==-facing && actor.pose==profile.Select(Mathf.Abs(actor.vx)>.1f,-facing*direction>0) && actor.poseSequence==unchecked(sequence+1),
                "Финальный взгляд не выбран до единственного общего изменения позы.");
            for(int i=0;i<fps;i++) {ActorPose.Advance(actor,dt);motion.Step(actor,Vector2.zero,false,dt,lookAtX:3-facing*20);}
            Require(actor.vx==0 && actor.pose==profile.Select(false,-facing*direction>0),"После обратного шага не завершилась остановка.");
            Reject(()=>motion.Step(actor,Vector2.zero,false,dt,lookAtX:float.NaN),"Некорректная цель взгляда");
        }
        private static void Endpoints(int fps,int direction)
        {
            float[] xs=direction>0?new[]{-.5f,-.49f,5.501f,6.6f}:new[]{6.5f,6.49f,.499f,-.6f};
            foreach(bool reverse in new[]{false,true}) foreach(float x in xs)
            {
                var room=Room(direction);if(reverse) Array.Reverse(room.platforms);
                var actor=Actor(direction,x);var support=SlopeLocomotionPose.ResolveSupport(room,Anatomy.Movement(actor),actor.Position);
                Require(support!=null && (support.slopeDirection!=0)==(x==xs[1]),"Flat/slope endpoint зависит от порядка или площади footprint.");
                new MovementController(room).Step(actor,Vector2.zero,false,1f/fps);
                Require(actor.pose.StartsWith("slope",StringComparison.Ordinal)==(x==xs[1]),"Resolved endpoint и контроллер расходятся.");
            }
        }
        private static void Priorities(int fps,int direction,SlopePoseProfile profile)
        {
            float dt=1f/fps;var room=Room(direction);var slope=new SlopeLocomotionPose();var motion=new MovementController(room);var actor=Actor(direction,3);
            actor.y+=1;actor.vy=-2;actor.pose="fall";
            for(int frame=0;frame<fps*2 && !ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);frame++)
            {ActorPose.Advance(actor,dt);motion.Step(actor,Vector2.zero,false,dt);}
            Require(actor.pose=="landing" && Mathf.Abs(actor.y-Height(direction,3))<.0001f,"Приземление на наклон подменено stationary slopeStand.");
            actor.poseTime=Anatomy.PoseDuration(actor,"landing");motion.Step(actor,Vector2.zero,false,dt);
            Require(actor.pose==profile.Select(false,actor.facing*direction>0),"После landing не выбрана стойка на наклоне.");
            actor.pose="jump";StanceAnimation.Select(actor,profile.StandUp,false,room:room,slope:slope);
            Require(actor.pose=="landing","Явный slopeStand target не распознан как стоячее приземление.");
            actor.poseTime=Anatomy.PoseDuration(actor,"landing");
            actor.crouched=true;StanceAnimation.Select(actor,"crouch",false,room:room,slope:slope);
            Require(actor.pose=="crouchLower","Наклон прервал опускание в приседание.");
            actor.poseTime=0;StanceAnimation.Select(actor,"crouch",true,room:room,slope:slope);
            Require(actor.pose=="crouchLower","CrouchLower обрывается до конца.");
            actor.poseTime=PonyAnimationData.Duration("crouchLower");StanceAnimation.Select(actor,"crouch",true,room:room,slope:slope);
            Require(actor.pose=="crouch","Приседание получило standing slope.");
            actor.crouched=false;StanceAnimation.Select(actor,profile.StandDown,true,room:room,slope:slope);
            Require(actor.pose=="crouchRise","Наклонная стойка пропустила подъём из приседания.");
            actor.poseTime=0;StanceAnimation.Select(actor,"idle",false,room:room,slope:slope);Require(actor.pose=="crouchRise","CrouchRise прерван наклоном.");
            actor.poseTime=PonyAnimationData.Duration("crouchRise");StanceAnimation.Select(actor,"idle",false,room:room,slope:slope);Require(profile.IsStanding(actor.pose),"CrouchRise не завершён в наклонную стойку.");
            var crouched=Actor(direction,3);crouched.crouched=true;crouched.pose="crouch";motion.Step(crouched,Vector2.right,false,dt);
            Require(crouched.pose=="crawl" && crouched.crouched,"Наклон заменил crouch locomotion.");
            var airborne=Actor(direction,3);airborne.y+=2;motion.Step(airborne,Vector2.right,false,dt);Require(!airborne.pose.StartsWith("slope",StringComparison.Ordinal),"Наклон выбран без контакта.");
            room.ladders=new[]{new RoomLadder {x=3,bottom=0,top=8,facing=LadderFacing.Left}};
            var climbing=Actor(direction,3);climbing.y=5;climbing.climbing=true;motion=new MovementController(room);motion.Step(climbing,Vector2.up,false,dt,lookAtX:50);
            Require(climbing.climbing && climbing.facing==-1 && !climbing.pose.StartsWith("slope",StringComparison.Ordinal),"Взгляд/наклон заменил ladder приоритет.");
            var passive=Actor(direction,3);passive.down=true;passive.facing=-1;motion.Step(passive,Vector2.right,false,dt,lookAtX:50);
            Require(passive.pose=="down" && passive.facing==-1,"Пассивное тело получило slope/lookAtX.");
            room.ladders=Array.Empty<RoomLadder>();
            foreach(string anatomy in new[]{"raider","radroach"})
            {
                var other=Actor(direction,3,anatomy);motion=new MovementController(room);motion.Step(other,Vector2.right,false,dt);
                Require(other.pose=="walk","Атласная анатомия получила чужую наклонную позу.");
            }
        }
        private static void CheckTies()
        {
            var hull=new MovementHull {width=1,height=1.4f};var room=Room(1);
            var a=new RoomSurface {x=2,y=0,width=2,height=2,slopeDirection=1,oneWay=true};
            var b=new RoomSurface {x=2,y=0,width=2,height=2,slopeDirection=-1,oneWay=true};
            var flat=new RoomSurface {x=3.4f,y=1.3f,width=.2f,height=.2f};var feet=new Vector2(3,1.5f);
            var all=new[]{a,b,flat};
            foreach(int[] permutation in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
            {room.platforms=permutation.Select(i=>all[i]).ToArray();Require(ReferenceEquals(SlopeLocomotionPose.ResolveSupport(room,hull,feet),flat),"Равная flat плоскость проиграла coverage/tie.");}
            foreach(bool reverse in new[]{false,true})
            {room.platforms=reverse?new[]{b,a}:new[]{a,b};Require(ReferenceEquals(SlopeLocomotionPose.ResolveSupport(room,hull,feet),a),"Симметричный slope tie зависит от массива.");}
            var lower=new RoomSurface {x=2,y=1.29f,width=2,height=.2f};
            room.platforms=new[]{lower,a};Require(ReferenceEquals(SlopeLocomotionPose.ResolveSupport(room,hull,feet),a),"Более далёкая flat плоскость подменила реальный slope contact.");
        }
        private static void CheckClips(SlopePoseProfile profile)
        {
            foreach(string pose in new[]{profile.StandUp,profile.StandDown,profile.WalkUp,profile.WalkDown})
            {
                var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose);bool moving=profile.IsMoving(pose);
                Require(clip!=null && clip.frames.Length==(moving?17:1) && clip.fps==30 && clip.loopStartFrame==(moving?1:0) && clip.playIntro==moving && clip.nonLooping!=moving,"Метаданные согласованного исходного клипа не совпали: "+pose);
                foreach(float phase in new[]{0f,.1f,.6f})
                {
                    var sample=new ActorState {id="slope-clip",pose=pose,poseTime=phase};
                    Require(Anatomy.TrySupportContacts(sample,out var contacts) && contacts.Count==4,"Нет общего anatomical support для нового клипа: "+pose);
                    foreach(string region in new[]{"head","torso","legs"}) Require(Anatomy.TryRegion(sample,region,out _,out _),"Нет anatomical region для нового клипа: "+pose+"/"+region);
                }
                Require(PonyAnimationData.FrameIndex(pose,0,false)==0,"Нет исходного кадра1.");
                if(moving) Require(PonyAnimationData.FrameIndex(pose,17f/30,false)==1,"Цикл повторяет intro вместо кадров2–17.");
            }
        }
        private static void CheckMalformed()
        {
            var manifest=new SlopePoseManifest {schema=1,profiles=new[]{new SlopePoseDefinition {anatomy="pony",standUp="su",standDown="sd",walkUp="wu",walkDown="wd"}}};
            var anatomy=new AnatomyCatalog {anatomies=new[]{new AnatomyDefinition {id="pony"}}};
            var library=new PonyLibrary {clips=new[]{new PonyClip {id="su",fps=30,nonLooping=true,frames=new PonyFrame[1]},new PonyClip {id="sd",fps=30,nonLooping=true,frames=new PonyFrame[1]},
                new PonyClip {id="wu",fps=30,playIntro=true,loopStartFrame=1,frames=new PonyFrame[17]},new PonyClip {id="wd",fps=30,playIntro=true,loopStartFrame=1,frames=new PonyFrame[17]}}};
            var prepared=SlopePoseCatalog.Create(manifest,anatomy,library);manifest.profiles[0].standUp="changed";
            Require(prepared["pony"].StandUp=="su","Профиль удерживает mutable declaration.");manifest.profiles[0].standUp="su";
            void Bad(Action<SlopePoseManifest,AnatomyCatalog,PonyLibrary> damage)
            {var m=StateJson.Copy(manifest);var a=StateJson.Copy(anatomy);var l=StateJson.Copy(library);damage(m,a,l);Reject(()=>SlopePoseCatalog.Create(m,a,l),"наклон");}
            Bad((m,a,l)=>m.schema=2);Bad((m,a,l)=>m.profiles=new[]{m.profiles[0],m.profiles[0]});Bad((m,a,l)=>m.profiles[0].anatomy="missing");
            Bad((m,a,l)=>a.anatomies[0].atlasArtwork="atlas");Bad((m,a,l)=>m.profiles[0].standUp=m.profiles[0].standDown);Bad((m,a,l)=>m.profiles[0].walkDown="missing");
            Bad((m,a,l)=>l.clips[0].frames=new PonyFrame[2]);Bad((m,a,l)=>l.clips[2].playIntro=false);Bad((m,a,l)=>l.clips[2].loopStartFrame=0);
            Bad((m,a,l)=>l.clips[2].loopStartFrame=17);Bad((m,a,l)=>l.clips[2].nonLooping=true);Bad((m,a,l)=>l.clips[2].fps=float.NaN);
        }
        private static void Reject(Action action,string context)
        {try {action();} catch(InvalidOperationException error) {Require(error.Message.IndexOf(context,StringComparison.OrdinalIgnoreCase)>=0,"Отказ потерял контекст: "+error.Message);return;}throw new InvalidOperationException("Некорректная декларация принята: "+context);}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Наклонные позы: "+reason);}
    }
}
