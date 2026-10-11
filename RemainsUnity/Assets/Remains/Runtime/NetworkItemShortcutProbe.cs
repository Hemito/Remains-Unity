using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine.UI;
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
        private bool directSelected,healedByUi,healingClosedForLifecycle;
        private int directSelections,commandSamples;
        private readonly Dictionary<string,LandingWitness> landings=new();
        private readonly HashSet<int> voicesBeforeThrow=new();
        private AudioClip landingClip;
        private AudioClip healingClip;
        private readonly Dictionary<string,HealingWitness> healing=new(StringComparer.Ordinal);
        [Serializable] private sealed class HealingWitness
        {public string transaction,actor,world,clip;public int voice,samples,channels,frequency;public float volume;public bool muted,playing;}
        private Vector2 initialPosition,fixturePosition;
        private bool Direct=>runtime.Options.Smoke.StartsWith("direct-items");
        [Serializable] private sealed class LandingWitness
        {public string prop,item,clip;public int phase,count,voice;public float volume,at,quietSeconds;public bool muted,captured;public int width,height;}
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
            if(Direct)
            {
                if(value.Options.Smoke.Contains("-frames-")==false || Screen.width!=1920 || Screen.height!=1080 || string.IsNullOrEmpty(value.Options.SaveDirectory))
                    throw new InvalidOperationException("Прямое действие требует кадров 1920×1080 и отдельных сохранений.");
                if(value.Options.Role=="host")
                {
                    if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Прямое действие требует новый мир; существующий сейв не заменяется.");
                    value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
                }
                if(!value.PropAudio.ItemSounds.TryGet("p10",out landingClip,out var reason))
                    throw new InvalidOperationException("Не разрешён WAV приземления p10: "+reason);
                value.PropAudio.LandingPlayed+=ObserveLanding;
                if(!value.HealingAudio.TryGet("heal",out healingClip,out _))
                    throw new InvalidOperationException("Нет подготовленного звука успешного лечения.");
                value.HealingAudio.HealingPlayed+=ObserveHealing;
            }
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
                    voicesBeforeThrow.Clear();
                    foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>()) voicesBeforeThrow.Add(voice.GetInstanceID());
                    heldObserved=dropObserved=heldReleased=dropReleased=directSelected=false;phaseStartedFrame=Time.frameCount;observedDropView=null;
                });
                NetworkClient.RegisterHandler<ItemShortcutBarrier>(barrier=>
                {
                    if(barrier.phase!=phase) return;
                    if(barrier.held && barrier.source==sender) heldReleased=true;
                    else if(!barrier.held && barrier.source==dropId) dropReleased=true;
                });
                NetworkClient.RegisterHandler<ItemShortcutDone>(_=>Finish(true,Direct?"Прямые действия настоящего UI в обычном хабе: лечение, выбор без расхода, одиночный/полный бросок ЛКМ, WAV приземления и E-подбор на всех участниках.":"UI-назначения аптечки/патронов, лечение, выбор без расхода и отдельный одиночный/полный бросок ЛКМ, общий подбор и резервные копии проверены."));
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
            commandSamples++;
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(phase==2 && !healIssued)
            {
                healIssued=true;
                if(Direct) {StartCoroutine(HealDirect());return default;}
                return new PlayerCommand {weaponShortcut="5"};
            }
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
                        if(Direct) {StartCoroutine(SelectDirectItem());return default;}
                        return new PlayerCommand {weaponShortcut=phase==3?"=":"-",shortcutWholeStack=phase==4,hasAim=true,aim=holdAim};
                    }
                    if(Direct && !directSelected || Time.unscaledTime-heldAt<.4f) return new PlayerCommand {hasAim=true,aim=holdAim};
                    var held=HeldItemSelection.Selected(actor);var view=runtime.HeldItems.Find(actor.id);
                    if(held?.definition!="p10" || InventoryJournal.Count(actor,"p10")!=selectedCountBefore || actor.inventoryUses.Count!=selectedUsesBefore ||
                        runtime.Session.Deposits.Pending || runtime.Session.World.containers.Count(container=>container.presentationItem!=null)!=selectedDropsBefore ||
                        !actor.heldItemPoseValid || actor.heldItemPoseY<=.1f || view==null || !view.enabled ||
                        Vector2.Distance(view.transform.position,HeldItemPose.Mount(actor))>.001f)
                    {Finish(false,$"Выбор не оставил предмет удерживаемым без расхода: phase={phase}, held={held?.id}, count={InventoryJournal.Count(actor,"p10")}, pose=({actor.heldItemPoseX},{actor.heldItemPoseY}), valid={actor.heldItemPoseValid}.");return default;}
                    // Every peer captures the still-held source before its separate LMB release.
                    if(!heldReleased) return new PlayerCommand {hasAim=true,aim=holdAim};
                    tossIssued=true;
                    voicesBeforeThrow.Clear();
                    foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>()) voicesBeforeThrow.Add(voice.GetInstanceID());
                    return new PlayerCommand {fire=true,throwWholeStack=phase==4,hasAim=true,aim=actor.Position+new Vector2(.6f,Direct ? .8f : .05f)};
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
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут назначений: phase={phase}, commands={commandSamples}, inventoryOpen={runtime.Hud.InventoryOpen}, levelPreparing={runtime.IsLevelPreparing}, healIssued={healIssued}, healedByUi={healedByUi}, drop={dropId}, actor={runtime.Session.LocalActor?.x}, heldObserved={heldObserved}, heldReleased={heldReleased}, dropObserved={dropObserved}, dropReleased={dropReleased}.");return;}
            var actor=runtime.Session.LocalActor;if(actor==null) return;
            var world=runtime.Session.World;
            if(!traveled && world.actors.Count(value=>value.player && value.connected)==runtime.Options.ProbePeers)
            {
                traveled=true;
                if(Direct)
                {
                    if(WorldRooms.Definition(world,"factory:0")!=LegacyFactoryFixture.DefinitionId || actor.level!="hub")
                        {Finish(false,"Прямое действие получило старый проверочный мир вместо обычного хаба.");return;}
                }
                else runtime.Session.Travel.RequestFactory();
                phase=1;
            }
            if(phase==1 && actor.level==(Direct?"hub":"factory:0") && !runtime.Session.Travel.Pending && !runtime.Hud.RoomPresentationPending && !setup)
            {setup=true;StartCoroutine(SetupInventory());}
            if(phase==2 && (!Direct || healedByUi && healing.Count==runtime.Options.ProbePeers) && actor.health>60 && InventoryJournal.Count(actor,"heal")==healBefore-1 && !ready)
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
            if(heldObserved || sender==null || !VisualsSettled || Direct && runtime.Session.LocalActorId==sender && !directSelected) return;
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
            if(Direct)
            {
                if(!landings.TryGetValue(drop.id,out var landing) || Time.unscaledTime-landing.at<.75f) return;
                if(landing.count!=1) {Finish(false,"Повторный WAV приземления одного броска: "+drop.id);return;}
                if(drop.held || Mathf.Abs(drop.vx)>.03f || Mathf.Abs(drop.vy)>.03f) return;
                landing.quietSeconds=Time.unscaledTime-landing.at;landing.captured=true;landing.width=Screen.width;landing.height=Screen.height;
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+$".phase-{phase}.landing.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                File.WriteAllText(runtime.Options.Report+$".phase-{phase}.landing.json",StateJson.ToJson(landing,true));
            }
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,phase);
            captures.Add(phase);dropObserved=true;
            NetworkClient.Send(new ItemShortcutObserved {phase=phase,held=false,source=drop.id});
        }
        private IEnumerator SetupInventory()
        {
            if(Direct)
            {
                // Explicit initial player fixture on the real lower hub floor. No items,
                // props or surfaces are injected; the route downstairs is outside this probe.
                var owner=runtime.Session.LocalActor;initialPosition=owner.Position;
                fixturePosition=new Vector2(owner.x,.8f);
                var room=runtime.Geometry.Get(owner.level);
                if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(owner),fixturePosition) ||
                    !ActorTerrainMotion.Supported(room,Anatomy.Movement(owner),fixturePosition))
                    {Finish(false,"Начальная нижняя площадка не свободна или не поддерживает персонажа: "+fixturePosition);yield break;}
                owner.Position=fixturePosition;owner.vx=owner.vy=0;owner.revision++;runtime.Session.PublishActor(owner);
                yield return null;yield return null;
            }
            runtime.Hud.ToggleInventory();
            var actor=runtime.Session.LocalActor;var filter=new InventoryItemFilter(WeaponCatalog.All);
            var heal=actor.inventory.First(item=>item.definition=="heal");healBefore=InventoryJournal.Count(actor,"heal");
            if(!Direct)
            {
                yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Items));
                int row=actor.inventory.Where(item=>filter.Includes(item,InventoryCategory.Items)).ToList().FindIndex(item=>item.id==heal.id);
                yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));
                yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(4));
                yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Ammunition));
                actor=runtime.Session.LocalActor;var ammo=actor.inventory.First(item=>item.definition=="p10");
                row=actor.inventory.Where(item=>filter.Includes(item,InventoryCategory.Ammunition)).ToList().FindIndex(item=>item.id==ammo.id);
                yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));
                yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(10));yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(11));
                actor=runtime.Session.LocalActor;
                if(ItemShortcuts.Get(actor,"5")?.definition!="heal" || ItemShortcuts.Get(actor,"-")?.id!=ammo.id || ItemShortcuts.Get(actor,"=")?.id!=ammo.id)
                {Finish(false,"Указатель не назначил реальные стопки на [5], [-] и [=].");yield break;}
            }
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,1);
            // Fixture damage must advance the state revision, just like a real damage result.
            runtime.Hud.ToggleInventory();actor.health=60;actor.damageSequence++;actor.revision++;
            runtime.Session.PublishActor(actor);phase=2;
        }
        private IEnumerator HealDirect()
        {
            runtime.Hud.ToggleInventory();
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Items));
            var actor=runtime.Session.LocalActor;
            var rows=actor.inventory.Where(i=>new InventoryItemFilter(WeaponCatalog.All).Includes(i,InventoryCategory.Items)).ToList();
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(rows.FindIndex(i=>i.definition=="heal")));
            long lifecycleBefore=runtime.Session.LocalActor.lifecycleSequence;
            yield return UiPointerProbe.Click(runtime.Hud.UseItemRect);
            healedByUi=true;
            // Existing HealingRules advances combat lifecycle. HUD closes old actor-context
            // windows on that transition; holding an ordinary item must keep its window open.
            healingClosedForLifecycle=!runtime.Hud.InventoryOpen && runtime.Session.LocalActor.lifecycleSequence!=lifecycleBefore;
            if(!runtime.Hud.InventoryOpen && !healingClosedForLifecycle)
                {Finish(false,"Окно лечения закрылось без перехода lifecycle.");yield break;}
            if(runtime.Hud.InventoryOpen) runtime.Hud.ToggleInventory();
        }
        private IEnumerator SelectDirectItem()
        {
            runtime.Hud.ToggleInventory();
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Ammunition));
            var actor=runtime.Session.LocalActor;
            var rows=actor.inventory.Where(i=>new InventoryItemFilter(WeaponCatalog.All).Includes(i,InventoryCategory.Ammunition)).ToList();
            var ammo=rows.Single(i=>i.definition=="p10");
            string selectedBefore=HeldItemSelection.Selected(actor)?.id;
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(rows.IndexOf(ammo)));
            actor=runtime.Session.LocalActor;
            if(HeldItemSelection.Selected(actor)?.id!=selectedBefore || InventoryJournal.Count(actor,"p10")!=selectedCountBefore || actor.inventoryUses.Count!=selectedUsesBefore)
                {Finish(false,"Один выбор строки уже использовал предмет.");yield break;}
            var use=runtime.Hud.UseItemRect.GetComponent<Button>();
            if(!use.interactable || use.GetComponentInChildren<Text>().text!="Взять в копыта")
                {Finish(false,"UI не показывает доступное действие удерживания патронов.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.UseItemRect);
            actor=runtime.Session.LocalActor;
            if(!runtime.Hud.InventoryOpen || HeldItemSelection.Selected(actor)?.id!=ammo.id ||
                InventoryJournal.Count(actor,"p10")!=selectedCountBefore || actor.inventoryUses.Count!=selectedUsesBefore ||
                ItemShortcuts.Keys.Any(slot=>ItemShortcuts.Get(actor,slot)?.definition=="p10"))
                {Finish(false,"Прямое действие требует shortcut, расходует предмет или закрывает UI.");yield break;}
            directSelections++;
            NetworkSceneRecord.Save(runtime,20+phase);
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+$".phase-{phase}.inventory.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
            runtime.Hud.ToggleInventory();directSelected=true;heldAt=Time.unscaledTime;
        }
        private void ObserveHealing(HealingSoundCue cue,AudioClip clip)
        {
            var voice=runtime.Audio.GetComponentsInChildren<AudioSource>().LastOrDefault(v=>v.clip==clip && v.volume>0);
            if(clip!=healingClip || cue.definition!="heal" || voice==null || !voice.isPlaying ||
                healing.ContainsKey(cue.transaction) || clip.channels!=1 || clip.frequency!=44100 || clip.samples!=31936)
            {Finish(false,"Звук лечения повторён или не создал ожидаемый native PCM голос: "+cue.transaction);return;}
            healing.Add(cue.transaction,new HealingWitness {transaction=cue.transaction,actor=cue.actor,world=cue.world,
                clip=clip.name,voice=voice.GetInstanceID(),volume=voice.volume,muted=voice.mute,playing=voice.isPlaying,
                channels=clip.channels,frequency=clip.frequency,samples=clip.samples});
        }
        private void ObserveLanding(string id,string item,AudioClip clip)
        {
            if(finished || phase is not (3 or 4) || item!="p10") return;
            if(!landings.TryGetValue(id,out var landing))
            {
                var prop=runtime.Session.World.containers.FirstOrDefault(c=>c.id==id);
                var voice=runtime.Audio.GetComponentsInChildren<AudioSource>().LastOrDefault(v=>v.clip==clip && v.volume>0 && !voicesBeforeThrow.Contains(v.GetInstanceID()));
                if(prop?.presentationItem!="p10" || clip!=landingClip || clip.samples<=0 || voice==null)
                    {Finish(false,"WAV приземления не соответствует реальному предмету/голосу: "+id);return;}
                landing=new LandingWitness {phase=phase,prop=id,item=item,clip=clip.name,voice=voice.GetInstanceID(),volume=voice.volume,muted=voice.mute,at=Time.unscaledTime};
                landings.Add(id,landing);
            }
            landing.count++;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(success && new[]{3,4,13,14}.Any(evidence=>!captures.Contains(evidence)))
            {success=false;detail="Не все этапы удержания/броска действительно наблюдались до подбора: "+string.Join(",",captures.OrderBy(value=>value));}
            if(success && Direct && (!healedByUi || healing.Count!=runtime.Options.ProbePeers ||
                runtime.HealingAudio.Accepted!=runtime.Options.ProbePeers || runtime.HealingAudio.Played!=runtime.Options.ProbePeers ||
                landings.Count!=2 || landings.Values.Any(l=>!l.captured || l.count!=1 || l.quietSeconds<.75f) ||
                runtime.Session.LocalActorId==sender && directSelections!=2))
            {success=false;detail="Недостаточно прямых UI/audio доказательств.";}
            finished=true;
            runtime.Session.SaveLocalCharacter();
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,total=total,observedStages=captures.OrderBy(value=>value).ToArray(),directInventory=Direct,ordinaryHub=Direct,healedByUi=healedByUi,directSelections=directSelections,
                commandSamples=commandSamples,healingClosedForLifecycle=healingClosedForLifecycle,startingPositionFixture=Direct,
                initialPosition=initialPosition,fixturePosition=fixturePosition,landings=landings.Values.OrderBy(l=>l.phase).ToArray(),
                healing=healing.Values.OrderBy(v=>v.actor,StringComparer.Ordinal).ToArray(),
                healingAccepted=runtime.HealingAudio.Accepted,healingPlayed=runtime.HealingAudio.Played},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy() {if(runtime!=null) {runtime.Session.Changed-=VisualChanged;runtime.PropAudio.LandingPlayed-=ObserveLanding;runtime.HealingAudio.HealingPlayed-=ObserveHealing;}}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase,total;public int[] observedStages;public bool directInventory,ordinaryHub,healedByUi,healingClosedForLifecycle,startingPositionFixture;public Vector2 initialPosition,fixturePosition;public int directSelections,commandSamples,healingAccepted,healingPlayed;public LandingWitness[] landings;public HealingWitness[] healing;}
    }
}
