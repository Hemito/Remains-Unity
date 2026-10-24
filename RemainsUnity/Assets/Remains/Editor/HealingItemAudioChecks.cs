using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class HealingItemAudioChecks
    {
        private static int conditions;
        public static void Run()
        {
            conditions=0;HealingItemAudioAuthoring.Check();CheckDeclaration();
            var root=new GameObject("Неактивная сессия проверки лечебного звука");root.SetActive(false);
            try
            {
                Require(!Mirror.NetworkServer.active && !Mirror.NetworkClient.active,"Detached Editor fixture требует отключённую сеть");
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState {worldId="healing-audio-world"};
                var healer=new ActorState {id="healer",owner="fixture",level="hub",player=true,connected=true,health=60,generation=2,lifecycleSequence=7};
                var kit=new ItemStack {id="kit",definition="heal",count=2};healer.inventory.Add(kit);world.actors.Add(healer);
                typeof(CoopSession).GetProperty(nameof(CoopSession.ServerWorld)).SetValue(session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,healer.id);
                var source=new SessionHealingAudio(session);int published=0;HealingSoundCue captured=default;
                source.Published+=cue=>{published++;captured=cue;};
                var earlyWorld=StateJson.Copy(world);
                Reject(()=>SessionHealingAudio.Capture(world,healer,"uncompleted","heal"),"Capture без completed transaction");
                Require(HealingRules.ApplyOnce(world,healer,healer,true,kit.id,"heal:once") && healer.health==95 && kit.count==1 &&
                    healer.lifecycleSequence==8 && world.completedTransactions.Contains("heal:once"),"Фактическое лечение не дало HP60→95/kit2→1");
                string after=StateJson.ToJson(world);
                captured=SessionHealingAudio.Capture(world,healer,"heal:once","heal");
                Require(StateJson.ToJson(world)==after && captured.actor==healer.id && captured.definition=="heal" && captured.Position==healer.BodyPosition &&
                    captured.generation==healer.generation && captured.lifecycle==healer.lifecycleSequence,"Capture изменил JSON или потерял source body/lifetime");
                // This tests the actual service's Published dedup before transport.
                // The two inactive-server warnings on the first Publish are expected;
                // no connections, sockets or native delivery are claimed here.
                Debug.Log("Лечебный звук: далее ожидаются две inactive-server Mirror warning detached Publish; доставка проверяется ordinary player отдельно.");
                source.Publish(healer,"heal:once","heal");
                Require(published==1,"Успех не опубликовал одну actual cue");
                Require(HealingRules.ApplyOnce(world,healer,healer,true,kit.id,"heal:once") && StateJson.ToJson(world)==after,"Duplicate изменил HP/kit/life");
                source.Publish(healer,"heal:once","heal");Require(published==1,"Duplicate опубликовал вторую cue");
                CheckReceipts(world,earlyWorld,captured);
                CheckRejections(source,world,healer,kit,()=>published);
                CheckRevival();CheckConsumer(session,source,captured);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log($"Лечебный звук: PASS — {conditions} условий, ApplyOnce→pure Capture HP60→95/kit2→1; отказ/fullHP/duplicate без новой Published, early wrong context не потребляет receipt, identity/gen/life/level/finite guards, actual revival и AudioSource, native PCM/catalog. Detached delivery не является native сетью или слышимым mix.");
        }
        private static void CheckReceipts(WorldState world,WorldState early,HealingSoundCue cue)
        {
            var receipts=new HealingAudioReceipts();string before=StateJson.ToJson(early),after=StateJson.ToJson(world);
            Require(!receipts.Accept(early,cue) && StateJson.ToJson(early)==before,"Early pre-heal life приняла cue/изменила snapshot");
            Require(receipts.Accept(world,cue) && !receipts.Accept(world,cue) && StateJson.ToJson(world)==after,
                "Корректная reliable copy не принята ровно один раз после раннего отказа");
            foreach(string fault in new[]{"world","actor","generation","lifecycle","level","x","y","transaction","definition"})
            {
                var bad=cue;
                if(fault=="world")bad.world="foreign";if(fault=="actor")bad.actor="absent";
                if(fault=="generation")bad.generation++;if(fault=="lifecycle")bad.lifecycle++;
                if(fault=="level")bad.level="factory:0";if(fault=="x")bad.x=float.NaN;if(fault=="y")bad.y=float.PositiveInfinity;
                if(fault=="transaction")bad.transaction="";if(fault=="definition")bad.definition=null;
                var tracker=new HealingAudioReceipts();
                Require(!tracker.Accept(world,bad) && tracker.Accept(world,cue) && !tracker.Accept(world,cue),
                    "Неверный context потребил receipt: "+fault);
            }
            var disconnected=StateJson.Copy(world);disconnected.Find(cue.actor).connected=false;
            var absentContext=new HealingAudioReceipts();
            Require(!absentContext.Accept(null,cue) && !absentContext.Accept(disconnected,cue) && absentContext.Accept(world,cue),
                "Отсутствующий/disconnected actor потребил receipt");
            receipts.Clear();Require(receipts.Accept(world,cue) && !receipts.Accept(world,cue),"Reset не очистил старый world context");
            var other=StateJson.Copy(world);other.worldId="next-world";var next=cue;next.world=other.worldId;
            Require(receipts.Accept(other,next) && !receipts.Accept(other,next),"Другой actual world не получил свой однократный receipt");
        }
        private static void CheckRejections(SessionHealingAudio source,WorldState world,ActorState actor,ItemStack kit,Func<int> published)
        {
            foreach(string fault in new[]{"full-health","down","disconnected","reserved","wrong-item"})
            {
                float hp=actor.health;bool down=actor.down,connected=actor.connected;
                if(fault=="full-health")actor.health=100;if(fault=="down")actor.down=true;if(fault=="disconnected")actor.connected=false;
                if(fault=="reserved")actor.inventoryReservations[kit.id]=kit.count;
                string before=StateJson.ToJson(world),transaction="rejected:"+fault;int count=published();
                Require(!HealingRules.ApplyOnce(world,actor,actor,true,fault=="wrong-item"?"absent-kit":kit.id,transaction) &&
                    StateJson.ToJson(world)==before && !world.completedTransactions.Contains(transaction),"Rejected heal изменил world: "+fault);
                Reject(()=>source.Publish(actor,transaction,"heal"),"Rejected Publish: "+fault);
                Require(published()==count && StateJson.ToJson(world)==before,"Rejected heal опубликовал cue/изменил JSON: "+fault);
                actor.health=hp;actor.down=down;actor.connected=connected;actor.inventoryReservations.Remove(kit.id);
            }
            var detached=actor.Copy();
            Reject(()=>SessionHealingAudio.Capture(world,detached,"heal:once","heal"),"Другой actor object подставлен вместо actual world actor");
            Reject(()=>SessionHealingAudio.Capture(world,actor,"heal:once",""),"Пустой definition cue");
        }
        private static void CheckRevival()
        {
            var world=new WorldState {worldId="revival-audio-world"};
            var healer=new ActorState {id="ally",player=true,connected=true,level="hub",x=0};
            var target=new ActorState {id="down",player=true,connected=true,level="hub",x=1,health=0,down=true};
            healer.inventory.Add(new ItemStack {id="revival-kit",definition="heal",count=2});world.actors.Add(healer);world.actors.Add(target);
            var progress=new RevivalProgress();
            Require(!progress.Step(healer,target,true,.75f),"Incomplete revival завершилась до timed progress");
            Reject(()=>SessionHealingAudio.Capture(world,healer,"revive","heal"),"Незавершённое поднятие получило cue");
            Require(progress.Step(healer,target,true,.75f) && HealingRules.ApplyOnce(world,healer,target,false,"revival-kit","revive") &&
                target.health==35 && !target.down && InventoryJournal.Count(healer,"heal")==1,"Actual timed revival не дала HP35/kit−1");
            string before=StateJson.ToJson(world);var cue=SessionHealingAudio.Capture(world,healer,"revive","heal");
            Require(cue.actor==healer.id && cue.lifecycle==healer.lifecycleSequence && cue.actor!=target.id && StateJson.ToJson(world)==before,
                "Revival cue использовала lifetime поднятой цели вместо healer либо изменила state");
            Require(HealingRules.ApplyOnce(world,healer,target,false,"revival-kit","revive") && StateJson.ToJson(world)==before,
                "Duplicate revival изменила kit/health/lifecycle");
        }
        private static void CheckDeclaration()
        {
            var catalog=HealingItemAudio.Load();
            Require(catalog.entries.Length==1 && catalog.entries[0].definition=="heal" && catalog.entries[0].clip==HealingItemAudioAuthoring.Resource &&
                catalog.entries[0].gain==.5f,"Нет единственного подтверждённого pot1/pot_use bank");
            foreach(int fault in new[]{0,1,2,3,4,5,6,7,8})
            {
                var invalid=new HealingItemAudio.Catalog {version=1,entries=new[]{new HealingItemAudio.Entry {definition="heal",clip="bank",gain=.5f}}};
                if(fault==0)invalid.version=2;if(fault==1)invalid.entries=null;if(fault==2)invalid.entries[0]=null;
                if(fault==3)invalid.entries[0].definition="";if(fault==4)invalid.entries[0].clip="";
                if(fault==5)invalid.entries[0].gain=float.NaN;if(fault==6)invalid.entries[0].gain=0;if(fault==7)invalid.entries[0].gain=1.1f;
                if(fault==8)invalid.entries=new[]{invalid.entries[0],invalid.entries[0]};
                Reject(()=>HealingItemAudio.Validate(invalid),"Invalid audio catalog: "+fault);
            }
        }
        private static void CheckConsumer(CoopSession session,SessionHealingAudio source,HealingSoundCue cue)
        {
            var root=new GameObject("Настоящий лечебный голос");
            try
            {
                var playback=root.AddComponent<GameplayAudio>();playback.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());
                var consumer=root.AddComponent<HealingItemAudio>();var declaration=HealingItemAudio.Load();consumer.Initialize(source,playback,declaration);
                Require(consumer.TryGet("heal",out var clip,out var gain) && clip==GameAssets.Load<AudioClip>(HealingItemAudioAuthoring.Resource) && gain==.5f,
                    "Consumer потерял фактический исходный WAV");
                declaration.entries[0].clip="changed-after-initialize";
                Require(consumer.TryGet("heal",out var retained,out _) && retained==clip,"Mutable declaration изменила prepared bank");
                Require(!consumer.TryGet("mod:unknown",out var missing,out _) && missing==null,"Неизвестная аптечка получила silent bank substitution");
                // Invoke the actual subscribed event after the public receipt gate. This
                // checks the consumer/voice, not generated Mirror serializers/delivery.
                var received=(Action<HealingSoundCue>)typeof(SessionHealingAudio).GetField("Received",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source);
                var receipts=new HealingAudioReceipts();string before=StateJson.ToJson(session.World);
                int count=0;consumer.HealingPlayed+=(actual,audio)=>{Require(actual.Key==cue.Key && audio==clip,"Voice witness потерял actual transaction/clip");count++;};
                if(receipts.Accept(session.World,cue))received(cue);
                if(receipts.Accept(session.World,cue))received(cue);
                var voices=root.GetComponentsInChildren<AudioSource>();
                Require(consumer.Accepted==1 && consumer.Played==1 && count==1 && voices.Length==1 && voices[0].clip==clip &&
                    voices[0].volume>0 && (!Application.isBatchMode || voices[0].mute),"Reliable/expedited replay не дал ровно один actual AudioSource");
                Require(StateJson.ToJson(session.World)==before,"Audio consumer изменил world JSON");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Reject(Action action,string detail)
        {conditions++;try {action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("Лечебный звук не отклонил: "+detail);}
        private static void Require(bool valid,string detail)
        {conditions++;if(!valid)throw new InvalidOperationException("Лечебный звук: "+detail);}
    }
}
