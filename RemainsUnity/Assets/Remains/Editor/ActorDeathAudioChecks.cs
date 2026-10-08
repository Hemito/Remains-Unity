using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorDeathAudioChecks
    {
        private sealed class Hit : IDamageRule
        {private readonly float value;public Hit(float value)=>this.value=value;public float Calculate(string weapon,float speed)=>value;}
        public static void Run()
        {
            CheckOutcomes();CheckCatalog();ActorDeathAudioAuthoring.Check();
            Debug.Log("Голоса смерти NPC: PASS — actual damage outcome, новый смертельный контакт, повтор/снимок/загрузка/owner-life-world guards, Player KO и явные исходные банки. Native воспроизведение проверяется отдельно.");
        }
        private static ActorState Npc(string id)=>new() {id=id,actorType="radroach",anatomy="radroach",level="factory:0",health=8,generation=3,lifecycleSequence=4};
        private static DamageResult Apply(WorldState world,ActorState actor,float damage,string attack)
        {
            var receiver=new DamageReceiver(new Hit(damage));DamageResult result=default;int calls=0;
            receiver.Applied+=value=>{result=value;calls++;};
            Require(receiver.Apply(actor,new AttackEvent {id=attack,attacker="source",weapon="pistol",level=actor.level},true),"Actual damage отклонён.");
            Require(calls==1 && result.actor==actor.id,"Actual damage не породил ровно одно подтверждение.");
            result.world=world.worldId; // The actual DamageFeedback publisher adds the current world.
            return result;
        }
        private static void CheckOutcomes()
        {
            var world=new WorldState {worldId="death-check-world"};var npc=Npc("death-check-npc");world.actors.Add(npc);
            var receipts=new ActorDeathReceipts();receipts.Bind(world);
            var initial=StateJson.Copy(world);
            var nonlethal=Apply(world,npc,3,"nonlethal");
            Require(!nonlethal.confirmedNpcDeath && !receipts.TryObserve(world,nonlethal) && npc.health==5,"Несмертельный урон породил голос смерти.");
            var death=Apply(world,npc,10,"lethal");
            Require(death.confirmedNpcDeath && npc.down && npc.health==0 && death.actorType=="radroach" &&
                death.lifecycleSequence==4 && death.damageSequence==2 && DamageFeedback.Valid(death),"Actual NPC death metadata потеряна.");
            Require(receipts.TryObserve(world,death) && !receipts.TryObserve(world,death),"Новая смерть/повтор не соответствуют одному событию.");
            var changedReceipt=death;changedReceipt.damageSequence++;changedReceipt.attack="other-lethal-receipt";
            Require(!receipts.TryObserve(world,changedReceipt),"Другой attack/damageSequence повторил голос в той же жизни NPC.");
            var saved=StateJson.Copy(world);receipts.Bind(saved);
            Require(!receipts.TryObserve(saved,death),"Повторный снимок породил голос смерти.");
            var loaded=new ActorDeathReceipts();loaded.Bind(saved);
            Require(!loaded.TryObserve(saved,death),"Загрузка уже мёртвого тела породила исторический голос.");
            npc.generation++;var handoff=death;handoff.generation=npc.generation;
            Require(!receipts.TryObserve(world,handoff),"Смена поколения владельца повторила смерть той же жизни.");
            foreach(int fault in Enumerable.Range(0,8))
            {
                var clean=StateJson.Copy(initial);var guard=new ActorDeathReceipts();guard.Bind(clean);var invalid=death;
                if(fault==0)invalid.generation=-1;
                if(fault==1)invalid.lifecycleSequence++;
                if(fault==2)invalid.world="other-world";
                if(fault==3)invalid.actorType="raider";
                if(fault==4)invalid.damageSequence=0;
                if(fault==5)invalid.confirmedNpcDeath=false;
                if(fault==6)clean.actors[0].player=true;
                if(fault==7)invalid.generation++;
                Require(!guard.TryObserve(clean,invalid),"Принят неверный death context: case="+fault);
            }
            // A remote peer can receive the confirmed cue before its reliable body snapshot.
            var remote=new ActorDeathReceipts();var remoteWorld=StateJson.Copy(initial);remote.Bind(remoteWorld);
            Require(remote.TryObserve(remoteWorld,death),"Правильный owner cue ошибочно зависит от отстающих remote HP/down.");
            var transferred=StateJson.Copy(initial);var late=new ActorDeathReceipts();late.Bind(transferred);transferred.actors[0].generation++;
            Require(late.TryObserve(transferred,death) && !late.TryObserve(transferred,death),
                "Confirmed receipt после смены владельца потерян или продублирован по новому generation.");
            transferred.actors[0].generation++;var newerReceipt=death;newerReceipt.generation=transferred.actors[0].generation;newerReceipt.damageSequence++;
            Require(!late.TryObserve(transferred,newerReceipt),"Новое поколение/sequence повторило смерть той же жизни NPC.");
            var newWorld=StateJson.Copy(initial);newWorld.worldId="next-world";remote.Bind(newWorld);
            Require(!remote.TryObserve(newWorld,death),"Реплика прошлого мира вошла в новый мир с теми же actor IDs.");
            var player=new ActorState {id="player-check",actorType="pony",anatomy="pony",player=true,health=7,level="factory:0",generation=1};
            world.actors.Add(player);var ko=Apply(world,player,100,"player-ko");
            Require(player.down && player.health==0 && !ko.confirmedNpcDeath && !receipts.TryObserve(world,ko),"KO игрока принят за смерть NPC.");
            // Binding a living actor already carrying old damage is not a replay permission.
            var past=StateJson.Copy(initial);past.actors[0].damageSequence=death.damageSequence;
            var freshBind=new ActorDeathReceipts();freshBind.Bind(past);
            Require(!freshBind.TryObserve(past,death),"Новое подключение воспроизвело исторический outcome живого mod NPC.");
        }
        private static void CheckCatalog()
        {
            var catalog=ActorDeathAudioCatalog.Load();
            foreach(var item in new[]{("raider","rm",6),("radroach","bloat",4)})
            for(int i=0;i<item.Item3;i++)
                Require(catalog.TrySelect(item.Item1,i,out var sound) && sound.Resource=="Original/Audio/ActorDeath/"+item.Item2+(i+1) &&
                    sound.Clip!=null && sound.Clip.samples>0 && sound.Gain==1,"Изменён исходный voice bank: "+item.Item1+"/"+i);
            Require(!catalog.TrySelect("not-declared",0,out _),"Необъявленный тип получил подставной голос.");
            foreach(int fault in Enumerable.Range(0,7))
            {
                var data=new ActorDeathAudioCatalog.Settings {schema=1,entries=new[]{new ActorDeathAudioCatalog.Entry {actorType="mod:npc",sourceBank="mod",gain=1,clips=new[]{"mod/clip"}}}};
                if(fault==0)data.schema=0;
                if(fault==1)data.entries[0].actorType="";
                if(fault==2)data.entries[0].clips=Array.Empty<string>();
                if(fault==3)data.entries[0].gain=float.NaN;
                if(fault==4)data.entries=new[]{data.entries[0],data.entries[0]};
                if(fault==5)data.entries[0].clips=new[]{"mod/clip","mod/clip"};
                if(fault==6)data.entries[0].sourceBank="";
                int lookups=0;bool rejected=false;
                try {ActorDeathAudioCatalog.Create(data,key=>{lookups++;return null;});}catch(InvalidOperationException){rejected=true;}
                Require(rejected && lookups==0,"Неверный модовый банк дошёл до asset lookup: case="+fault);
            }
            var clip=AudioClip.Create("Проверка модового голоса",8,1,44100,false);
            try
            {
                int lookups=0;
                var data=new ActorDeathAudioCatalog.Settings {schema=1,entries=new[]{
                    new ActorDeathAudioCatalog.Entry {actorType="mod:a",sourceBank="mod",gain=.5f,clips=new[]{"mod/clip"}},
                    new ActorDeathAudioCatalog.Entry {actorType="mod:b",sourceBank="mod",gain=1,clips=new[]{"mod/clip"}}}};
                var custom=ActorDeathAudioCatalog.Create(data,key=>{lookups++;return clip;});
                data.entries[0].clips[0]="mutated";data.entries[0].gain=0;
                Require(lookups==1 && custom.TrySelect("mod:a",0,out var sound) && sound.Resource=="mod/clip" && sound.Gain==.5f,
                    "Prepared mod bank повторно ищет shared resource или зависит от mutable source.");
                bool missing=false;try {ActorDeathAudioCatalog.Create(new ActorDeathAudioCatalog.Settings {schema=1,entries=new[]{
                    new ActorDeathAudioCatalog.Entry {actorType="missing",sourceBank="mod",gain=1,clips=new[]{"missing"}}}},key=>null);}
                catch(InvalidOperationException){missing=true;}
                Require(missing,"Отсутствующий клип принят как тихий fallback.");
            }
            finally {UnityEngine.Object.DestroyImmediate(clip);}
        }
        private static void Require(bool value,string reason)
        {if(!value)throw new InvalidOperationException("Голоса смерти NPC: "+reason);}
    }
}
