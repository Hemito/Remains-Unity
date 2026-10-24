using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class SlopeAnimationAudioChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const float Duration=32f/15,Speed=.2f;
        private static readonly string[] Paths={FactoryWorkshopDraft.Path,FactoryOfficeDraft.Path,FactoryWarehouseDraft.Path};
        private sealed class PathFixture
        {public RoomLayout room;public Vector2 start;public int direction;public string bank;}

        public static void Run()
        {
            Anatomy.Load();
            var settings=JsonUtility.FromJson<MovementAudio.Settings>(GameAssets.Load<TextAsset>("Presentation/movement-audio").text);
            CheckDeclarations(settings);
            int cases=0;
            foreach(string path in Paths)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var authoring=prefab==null?null:prefab.GetComponent<RoomAuthoring>();
                Require(authoring!=null,"Нет сохранённой комнаты: "+path);
                var room=authoring.CreateLayout(false);
                string before=StateJson.ToJson(room);
                foreach(bool uphill in new[]{true,false})
                {
                    var fixture=FindPath(room,uphill,path);
                    foreach(int fps in new[]{30,60,144})
                    foreach(bool remote in new[]{false,true})
                    {CheckConsumer(fixture,settings,uphill,fps,remote);cases++;}
                }
                Require(before==StateJson.ToJson(room),"Звуковая проверка изменила сохранённую геометрию: "+path);
            }
            Debug.Log($"Аудио наклонных поз: PASS — {cases} локальных/удалённых случаев на сохранённых18/53/40; 16 actual voices за четыре цикла при30/60/144FPS, материал опоры, тихая стойка, остановка/пауза/возврат/смена поколения/разрыв/коррекция без повторов. Это проверка presentation, не обычного маршрута оригинала.");
        }

        private static void CheckDeclarations(MovementAudio.Settings settings)
        {
            Require(settings?.rhythms!=null && settings.quietPoses!=null,"Нет каталога ритмов движения");
            var walk=Find(settings,"walk");
            var expected=new[]{5f/30,7f/30,13f/30,15f/30};
            Require(Mathf.Abs(walk.period-16f/30)<.000001f && walk.beats.Length==4,"Изменён базовый walk rhythm");
            for(int i=0;i<expected.Length;i++) Require(Mathf.Abs(walk.beats[i]-expected[i])<.000001f,"Изменена отметка walk: "+i);
            foreach(string pose in new[]{"slopeWalkUp","slopeWalkDown"})
            {
                var pattern=Find(settings,pose);pattern.Validate();
                var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose);
                Require(clip!=null && !clip.nonLooping && clip.playIntro && clip.loopStartFrame==1 &&
                    Mathf.Abs(pattern.period-(clip.frames.Length-clip.loopStartFrame)/clip.fps)<.000001f,
                    "Ритм не соответствует объявленному циклу resource clip: "+pose);
                Require(pattern.period==walk.period && pattern.beats.Length==walk.beats.Length,"Наклонный ритм не объявлен адаптацией walk: "+pose);
                for(int i=0;i<walk.beats.Length;i++) Require(pattern.beats[i]==walk.beats[i],"Неверная наклонная отметка: "+pose+"/"+i);
                Require(Array.IndexOf(settings.quietPoses,pose)<0,"Движущаяся наклонная поза объявлена тихой: "+pose);
            }
            foreach(string pose in new[]{"slopeStandUp","slopeStandDown"})
            {
                var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose);
                Require(clip!=null && clip.frames.Length==1 && clip.nonLooping,"Quiet стойка не соответствует неподвижному resource clip: "+pose);
                Require(Array.Find(settings.rhythms,p=>p.pose==pose)==null && Array.FindAll(settings.quietPoses,p=>p==pose).Length==1,
                    "Нет единственной явной тихой наклонной стойки: "+pose);
            }
        }
        private static MovementBeatPattern Find(MovementAudio.Settings settings,string pose)
        {
            var matches=Array.FindAll(settings.rhythms,p=>p.pose==pose);
            Require(matches.Length==1,"Нет единственного declared rhythm: "+pose);return matches[0];
        }

        // The fixture searches only saved surfaces. Position comes from the actual
        // terrain solver, without a second slope equation or invented material.
        private static PathFixture FindPath(RoomLayout room,bool uphill,string path)
        {
            var hull=Anatomy.Movement("pony");
            foreach(var surface in room.platforms)
            {
                if(surface.slopeDirection==0) continue;
                int direction=surface.slopeDirection*(uphill?1:-1);
                for(int sample=0;sample<=48;sample++)
                {
                    float x=surface.x-hull.width*.5f+.001f+(surface.width+hull.width-.002f)*sample/48;
                    var start=new Vector2(x,RoomSupport.Top(surface,x,hull.width));
                    var position=start;string bank=null;bool valid=true;
                    for(int frame=0;frame<=128;frame++)
                    {
                        if(frame>0) position=ActorTerrainMotion.Move(room,hull,position,new Vector2(direction*(Speed*Duration+.03f)/128,-.001f),false);
                        var support=SlopeLocomotionPose.ResolveSupport(room,hull,position);
                        string current=SurfaceContactSound.Resolve(room,hull,position);
                        if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position) ||
                            support==null || support.slopeDirection!=surface.slopeDirection || current!=surface.movementSound || string.IsNullOrEmpty(current))
                        {valid=false;break;}
                        bank??=current;
                    }
                    if(valid && Mathf.Abs(position.x-start.x)>Speed*Duration-.002f && Mathf.Abs(position.y-start.y)>.15f)
                        return new PathFixture {room=room,start=start,direction=direction,bank=bank};
                }
            }
            throw new InvalidOperationException($"Аудио наклона: нет конечного clear/supported пути по настоящей опоре; asset={path}, room={room.id}, uphill={uphill}. Геометрия/материал не подменены.");
        }

        private static void CheckConsumer(PathFixture fixture,MovementAudio.Settings settings,bool uphill,int fps,bool remote)
        {
            string moving=uphill?"slopeWalkUp":"slopeWalkDown",standing=uphill?"slopeStandUp":"slopeStandDown";
            var root=new GameObject("Проверка аудио наклонной позы");SimulationGeometry geometry=null;
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var actor=new ActorState {id="slope-subject",owner=remote?"remote":"local",player=true,connected=true,level="factory:0",
                    actorType="pony",pose=moving,facing=fixture.direction,vx=fixture.direction*Speed,Position=fixture.start};
                var listener=remote?new ActorState {id="observer",owner="local",player=true,connected=true,actorType="training-dummy",level=actor.level,Position=fixture.start}:actor;
                world.actors.Add(actor);if(remote) world.actors.Add(listener);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,listener.id);
                geometry=new SimulationGeometry(_=>fixture.room);geometry.Reconcile(world);
                var playback=root.AddComponent<GameplayAudio>();playback.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());
                var audio=root.AddComponent<MovementAudio>();audio.Initialize(session,geometry,playback);
                var reconcile=typeof(MovementAudio).GetMethod("Reconcile",Private);var step=typeof(MovementAudio).GetMethod("LateUpdate",Private);
                reconcile.Invoke(audio,null);step.Invoke(audio,null);
                var hull=Anatomy.Movement(actor);int voices=0;
                void Sample(float phase,float distance=0)
                {
                    if(distance!=0) actor.Position=ActorTerrainMotion.Move(fixture.room,hull,actor.Position,new Vector2(fixture.direction*distance,-.001f),false);
                    actor.poseTime=phase;step.Invoke(audio,null);
                }
                void Expect(int added,string context)
                {
                    int actual=root.GetComponentsInChildren<AudioSource>().Length;
                    Require(actual==voices+added,$"{context}: pose={moving}, fps={fps}, remote={remote}, expectedVoices={voices+added}, actual={actual}, position={actor.Position}, phase={actor.poseTime}");
                    voices=actual;
                }
                float previous=0;
                for(int frame=1;frame<=Math.Ceiling(Duration*fps);frame++)
                {
                    float phase=Mathf.Min(frame/(float)fps,Duration);
                    Sample(phase,Speed*(phase-previous));previous=phase;
                    Require(ActorTerrainMotion.Supported(fixture.room,hull,actor.Position) && ActorTerrainMotion.IsClear(fixture.room,hull,actor.Position),"Путь потерял реальную опору/clearance");
                    Require(SurfaceContactSound.Resolve(fixture.room,hull,actor.Position)==fixture.bank,"Ритм выбрал материал не своей опоры");
                }
                Expect(16,"Четыре цикла");
                Require((remote?audio.RemoteContacts:audio.LocalContacts)==16 && (remote?audio.LocalContacts:audio.RemoteContacts)==0 &&
                    audio.Landings==0 && audio.LandingVoices==0 && audio.LadderContacts==0,"Наклонные шаги подменены посадкой/лестницей/другим actor");
                var bank=Array.Find(settings.banks,b=>b.id==fixture.bank);
                Require(bank!=null,"Нет действительного банка опоры: "+fixture.bank);
                var sources=root.GetComponentsInChildren<AudioSource>();
                for(int i=0;i<sources.Length;i++)
                    Require(sources[i].clip==GameAssets.Load<AudioClip>(bank.clips[i%bank.clips.Length]) && sources[i].volume>0 &&
                        Mathf.Abs(sources[i].volume-playback.Volume*settings.volume)<.00001f && (!Application.isBatchMode || sources[i].mute),
                        "Actual voice потерял WAV/громкость/подавление batchmode: "+i);
                Sample(actor.poseTime);Expect(0,"Повторный снимок");
                actor.vx=0;ActorPose.Select(actor,standing);Sample(3);Expect(0,"Тихая стойка");
                ActorPose.Select(actor,moving);Sample(.18f,.001f);Expect(0,"Неподвижная/первая возобновлённая фаза");
                actor.vx=fixture.direction*Speed;Sample(.24f,.001f);Expect(1,"Новый шаг после остановки");
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
                paused.Add(actor.level);Sample(.8f,.001f);Expect(0,"Пауза");
                paused.Remove(actor.level);Sample(.8f);Expect(0,"Возобновление без восстановления пропущенных шагов");
                Sample(.97f,.001f);Expect(1,"Следующая действительная отметка после паузы");
                Sample(3.2f,.001f);Expect(0,"Разрыв фазы не воспроизводит историю");
                Sample(3.22f,.001f);Expect(0,"Первый короткий интервал после разрыва");
                ActorPose.Select(actor,standing);Sample(0);ActorPose.Select(actor,moving);Sample(0);
                Sample(.18f,.001f);Expect(1,"Первый шаг новой серии");
                Sample(.16f,.001f);Expect(0,"Коррекция назад");Sample(.19f,.001f);Expect(0,"Повторное пересечение старой отметки");
                Sample(.24f,.001f);Expect(1,"Следующая отметка после коррекции");
                actor.generation++;Sample(0);Expect(0,"Новая owner generation");
                var restored=StateJson.Copy(actor);int index=world.actors.IndexOf(actor);world.actors[index]=restored;actor=restored;
                if(!remote) listener=actor;
                reconcile.Invoke(audio,null);Sample(0);Expect(0,"Rebind того же восстановленного снимка");
                Sample(.18f,.001f);Expect(1,"Новый шаг после generation/rebind");
                ActorSessionRules.Disconnect(actor);Sample(1);Expect(0,"Отключённое тело");
                ActorSessionRules.Connect(actor,"reconnected");ActorPose.Select(actor,moving);Sample(0);Expect(0,"Reconnect baseline");
                Sample(.18f,.001f);Expect(1,"Первый новый шаг подключённого владельца");
                listener.level="hub";geometry.Reconcile(world);reconcile.Invoke(audio,null);
                listener.level="factory:0";geometry.Reconcile(world);reconcile.Invoke(audio,null);Sample(actor.poseTime);Expect(0,"Возврат слушателя/уровня не повторил старый шаг");
                Require(actor.inventory.Count==0 && actor.inventoryUses.Count==0 && world.completedTransactions.Count==0,"Аудио наблюдатель изменил инвентарь/журнал");
            }
            finally {geometry?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Аудио наклонной позы: "+detail);}
    }
}
