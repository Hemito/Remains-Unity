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
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    public struct CosmeticProbeReady : NetworkMessage {public string actor,world;}
    public struct CosmeticProbeBegin : NetworkMessage {public string json;}
    public struct CosmeticProbeActionDone : NetworkMessage {public int phase;public string actor;}
    public struct CosmeticProbeObserve : NetworkMessage {public int phase;}
    public struct CosmeticProbeObserved : NetworkMessage {public string json;}
    public struct CosmeticProbeNext : NetworkMessage {public int phase;}
    public struct CosmeticProbeDone : NetworkMessage {public bool success;public string detail;}

    // Opt-in native witness. It operates real UI/input and observes authored graphics;
    // no actor position, pose, quantity, health or visual plan is assigned by the probe.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkCosmeticEquipmentProbe : MonoBehaviour
    {
        [Serializable] public sealed class Baseline
        {
            public string actor,character,owner,inventory,uses,weapon,weaponItem,heldItem,initialEquipment;
            public string pip,antirad;public int generation,lifecycle;public long equipmentRevision,weaponSelection;
            public float health,mana,x,y,hullWidth,hullHeight;
        }
        [Serializable] private sealed class Start
        {public string world;public bool reload;public Baseline[] actors;}
        [Serializable] public sealed class Visual
        {
            public string actor,definition,item,visual,clip;public string[] visualIds,meshes,materials;
            public long equipmentRevision,weaponSelection;public int frame,parts,wearableParts,hiddenBaseParts,facing,generation,lifecycle;
            public float x,y,vx,vy,health,mana,hullWidth,hullHeight;
            public bool matched,hidden,grounded;public string inventory,uses,owner,weapon,weaponItem,heldItem;
        }
        [Serializable] public sealed class Proof
        {
            public int phase,unityFrame,width,height;public string observer,world,label,png,scene;
            public bool uiAction,inventoryOpen,ordinaryInput,repeatIdempotent;public int repeatCommands,rightCommands,leftCommands;
            public int directionAimCommands,lastDirectionAimFrame;public float aimX,aimY;
            public Visual[] actors;
        }
        [Serializable] public sealed class Report
        {
            public int schema=1;
            public bool success,reload,loadedStateVerified,ordinaryInputs,noLiveFixtures,characterSaved,worldSaved;
            public string detail,observer,world,saveDirectory,selectedSnapshot,characterEquipment;
            public int participants,frameRate,phase,uiUses,shortcutAssignments,repeatCommands,rightCommands,leftCommands;
            public Proof[] proofs;public Baseline[] initialActors;public string limitation;
        }
        private PrototypeRuntime runtime;
        private Start baseline;
        private readonly Dictionary<string,Baseline> actors=new(StringComparer.Ordinal);
        private readonly Dictionary<string,WearableRenderAsset> assets=new(StringComparer.Ordinal);
        private PonyRenderAsset bare;
        private readonly List<Proof> proofs=new();
        private readonly HashSet<int> readyPeers=new(),actionPeers=new(),observedPeers=new();
        private readonly HashSet<int> observedPhases=new();
        private bool readySent,beginSent,begun,actionStarted,actionDone,observe,observed,finished,observeSent,advanceSent,finalCommit,doneSent;
        private long finalRevision;
        private int phase,repeatCommands,rightCommands,leftCommands,phaseRepeats,uiUses,shortcutAssignments;
        private int directionAimCommands,lastDirectionAimFrame=-1;
        private Vector2 directionAim;
        private float deadline,changedAt;
        private bool repeatIdempotent,loadedVerified;
        private string repeatEquipment;
        private bool Reload=>runtime.Options.Smoke.StartsWith("cosmetics-reload-frames-",StringComparison.Ordinal);
        private ActorState Actor=>runtime.Session.LocalActor;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;
            if(value.Hud==null || !(value.Options.Smoke.StartsWith("cosmetics-frames-",StringComparison.Ordinal) || Reload) ||
                value.Options.ProbePeers is <1 or >2 || string.IsNullOrEmpty(value.Options.SaveDirectory) ||
                string.IsNullOrEmpty(value.Options.Report)) throw new InvalidOperationException("Проверка одежды требует явный cosmetics-frames, HUD, отдельные сохранения и 1–2 участника.");
            if(Screen.width is not (1280 or 1920) || Screen.height!=(Screen.width==1280?720:1080))
                throw new InvalidOperationException("Проверка одежды требует кадр 1280×720 или 1920×1080.");
            if(value.Options.Role=="host" && (value.Session.Saves.SelectedSnapshot()!=null)!=Reload)
                throw new InvalidOperationException("Режим одежды не соответствует наличию выбранной копии: новый запуск требует пустой save-dir, reload — скопированную проверенную библиотеку.");
            deadline=Time.unscaledTime+180;value.CommandSource=Command;
            value.Session.ServerReady+=RegisterServer;value.Session.ClientReady+=RegisterClient;value.Session.Changed+=Changed;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<CosmeticProbeBegin>(message=>Guard(()=>Begin(StateJson.FromJson<Start>(message.json))));
            NetworkClient.RegisterHandler<CosmeticProbeObserve>(message=>Guard(()=>{Require(message.phase==phase,"Барьер наблюдения другой фазы.");observe=true;}));
            NetworkClient.RegisterHandler<CosmeticProbeNext>(message=>Guard(()=>Next(message.phase)));
            NetworkClient.RegisterHandler<CosmeticProbeDone>(message=>End(message.success,message.detail));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<CosmeticProbeReady>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor!=null && actor.id==message.actor && message.world==runtime.Session.ServerWorld.worldId,"Чужой initial ready.");
                readyPeers.Add(connection.connectionId);
            }));
            NetworkServer.RegisterHandler<CosmeticProbeActionDone>((connection,message)=>Guard(()=>
            {
                Require(message.phase==phase && runtime.Session.ConnectedActor(connection)?.id==message.actor,"Чужой action barrier.");
                actionPeers.Add(connection.connectionId);
            }));
            NetworkServer.RegisterHandler<CosmeticProbeObserved>((connection,message)=>Guard(()=>
            {
                var proof=StateJson.FromJson<Proof>(message.json);
                Require(observeSent && proof?.phase==phase && proof.world==baseline.world && proof.observer==runtime.Session.ConnectedActor(connection)?.id,
                    "Непривязанное наблюдение одежды.");
                ValidateProof(proof);observedPeers.Add(connection.connectionId);
            }));
            NetworkServer.RegisterHandler<CosmeticProbeDone>((connection,message)=>{if(!message.success) Fail(message.detail);});
        }
        private void Changed()=>changedAt=Time.frameCount;
        private bool Settled=>Actor!=null && Actor.level=="hub" && !runtime.IsLevelPreparing && runtime.LevelPresentationReady &&
            !runtime.Hud.RoomPresentationPending && !runtime.Session.Travel.Pending && !runtime.Session.Pause.IsPaused("hub") &&
            runtime.ActiveRoomLighting is {IsReady:true,IsUpdating:false};
        private void Begin(Start value)
        {
            Require(!begun && value?.world==runtime.Session.World.worldId && value.reload==Reload && value.actors?.Length==runtime.Options.ProbePeers,
                "Неверный initial baseline одежды.");
            baseline=value;foreach(var entry in value.actors) actors.Add(entry.actor,entry);
            Require(actors.ContainsKey(Actor.id),"Baseline не содержит локального игрока.");
            bare=GameAssets.Load<PonyRenderAsset>("Original/Pony/render")??throw new InvalidOperationException("Нет native базового asset пони.");
            foreach(string definition in new[]{"armor.pip","armor.antirad"})
            {
                var data=CosmeticCatalog.Current.Get(definition);
                Require(WearableRenderCatalog.TryGet(data.Visual,out var asset),"Нет native рисунка: "+data.Visual);assets.Add(definition,asset);
            }
            foreach(var entry in baseline.actors)
            {
                var live=runtime.Session.World.Find(entry.actor);Require(live!=null,"Нет actual initial actor.");
                ValidateInvariant(live,entry,0);
                Require(StateJson.ToJson(live.cosmeticEquipment)==entry.initialEquipment &&
                    (Reload?Selected(live)?.definition=="armor.antirad":Selected(live)==null),"Actual initial wardrobe не соответствует canonical fresh/reload.");
            }
            loadedVerified=Reload;begun=true;Next(1);
        }
        private static Baseline CaptureBaseline(ActorState actor)
        {
            var pip=actor.inventory.Single(item=>item.definition=="armor.pip");var anti=actor.inventory.Single(item=>item.definition=="armor.antirad");
            Require(pip.count>0 && anti.count>0,"В начальном инвентаре нет двух настоящих комплектов.");
            var hull=Anatomy.Movement(actor);
            return new Baseline {actor=actor.id,character=actor.character,owner=actor.owner,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                health=actor.health,mana=actor.mana,x=actor.x,y=actor.y,hullWidth=hull.width,hullHeight=hull.height,
                inventory=StateJson.ToJson(actor.inventory),uses=StateJson.ToJson(actor.inventoryUses),pip=pip.id,antirad=anti.id,
                initialEquipment=StateJson.ToJson(actor.cosmeticEquipment),
                equipmentRevision=actor.cosmeticEquipment?.revision??0,weapon=actor.weapon,weaponItem=actor.equippedWeaponItem,
                weaponSelection=actor.weaponSelectionRevision,heldItem=actor.heldItemId};
        }
        private void Next(int value)
        {
            Require(value is >=1 and <=7 && (!begun || phase==0 || value==phase+1 || value==1 && phase==0),"Нарушен порядок фаз одежды.");
            phase=value;actionStarted=actionDone=observe=observed=observeSent=advanceSent=false;phaseRepeats=0;
            directionAimCommands=0;lastDirectionAimFrame=-1;directionAim=Vector2.zero;
            actionPeers.Clear();observedPeers.Clear();changedAt=Time.frameCount;
        }
        private PlayerCommand Command()
        {
            if(!begun || finished || !Settled || Actor==null || runtime.Hud.BlocksGameplayInput) return default;
            if(phase is 3 or 4)
            {
                int direction=phase==3?1:-1;
                // Keep the real aim intent during braking and the peer witness barrier.
                // No actor/body/weapon state is assigned by the probe.
                directionAim=Actor.BodyPosition+Vector2.up*(Anatomy.Movement(Actor).height*.5f)+Vector2.right*(direction*3);
                directionAimCommands++;lastDirectionAimFrame=Time.frameCount;
                var command=new PlayerCommand {hasAim=true,aim=directionAim};
                float target=actors[Actor.id].x+(phase==3?.55f:-.3f);
                // A small ordinary analog walk approaches only from its declared side.
                // Reverse correction could turn the actor immediately before the witness.
                if(actionStarted && !actionDone && direction*(target-Actor.x)>.035f)
                {
                    if(phase==3) rightCommands++;else leftCommands++;
                    command.movement=new Vector2(direction*.2f,0);
                }
                return command;
            }
            if(!actionStarted || actionDone) return default;
            if(phase==2 && !runtime.Hud.InventoryOpen && phaseRepeats<3)
            {phaseRepeats++;repeatCommands++;return new PlayerCommand {weaponShortcut="9"};}
            return default;
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Fail($"Таймаут одежды: phase={phase}, ready={readyPeers.Count}, action={actionPeers.Count}, observed={observedPeers.Count}, preparing={runtime.IsLevelPreparing}, presentation={runtime.LevelPresentationReady}, ui={runtime.Hud.BlocksGameplayInput}.");return;}
            if(Actor==null || !NetworkClient.isConnected) return;
            if(!readySent && Settled && Standing(Actor) && ActorBodySupport.Sample(Actor).Grounded &&
                runtime.Session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {
                Require(WorldRooms.Definition(runtime.Session.World,"factory:0")==LegacyFactoryFixture.DefinitionId,"Нужен исторический мир со старым собранным заводом (LegacyFactoryFixture).");
                readySent=true;NetworkClient.Send(new CosmeticProbeReady {actor=Actor.id,world=runtime.Session.World.worldId});
            }
            if(NetworkServer.active && !beginSent && readyPeers.Count==runtime.Options.ProbePeers)
            {
                beginSent=true;
                var start=new Start {world=runtime.Session.ServerWorld.worldId,reload=Reload,
                    actors=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).OrderBy(a=>a.id,StringComparer.Ordinal).Select(CaptureBaseline).ToArray()};
                NetworkServer.SendToAll(new CosmeticProbeBegin {json=StateJson.ToJson(start)});
            }
            if(!begun || !Settled) return;
            if(!actionStarted)
            {ValidateInvariant(Actor,actors[Actor.id],phase);actionStarted=true;if(phase is 1 or 2 or 5 or 6 or 7) StartCoroutine(Run(ApplyUi()));}
            if(phase==2 && phaseRepeats==3 && !actionDone)
            {
                Require(StateJson.ToJson(Actor.cosmeticEquipment)==repeatEquipment,"Повтор назначенной клавиши изменил выбранный комплект/stamp.");repeatIdempotent=true;SendActionDone();
            }
            if(phase is 3 or 4 && !actionDone && lastDirectionAimFrame==Time.frameCount &&
                (phase==3?1:-1)*(actors[Actor.id].x+(phase==3?.55f:-.3f)-Actor.x)<=.035f && Standing(Actor))
            {
                Require(Mathf.Abs(Actor.x-(actors[Actor.id].x+(phase==3?.55f:-.3f)))<=.06f &&
                    Actor.facing==(phase==3?1:-1) && Mathf.Abs(Actor.y-actors[Actor.id].y)<.0001f,
                    $"Обычная ходьба не подтвердила цель/этаж/сторону: phase={phase}, actor={Actor.id}, x={Actor.x:R}, y={Actor.y:R}, targetX={actors[Actor.id].x+(phase==3?.55f:-.3f):R}, baselineY={actors[Actor.id].y:R}, vx={Actor.vx:R}, vy={Actor.vy:R}, facing={Actor.facing}, expectedFacing={(phase==3?1:-1)}, aim={directionAim}, aimCommands={directionAimCommands}, commandFrame={lastDirectionAimFrame}, frame={Time.frameCount}.");
                SendActionDone();
            }
            if(NetworkServer.active && actionPeers.Count==runtime.Options.ProbePeers && !observeSent &&
                baseline.actors.All(entry=>MatchesStage(runtime.Session.ServerWorld.Find(entry.actor),entry)))
            {observeSent=true;NetworkServer.SendToAll(new CosmeticProbeObserve {phase=phase});}
            if(NetworkServer.active && observedPeers.Count==runtime.Options.ProbePeers)
            {
                if(phase<7)
                {if(!advanceSent) {advanceSent=true;NetworkServer.SendToAll(new CosmeticProbeNext {phase=phase+1});}}
                else if(!finalCommit) {finalCommit=true;runtime.Session.CommitWorld();finalRevision=runtime.Session.ServerWorld.revision;}
                else if(phase==7 && !doneSent && runtime.Session.HasInventoryBackups(finalRevision))
                {doneSent=true;NetworkServer.SendToAll(new CosmeticProbeDone {success=true,detail="Настоящие UI-надевание/снятие, повтор клавиши, обе стороны, составные native части у всех участников и durable wardrobe проверены."});}
            }
        }
        private void SendActionDone()
        {ValidateInvariant(Actor,actors[Actor.id],phase);actionDone=true;NetworkClient.Send(new CosmeticProbeActionDone {phase=phase,actor=Actor.id});}
        private IEnumerator ApplyUi()
        {
            if(!runtime.Hud.InventoryOpen) runtime.Hud.ToggleInventory();yield return null;
            Require(runtime.Hud.InventoryOpen,"Личное окно не открылось.");
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Armor));
            string definition=phase<=2?"armor.pip":"armor.antirad";
            var choices=Actor.inventory.Where(item=>CosmeticEquipment.IsWearable(item)).ToArray();int row=Array.FindIndex(choices,item=>item.definition==definition);
            Require(row>=0 && row<runtime.Hud.PersonalPageSize && runtime.Hud.PersonalFirstVisibleRow==0,"Комплект отсутствует на первой реальной странице Armor.");
            string before=StateJson.ToJson(Actor.cosmeticEquipment);
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));
            Require(runtime.Hud.PersonalSelectedItemId==choices[row].id && StateJson.ToJson(Actor.cosmeticEquipment)==before,"Выбор строки сам надел комплект.");
            if(phase==2)
            {
                repeatEquipment=StateJson.ToJson(Actor.cosmeticEquipment);
                yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(8));shortcutAssignments++;
                Require(ItemShortcuts.Get(Actor,"9")?.id==choices[row].id && StateJson.ToJson(Actor.cosmeticEquipment)==repeatEquipment,"Назначение изменило одежду либо потеряло stable stack ID.");
                runtime.Hud.ToggleInventory();Require(!runtime.Hud.InventoryOpen,"Окно блокирует обычную быструю клавишу.");yield break;
            }
            var use=runtime.Hud.UseItemRect.GetComponent<Button>();string caption=phase==6?"Снять":"Надеть";
            Require(use.interactable && use.GetComponentInChildren<Text>().text==caption,"Неверная доступность/подпись UI: "+caption);
            yield return UiPointerProbe.Click(runtime.Hud.UseItemRect);uiUses++;
            Require(runtime.Hud.InventoryOpen,"Действие автоматически закрыло инвентарь.");
            Require(MatchesEquipment(Actor,actors[Actor.id]),"Настоящий UI не изменил ожидаемый слот.");SendActionDone();
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(finished || !begun || !observe || observed || Time.frameCount<=changedAt || !Settled) return;
            if(!baseline.actors.All(entry=>MatchesStage(runtime.Session.World.Find(entry.actor),entry))) return;
            var visual=new List<Visual>();foreach(var entry in baseline.actors)
            {
                if(!TryVisual(runtime.Session.World.Find(entry.actor),entry,out var witness)) return;visual.Add(witness);
            }
            var proof=new Proof {phase=phase,unityFrame=Time.frameCount,width=Screen.width,height=Screen.height,observer=Actor.id,world=baseline.world,
                label=Label(phase),uiAction=phase is 1 or 5 or 6 or 7,inventoryOpen=runtime.Hud.InventoryOpen,ordinaryInput=true,
                repeatIdempotent=repeatIdempotent,repeatCommands=repeatCommands,rightCommands=rightCommands,leftCommands=leftCommands,
                directionAimCommands=directionAimCommands,lastDirectionAimFrame=lastDirectionAimFrame,aimX=directionAim.x,aimY=directionAim.y,
                actors=visual.ToArray(),png=runtime.Options.Report+$".phase-{phase}.live.png",scene=runtime.Options.Report+$".phase-{phase}.scene.json"};
            ValidateProof(proof);NetworkSceneRecord.Save(runtime,phase);proofs.Add(proof);observedPhases.Add(phase);observed=true;
            if(phase==2) Require(!runtime.Hud.InventoryOpen,"Окно не закрыто перед обычной ходьбой.");
            NetworkClient.Send(new CosmeticProbeObserved {json=StateJson.ToJson(proof)});
        });
        private bool TryVisual(ActorState actor,Baseline entry,out Visual witness)
        {
            witness=null;var view=runtime.FindActorVisual(actor.id) as PonyVisual;
            if(view==null || !view.gameObject.activeInHierarchy || view.DrawnFrame<0 || view.CurrentDrawPlan==null || !view.TryGetDrawnBounds(out _)) return false;
            var plan=view.CurrentDrawPlan;string expectedDefinition=ExpectedDefinition(phase);bool clothed=expectedDefinition!=null;
            if(plan.HasEquipment!=clothed || plan.VisualIds.Count!=(clothed?1:0)) return false;
            if(clothed && plan.VisualIds[0]!=CosmeticCatalog.Current.Get(expectedDefinition).Visual) return false;
            var parts=clothed?plan.Frames(view.DrawnClip)[view.DrawnFrame]:PonyRenderSelection.Frames(PonyAppearance.Snapshot(actor),view.DrawnClip)[view.DrawnFrame];
            var rendered=view.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy).OrderBy(r=>r.sortingOrder).ToArray();
            if(rendered.Length!=parts.Length || view.DrawnPartCount!=parts.Length) return false;
            for(int i=0;i<parts.Length;i++) if(rendered[i].GetComponent<MeshFilter>()?.sharedMesh!=parts[i].mesh || rendered[i].sharedMaterial!=parts[i].material ||
                rendered[i].sortingOrder!=WorldVisualOrderPolicy.ActorPartOrder+i) return false;
            int clothing=0,hidden=0;
            if(clothed)
            {
                var frame=Array.Find(assets[expectedDefinition].Clips,clip=>clip.id==view.DrawnClip).frames[view.DrawnFrame];
                var meshes=new HashSet<Mesh>(frame.parts.Select(part=>part.mesh));clothing=parts.Count(part=>meshes.Contains(part.mesh));
                foreach(int index in frame.hiddenBasePartIndices)
                {
                    var original=Array.Find(bare.Clips,clip=>clip.id==view.DrawnClip).frames[view.DrawnFrame].parts[index];
                    // Shared compiled meshes can legitimately be used by a different layer.
                    // The absent base part plus exact plan↔renderer mapping proves the hide.
                    Require(!parts.Any(part=>ReferenceEquals(part,original)),"Скрытая базовая часть осталась в составном плане: "+original.sourcePath);hidden++;
                }
                Require(clothing>0,"План надет, но ни одной actual wearable части нет.");
            }
            ValidateInvariant(actor,entry,phase);
            var sample=ActorBodySupport.Sample(actor);var hull=Anatomy.Movement(actor);var selected=Selected(actor);
            witness=new Visual {actor=actor.id,definition=selected?.definition,item=selected?.item,visual=clothed?assets[expectedDefinition].VisualId:null,
                visualIds=plan.VisualIds.ToArray(),equipmentRevision=actor.cosmeticEquipment?.revision??0,clip=view.DrawnClip,frame=view.DrawnFrame,
                parts=parts.Length,wearableParts=clothing,hiddenBaseParts=hidden,hidden=true,matched=true,grounded=sample.Grounded,
                meshes=rendered.Select(r=>r.GetComponent<MeshFilter>().sharedMesh.name).ToArray(),materials=rendered.Select(r=>r.sharedMaterial.name).ToArray(),
                facing=actor.facing,x=actor.x,y=actor.y,vx=actor.vx,vy=actor.vy,health=actor.health,mana=actor.mana,hullWidth=hull.width,hullHeight=hull.height,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,owner=actor.owner,inventory=StateJson.ToJson(actor.inventory),uses=StateJson.ToJson(actor.inventoryUses),
                weapon=actor.weapon,weaponItem=actor.equippedWeaponItem,heldItem=actor.heldItemId,weaponSelection=actor.weaponSelectionRevision};
            return true;
        }
        private bool MatchesStage(ActorState actor,Baseline entry)=>actor!=null && MatchesEquipment(actor,entry) && Standing(actor) &&
            actor.weaponSelectionRevision==entry.weaponSelection+(phase>=2?1:0) &&
            (phase is not (3 or 4) || actor.facing==(phase==3?1:-1) && Mathf.Abs(actor.y-entry.y)<.0001f &&
                Mathf.Abs(actor.x-(entry.x+(phase==3?.55f:-.3f)))<=.06f);
        private bool MatchesEquipment(ActorState actor,Baseline entry)
        {
            var selected=Selected(actor);string definition=ExpectedDefinition(phase);
            return actor.cosmeticEquipment is {version:1} && actor.cosmeticEquipment.revision==entry.equipmentRevision+(phase<=4?1:phase-3) &&
                (definition==null?selected==null:selected?.definition==definition && selected.item==(definition=="armor.pip"?entry.pip:entry.antirad));
        }
        private static CosmeticSelection Selected(ActorState actor)=>actor.cosmeticEquipment?.version==1?
            actor.cosmeticEquipment.entries.SingleOrDefault(entry=>entry.item!=null):null;
        private static bool Standing(ActorState actor)=>!actor.down && !actor.climbing && !actor.crouched && actor.vx==0 && actor.vy==0;
        private static string ExpectedDefinition(int value)=>value<=4?"armor.pip":value==6?null:"armor.antirad";
        private static string Label(int value)=>value switch {1=>"pip-ui",2=>"shortcut-repeat",3=>"walk-right",4=>"walk-left",5=>"antirad-ui",6=>"removed-ui",7=>"antirad-final",_=>throw new InvalidOperationException("Неизвестная фаза одежды.")};
        private static void ValidateInvariant(ActorState actor,Baseline entry,int currentPhase)
        {
            var hull=Anatomy.Movement(actor);
            Require(actor.id==entry.actor && actor.character==entry.character && actor.owner==entry.owner && actor.generation==entry.generation &&
                actor.lifecycleSequence==entry.lifecycle && actor.health==entry.health && actor.mana==entry.mana && !actor.down && actor.level=="hub" &&
                StateJson.ToJson(actor.inventory)==entry.inventory && StateJson.ToJson(actor.inventoryUses)==entry.uses && actor.weapon==entry.weapon &&
                actor.equippedWeaponItem==entry.weaponItem && actor.heldItemId==entry.heldItem &&
                (currentPhase==2?actor.weaponSelectionRevision>=entry.weaponSelection && actor.weaponSelectionRevision<=entry.weaponSelection+1:
                    actor.weaponSelectionRevision==entry.weaponSelection+(currentPhase>=3?1:0)) &&
                hull.width==entry.hullWidth && hull.height==entry.hullHeight,"Одежда изменила inventory/HP/оружие/physics/lifetime: "+actor.id);
        }
        private void ValidateProof(Proof proof)
        {
            Require(proof.actors?.Length==baseline.actors.Length && proof.width==Screen.width && proof.height==Screen.height && proof.ordinaryInput &&
                proof.uiAction==(proof.phase is 1 or 5 or 6 or 7) && proof.label==Label(proof.phase),"Неполное actual наблюдение одежды.");
            foreach(var entry in baseline.actors)
            {
                var visual=proof.actors.Single(row=>row.actor==entry.actor);string definition=ExpectedDefinition(proof.phase);
                Require(visual.matched && visual.hidden && visual.grounded && visual.parts>0 && visual.meshes.Length==visual.parts && visual.materials.Length==visual.parts &&
                    visual.inventory==entry.inventory && visual.uses==entry.uses && visual.health==entry.health && visual.mana==entry.mana &&
                    visual.owner==entry.owner && visual.generation==entry.generation && visual.lifecycle==entry.lifecycle && visual.hullWidth==entry.hullWidth &&
                    visual.weapon==entry.weapon && visual.weaponItem==entry.weaponItem && visual.heldItem==entry.heldItem &&
                    visual.weaponSelection==entry.weaponSelection+(proof.phase>=2?1:0) &&
                    visual.hullHeight==entry.hullHeight && visual.equipmentRevision==entry.equipmentRevision+(proof.phase<=4?1:proof.phase-3) &&
                    visual.definition==definition && (definition==null?visual.item==null && visual.visualIds.Length==0 && visual.wearableParts==0:
                        visual.visualIds.Length==1 && visual.visual==CosmeticCatalog.Current.Get(definition).Visual && visual.wearableParts>0),"Renderer/inventory/stamp свидетельство не соответствует настоящей фазе: "+entry.actor);
                if(proof.phase is 3 or 4) Require(visual.facing==(proof.phase==3?1:-1) && Mathf.Abs(visual.y-entry.y)<.0001f &&
                    Mathf.Abs(visual.x-(entry.x+(proof.phase==3?.55f:-.3f)))<=.06f,
                    $"Не наблюдается обычная цель/сторона/поддерживаемый этаж: phase={proof.phase}, observer={proof.observer}, actor={visual.actor}, x={visual.x:R}, y={visual.y:R}, targetX={entry.x+(proof.phase==3?.55f:-.3f):R}, baselineY={entry.y:R}, facing={visual.facing}, expectedFacing={(proof.phase==3?1:-1)}.");
            }
            Require(proof.phase<2 || proof.repeatIdempotent && proof.repeatCommands==3,"Повтор shortcut не доказан.");
            Require(proof.phase<3 || proof.rightCommands>0,"Нет обычной ходьбы вправо.");Require(proof.phase<4 || proof.leftCommands>0,"Нет обычной ходьбы влево.");
            if(proof.phase is 3 or 4) Require(proof.directionAimCommands>0 && proof.lastDirectionAimFrame==proof.unityFrame &&
                float.IsFinite(proof.aimX) && float.IsFinite(proof.aimY),"Прицел ходьбы не удержан до фактического renderer свидетельства.");
        }
        private IEnumerator Run(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            try
            {
            while(stack.Count>0 && !finished)
            {
                object current=null;bool moved=false;Exception failure=null;
                try {moved=stack.Peek().MoveNext();if(moved) current=stack.Peek().Current;}
                catch(Exception error) {failure=error;}
                if(failure!=null) {Fail(failure.Message);yield break;}
                if(!moved) {if(stack.Pop() is IDisposable done) done.Dispose();continue;}
                if(current is IEnumerator nested) {stack.Push(nested);continue;}yield return current;
            }
            }
            finally {while(stack.Count>0) if(stack.Pop() is IDisposable disposable) disposable.Dispose();}
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Проверка одежды: "+reason);}
        private void Guard(Action action) {if(finished)return;try {action();}catch(Exception error) {Fail(error.Message);}}
        private void Fail(string reason)
        {
            if(finished)return;
            if(NetworkServer.active) NetworkServer.SendToAll(new CosmeticProbeDone {success=false,detail=reason});
            else if(NetworkClient.isConnected) NetworkClient.Send(new CosmeticProbeDone {success=false,detail=reason});
            End(false,reason);
        }
        private void End(bool success,string detail)
        {
            if(finished)return;finished=true;bool saved=false,worldSaved=false;string selected=null,equipment=null;
            try
            {
                if(success)
                {
                    Require(observedPhases.Count==7 && uiUses==4 && shortcutAssignments==1 && repeatCommands==3 && rightCommands>0 && leftCommands>0,
                        "Finish до полного локального UI/input/renderer evidence.");
                    runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();var character=runtime.Session.Saves.LoadCharacter();
                    Require(character?.id==Actor.id && StateJson.ToJson(character.cosmeticEquipment)==StateJson.ToJson(Actor.cosmeticEquipment) &&
                        StateJson.ToJson(character.inventory)==actors[Actor.id].inventory && Selected(character)?.definition=="armor.antirad","Диск потерял выбранную одежду/количество.");
                    equipment=StateJson.ToJson(character.cosmeticEquipment);saved=true;
                    var snapshot=runtime.Session.Saves.SelectedSnapshot();var copy=snapshot?.CreateLiveWorld();
                    Require(copy?.worldId==baseline.world && baseline.actors.All(entry=>MatchesEquipment(copy.Find(entry.actor),entry)),"Durable world не содержит последние wardrobe stamps каждого игрока.");
                    selected=snapshot.Id;worldSaved=true;
                }
            }
            catch(Exception error) {success=false;detail+=" "+error.Message;}
            var report=new Report {success=success,detail=detail,phase=phase,reload=Reload,loadedStateVerified=loadedVerified,ordinaryInputs=true,noLiveFixtures=true,
                observer=Actor?.id,world=baseline?.world,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,proofs=proofs.ToArray(),initialActors=baseline?.actors,
                uiUses=uiUses,shortcutAssignments=shortcutAssignments,repeatCommands=repeatCommands,rightCommands=rightCommands,leftCommands=leftCommands,
                characterSaved=saved,worldSaved=worldSaved,selectedSnapshot=selected,characterEquipment=equipment,saveDirectory=runtime.Options.SaveDirectory,
                limitation="Проверяется независимый Unity visual-only контракт и явный повторный запуск скопированных сейвов отдельным reload режимом. Оригинальный runtime, защита, прочность и отключение посреди кадра не подтверждаются."};
            try {File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));}
            catch(Exception error) {success=false;Debug.LogError("Не записан отчёт одежды: "+error.Message);}
            if(success) Debug.Log("NetworkCosmeticEquipmentProbe PASS: "+detail);else Debug.LogError("NetworkCosmeticEquipmentProbe FAIL: "+detail);
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void OnDestroy()
        {
            if(runtime?.Session==null)return;runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;runtime.Session.Changed-=Changed;
        }
    }
}
