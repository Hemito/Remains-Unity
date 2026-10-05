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
    public struct ItemShortcutObserved : NetworkMessage {public int phase;public bool held;public string source;}
    public struct ItemShortcutBarrier : NetworkMessage {public int phase;public bool held;public string source;}
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkItemShortcutProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,total,healBefore,sourceBefore,targetBefore;
        private string sender,recipient,dropId;
        private float deadline;
        private bool traveled,setup,healIssued,tossIssued,claimIssued,ready,finished,selectionIssued;
        private bool heldObserved,dropObserved,heldReleased,dropReleased;
        private int visualChangedFrame,phaseStartedFrame;
        private PropView observedDropView;
        private float heldAt;
        private int selectedCountBefore,selectedUsesBefore,selectedDropsBefore;
        private readonly HashSet<string> initialUses=new();
        private readonly HashSet<int> captures=new();
        private readonly Dictionary<int,HashSet<int>> participants=new();
        private readonly Dictionary<string,HashSet<int>> observations=new();
        private readonly HashSet<string> releasedBarriers=new();
        public void Initialize(PrototypeRuntime value)
        {
            if(value.Hud==null) throw new InvalidOperationException("Проверка быстрых клавиш требует игровой интерфейс: запустите сценарий с CaptureFrames.");
            runtime=value;deadline=Time.unscaledTime+90;value.CommandSource=Command;value.Session.Changed+=VisualChanged;
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
                    phase=step.stage+1;tossIssued=claimIssued=ready=selectionIssued=false;dropId=null;
                    heldObserved=dropObserved=heldReleased=dropReleased=false;phaseStartedFrame=Time.frameCount;observedDropView=null;
                });
                NetworkClient.RegisterHandler<ItemShortcutBarrier>(barrier=>
                {
                    if(barrier.phase!=phase) return;
                    if(barrier.held && barrier.source==sender) heldReleased=true;
                    else if(!barrier.held && barrier.source==dropId) dropReleased=true;
                });
                NetworkClient.RegisterHandler<ItemShortcutDone>(_=>Finish(true,"UI-назначения аптечки/патронов, лечение, выбор без расхода и отдельный одиночный/полный бросок ЛКМ, общий подбор и резервные копии проверены."));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<ItemShortcutObserved>((connection,message)=>
                {
                    if(message.phase!=phase || phase is not (3 or 4) || string.IsNullOrEmpty(message.source)) return;
                    if(message.held && message.source!=sender) return;
                    if(!message.held && !value.Session.ServerWorld.containers.Any(container=>container.id==message.source && container.presentationItem=="p10" && container.items.Count>0)) return;
                    string key=$"{message.phase}:{message.held}:{message.source}";
                    if(!observations.TryGetValue(key,out var peers)) observations.Add(key,peers=new());
                    peers.Add(connection.connectionId);
                    if(peers.Count!=value.Options.ProbePeers || !releasedBarriers.Add(key)) return;
                    NetworkServer.SendToAll(new ItemShortcutBarrier {phase=message.phase,held=message.held,source=message.source});
                });
                NetworkServer.RegisterHandler<ItemShortcutReady>((connection,message)=>
                {
                    if(!participants.TryGetValue(message.stage,out var peers)) participants.Add(message.stage,peers=new());
                    peers.Add(connection.connectionId);
                    if(peers.Count!=value.Options.ProbePeers || message.stage>=3) return;
                    var actors=value.Session.ServerWorld.actors.Where(actor=>actor.player && actor.connected).OrderBy(actor=>actor.character).ToArray();
                    string source=actors[0].id,target=actors.Length==1?source:actors.First(actor=>actor.id!=source).id;
                    NetworkServer.SendToAll(new ItemShortcutStep {stage=message.stage+1,sender=source,recipient=target,
                        total=actors.Sum(actor=>InventoryJournal.Count(actor,"p10"))});
                });
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(phase==2 && !healIssued) {healIssued=true;return new PlayerCommand {weaponShortcut="5"};}
            if(phase is 3 or 4)
            {
                if(actor.id==sender && !tossIssued)
                {
                    var holdAim=actor.Position+new Vector2(.6f,1.25f);
                    if(!selectionIssued)
                    {
                        selectionIssued=true;heldAt=Time.unscaledTime;
                        selectedCountBefore=InventoryJournal.Count(actor,"p10");selectedUsesBefore=actor.inventoryUses.Count;
                        selectedDropsBefore=runtime.Session.World.containers.Count(container=>container.presentationItem!=null);
                        return new PlayerCommand {weaponShortcut=phase==3?"=":"-",shortcutWholeStack=phase==4,hasAim=true,aim=holdAim};
                    }
                    if(Time.unscaledTime-heldAt<.4f) return new PlayerCommand {hasAim=true,aim=holdAim};
                    var held=HeldItemSelection.Selected(actor);var view=runtime.HeldItems.Find(actor.id);
                    if(held?.definition!="p10" || InventoryJournal.Count(actor,"p10")!=selectedCountBefore || actor.inventoryUses.Count!=selectedUsesBefore ||
                        runtime.Session.Deposits.Pending || runtime.Session.World.containers.Count(container=>container.presentationItem!=null)!=selectedDropsBefore ||
                        !actor.heldItemPoseValid || actor.heldItemPoseY<=.1f || view==null || !view.enabled ||
                        Vector2.Distance(view.transform.position,HeldItemPose.Mount(actor))>.001f)
                    {Finish(false,$"Выбор не оставил предмет удерживаемым без расхода: phase={phase}, held={held?.id}, count={InventoryJournal.Count(actor,"p10")}, pose=({actor.heldItemPoseX},{actor.heldItemPoseY}), valid={actor.heldItemPoseValid}.");return default;}
                    // Every peer captures the still-held source before its separate LMB release.
                    if(!heldReleased) return new PlayerCommand {hasAim=true,aim=holdAim};
                    tossIssued=true;
                    return new PlayerCommand {fire=true,throwWholeStack=phase==4,hasAim=true,aim=actor.Position+new Vector2(.6f,.05f)};
                }
                if(actor.id==recipient && dropId!=null && !claimIssued)
                {
                    var drop=runtime.Session.World.containers.Find(container=>container.id==dropId);if(drop==null || drop.items.Count==0) return default;
                    var cursor=((InventorySource)drop).Bounds.center;
                    var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,cursor,runtime.Session.Travel.Points(actor.level));
                    if(target.kind==InteractionKind.Pickup && target.id==dropId)
                    {
                        if(!dropReleased) return new PlayerCommand {hasAim=true,aim=cursor};
                        claimIssued=true;return new PlayerCommand {interact=true,hasAim=true,aim=cursor};
                    }
                    return new PlayerCommand {movement=new Vector2(Math.Sign(drop.x-actor.x),0)};
                }
            }
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут назначений: phase={phase}, drop={dropId}, actor={runtime.Session.LocalActor?.x}, heldObserved={heldObserved}, heldReleased={heldReleased}, dropObserved={dropObserved}, dropReleased={dropReleased}.");return;}
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
                ObserveHeldEvidence();
                var drop=world.containers.FirstOrDefault(container=>container.presentationItem=="p10");
                if(drop!=null)
                {
                    dropId=drop.id;
                    if(drop.items.Count>0) ObserveDropEvidence(drop);
                }
                if(actor.id==recipient && runtime.Hud.ContainerOpen)
                {Finish(false,"Подбор свободной вещи открыл окно контейнера.");return;}
                int debits=world.Find(sender).inventoryUses.Count(use=>!initialUses.Contains(use.id));
                int sourceCount=InventoryJournal.Count(world.Find(sender),"p10"),targetCount=InventoryJournal.Count(world.Find(recipient),"p10");
                int expectedSource=sender==recipient?sourceBefore:phase==3?sourceBefore-1:0;
                int expectedTarget=sender==recipient?sourceBefore:phase==3?targetBefore+1:targetBefore+sourceBefore;
                if(!ready && heldObserved && heldReleased && dropObserved && dropReleased && debits>=phase-2 && sourceCount==expectedSource && targetCount==expectedTarget &&
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
        private void VisualChanged()=>visualChangedFrame=Time.frameCount;
        private bool VisualsSettled=>Time.frameCount>Math.Max(visualChangedFrame,phaseStartedFrame);
        private bool Visible(SpriteRenderer view)
        {
            if(view==null || view.sprite==null || !view.enabled || view.color.a<=0 || !view.gameObject.activeInHierarchy) return false;
            var camera=Camera.main;
            if(camera==null) throw new InvalidOperationException("Проверка предметов: отсутствует игровая камера для записи наблюдения.");
            var point=camera.WorldToViewportPoint(view.bounds.center);
            return point.z>0 && point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1;
        }
        private void ObserveHeldEvidence()
        {
            if(heldObserved || sender==null || !VisualsSettled) return;
            var actor=runtime.Session.World.Find(sender);var view=runtime.HeldItems.Find(sender);
            if(actor==null || HeldItemSelection.Selected(actor)?.definition!="p10" || !actor.heldItemPoseValid ||
                actor.heldItemPoseY<=.1f || !Visible(view) || Vector2.Distance(view.transform.position,HeldItemPose.Mount(actor))>.005f) return;
            int evidence=10+phase;
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,evidence);
            captures.Add(evidence);heldObserved=true;
            NetworkClient.Send(new ItemShortcutObserved {phase=phase,held=true,source=sender});
        }
        private void ObserveDropEvidence(ContainerState drop)
        {
            if(dropObserved || !VisualsSettled) return;
            // Wait a frame after Changed, then require the actual rendered source and claimable content.
            if(observedDropView==null) observedDropView=UnityEngine.Object.FindObjectsByType<PropView>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                .FirstOrDefault(value=>value.name=="Брошенный предмет "+drop.id);
            if(observedDropView==null || !Visible(observedDropView.Artwork)) return;
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,phase);
            captures.Add(phase);dropObserved=true;
            NetworkClient.Send(new ItemShortcutObserved {phase=phase,held=false,source=drop.id});
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
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(success && new[]{3,4,13,14}.Any(evidence=>!captures.Contains(evidence)))
            {success=false;detail="Не все этапы удержания/броска действительно наблюдались до подбора: "+string.Join(",",captures.OrderBy(value=>value));}
            finished=true;
            runtime.Session.SaveLocalCharacter();
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,total=total,observedStages=captures.OrderBy(value=>value).ToArray()},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy() {if(runtime!=null) runtime.Session.Changed-=VisualChanged;}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase,total;public int[] observedStages;}
    }
}
