using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Networking;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class WeaponRecoilReplicationChecks
    {
        public static void Run()
        {
            CheckSerialization();CheckWire();CheckMotionAndSelection();CheckReconciliation();CheckPauseAndMalformed();
        }
        private static ActorState Actor()=>new()
        {
            id="recoil-check",owner="owner",generation=2,lifecycleSequence=3,level="hub",motionSequence=12,
            x=17,y=10.4f,vx=-2,health=73,pose="climb",climbing=true,weapon="pistol",weaponInventoryVersion=1,
            equippedWeaponItem="gun-a",weaponSelectionRevision=4,
            inventory=new List<ItemStack> {new() {id="gun-a",definition="pistol",count=1,weaponState=new() {rounds=12}}},
            weaponRecoil=new WeaponRecoilPose {offsetX=.2f,angleDegrees=6,pauseRevision=3,extensions=Extensions()}
        };
        private static IDictionary<string,JToken> Extensions()=>new Dictionary<string,JToken>
        {
            ["mod:recoil"]=StateJson.FromJson<WeaponRecoilPose>("{\"mod:recoil\":{\"exact\":0.1234567890123456789012345678,\"label\":\"отдача\",\"nested\":[true,\"1e100\"]}}").extensions["mod:recoil"]
        };
        private static void CheckSerialization()
        {
            var actor=Actor();var world=new WorldState {worldId="recoil-world"};world.actors.Add(actor);
            var shot=new RangedShotPose {worldId=world.worldId,weaponItem="gun-a",rootX=17,rootY=11.2f,angleDegrees=6,
                selectionRevision=4,actorGeneration=2,actorLifecycle=3,hasCasing=true,casingX=17.1f,casingY=11.3f,extensions=Extensions()};
            var attack=new AttackEvent {weapon="pistol",rangedPose=shot};
            var copied=world.Copy();SameSample(actor.weaponRecoil,copied.actors[0].weaponRecoil,"сохранение мира");
            Require(!ReferenceEquals(actor.weaponRecoil,copied.actors[0].weaponRecoil) &&
                !ReferenceEquals(actor.weaponRecoil.extensions,copied.actors[0].weaponRecoil.extensions),"Снимок сохранил mutable alias отдачи");
            var savedAttack=StateJson.Copy(attack);savedAttack.rangedPose.Validate("проверка сохранения");
            Require(StateJson.ToJson(savedAttack.rangedPose)==StateJson.ToJson(shot),"Сохранение потеряло историческую позу выстрела/unknown");
            var message=StateJson.Copy(new PoseMessage {actor=actor.id,weaponRecoil=actor.weaponRecoil});
            SameSample(actor.weaponRecoil,message.weaponRecoil,"JSON позы");
            Require(StateJson.FromJson<ActorState>("{\"id\":\"legacy\"}").weaponRecoil==null &&
                StateJson.FromJson<AttackEvent>("{}").rangedPose==null,"Legacy missing sample не остался явно отсутствующим");
            WeaponRecoilReplication.NeutralizeWorld(copied,3);Neutral(copied.actors[0],3,"новый live мир");
            Require(actor.weaponRecoil.offsetX==.2f && JToken.DeepEquals(actor.weaponRecoil.extensions["mod:recoil"],
                copied.actors[0].weaponRecoil.extensions["mod:recoil"]),"Сброс live мира изменил source или потерял unknown");
            foreach(Action<RangedShotPose> change in new Action<RangedShotPose>[] {x=>x.version=2,x=>x.rootX=float.NaN,
                x=>x.angleDegrees=float.PositiveInfinity,x=>x.casingY=float.NegativeInfinity})
            {var invalid=StateJson.Copy(shot);change(invalid);Reject(()=>invalid.Validate("неверная поза выстрела"),"RangedShotPose принял неверные данные");}
        }
        private static void CheckWire()
        {
            foreach(var sample in new[] {null,WeaponRecoilPose.Neutral(3),Actor().weaponRecoil})
            {
                var writer=new NetworkWriter();writer.WriteWeaponRecoilPose(sample);
                var reader=new NetworkReader(writer.ToArraySegment());var result=reader.ReadWeaponRecoilPose();
                Require(reader.Position==writer.Position,"Wire оставил непрочитанные bytes отдачи");
                if(sample==null) Require(result==null,"Wire воскресил отсутствующий sample");else SameSample(sample,result,"Mirror wire");
            }
            var ordinary=new NetworkWriter();ordinary.WriteWeaponRecoilPose(WeaponRecoilPose.Neutral(3));
            Require(ordinary.Position==23,"Ordinary wire sample неожиданно включает JSON вместо compact fields");
            var corrupt=new NetworkWriter();corrupt.WriteBool(true);corrupt.WriteInt(1);corrupt.WriteFloat(.2f);corrupt.WriteFloat(6);
            corrupt.WriteLong(3);corrupt.WriteString(StateJson.ToJson(new WeaponRecoilPose {offsetX=.3f,angleDegrees=6,pauseRevision=3,extensions=Extensions()}));
            Reject(()=>new NetworkReader(corrupt.ToArraySegment()).ReadWeaponRecoilPose(),"Wire принял противоречивые compact/JSON fields");
            var malformed=new NetworkWriter();malformed.WriteWeaponRecoilPose(new WeaponRecoilPose {version=2,offsetX=float.NaN,pauseRevision=3});
            var badSample=new NetworkReader(malformed.ToArraySegment()).ReadWeaponRecoilPose();
            Require(badSample.version==2 && float.IsNaN(badSample.offsetX),"Wire скрыл malformed known sample до контекстной проверки");
        }
        private static void CheckMotionAndSelection()
        {
            var source=Actor();var target=source.Copy();target.x=0;target.weaponRecoil.offsetX=.7f;
            ActorPose.CopyMotion(source,target);SameSample(source.weaponRecoil,target.weaponRecoil,"CopyMotion");
            Require(target.x==17 && !ReferenceEquals(source.weaponRecoil,target.weaponRecoil),"CopyMotion сохранил alias/потерял body");
            foreach(string boundary in new[] {"item","definition","selection","inventory"})
            {
                target=source.Copy();target.weaponRecoil.offsetX=.7f;ChangeSelection(target,boundary);
                ActorPose.CopyMotion(source,target);
                Require(target.weaponRecoil.offsetX==.7f && target.x==17,"CopyMotion перенёс recoil через "+boundary);
            }
            target=source.Copy();target.weaponSelectionRevision=3;target.equippedWeaponItem=null;target.weapon=null;
            target.weaponRecoil.offsetX=.7f;WeaponInventory.PreserveSelection(source,target);
            Require(target.weaponSelectionRevision==4 && target.equippedWeaponItem=="gun-a","PreserveSelection потерял выбранный экземпляр");
            SameSample(source.weaponRecoil,target.weaponRecoil,"PreserveSelection");
            source.weaponRecoil=null;target=source.Copy();ActorPose.CopyMotion(source,target);
            Neutral(target,-1,"legacy CopyMotion");
        }
        private static void CheckReconciliation()
        {
            var current=Actor();var incoming=current.Copy();incoming.motionSequence=13;incoming.weaponRecoil.offsetX=.7f;incoming.x=40;incoming.health=35;
            WeaponRecoilReplication.Reconcile(current,incoming,"owner",3,false,"локальный владелец");
            SameSample(current.weaponRecoil,incoming.weaponRecoil,"owner preserve");Body(incoming,40,35,"owner preserve");
            incoming=current.Copy();incoming.weaponRecoil.offsetX=.7f;
            WeaponRecoilReplication.Reconcile(current,incoming,"other-owner",3,false,"remote старый");
            SameSample(current.weaponRecoil,incoming.weaponRecoil,"remote sequence preserve");
            incoming=current.Copy();incoming.motionSequence=13;incoming.weaponRecoil.offsetX=.7f;
            WeaponRecoilReplication.Reconcile(current,incoming,"other-owner",3,false,"remote новый");
            Require(incoming.weaponRecoil.offsetX==.7f,"Свежая remote отдача потеряна");
            foreach(string boundary in new[] {"id","owner","generation","lifecycle","level","item","definition","selection","inventory"})
            {
                incoming=current.Copy();incoming.x=40;incoming.health=35;
                switch(boundary)
                {
                    case "id":incoming.id="other";break;case "owner":incoming.owner="other";break;
                    case "generation":incoming.generation++;break;case "lifecycle":incoming.lifecycleSequence++;break;
                    case "level":incoming.level="factory:0";break;default:ChangeSelection(incoming,boundary);break;
                }
                WeaponRecoilReplication.Reconcile(current,incoming,"owner",3,false,"граница "+boundary);
                Neutral(incoming,3,boundary);Body(incoming,40,35,boundary);
            }
            incoming=current.Copy();WeaponRecoilReplication.Reconcile(null,incoming,"owner",3,false,"первая публикация");
            Neutral(incoming,3,"отсутствующий previous actor");
        }
        private static void CheckPauseAndMalformed()
        {
            foreach(long stamp in new long[] {-1,2,4})
            {
                var actor=Actor();actor.weaponRecoil.pauseRevision=stamp;
                WeaponRecoilReplication.Normalize(actor,3,false,"устаревшая/будущая пауза");Neutral(actor,3,"pause stamp");Body(actor,17,73,"pause stamp");
            }
            var paused=Actor();WeaponRecoilReplication.Normalize(paused,3,true,"пауза");Neutral(paused,3,"paused");
            var legacy=Actor();legacy.weaponRecoil=null;WeaponRecoilReplication.Normalize(legacy,3,false,"legacy");Neutral(legacy,3,"legacy");
            foreach(Action<WeaponRecoilPose> change in new Action<WeaponRecoilPose>[] {x=>x.version=2,x=>x.offsetX=float.NaN,
                x=>x.angleDegrees=float.PositiveInfinity,x=>x.pauseRevision=-2})
            {
                var actor=Actor();change(actor.weaponRecoil);int warnings=0;
                WeaponRecoilReplication.Normalize(actor,3,false,"неверный образец",message=>{warnings++;Require(message.Contains(actor.id),"Warning без actor id");});
                Require(warnings==1,"Malformed sample не получил один контекстный warning");Neutral(actor,3,"malformed");Body(actor,17,73,"malformed");
                Require(JToken.DeepEquals(actor.weaponRecoil.extensions["mod:recoil"],Extensions()["mod:recoil"]),"Malformed reset потерял unknown");
            }
            var current=Actor();var incoming=current.Copy();incoming.weaponRecoil.version=2;int count=0;
            WeaponRecoilReplication.Reconcile(current,incoming,"owner",3,false,"malformed owner superseded",_=>count++);
            Require(count==1,"Preserve owner скрыл malformed incoming");SameSample(current.weaponRecoil,incoming.weaponRecoil,"validated preserve");
            var stale=Actor();var sample=stale.weaponRecoil.Copy();sample.offsetX=.7f;
            WeaponRecoilReplication.ApplyPoseSample(stale,sample,3,"pistol",3,false,"старый выбор");
            Require(stale.weaponRecoil.offsetX==.2f,"Старый selection packet заменил recoil текущего экземпляра");
            WeaponRecoilReplication.ApplyPoseSample(stale,sample,4,"crowbar",3,false,"смена definition");Neutral(stale,3,"definition mismatch");
            var live=Actor();sample.pauseRevision=2;
            WeaponRecoilReplication.ApplyPoseSample(live,sample,4,"pistol",3,false,"новая body / старая пауза");
            Neutral(live,3,"pose stale pause");Body(live,17,73,"pose stale pause");
            var unsafeUnknown=Actor();unsafeUnknown.weaponRecoil.extensions["too-large"]=new JValue(1e100);
            Reject(()=>WeaponRecoilReplication.Reset(unsafeUnknown,3),"Сброс silently потерял точность unknown numeric");
            var collision=Actor();collision.weaponRecoil.extensions["version"]=new JValue(9);
            Reject(()=>WeaponRecoilReplication.Reset(collision,3),"Сброс принял конфликт unknown имени с known field",
                "Дополнительное поле version конфликтует с полем состояния WeaponRecoilPose.");
        }
        private static void ChangeSelection(ActorState actor,string field)
        {
            switch(field) {case "item":actor.equippedWeaponItem="gun-b";break;case "definition":actor.weapon="crowbar";break;
                case "selection":actor.weaponSelectionRevision++;break;case "inventory":actor.weaponInventoryVersion=0;break;}
        }
        private static void SameSample(WeaponRecoilPose expected,WeaponRecoilPose actual,string context)=>
            Require(StateJson.ToJson(expected)==StateJson.ToJson(actual),"Образец отдачи/unknown отличается: "+context);
        private static void Neutral(ActorState actor,long stamp,string context)=>Require(actor.weaponRecoil!=null && actor.weaponRecoil.version==1 &&
            actor.weaponRecoil.offsetX==0 && actor.weaponRecoil.angleDegrees==0 && actor.weaponRecoil.pauseRevision==stamp,"Отдача не сброшена: "+context);
        private static void Body(ActorState actor,float x,float health,string context)=>Require(actor.x==x && actor.y==10.4f && actor.health==health &&
            actor.pose=="climb" && actor.climbing,"Проверка отдачи изменила body/HP: "+context);
        private static void Reject(Action action,string reason,string expectedDiagnostic=null)
        {
            try {action();} catch(InvalidOperationException) when(expectedDiagnostic==null) {return;}
            catch(InvalidDataException error) when(expectedDiagnostic==null || error.Message==expectedDiagnostic) {return;}
            catch(System.Reflection.TargetInvocationException error) when(ExpectedWrappedDataError(error,expectedDiagnostic)) {return;}
            catch(Newtonsoft.Json.JsonSerializationException error) when(ExpectedWrappedDataError(error,expectedDiagnostic)) {return;}
            throw new InvalidOperationException(reason);
        }
        private static bool ExpectedWrappedDataError(Exception error,string expectedDiagnostic)
        {
            if(expectedDiagnostic==null) return false;
            while(error is System.Reflection.TargetInvocationException || error is Newtonsoft.Json.JsonSerializationException)
                error=error.InnerException;
            return error is InvalidDataException && error.Message==expectedDiagnostic;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
