using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class MechanismOperationsChecks
    {
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            CheckMigration();CheckValidation();CheckOperations();CheckAtomicity();
            if(log) Debug.Log("Механизмы: backup-before-migration, canonical IDs/links/version, ремонт отдельно от запуска, dedup, однократное прибытие, monotonic sequence, reload и точные unknown данные — PASS.");
        }
        private static void CheckMigration()
        {
            var world=new WorldState {extensions=Unknown()};
            using var reader=new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(StateJson.ToJson(world))) {FloatParseHandling=Newtonsoft.Json.FloatParseHandling.Decimal};
            var legacy=JObject.Load(reader);legacy.Remove("mechanismVersion");legacy.Remove("movingPlatforms");legacy.Remove("mechanismButtons");
            world=StateJson.FromJson<WorldState>(legacy.ToString());
            Require(JToken.DeepEquals(world.extensions["mod:payload"],Unknown()["mod:payload"]),"Старый JSON потерял точное unknown число");
            Require(world.mechanismVersion==0 && world.movingPlatforms.Count==0 && world.mechanismButtons.Count==0,"Старый JSON не получил явную legacy-версию");
            string before=StateJson.ToJson(world);int callbacks=0;
            Reject(()=>MechanismPersistence.Prepare(world,()=>throw new InvalidOperationException("Отказ резервной копии")),"Миграция без backup");
            Require(StateJson.ToJson(world)==before,"Отказ backup изменил исходник");
            Reject(()=>MechanismPersistence.Prepare(world,null),"Отсутствующий callback принят");
            Require(MechanismPersistence.Prepare(world,()=>{callbacks++;Require(StateJson.ToJson(world)==before,"Backup вызван после мутации");}),"Миграция не выполнена");
            Require(world.mechanismVersion==1 && world.revision==1 && callbacks==1,"Неверные результаты миграции");
            Require(!MechanismPersistence.Prepare(world,()=>callbacks++) && callbacks==1,"Повторная миграция вызвала backup/мутацию");
            var restored=StateJson.Copy(world);MechanismPersistence.ValidateWorld(restored);
            Require(JToken.DeepEquals(restored.extensions["mod:payload"],world.extensions["mod:payload"]),"Миграция/reload потеряли unknown world data");
        }
        private static void CheckValidation()
        {
            var world=World();MechanismPersistence.ValidateWorld(world,(_,_)=>Type());
            Bad(w=>w.mechanismVersion=2,"Будущая версия принята");
            Bad(w=>w.mechanismVersion=0,"Legacy-версия с records принята");
            Bad(w=>w.movingPlatforms=null,"Null collection принята");
            Bad(w=>w.mechanismButtons.Add(StateJson.Copy(w.mechanismButtons[0])),"Duplicate button ID принят");
            Bad(w=>w.movingPlatforms.Add(StateJson.Copy(w.movingPlatforms[0])),"Duplicate platform ID принят");
            Bad(w=>w.actors[0].id=w.movingPlatforms[0].id,"Cross-kind ID принят");
            Bad(w=>w.mechanismButtons[0].platformId="factory:0:missing","Missing link принят");
            Bad(w=>w.mechanismButtons[0].roomId="another-room","Cross-room link принят");
            Bad(w=>w.mechanismButtons[0].groupId="another-group","Cross-group link принят");
            Bad(w=>w.mechanismButtons[0].level="factory:6","Cross-level placement принят");
            Bad(w=>w.mechanismButtons[0].schema=2,"Future record schema принята");
            Bad(w=>w.movingPlatforms[0].definitionVersion=2,"Definition version mismatch принят",true);
            Bad(w=>{var p=w.movingPlatforms[0];p.segment=Segment(2);p.revision=1;},"Sequence выше revision принят");
            Reject(()=>MechanismPersistence.ValidateWorld(world,(_,_)=>null),"Отсутствующее определение подменено");
            world.mechanismButtons[0].repairState="mod:custom";
            Reject(()=>MechanismPersistence.ValidateWorld(world),"Unknown state получил fallback");
            MechanismPersistence.ValidateWorld(world,supportedRepairState:id=>id=="mod:custom" || MechanismRepairStates.BuiltIn(id));
            var reload=StateJson.Copy(world);Require(reload.mechanismButtons[0].repairState=="mod:custom","Unknown registered state потерян");
        }
        private static void CheckOperations()
        {
            var world=World();var actor=world.actors[0];var platform=world.movingPlatforms[0];var button=world.mechanismButtons[0];var other=world.mechanismButtons[1];
            var operations=new MechanismOperations(world);var type=Type();
            string actorBefore=StateJson.ToJson(actor);
            var candidate=Candidate(actor,button,"mechanism:repair1");
            Require(operations.ApplyRepair(actor,button,candidate,2,"mechanism:repair1").Changed,"Ремонт не применён");
            Require(button.repairState==MechanismRepairStates.Ready && !button.pressed && platform.segment==null &&
                other.repairState==MechanismRepairStates.NeedsRepair && StateJson.ToJson(actor)==actorBefore,"Ремонт запустил механизм/изменил другую кнопку или предметы");
            string snapshot=StateJson.ToJson(world);
            Require(operations.ApplyRepair(actor,button,candidate,2,"mechanism:repair1").Outcome==MechanismOperationOutcome.Duplicate && StateJson.ToJson(world)==snapshot,"Повтор ремонта применён дважды");
            Require(operations.TryActivate(actor,button,platform,type,10,button.revision,2,"mechanism:activate1").Changed,"Исправная кнопка не запустила переход");
            long sequence=platform.segment.sequence;
            Require(button.pressed && !other.pressed && platform.endpoint==0 && platform.segment.fromEndpoint==0 && platform.segment.toEndpoint==1 &&
                sequence==platform.revision,"Нажатие/направление/sequence неверны");
            platform.segment.extensions=Unknown();
            var midsegment=StateJson.Copy(world);MechanismPersistence.ValidateWorld(midsegment,(_,_)=>Type());
            Require(midsegment.movingPlatforms[0].segment.sequence==sequence && midsegment.movingPlatforms[0].segment.startElapsedSeconds==10 &&
                JToken.DeepEquals(midsegment.movingPlatforms[0].segment.extensions["mod:payload"],Unknown()["mod:payload"]),"Canonical midsegment reload потерял phase/sequence/unknown");
            snapshot=StateJson.ToJson(world);
            Require(operations.TryActivate(actor,button,platform,type,10,button.revision,2,"mechanism:activate1").Outcome==MechanismOperationOutcome.Duplicate && StateJson.ToJson(world)==snapshot,"Повтор запуска изменил segment");
            var secondCandidate=Candidate(actor,other,"mechanism:repair2");operations.ApplyRepair(actor,other,secondCandidate,2,"mechanism:repair2");
            UnchangedWorld(world,()=>operations.TryActivate(actor,other,platform,type,11,other.revision,2,"mechanism:busy"),MechanismOperationOutcome.Rejected,"Moving activation");
            UnchangedWorld(world,()=>operations.TryActivate(actor,other,platform,type,100,other.revision,2,"mechanism:busy"),MechanismOperationOutcome.Rejected,"Elapsed segment получил implicit completion");
            UnchangedWorld(world,()=>operations.Complete(platform,type,11,sequence),MechanismOperationOutcome.Unchanged,"Раннее completion");
            UnchangedWorld(world,()=>operations.Complete(platform,type,12,sequence+1),MechanismOperationOutcome.Rejected,"Wrong sequence completion");
            Require(operations.Complete(platform,type,12,sequence).Changed && platform.endpoint==1 && platform.segment==null,"Прибытие не зафиксировано");
            string archiveKey="remains:mechanism:retired-segment:"+sequence;
            Require(JToken.DeepEquals(platform.extensions[archiveKey]["mod:payload"],Unknown()["mod:payload"]),"Completion потерял precision unknown segment data");
            UnchangedWorld(world,()=>operations.Complete(platform,type,100,sequence),MechanismOperationOutcome.Unchanged,"Повтор completion изменил revision");
            var restored=StateJson.Copy(world);MechanismPersistence.ValidateWorld(restored,(_,_)=>Type());
            Require(JToken.DeepEquals(restored.movingPlatforms[0].extensions[archiveKey],platform.extensions[archiveKey]) &&
                JToken.DeepEquals(restored.mechanismButtons[0].extensions["mod:payload"],button.extensions["mod:payload"]),"Reload потерял archive/button unknown");
            operations=new MechanismOperations(restored);platform=restored.movingPlatforms[0];actor=restored.actors[0];button=restored.mechanismButtons[0];other=restored.mechanismButtons[1];
            UnchangedWorld(restored,()=>operations.TryActivate(actor,button,platform,type,13,button.revision,2,"mechanism:activate1"),MechanismOperationOutcome.Duplicate,"Saved journal не дедуплицирует после reload");
            Require(operations.TryActivate(actor,other,platform,type,13,other.revision,2,"mechanism:activate2").Changed &&
                platform.segment.sequence>sequence && platform.segment.toEndpoint==0 && button.pressed && other.pressed,"Обратный запуск после reload потерял monotonic sequence/независимые pressed");
            UnchangedWorld(restored,()=>operations.Complete(platform,type,15,sequence),MechanismOperationOutcome.Rejected,"Stale completion завершил новый переход");
            Require(operations.Complete(platform,type,15,platform.segment.sequence).Changed && platform.endpoint==0,"Обратное прибытие неверно");
        }
        private static void CheckAtomicity()
        {
            var world=World();var actor=world.actors[0];var button=world.mechanismButtons[0];var platform=world.movingPlatforms[0];var operations=new MechanismOperations(world);
            var candidate=Candidate(actor,button,"mechanism:pending");
            world.pendingGrants.Add(new PendingGrant {transaction="mechanism:pending"});
            UnchangedWorld(world,()=>operations.ApplyRepair(actor,button,candidate,2,"mechanism:pending"),MechanismOperationOutcome.Rejected,"Pending journal collision");
            world.pendingGrants.Clear();
            UnchangedWorld(world,()=>operations.ApplyRepair(actor,button,candidate,2,"inventory:collision"),MechanismOperationOutcome.Rejected,"Неверный namespace ID");
            actor.damageSequence++;
            UnchangedWorld(world,()=>operations.ApplyRepair(actor,button,candidate,2,"mechanism:pending"),MechanismOperationOutcome.Rejected,"Damage candidate принят");actor.damageSequence--;
            actor.x=100;
            UnchangedWorld(world,()=>operations.ApplyRepair(actor,button,candidate,2,"mechanism:pending"),MechanismOperationOutcome.Rejected,"Радиус candidate не проверен");actor.x=4;
            actor.connected=false;
            UnchangedWorld(world,()=>operations.ApplyRepair(actor,button,candidate,2,"mechanism:pending"),MechanismOperationOutcome.Rejected,"Отключённый actor принят");actor.connected=true;
            operations.ApplyRepair(actor,button,candidate,2,"mechanism:pending");
            UnchangedWorld(world,()=>operations.TryActivate(actor,button,platform,Type(),10,button.revision-1,2,"mechanism:stale"),MechanismOperationOutcome.Rejected,"Stale button revision");
            UnchangedWorld(world,()=>operations.TryActivate(actor,StateJson.Copy(button),platform,Type(),10,button.revision,2,"mechanism:detached"),MechanismOperationOutcome.Rejected,"Detached button mutated");
            world.revision=long.MaxValue;string before=StateJson.ToJson(world);
            Reject(()=>operations.TryActivate(actor,button,platform,Type(),10,button.revision,2,"mechanism:overflow"),"World counter overflow принят");
            Require(StateJson.ToJson(world)==before,"Overflow изменил часть состояния");world.revision=2;
            before=StateJson.ToJson(world);
            Reject(()=>operations.TryActivate(actor,button,platform,Type(),double.MaxValue,button.revision,2,"mechanism:time-overflow"),"Не представимый segment end принят");
            Require(StateJson.ToJson(world)==before,"Не представимый конец изменил кнопку");
            operations.TryActivate(actor,button,platform,Type(),10,button.revision,2,"mechanism:archive-collision");
            platform.segment.extensions=Unknown();string key="remains:mechanism:retired-segment:"+platform.segment.sequence;
            platform.extensions[key]=new JValue("existing mod data");before=StateJson.ToJson(world);
            Reject(()=>operations.Complete(platform,Type(),12,platform.segment.sequence),"Archive collision перезаписан");
            Require(StateJson.ToJson(world)==before,"Archive collision частично изменил arrival");
        }
        private static void Bad(Action<WorldState> corrupt,string label,bool definition=false)
        {
            var world=World();corrupt(world);string before=StateJson.ToJson(world);
            Reject(()=>MechanismPersistence.ValidateWorld(world,definition?(_,_)=>Type():null),label);
            Require(StateJson.ToJson(world)==before,"Validation изменила исходник: "+label);
        }
        private static void UnchangedWorld(WorldState world,Func<MechanismOperationResult> action,MechanismOperationOutcome outcome,string label)
        {
            string before=StateJson.ToJson(world);var result=action();
            Require(result.Outcome==outcome && StateJson.ToJson(world)==before,label);
            if(outcome==MechanismOperationOutcome.Rejected) Require(!string.IsNullOrWhiteSpace(result.Reason),label+": нет причины отказа");
        }
        private static ButtonRepairCandidate Candidate(ActorState actor,MechanismButtonState button,string id)
        {
            var progress=new ButtonRepairProgress();progress.Begin(id,actor,button,.5,2);
            return progress.Step(actor,button,true,false,.5).Candidate;
        }
        private static WorldState World()
        {
            var world=new WorldState {mechanismVersion=1,extensions=Unknown()};
            world.actors.Add(new ActorState {id="repairer",player=true,connected=true,x=4,y=2});
            world.movingPlatforms.Add(new MovingPlatformState {id="factory:0:platform",definition="base:platform2",definitionVersion=1,
                level="factory:0",roomId="base:factory-workshop-18",groupId="d2",startX=5,startY=2,endX=8,endY=2,extensions=Unknown()});
            foreach(string id in new[]{"button1","button2"}) world.mechanismButtons.Add(new MechanismButtonState {id="factory:0:"+id,definition="base:knop3",
                level="factory:0",roomId="base:factory-workshop-18",groupId="d2",platformId="factory:0:platform",x=4,y=2,
                repairState=MechanismRepairStates.NeedsRepair,extensions=Unknown()});
            return world;
        }
        private static MovingPlatformDefinition Type()=>new() {id="base:platform2",version=1,width=2.4f,height=.78f,graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=2,
            surfaceTexture="floor",movementSound="metal"};
        private static MovingPlatformSegment Segment(long sequence)=>new() {sequence=sequence,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=10,durationSeconds=2};
        private static IDictionary<string,JToken> Unknown()=>new Dictionary<string,JToken> {{"mod:payload",new JObject {
            ["precise"]=new JValue(1234567890123456789.125m),["nested"]=new JArray("данные",7)}}};
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Проверка операций механизмов: "+reason);}
        private static void Reject(Action action,string reason)
        {
            try {action();}catch(Exception error) when(error is InvalidOperationException || error is ArgumentException) {return;}
            throw new InvalidOperationException("Проверка операций механизмов: "+reason);
        }
    }
}
