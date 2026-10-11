using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Runtime;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationRouteChecks
    {
        public static void Run()
        {
            var proof=TwoContacts();
            Require(FactoryContinuationRouteProof.Destruction(proof,out var reason),"Два настоящих DoorDamageOperations не прошли proof: "+reason);
            var failures=new Action<FactoryContinuationRouteEvidence>[] {
                p=>p.initialDoor.integrity.health=29,p=>p.initialDoor.interactive=true,p=>p.destroyedDoor.open=true,
                p=>p.destroyedDoor.width+=.1f,p=>p.destroyedDoor.revision=1,p=>p.destroyedDoor.integrity.health=1,
                p=>p.contacts[0].world="another-world",p=>p.contacts[0].sourceGeneration++,
                p=>p.contacts[0].decisionLifecycle++,p=>p.contacts[0].attack.sourceOwner="another-owner",
                p=>p.contacts[0].attack.id=p.contacts[1].attack.id,p=>p.replies[0].changed=false,
                p=>p.replies[1].destroyed=false,p=>p.replies[1].health=1,p=>p.attacks[0].attacker="another-player",
                p=>p.attacks=Array.Empty<AttackEvent>(),p=>p.replies=p.replies.Concat(new[]{p.replies[0]}).ToArray()
            };
            foreach(var change in failures)
            {
                var invalid=StateJson.Copy(proof);change(invalid);
                Require(!FactoryContinuationRouteProof.Destruction(invalid,out reason) && !string.IsNullOrEmpty(reason),"Неверное свидетельство разрушения принято.");
            }
            var cup=new ContainerState {id=FactoryContinuationRouteProof.CupId,definition="cup",level="factory:0",hasPhysicalBody=true,
                width=.94f,height=1.6f,mass=60.16f,anchorHeight=0,
                items=new List<ItemStack>{new(){id="one",definition="caps",count=4},new(){id="two",definition="pin",count=1},new(){id="three",definition="kombu_comp",count=1}}};
            FactoryContinuationRouteProof.ValidateCup(cup);
            cup.items[0].count=3;
            bool rejected=false;try {FactoryContinuationRouteProof.ValidateCup(cup);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Изменённый исходный loot cup28 принят.");
            var json=JObject.Parse(StateJson.ToJson(default(FactoryRouteMotionWitness)));
            Require(json.ContainsKey("kind") && json.ContainsKey("x") && json.ContainsKey("climbing") && json.ContainsKey("surfaceWidth"),"Readonly motion witnesses исчезли из отчёта.");
            ContentStateFields(cup,proof);
            int observations=ObservationFence();
            SnapshotWitnesses(proof);
            Debug.Log($"Комната 27: exact door proof, {failures.Length} отрицательных случаев, три исходные стопки, независимый snapshot настоящего motion witness и {observations} canonical sequence-fence случаев проверены.");
        }
        private static void SnapshotWitnesses(FactoryContinuationRouteEvidence source)
        {
            var room=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId).CreateLayout(false);
            var actor=new ActorState {id="snapshot-route-player",owner="snapshot-owner",player=true,connected=true,
                generation=1,lifecycleSequence=3,x=125.38f,y=.8f};
            ActorBodySupport.Bind(actor,room);
            try
            {
                var route=new FactoryContinuationRouteMotion(room,actor);
                var first=route.Witnesses[0];
                Require(first.kind=="warehouse-bottom" && first.x==125.38f && first.y==.8f && first.bodyBound && first.supported,
                    "Snapshot fixture не получил настоящий первый warehouse-bottom witness с общей опорой тела.");
                source.motion=route.Witnesses.ToArray();
                source.initialDoor.extensions=new Dictionary<string,JToken>{{"probe:door",new JObject { ["value"]="retained" }}};
                string original=StateJson.ToJson(source);
                var snapshot=FactoryContinuationRouteProof.Snapshot(source);
                Require(snapshot!=source && snapshot.motion!=source.motion,"Snapshot разделяет изменяемый отчёт/массив с источником.");
                var rendered=JObject.Parse(StateJson.ToJson(snapshot));
                Require(JToken.DeepEquals(rendered["motion"],JToken.Parse(StateJson.ToJson(source.motion))),
                    "Snapshot+ToJson потерял readonly kind/x/y/body/contact/surface поля настоящего witness.");
                Require(StateJson.ToJson(snapshot)==original,"Snapshot изменил обычные поля или неизвестные данные отчёта.");
                snapshot.destroyedDoor.integrity.health=5;
                ((JObject)snapshot.initialDoor.extensions["probe:door"])["value"]="changed";
                snapshot.motion[0]=default;
                Require(StateJson.ToJson(source)==original && source.motion[0].kind=="warehouse-bottom" && source.motion[0].bodyBound,
                    "Изменение snapshot door/extension/motion array изменило исходное свидетельство.");
                Require(FactoryContinuationRouteProof.Snapshot(null)==null,"Пустое свидетельство стало выдуманным snapshot.");
            }
            finally {ActorBodySupport.Unbind(actor);}
        }
        private static int ObservationFence()
        {
            Anatomy.Load();
            var room=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId).CreateLayout(false);
            string roomBefore=StateJson.ToJson(room);
            var canonical=new ActorState {id="observation-player",owner="observation-owner",player=true,connected=true,
                generation=1,lifecycleSequence=3,revision=32,level="factory:0",x=163.04097f,y=4,vx=1.79998755f,motionSequence=1265};
            int cases=0;
            void Check(ActorState actor,int phase,uint required,FactoryContinuationObservationStatus expected,string label,
                string id="observation-player",int generation=1,int lifecycle=3,RoomLayout geometry=null)
            {
                string before=actor==null?null:StateJson.ToJson(actor);
                var status=FactoryContinuationRouteProof.EvaluateObservation(actor,id,generation,lifecycle,phase,required,geometry??room,out var reason);
                Require(status==expected,$"Неверный canonical fence: case={label}, expected={expected}, actual={status}, reason={reason}.");
                Require(expected==FactoryContinuationObservationStatus.Ready?reason==null:!string.IsNullOrEmpty(reason),"Нет контекста неподтверждённого барьера: "+label);
                Require((actor==null?null:StateJson.ToJson(actor))==before,"Наблюдение изменило канонического игрока: "+label);
                cases++;
            }
            // Recorded failure states: waiting is valid only until the actual pose fence arrives.
            Check(canonical,0,1276,FactoryContinuationObservationStatus.Pending,"recorded-moving-before-fence");
            Check(canonical,0,1265,FactoryContinuationObservationStatus.Rejected,"recorded-moving-reached-fence");
            canonical.x=163.120987f;canonical.vx=0;canonical.motionSequence=1276;
            Check(canonical,0,1276,FactoryContinuationObservationStatus.Ready,"recorded-stopped-same-revision");
            Check(canonical,0,1275,FactoryContinuationObservationStatus.Ready,"newer-canonical");
            Check(canonical,0,1277,FactoryContinuationObservationStatus.Pending,"stopped-not-yet-fenced");
            Check(canonical,0,1277,FactoryContinuationObservationStatus.Rejected,"wrong-id-before-fence",id:"another-player");
            Check(canonical,0,1277,FactoryContinuationObservationStatus.Rejected,"wrong-generation-before-fence",generation:2);
            Check(canonical,0,1277,FactoryContinuationObservationStatus.Rejected,"wrong-lifecycle-before-fence",lifecycle:4);
            var invalid=canonical.Copy();invalid.level="hub";
            Check(invalid,0,1277,FactoryContinuationObservationStatus.Rejected,"wrong-level-before-fence");
            Check(canonical,1,1277,FactoryContinuationObservationStatus.Rejected,"unknown-phase-before-fence");
            Check(null,0,1277,FactoryContinuationObservationStatus.Rejected,"missing-canonical");
            invalid=canonical.Copy();invalid.x=162;
            Check(invalid,0,1276,FactoryContinuationObservationStatus.Rejected,"stopped-wrong-position");
            invalid=canonical.Copy();invalid.y=4.01f;
            Check(invalid,0,1276,FactoryContinuationObservationStatus.Rejected,"wrong-height");
            invalid=canonical.Copy();invalid.vx=.10001f;
            Check(invalid,0,1276,FactoryContinuationObservationStatus.Rejected,"still-moving");
            invalid=canonical.Copy();invalid.vy=.00101f;
            Check(invalid,0,1276,FactoryContinuationObservationStatus.Rejected,"still-falling");
            invalid=canonical.Copy();invalid.climbing=true;
            Check(invalid,0,1276,FactoryContinuationObservationStatus.Rejected,"climbing");
            var noSupport=StateJson.Copy(room);noSupport.platforms=Array.Empty<RoomSurface>();noSupport.dynamicSurfaces=null;
            Check(canonical,0,1276,FactoryContinuationObservationStatus.Rejected,"absent-actual-surface",geometry:noSupport);
            var returned=canonical.Copy();returned.x=125.38f;returned.y=.8f;
            Check(returned,2,1276,FactoryContinuationObservationStatus.Ready,"supported-return");
            invalid=canonical.Copy();invalid.motionSequence=uint.MaxValue;
            Check(invalid,0,0,FactoryContinuationObservationStatus.Pending,"wrap-await-zero");
            invalid.motionSequence=0;
            Check(invalid,0,0,FactoryContinuationObservationStatus.Ready,"wrap-arrived-zero");
            Check(invalid,0,uint.MaxValue,FactoryContinuationObservationStatus.Ready,"wrap-newer-canonical");
            Check(invalid,0,1,FactoryContinuationObservationStatus.Pending,"wrap-await-next");
            Require(StateJson.ToJson(room)==roomBefore,"Canonical fence checks изменили сохранённую геометрию.");
            return cases;
        }
        private static void ContentStateFields(ContainerState cup,FactoryContinuationRouteEvidence evidence)
        {
            var method=typeof(NetworkTravelPointProbe).GetMethod("ContentState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Require(method!=null,"Нет обычного снимка сравнения при повторном входе.");
            var world=new WorldState {containers=new List<ContainerState>{cup},doors=new List<DoorState>{StateJson.Copy(evidence.initialDoor)}};
            string Read()=>(string)method.Invoke(null,new object[]{world});
            string closed=Read();cup.appearance=new ContainerAppearanceState {version=1,opened=true,revision=1};
            string opened=Read();Require(closed!=opened,"Снимок повторного входа потерял public поля состояния открытия контейнера.");
            world.doors[0]=StateJson.Copy(evidence.destroyedDoor);
            string destroyed=Read();Require(opened!=destroyed,"Снимок повторного входа потерял health/receipts целостности.");
            cup.items[0].extensions=new Dictionary<string,JToken>{{"probe:unknown","retained-data"}};
            Require(destroyed!=Read(),"Снимок повторного входа потерял неизвестное расширение предмета.");
        }
        private static FactoryContinuationRouteEvidence TwoContacts()
        {
            var before=new DoorState {id=FactoryContinuationRouteMotion.SeptumId,level="factory:0",definition="office-septum",
                surfaceTexture="door:wood",x=164.8f,y=4,width=.8f,height=2.4f,interactive=false,
                integrity=new DoorIntegrity {maxHealth=30,health=30}};
            var after=StateJson.Copy(before);var attacks=new List<AttackEvent>();var contacts=new List<DoorDamageRequestData>();var replies=new List<DoorDamageReply>();
            for(int i=0;i<2;i++)
            {
                var attack=new AttackEvent {id="probe-attack-"+i,attacker="player:session-host",sourcePlayer=true,sourceOwner="session-host",level="factory:0",weapon="pistol",speed=20,
                    originX=163.8f,originY=5.2f,endX=166,endY=5.2f};
                attacks.Add(attack);
                var contact=StateJson.Copy(attack);contact.originX=164.7f;contact.endX=164.85f;
                contacts.Add(new DoorDamageRequestData {world="proof-world",door=before.id,decisionActor=attack.attacker,
                    decisionGeneration=3,decisionLifecycle=4,sourceGeneration=3,sourceLifecycle=4,attack=contact,contact=new Vector2(164.8f,5.2f)});
                Require(DoorDamageOperations.Apply(after,attack.id,18,out _)==DoorChange.Changed,"Не применён обычный урон proof fixture.");
                replies.Add(new DoorDamageReply {door=after.id,attack=attack.id,accepted=true,changed=true,revision=after.revision,health=after.integrity.health,destroyed=after.Destroyed});
            }
            return new FactoryContinuationRouteEvidence {actor="player:session-host",world="proof-world",driverGeneration=3,driverLifecycle=4,
                initialDoor=before,destroyedDoor=after,attacks=attacks.ToArray(),contacts=contacts.ToArray(),replies=replies.ToArray()};
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
