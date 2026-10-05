using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ItemShortcutReady : NetworkMessage {public int stage;}
    public struct ItemShortcutStep : NetworkMessage {public int stage,total;public string sender,recipient;}
    public struct ItemShortcutDone : NetworkMessage { }
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkItemShortcutProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,total,healBefore,sourceBefore,targetBefore;
        private string sender,recipient,dropId;
        private float deadline;
        private bool traveled,setup,healIssued,tossIssued,claimIssued,ready,finished;
        private readonly HashSet<string> initialUses=new();
        private readonly HashSet<int> captures=new();
        private readonly Dictionary<int,HashSet<int>> participants=new();
        public void Initialize(PrototypeRuntime value)
        {
            if(value.Hud==null) throw new InvalidOperationException("Проверка быстрых клавиш требует игровой интерфейс: запустите сценарий с CaptureFrames.");
            runtime=value;deadline=Time.unscaledTime+90;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ItemShortcutStep>(step=>
                {
                    sender=step.sender;recipient=step.recipient;total=step.total;
                    if(step.stage==2)
                    {
                        foreach(var use in value.Session.World.Find(sender).inventoryUses) initialUses.Add(use.id);
                        sourceBefore=InventoryJournal.Count(value.Session.World.Find(sender),"p10");
                        targetBefore=InventoryJournal.Count(value.Session.World.Find(recipient),"p10");
                    }
                    phase=step.stage+1;tossIssued=claimIssued=ready=false;dropId=null;
                });
                NetworkClient.RegisterHandler<ItemShortcutDone>(_=>Finish(true,"UI-назначения аптечки/патронов, лечение, одиночный и полный бросок по клавишам, общий подбор и резервные копии проверены."));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ItemShortcutReady>((connection,message)=>
            {
                if(!participants.TryGetValue(message.stage,out var peers)) participants.Add(message.stage,peers=new());
                peers.Add(connection.connectionId);
                if(peers.Count!=value.Options.ProbePeers || message.stage>=3) return;
                var actors=value.Session.ServerWorld.actors.Where(actor=>actor.player && actor.connected).OrderBy(actor=>actor.character).ToArray();
                string source=actors[0].id,target=actors.Length==1?source:actors.First(actor=>actor.id!=source).id;
                NetworkServer.SendToAll(new ItemShortcutStep {stage=message.stage+1,sender=source,recipient=target,
                    total=actors.Sum(actor=>InventoryJournal.Count(actor,"p10"))});
            });
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(phase==2 && !healIssued) {healIssued=true;return new PlayerCommand {weaponShortcut="5"};}
            if(phase is 3 or 4)
            {
                if(actor.id==sender && !tossIssued)
                {
                    tossIssued=true;
                    return new PlayerCommand {weaponShortcut=phase==3?"=":"-",shortcutWholeStack=phase==4,hasAim=true,aim=actor.Position+Vector2.right*.6f};
                }
                if(actor.id==recipient && dropId!=null && !runtime.Hud.ContainerOpen)
                {
                    var drop=runtime.Session.World.containers.Find(container=>container.id==dropId);if(drop==null || drop.items.Count==0) return default;
                    var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,runtime.Session.Travel.Points(actor.level));
                    if(target.kind==InteractionKind.Container && target.id==dropId) return new PlayerCommand {interact=true};
                    return new PlayerCommand {movement=new Vector2(Math.Sign(drop.x-actor.x),0)};
                }
            }
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут назначений: phase={phase}, drop={dropId}, actor={runtime.Session.LocalActor?.x}.");return;}
            var actor=runtime.Session.LocalActor;if(actor==null) return;
            var world=runtime.Session.World;
            if(!traveled && world.actors.Count(value=>value.player && value.connected)==runtime.Options.ProbePeers)
            {traveled=true;runtime.Session.Travel.RequestFactory();phase=1;}
            if(phase==1 && actor.level=="factory:0" && !runtime.Session.Travel.Pending && !setup)
            {setup=true;StartCoroutine(SetupInventory());}
            if(phase==2 && actor.health>60 && InventoryJournal.Count(actor,"heal")==healBefore-1 && !ready)
            {ready=true;NetworkClient.Send(new ItemShortcutReady {stage=1});}
            if(phase is 3 or 4)
            {
                var drop=world.containers.FirstOrDefault(container=>container.presentationItem=="p10");
                if(drop!=null)
                {
                    dropId=drop.id;
                    if(runtime.Options.Smoke.Contains("-frames-") && drop.items.Count>0 && captures.Add(phase)) NetworkSceneRecord.Save(runtime,phase);
                }
                if(actor.id==recipient && runtime.Hud.ContainerOpen && runtime.Hud.ShownInventorySourceId==dropId && !claimIssued)
                {claimIssued=true;StartCoroutine(Claim());}
                int debits=world.Find(sender).inventoryUses.Count(use=>!initialUses.Contains(use.id));
                int sourceCount=InventoryJournal.Count(world.Find(sender),"p10"),targetCount=InventoryJournal.Count(world.Find(recipient),"p10");
                int expectedSource=sender==recipient?sourceBefore:phase==3?sourceBefore-1:0;
                int expectedTarget=sender==recipient?sourceBefore:phase==3?targetBefore+1:targetBefore+sourceBefore;
                if(!ready && debits>=phase-2 && sourceCount==expectedSource && targetCount==expectedTarget &&
                    !world.containers.Any(container=>container.presentationItem!=null) && world.pendingDeposits.Count==0 && world.pendingGrants.Count==0)
                {
                    if(world.Find(recipient).inventory.Count(item=>item.definition=="p10")!=1)
                    {Finish(false,"Брошенные боеприпасы не объединены в реальную стопку.");return;}
                    ready=true;NetworkClient.Send(new ItemShortcutReady {stage=phase-1});
                }
            }
            if(NetworkServer.active && phase==4 && participants.TryGetValue(3,out var all) && all.Count==runtime.Options.ProbePeers &&
                runtime.Session.HasInventoryBackups(runtime.Session.ServerWorld.revision))
            {phase=5;NetworkServer.SendToAll(new ItemShortcutDone());}
        }
        private IEnumerator SetupInventory()
        {
            runtime.Hud.ToggleInventory();
            var actor=runtime.Session.LocalActor;var filter=new InventoryItemFilter(WeaponCatalog.All);
            var heal=actor.inventory.First(item=>item.definition=="heal");healBefore=InventoryJournal.Count(actor,"heal");
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Items));
            int row=actor.inventory.Where(item=>filter.Includes(item,InventoryCategory.Items)).ToList().FindIndex(item=>item.id==heal.id);
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(4));
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Ammunition));
            actor=runtime.Session.LocalActor;var ammo=actor.inventory.First(item=>item.definition=="p10");
            row=actor.inventory.Where(item=>filter.Includes(item,InventoryCategory.Ammunition)).ToList().FindIndex(item=>item.id==ammo.id);
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));
            yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(10));yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(11));
            actor=runtime.Session.LocalActor;
            if(ItemShortcuts.Get(actor,"5")?.definition!="heal" || ItemShortcuts.Get(actor,"-")?.id!=ammo.id || ItemShortcuts.Get(actor,"=")?.id!=ammo.id)
            {Finish(false,"Указатель не назначил реальные стопки на [5], [-] и [=].");yield break;}
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,1);
            // Fixture damage must advance the state revision, just like a real damage result.
            runtime.Hud.ToggleInventory();actor.health=60;actor.damageSequence++;actor.revision++;
            runtime.Session.PublishActor(actor);phase=2;
        }
        private IEnumerator Claim() {yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0),followTarget:true);}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            runtime.Session.SaveLocalCharacter();
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,total=total},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase,total;}
    }
}
