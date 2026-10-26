using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Remains.Editor
{
    public static class SelfHealingReconciliationChecks
    {
        private const string Owner="connection:2",WorldId="self-heal-check",Transaction="self-heal:exact";
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            ActorTypes.Register(new ActorTypeDefinition {id="self-heal-check",presentation="pony"},true);
            foreach(string pose in new[]{"run","climb","landing"}) DelayedMovingAcknowledgement(pose);
            NewOwnerDamage(false);NewOwnerDamage(true);UnrelatedOwnerRevision();
            foreach(int independentLife in new[]{0,1,2}) LaterKnockdown(independentLife);
            InvalidTransitions();MissingObservation();RealRevival();NewerHoldTarget();JournalOnlyLiveAcknowledgement();
            if(log) Debug.Log("Самолечение: PASS — exact transaction + recoverable journal, задержанный ack после landing, run/climb и фаза/часы motion, более поздний и частично подтверждённый owner damage, немедленно публикуемая новая revision, дубль без повторного HP/расхода, stale old-life snapshot, отказы identity/gen/level/lifecycle/reply, down без неявного revive. Live сеть не проверена.");
        }
        private sealed class Fixture
        {
            public readonly WorldState Server=new() {worldId=WorldId};
            public ActorState Local;
            public readonly SelfHealingReconciliation Tracker;
            public readonly List<string> Warnings=new();
            public Fixture(bool capture=true)
            {
                Tracker=new SelfHealingReconciliation(Warnings.Add);
                var actor=new ActorState {id="office-rider",owner=Owner,actorType="self-heal-check",player=true,connected=true,level="factory:0",
                    generation=2,lifecycleSequence=3,revision=10,damageSequence=2,health=20,x=16.22f,y=8.881229f,vy=2,climbing=true,
                    pose="climb",poseTime=1.25f,poseSequence=5,motionSequence=40,motionSentAt=700.2,motionClockGeneration=2,
                    crouched=true,jumpSupportRemaining=.08f};
                actor.inventory.Add(new ItemStack {id="heal-stack",definition="heal",count=2});Server.actors.Add(actor);Local=actor.Copy();
                if(capture) Tracker.Capture(WorldId,Transaction,Local);
            }
            public ActorState Accept()
            {
                var actor=Server.actors[0];
                Require(HealingRules.ApplyOnce(Server,actor,actor,true,"heal-stack",Transaction),"Канонический self-heal не принят.");
                return actor.Copy();
            }
            public bool Apply(ref ActorState incoming)=>Tracker.TryApply(WorldId,Server.completedTransactions,Local,ref incoming,Owner);
            public void Damage(float amount,bool observe=true)
            {
                var receiver=new DamageReceiver(new FixedDamage(amount));
                Require(receiver.Apply(Local,new AttackEvent {id="damage:"+Local.damageSequence,attacker="enemy",level=Local.level,weapon="check"},true),"Fixture damage не применён.");
                if(observe) Tracker.Observe(WorldId,Local); // Production PublishActor observes immediately after real DamageReceiver.
            }
        }
        private sealed class FixedDamage : IDamageRule
        {private readonly float amount;public FixedDamage(float amount)=>this.amount=amount;public float Calculate(string weapon,float speed)=>amount;}
        private static void DelayedMovingAcknowledgement(string mode)
        {
            var f=new Fixture();var incoming=f.Accept();
            Require(incoming.lifecycleSequence==4 && incoming.health==55 && incoming.pose=="climb" && incoming.poseTime==1.25f,
                "Self-heal заменил locomotion idle или не сохранил прежний health/life контракт.");
            f.Local.Position=new Vector2(16.22f,10.4f);f.Local.motionSequence=70;f.Local.motionSentAt=703.5;
            f.Local.motionClockGeneration=3;f.Local.poseSequence=9;f.Local.poseTime=2.8f;f.Local.poseCorrectionRemaining=.09f;
            f.Local.pose=mode=="run"?"run":mode=="climb"?"climb":"idle";f.Local.climbing=mode=="climb";
            f.Local.vx=mode=="run"?3:0;f.Local.vy=mode=="climb"?2:0;
            string motion=Motion(f.Local);
            Require(f.Tracker.AwaitingCommit(WorldId,new List<string>(),f.Local,incoming,Owner) &&
                !f.Tracker.TryApply(WorldId,new List<string>(),f.Local,ref incoming,Owner),"Actor message до journal принял лечение.");
            Require(f.Apply(ref incoming) && Motion(incoming)==motion && incoming.poseCorrectionRemaining==.09f && incoming.health==55 && incoming.lifecycleSequence==4,
                "Delayed ack y8.881229 откатил достигнутый landing y10.4: mode="+mode);
            Require(ActorVersion.CanPublishOwnerState(incoming,f.Server.actors[0]),"Reconciled checkpoint нельзя опубликовать без следующего damage/input.");
            f.Local=incoming.Copy();var duplicate=f.Server.actors[0].Copy();
            Require(!f.Apply(ref duplicate) && duplicate.health==55,"Дубль повторно применил переход/лечебный HP.");
            ActorMotionReplication.PreserveNewerMotion(f.Local,duplicate);ActorPose.CopyMotion(f.Local,duplicate);
            Require(Motion(duplicate)==motion,"Обычный same-life duplicate откатил новую позу.");
            var stale=f.Local.Copy();stale.lifecycleSequence=3;stale.y=8.881229f;stale.inventory[0].count=2;
            Require(f.Tracker.DiscardPreHealingState(WorldId,f.Local,stale,Owner),"Pre-heal full snapshot может откатить life/kit/HP.");
            string once=StateJson.ToJson(f.Server);
            Require(HealingRules.ApplyOnce(f.Server,f.Server.actors[0],f.Server.actors[0],true,"heal-stack",Transaction) &&
                StateJson.ToJson(f.Server)==once && InventoryJournal.Count(f.Server.actors[0],"heal")==1,"Дубликат transaction повторно расходовал kit/HP/life.");
        }
        private static void NewOwnerDamage(bool partial)
        {
            var f=new Fixture();var incoming=f.Accept();f.Damage(7);f.Damage(4);
            if(partial) {incoming.damageSequence=3;incoming.health=48;incoming.resolvedAttacks.Add("damage:2");}
            Require(f.Apply(ref incoming) && incoming.health==44 && incoming.damageSequence==4 && incoming.lifecycleSequence==4 &&
                incoming.resolvedAttacks.Contains("damage:2") && incoming.resolvedAttacks.Contains("damage:3"),"Heal/damage частично подтверждён дважды или утрачен: partial="+partial);
            Require(ActorVersion.CanPublishOwnerState(incoming,f.Server.actors[0]),"После ack unacked damage нельзя передать canonical owner-state.");
            // The real UpdateActor gate will accept this new-life/new-revision checkpoint.
            // No further attack, movement command, or second heal is needed for publication.
            f.Server.actors[0]=incoming.Copy();
            Require(f.Server.actors[0].health==44 && f.Server.actors[0].damageSequence==4 && InventoryJournal.Count(f.Server.actors[0],"heal")==1,
                "Canonical checkpoint не сохранил fused health или подтвердил лишний kit.");
        }
        private static void UnrelatedOwnerRevision()
        {
            var f=new Fixture();var incoming=f.Accept();f.Local.revision+=8;f.Local.mana=12;
            f.Local.extensions??=new Dictionary<string,JToken>(StringComparer.Ordinal);
            f.Local.extensions["check:module"]=new JObject { ["state"]="owner-progress" };
            Require(f.Apply(ref incoming) && incoming.health==55 && incoming.mana==12 && incoming.revision>f.Local.revision &&
                incoming.extensions["check:module"]["state"].Value<string>()=="owner-progress",
                "Более новая owner revision потеряла accepted heal либо собственное mana.");
        }
        private static void LaterKnockdown(int independentLife)
        {
            var f=new Fixture();var incoming=f.Accept();f.Damage(30);f.Local.lifecycleSequence+=independentLife;
            f.Local.y=10.4f;f.Local.pose="down";f.Local.climbing=false;f.Local.jumpSupportRemaining=0;
            f.Local.holdEpoch=2;f.Local.hold=new ActorHoldState {holder="ally",token="new-hold",sequence=5,targetX=17,targetY=10};
            incoming.holdEpoch=3;incoming.hold=new ActorHoldState {holder="ally",token="canonical-hold",sequence=2,targetX=18,targetY=10};
            Require(f.Apply(ref incoming) && incoming.down && incoming.health==0 && incoming.damageSequence==3 &&
                incoming.lifecycleSequence==Math.Max(4,f.Local.lifecycleSequence) && incoming.pose=="down" && InventoryJournal.Count(incoming,"heal")==1,
                "Delayed self-heal неявно поднял после нового damage/down/life: deltaLife="+independentLife);
            Require(incoming.y==10.4f && !incoming.climbing && incoming.jumpSupportRemaining==0 && incoming.holdEpoch==3 && incoming.hold.token=="canonical-hold",
                "Последнее unconscious body/новый canonical hold откатились при heal ack.");
            if(independentLife<2) Require(ActorVersion.CanPublishOwnerState(incoming,f.Server.actors[0]),"Down checkpoint нельзя публиковать при одинаковой canonical life.");
            else Require(!ActorVersion.CanPublishOwnerState(incoming,f.Server.actors[0]) && f.Warnings.Count==1 && f.Warnings[0].Contains(Transaction),
                "Более новая независимая down-life обошла canonical gate либо потеряла диагностический контекст.");
        }
        private static void InvalidTransitions()
        {
            foreach(string reason in new[]{"no-pending","no-journal","world","owner","local-owner","id","generation","level","incoming-life","local-life","incoming-down","rejected","reset"})
            {
                var f=new Fixture(reason!="no-pending");var incoming=f.Accept();string world=WorldId,owner=Owner;
                ICollection<string> completed=f.Server.completedTransactions;
                switch(reason)
                {
                    case "no-journal":completed=new List<string>{"unrelated"};break;
                    case "world":world="another-world";break;
                    case "owner":incoming.owner="other-owner";break;
                    case "local-owner":owner="other-owner";break;
                    case "id":incoming.id="another-actor";break;
                    case "generation":incoming.generation++;break;
                    case "level":incoming.level="hub";break;
                    case "incoming-life":incoming.lifecycleSequence++;break;
                    case "local-life":f.Local.lifecycleSequence++;break;
                    case "incoming-down":incoming.down=true;incoming.health=0;incoming.pose="down";break;
                    case "rejected":f.Tracker.Reject(Transaction);break;
                    case "reset":f.Tracker.Reset();break;
                }
                string before=StateJson.ToJson(incoming);
                Require(!f.Tracker.TryApply(world,completed,f.Local,ref incoming,owner) && StateJson.ToJson(incoming)==before,
                    "Неподтверждённый/чужой transition изменён: "+reason);
            }
        }
        private static void MissingObservation()
        {
            var f=new Fixture();var incoming=f.Accept();f.Damage(7,false);f.Damage(4,false);incoming.damageSequence=3;incoming.health=48;
            bool rejected=false;try {f.Apply(ref incoming);}catch(InvalidOperationException e)
            {rejected=e.Message.Contains("Нет локальной выборки") && e.Message.Contains(Transaction) && e.Message.Contains(f.Local.id);}
            Require(rejected,"Отсутствующий partial checkpoint молча заменён предположением.");
        }
        private static void RealRevival()
        {
            var f=new Fixture();f.Damage(30);var incoming=f.Local.Copy();var healer=new ActorState {id="ally",player=true,connected=true,level=incoming.level,x=incoming.x,y=incoming.y};
            healer.inventory.Add(SharedInventory.Create("heal",1));
            Require(HealingRules.Apply(healer,incoming,false) && incoming.pose=="idle" && incoming.lifecycleSequence==4 && incoming.health==35 && !incoming.down,
                "Реальное revive утратило отдельный idle/life контракт.");
            Require(!f.Tracker.TryApply(WorldId,new List<string>{"actual-revival"},f.Local,ref incoming,Owner) && incoming.y==f.Local.y,
                "Старый pending self-heal разрешил движение через настоящую revival границу.");
        }

        private static void NewerHoldTarget()
        {
            var f=new Fixture();var incoming=f.Accept();f.Local.holdEpoch=incoming.holdEpoch=5;
            f.Local.hold=new ActorHoldState {holder="ally",token="same-hold",sequence=8,targetX=19,targetY=11};
            incoming.hold=new ActorHoldState {holder="ally",token="same-hold",sequence=3,targetX=18,targetY=10};
            ActorHoldRules.PreserveNewerTarget(f.Local,incoming);
            Require(f.Apply(ref incoming) && incoming.holdEpoch==5 && incoming.hold.sequence==8 && incoming.hold.targetX==19,
                "Self-heal откатил shared hold target/epoch.");
        }

        private static string Motion(ActorState actor)
        {
            var motion=new ActorState {weaponSelectionRevision=actor.weaponSelectionRevision};ActorPose.CopyMotion(actor,motion);
            return StateJson.ToJson(motion);
        }
        private static void JournalOnlyLiveAcknowledgement()
        {
            var f=new Fixture(false);
            f.Server.worldId=Guid.NewGuid().ToString("N");
            string world=f.Server.worldId;
            f.Tracker.Capture(world,Transaction,f.Local);
            var stranger=new ActorState {id="journal-only-unrelated",owner=Owner,level=f.Local.level};
            f.Server.actors.Add(stranger);
            f.Accept(); // The actor already carries the accepted heal; only its durable proof is delayed.
            f.Server.completedTransactions.Remove(Transaction);
            // Transport v2: the journal entry arrives as a world tick; the client's live world is a shallow copy with the tick
            // applied, and unchanged actors keep their live objects (as CoopSession.ReceiveTick).
            WorldChanges.Track(f.Server);
            var composer=new Remains.Persistence.WorldTickComposer();
            Require(composer.Next(f.Server,part=>WorldChanges.IsMarked(f.Server,part))==null,"Первый тик должен только запомнить мир.");
            var current=StateJson.FromJson<WorldState>(StateJson.ToJson(f.Server));
            // A later owner-decided fall has the accepted lifetime, but must retain its down state.
            f.Local.health=0;f.Local.down=true;f.Local.damageSequence++;
            f.Local.lifecycleSequence=f.Server.actors[0].lifecycleSequence;
            f.Local.revision+=5;f.Tracker.Observe(world,f.Local);
            current.actors[0]=f.Local;
            var unrelated=current.actors[1];unrelated.x+=123;
            f.Server.completedTransactions.Add(Transaction);f.Server.revision++;
            string hostBefore=StateJson.ToJson(f.Server);
            var tick=composer.Next(f.Server,part=>WorldChanges.IsMarked(f.Server,part));
            Require(tick!=null,"Журнальный ack не дал тика.");
            foreach(var list in tick.lists) Require(list.name!="actors" || (list.keys?.Length??0)==0,"Журнальный ack неожиданно изменил части актёров.");
            var live=Remains.Persistence.WorldTickLists.ShallowCopy(current);
            Remains.Persistence.WorldTickApplier.Apply(live,Remains.Persistence.WorldTickApplier.Parse(live,tick),ValidatedStateCopy.Copy);
            var reused=new HashSet<string>(StringComparer.Ordinal);
            var held=new HashSet<ActorState>(current.actors);
            foreach(var actor in live.actors) if(held.Contains(actor)) reused.Add(actor.id);
            Require(reused.Contains(f.Local.id) && reused.Contains(unrelated.id),"Журнальный ack не проверяет настоящий unchanged reuse.");
            Func<string,ActorState> canonical=id=>StateJson.Copy(f.Server.Find(id));
            f.Tracker.MaterializeCanonical(live,reused,canonical);
            Require(!reused.Contains(f.Local.id) && reused.Contains(unrelated.id) && ReferenceEquals(live.actors[1],unrelated),
                "MaterializeCanonical затронул постороннюю проекцию или пропустил pending.");
            var incoming=live.actors[0];
            Require(f.Tracker.TryApply(world,live.completedTransactions,f.Local,ref incoming,Owner) && incoming.down && incoming.health==0,
                "Журнальный proof не завершил ack с сохранением позднего падения.");
            live.actors[0]=incoming;
            Require(f.Tracker.RequiresCanonical(incoming.id),"Acknowledged-состояние потеряло canonical guard.");
            string accepted=StateJson.ToJson(incoming);
            var duplicate=canonical(incoming.id);
            Require(!f.Tracker.TryApply(world,live.completedTransactions,incoming,ref duplicate,Owner) && StateJson.ToJson(incoming)==accepted,
                "Повторный ack изменил здоровье или повторил расход.");
            Require(StateJson.ToJson(f.Server)==hostBefore,"Live ack изменил каноническую копию хоста.");
            canonical(incoming.id).inventory.Clear();
            Require(StateJson.ToJson(f.Server)==hostBefore,"Каноническая копия актёра разделяет inventory с живым миром.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка подтверждения самолечения: "+reason);}
    }
}
