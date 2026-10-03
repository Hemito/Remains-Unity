using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class MeleeImpactAudioChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            var root=new GameObject("Проверка звуков контакта ближнего оружия");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var observer=new ActorState {id="source",owner="remote",player=true,connected=true,level="hub",x=-3};
                var target=new ActorState {id="target",owner="remote",actorType="pony",level="hub",x=2};
                world.actors.Add(observer);world.actors.Add(target);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,observer.id);
                var geometry=new SimulationGeometry(session);
                var rooms=(Dictionary<string,RoomLayout>)typeof(SimulationGeometry).GetField("rooms",Private).GetValue(geometry);
                rooms.Add("hub",new RoomLayout {platforms=Array.Empty<RoomSurface>()});
                var combat=new CombatController(session,geometry);
                var playback=root.AddComponent<GameplayAudio>();playback.Initialize(session,combat.Reload,root.AddComponent<Camera>());
                var audio=root.AddComponent<MeleeImpactAudio>();audio.Initialize(combat,playback);
                var receive=typeof(CombatController).GetMethod("ReceiveAttack",Private);
                int expected=0;
                foreach(string weapon in new[]{"crowbar","spear"})
                foreach(string type in new[]{"pony","raider","training-dummy"})
                {
                    target.actorType=type;
                    var attack=new AttackEvent {id=weapon+type,attacker=observer.id,sourceOwner="remote",sourcePlayer=true,
                        weapon=weapon,level="hub",originY=.7f,endX=4,endY=.7f,radius=.08f};
                    receive.Invoke(combat,new object[]{attack});receive.Invoke(combat,new object[]{attack});
                    expected++;
                    Require(audio.Played==expected,"Контакт пропущен или повторён: "+attack.id);
                    var source=root.GetComponentsInChildren<AudioSource>().Last();
                    var clip=Resources.Load<AudioClip>("Original/Audio/"+(weapon=="spear"?"hit-blade":"hit-flesh"));
                    Require(source.clip==clip,"Неверный WAV: "+attack.id);
                    float gain=weapon=="spear"?.8f:.5f;
                    Require(Mathf.Abs(source.volume-playback.Volume*gain*GameplayAudio.Attenuation(Vector2.Distance(attack.End,observer.Position)))<.00001f,"Неверная громкость: "+attack.id);
                    Require(!Application.isBatchMode || source.mute,"Фоновый звук не заглушен.");
                    target.id+="-next";
                    receive.Invoke(combat,new object[]{attack});expected++;
                    Require(audio.Played==expected,"Вторая цель одного взмаха ошибочно заглушена.");
                }
                var miss=new AttackEvent {id="miss",attacker=observer.id,sourceOwner="remote",sourcePlayer=true,
                    weapon="crowbar",level="hub",originY=20,endX=4,endY=20};
                receive.Invoke(combat,new object[]{miss});
                Require(audio.Played==expected,"Промах создал звук контакта.");
                world.friendlyFire=false;target.player=true;target.connected=true;
                miss.id="friendly-disabled";miss.originY=miss.endY=.7f;
                receive.Invoke(combat,new object[]{miss});
                Require(audio.Played==expected,"Выключенный дружественный огонь создал контакт с союзником.");
                Debug.Log("Контакты ближнего оружия: PASS — реальные пересечения, два WAV и уровня, три типа целей, повтор/вторая цель, промах и выключенный FF.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка звука ближнего оружия: "+reason);}
    }
}
