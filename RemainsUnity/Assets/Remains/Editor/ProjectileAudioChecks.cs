using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class ProjectileAudioChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            var mix=AudioMixSettings.Load();
            Require(mix.Pan(10,10)==0 && mix.Pan(-100,0)==-1 && mix.Pan(100,0)==1,"Панорама не центрируется или выходит за границы.");
            Require(Mathf.Abs(mix.Pan(-10,0)+mix.Pan(10,0))<.00001f,"Панорама несимметрична.");
            Check("Hub/concrete","hit-concrete",false);
            Check("floor","hit-metal",false);
            Check("Hub/concrete","hit-bullet",true);
            CheckAttackMix();
            CheckDistantAttacks();
            Debug.Log("Звуки попаданий: PASS — бетон, металл, актёр перед стеной, отсутствие повторов.");
        }
        public static void CheckDistantAttacks()
        {
            var root=new GameObject("Проверка дальних звуков атак");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var actor=new ActorState {id="listener",player=true,connected=true,level="hub"};
                world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                var playback=root.AddComponent<GameplayAudio>();
                playback.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());
                var receive=typeof(GameplayAudio).GetMethod("Attack",Private);
                for(int i=0;i<64;i++)
                    receive.Invoke(playback,new object[]{new AttackEvent {id="distant-"+i,weapon="pistol",attacker="remote",level="hub",originX=30}});
                Require(root.GetComponentsInChildren<AudioSource>().Length==0,
                    "Неслышимые дальние атаки заняли источники и могут заглушить близкий выстрел");
                actor.x=30;
                receive.Invoke(playback,new object[]{new AttackEvent {id="distant-0",weapon="pistol",attacker="remote",level="hub",originX=30}});
                Require(root.GetComponentsInChildren<AudioSource>().Length==0,"Поздний повтор далёкого выстрела зазвучал после приближения");
                receive.Invoke(playback,new object[]{new AttackEvent {id="nearby",weapon="pistol",attacker="remote",level="hub",originX=31}});
                var source=root.GetComponentInChildren<AudioSource>();
                Require(source!=null && source.volume>0 && playback.RemoteShots==1,
                    "Близкий выстрел потерян после потока неслышимых атак");
                Require(!Application.isBatchMode || source.mute,"Фоновая проверка не заглушена");
                Debug.Log("Дальние атаки: PASS — не занимают источники, поздний повтор не звучит, близкий выстрел сохранён.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckAttackMix()
        {
            var root=new GameObject("Проверка баланса звуков атак");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var actor=new ActorState {id="listener",player=true,connected=true,level="hub"};
                world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                var playback=root.AddComponent<GameplayAudio>();
                playback.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());
                var receive=typeof(GameplayAudio).GetMethod("Attack",Private);
                foreach(var weapon in new[]{"pistol","energy","grenade","crowbar","spear"})
                {
                    var attack=new AttackEvent {id="mix-"+weapon,weapon=weapon,attacker=actor.id,level=actor.level};
                    int before=root.GetComponentsInChildren<AudioSource>().Length;
                    receive.Invoke(playback,new object[]{attack});
                    var sources=root.GetComponentsInChildren<AudioSource>();
                    Require(sources.Length==before+1,"Не создан источник атаки: "+weapon);
                    var source=sources.Last();
                    float initial=source.volume;
                    bool melee=weapon is "crowbar" or "spear";
                    Require(melee ? initial>=playback.Volume*.5f && initial<=playback.Volume : Mathf.Abs(initial-playback.Volume)<.00001f,
                        "Неверный относительный уровень: "+weapon);
                    Require(!Application.isBatchMode || source.mute,"Фоновая проверка атаки не заглушена");
                    receive.Invoke(playback,new object[]{attack});
                    Require(root.GetComponentsInChildren<AudioSource>().Length==before+1,"Повтор атаки создал второй звук: "+weapon);
                    playback.Volume*=.5f;
                    typeof(GameplayAudio).GetMethod("Update",Private).Invoke(playback,null);
                    Require(Mathf.Abs(source.volume-initial*.5f)<.00001f,"Громкость повторно выбрана при обновлении микса: "+weapon);
                    playback.Volume=playback.BaseVolume;
                }
                Debug.Log("Баланс атак: PASS — уровни каталога, отсутствие повторов, сохранение усиления при изменении громкости.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Check(string texture,string expected,bool actorBlocks)
        {
            var root=new GameObject("Проверка звуков контакта");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var observer=new ActorState {id="observer",owner="listener",player=true,connected=true,level="hub",x=-2};
                world.actors.Add(observer);
                if(actorBlocks) world.actors.Add(new ActorState {id="target",actorType="training-dummy",level="hub",x=2});
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,observer.id);
                // The near surface passes shots; the farther one must supply the sound key.
                var geometry=new SimulationGeometry(_=>new RoomLayout {platforms=new[]{
                    new RoomSurface {x=1,y=0,width=.1f,height=2,texture="ignored",shotsPassThrough=true},
                    new RoomSurface {x=4,y=0,width=.2f,height=2,texture=texture}}});
                geometry.Reconcile(world);
                int hits=0,contacts=0;
                var controller=new ProjectileController(session,geometry,(_,_,_)=>hits++);
                controller.ContactObserved+=contact=>
                {
                    contacts++;
                    Require(actorBlocks?contact.target=="target":contact.surfaceTexture==texture && Mathf.Abs(contact.position.x-4)<.0001f,
                        "Событие не соответствует ближайшему непрозрачному препятствию");
                };
                var camera=root.AddComponent<Camera>();
                var playback=root.AddComponent<GameplayAudio>();playback.Initialize(session,new WeaponReloadController(session),camera);
                var audio=root.AddComponent<ProjectileAudio>();audio.Initialize(controller,playback);
                world.projectiles.Add(new ProjectileState {launch=new AttackEvent {id="sound-shot",attacker="source",sourceOwner="remote",
                    sourcePlayer=true,weapon="pistol",level="hub",originY=.7f,endX=8,endY=.7f},owner="remote",generation=1,flightSpeed=90});
                typeof(ProjectileController).GetMethod("Reconcile",Private).Invoke(controller,null);
                controller.Step(.1f);controller.Step(.1f);
                var sources=root.GetComponentsInChildren<AudioSource>();
                Require(contacts==1 && hits==(actorBlocks?1:0) && audio.Played==1 && sources.Length==1,
                    "Пропущен или повторён контакт/звук; стена за актёром не должна звучать");
                Require(sources[0].clip==Resources.Load<AudioClip>("Original/Audio/"+expected),"Выбран неверный WAV");
                Require(!Application.isBatchMode || sources[0].mute,"Фоновая проверка не заглушена");
                Require(sources[0].panStereo>0,"Правый источник не слышен справа.");
                camera.transform.position=new Vector3(20,0,-10);
                typeof(GameplayAudio).GetMethod("Update",Private).Invoke(playback,null);
                Require(sources[0].panStereo<0,"Активный звук не следует за изменением положения камеры.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка звука попадания: "+reason);}
    }
}
