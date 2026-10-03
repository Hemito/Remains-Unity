using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct WeaponTransferDone : NetworkMessage { }
    public struct WeaponTransferFinish : NetworkMessage { }

    // Real commands and coordinated container transactions; never edits inventory or magazines.
    public sealed class NetworkWeaponTransferProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private string transferred,own;
        private int stage;
        private float deadline,settled=-1,finishAt;
        private bool traveled,depositRequested,claimRequested,receivedNine,sawSeven,reported,finished;
        private readonly HashSet<int> completed=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Resume=>runtime.Options.Smoke.Contains("resume");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<WeaponTransferDone>((connection,message)=>
            {
                completed.Add(connection.connectionId);
                if(completed.Count==2) NetworkServer.SendToAll(new WeaponTransferFinish());
            });
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<WeaponTransferFinish>(_=>finishAt=Time.unscaledTime+1);
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null || Resume) return default;
            var command=new PlayerCommand {hasAim=true,aim=actor.Position+Vector2.up*20};
            if(stage==1 && Host)
            {
                if(actor.equippedWeaponItem!=transferred) command.weaponItem=transferred;
                command.fire=actor.inventory.Find(item=>item.id==transferred)?.weaponState.rounds>9;
            }
            if(stage==2 && Host) {command.fire=true;command.reload=true;} // Removed item must stop functioning.
            if(stage==3 && !Host)
            {
                if(actor.equippedWeaponItem!=transferred) command.weaponItem=transferred;
                command.fire=actor.inventory.Find(item=>item.id==transferred)?.weaponState.rounds>7;
            }
            if(stage==4 && !Host) command.reload=!runtime.Combat.Reload.Active;
            return command;
        }
        private void Update()
        {
            if(finished) return;
            if(finishAt>0) {if(Time.unscaledTime>=finishAt) Finish(true,"Переданный пистолет сохранил ID и магазин; собственный пистолет получателя независим. Старый владелец не стреляет копией. Сохранение проверено.");return;}
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут передачи: stage={stage}, deposit={depositRequested}, claim={claimRequested}, item={transferred}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=2) return;
            var donor=session.World.actors.Single(a=>a.character=="session-host");
            var receiver=session.World.actors.Single(a=>a.player && a.character!="session-host");
            if(Resume)
            {
                if(transferred==null) transferred=receiver.equippedWeaponItem;
                stage=5;
            }
            else
            {
                if(transferred==null) transferred=donor.inventory.Single(item=>item.definition=="pistol").id;
                if(own==null) own=receiver.inventory.Single(item=>item.definition=="pistol").id;
                if(!traveled) {traveled=true;session.Travel.RequestFactory();}
                if(donor.level!="factory:0" || receiver.level!="factory:0" || session.Travel.Pending) return;
                if(stage==0) stage=1;
                if(Host && stage==1 && actor.inventory.Find(item=>item.id==transferred)?.weaponState.rounds==9)
                {
                    depositRequested=session.Deposits.Request(ContainerState.PrototypeId,transferred);
                    if(!depositRequested) {Finish(false,"Не удалось запросить внесение пистолета.");return;}
                    stage=2;
                }
                var stored=session.World.containers.SelectMany(c=>c.items).FirstOrDefault(item=>item.id==transferred);
                if(!Host && stored!=null && !claimRequested)
                {
                    if(stored.weaponState?.rounds!=9) {Finish(false,"Контейнер получил другой магазин.");return;}
                    claimRequested=true;session.RequestClaim(ContainerState.PrototypeId,transferred);
                }
                var received=receiver.inventory.Find(item=>item.id==transferred);
                if(!Host && received!=null && stage<3)
                {
                    receivedNine=received.weaponState.rounds==9;
                    if(!receivedNine) {Finish(false,"Получатель не получил сохранённые девять патронов.");return;}
                    stage=3;
                }
                if(!Host && stage==3 && received?.weaponState.rounds==7) {sawSeven=true;stage=4;}
                if(!Host && stage==4 && received?.weaponState.rounds==12 && !runtime.Combat.Reload.Active) stage=5;
                if(Host && stage==2 && received?.weaponState.rounds==12 && InventoryJournal.Count(receiver,"p10")==31) stage=5;
            }
            if(stage!=5 || reported) return;
            if(settled<0) settled=Time.unscaledTime;
            if(Time.unscaledTime-settled<3) return;
            var transferredItem=receiver.inventory.SingleOrDefault(item=>item.id==transferred);
            var other=receiver.inventory.SingleOrDefault(item=>item.definition=="pistol" && item.id!=transferred);
            int occurrences=session.World.actors.Sum(a=>a.inventory.Count(item=>item.id==transferred))+
                session.World.containers.Sum(c=>c.items.Count(item=>item.id==transferred));
            bool valid=donor.inventory.All(item=>item.definition!="pistol") && string.IsNullOrEmpty(donor.weapon) &&
                occurrences==1 && transferredItem?.weaponState.rounds==12 && other?.weaponState.rounds==12 &&
                InventoryJournal.Count(receiver,"p10")==31 && session.World.pendingDeposits.Count==0 && session.World.pendingGrants.Count==0;
            if(!Resume) valid &= Host?runtime.Audio.LocalShots==3:receivedNine && sawSeven && runtime.Audio.LocalShots==2 && other.id==own;
            if(!valid) {Finish(false,"Нарушена независимость оружия, боезапаса или единственность переданного предмета.");return;}
            session.SaveLocalCharacter();
            var saved=session.Saves.LoadCharacter();
            if(saved.weaponInventoryVersion!=1 || (Host?saved.inventory.Any(item=>item.id==transferred):
                saved.inventory.Single(item=>item.id==transferred).weaponState.rounds!=12))
            {Finish(false,"Личный сейв потерял результат передачи.");return;}
            File.WriteAllText(runtime.Options.Report+".world.json",Remains.Serialization.StateJson.ToJson(session.World,true));
            reported=true;NetworkClient.Send(new WeaponTransferDone());
        }
        private void Finish(bool success,string detail)
        {
            finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,item=transferred,stage=stage},true));
            Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success;public string detail,item;public int stage;}
    }
}
