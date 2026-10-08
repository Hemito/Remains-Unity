using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remains.Runtime
{
    public struct ContainerWindowReady : NetworkMessage { }
    public struct ContainerWindowFinish : NetworkMessage { }
    public struct ContainerWindowHoverReady : NetworkMessage {public string world,source,item;public int count;}
    public struct ContainerWindowClick : NetworkMessage {public string world,item;public long clickAt;}
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkContainerWindowProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,since,walkX;
        private bool traveled,finished,pointerCheck,ready;
        private string weapon="pistol";
        private int localAttacks;
        private bool clickReleased,approachVerified,clickSent;
        private long clickAt;
        private string originalItem,originalWorld,hoverStage;
        private Rect originalWindow,originalSlot;
        private Vector2 originalPoint;
        private float maxWindowDrift,maxSlotDrift,maxAimTurn,firstAimAngle;
        private int aimCommands,stackClicks;
        private readonly List<ApproachSample> approach=new();
        private readonly HashSet<int> hoverPeers=new();
        private readonly HashSet<int> peers=new();
        private readonly HashSet<int> captured=new();
        [Serializable] private sealed class OpeningWitness {public string source,definition,clip;public int voice,channels,frequency,samples;public float volume;public bool playing,muted;}
        private readonly List<OpeningWitness> openings=new();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            value.ContainerAudio.Opened+=ObserveOpening;
            value.Session.AttackReceived+=attack=>{if(attack.attacker==value.Session.LocalActorId) localAttacks++;};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ContainerWindowFinish>(_=>Finish(approachVerified && stackClicks==1,"Постепенный обычный aim к исходной клетке сохранил окно; один клик дал целую стопку. E-окно, отсутствие выстрела/удара/броска, вкладка брони, ходьба/бег/дальность и Escape проверены."));
                NetworkClient.RegisterHandler<ContainerWindowClick>(message=>
                {
                    if(!approachVerified || message.world!=originalWorld || message.item!=originalItem)
                    {Finish(false,"Барьер клика относится к другой исходной клетке/миру или подход ещё не проверен.");return;}
                    clickAt=message.clickAt;clickReleased=true;
                });
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<ContainerWindowReady>((connection,_)=>peers.Add(connection.connectionId));
                NetworkServer.RegisterHandler<ContainerWindowHoverReady>((connection,message)=>
                {
                    var box=value.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
                    if(message.world!=value.Session.World.worldId || message.source!=box.id ||
                        box.items.Count==0 || message.item!=box.items[0].id || message.count!=20 || box.items[0].count!=20 || box.items[0].definition!="caps")
                    {Finish(false,"Барьер подхода потерял исходный контейнер/стопку20: peer="+connection.connectionId);return;}
                    hoverPeers.Add(connection.connectionId);
                });
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(actor.weapon!=weapon) return new PlayerCommand {weaponItem=actor.inventory.Single(i=>i.definition==weapon).id};
            if(phase is 1 or 5 or 7 && actor.level=="factory:0" && !runtime.Session.Travel.Pending && !runtime.Hud.ContainerOpen)
            {
                var box=runtime.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
                var cursor=((InventorySource)box).Bounds.center;
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,cursor,runtime.Session.Travel.Points(actor.level));
                if(target.kind==InteractionKind.Container && target.id==ContainerState.PrototypeId) return new PlayerCommand {interact=true,hasAim=true,aim=cursor};
                return new PlayerCommand {movement=new Vector2(Math.Sign(box.x-actor.x),0)};
            }
            if(phase is 3 or 4 or 6) return new PlayerCommand {movement=Vector2.right,sprint=phase==4};
            var mouse=Mouse.current;
            if(pointerCheck && mouse?.name=="SessionProbeMouse")
            {
                var camera=Camera.main;
                if(camera==null) {Finish(false,"Нет actual world camera для обычного mouse aim.");return default;}
                var screen=mouse.position.ReadValue();aimCommands++;
                return new PlayerCommand {fire=mouse.leftButton.isPressed,hasAim=true,aim=camera.ScreenToWorldPoint(screen),hasPointer=true,pointer=screen};
            }
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут окна контейнера: phase={phase}, hover={hoverStage}, samples={approach.Count}, barrier={hoverPeers.Count}/{runtime.Options.ProbePeers}.");return;}
            var actor=runtime.Session.LocalActor;if(actor==null) return;
            var world=runtime.Session.World;
            if(NetworkServer.active && !clickSent && hoverPeers.Count==runtime.Options.ProbePeers)
            {
                clickSent=true;var item=world.containers.Single(c=>c.id==ContainerState.PrototypeId).items[0];
                NetworkServer.SendToAll(new ContainerWindowClick {world=world.worldId,item=item.id,clickAt=DateTime.UtcNow.AddSeconds(1).Ticks});
            }
            if(!traveled && world.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {traveled=true;runtime.Session.Travel.RequestFactory();phase=1;}
            if(phase==1 && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.InventoryOpen || runtime.Hud.BlocksGameplayInput || runtime.Hud.PauseMenu.IsOpen)
                {Finish(false,"E открыл личный инвентарь или заблокировал движение.");return;}
                phase=2;StartCoroutine(CheckClicks());
            }
            if(phase==3 && Time.unscaledTime-since>.15f)
            {
                if(!runtime.Hud.ContainerOpen || actor.x-walkX<.1f) {Finish(false,"Ходьба у контейнера закрыла окно или не двигала персонажа.");return;}
                phase=4;
            }
            if(phase==4 && !runtime.Hud.ContainerOpen) phase=5;
            if(phase==5 && runtime.Hud.ContainerOpen) phase=6;
            if(phase==6 && !runtime.Hud.ContainerOpen)
            {
                var box=world.containers.Single(c=>c.id==ContainerState.PrototypeId);
                if(Vector2.Distance(actor.Position,box.Position)<=2) {Finish(false,"Окно закрылось до выхода за дальность.");return;}
                phase=7;
            }
            if(phase==7 && runtime.Hud.ContainerOpen)
            {
                runtime.Hud.ToggleMenu();
                if(runtime.Hud.ContainerOpen || runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Escape не закрыл контейнер первым действием.");return;}
                runtime.Hud.ToggleInventory();runtime.Hud.ToggleMenu();
                if(runtime.Hud.InventoryOpen || runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Первый Escape не закрыл личный инвентарь.");return;}
                runtime.Hud.ToggleMenu();
                if(!runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Второй Escape не открыл меню.");return;}
                runtime.Hud.ToggleMenu();phase=8;
            }
            if(phase==8 && !ready) {ready=true;NetworkClient.Send(new ContainerWindowReady());}
            if(NetworkServer.active && peers.Count==runtime.Options.ProbePeers && phase==8)
            {phase=9;NetworkServer.SendToAll(new ContainerWindowFinish());}
        }
        private IEnumerator CheckClicks()
        {
            yield return null;yield return null;
            var actor=runtime.Session.LocalActor;
            int rounds=WeaponAmmunition.Magazine(actor,"pistol").rounds;
            int grenades=InventoryJournal.Count(actor,"grenade");
            pointerCheck=true;
            yield return ApproachAndClickOriginalSlot();
            if(finished) yield break;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerWindowRect,new Vector2(-42,107));
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            weapon="spear";yield return null;yield return null;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            if(runtime.Combat.IsMeleeActive) {Finish(false,"Клик по пустой клетке начал удар копьём.");yield break;}
            weapon="grenade";yield return null;yield return null;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            pointerCheck=false;
            while(runtime.Session.World.pendingGrants.Count!=0 || runtime.Session.World.actors.SelectMany(a=>a.inventory).Where(i=>i.definition=="caps").Sum(i=>i.count)<20) yield return null;
            actor=runtime.Session.LocalActor;
            var caps=runtime.Session.World.actors.SelectMany(a=>a.inventory).Where(i=>i.definition=="caps").ToArray();
            if(actor.inventory.Single(i=>i.definition=="pistol").weaponState.rounds!=rounds || InventoryJournal.Count(actor,"grenade")!=grenades || localAttacks!=0 ||
                caps.Length!=1 || caps[0].count!=20 || caps[0].id!=originalItem ||
                runtime.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId).items.Any(i=>i.id==originalItem))
            {Finish(false,"Клик окна потратил боеприпасы/гранату либо целая стопка не выдана ровно одному игроку.");yield break;}
            string beforeArmor=StateJson.ToJson(actor.inventory),held=actor.equippedWeaponItem;
            runtime.Hud.ToggleInventory();
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Armor));
            if(!runtime.Hud.InventoryOpen || !runtime.Hud.BlocksGameplayInput || runtime.Hud.PersonalCategory!=InventoryCategory.Armor)
            {Finish(false,"Указатель не открыл вкладку брони или разблокировал игровое управление.");yield break;}
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,20);
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.All));
            actor=runtime.Session.LocalActor;
            if(runtime.Hud.PersonalCategory!=InventoryCategory.All || StateJson.ToJson(actor.inventory)!=beforeArmor || actor.equippedWeaponItem!=held)
            {Finish(false,"Переключение вкладки брони изменило вещи/оружие или не вернулось в общий список.");yield break;}
            runtime.Hud.ToggleInventory();
            if(runtime.Hud.InventoryOpen || !runtime.Hud.ContainerOpen || runtime.Hud.PauseMenu.IsOpen)
            {Finish(false,"Закрытие личного окна не вернуло отдельное окно контейнера.");yield break;}
            weapon="pistol";walkX=actor.x;since=Time.unscaledTime;phase=3;
        }
        // A virtual Unity mouse follows fixed SCREEN points, never a moving cell.
        // Its position feeds the ordinary PlayerCommand aim even before pressing.
        private IEnumerator ApproachAndClickOriginalSlot()
        {
            hoverStage="waiting-placement";
            var window=runtime.Hud.ContainerWindowRect;var slot=runtime.Hud.ContainerSlotRect(0);
            while(!finished && !window.gameObject.activeInHierarchy) yield return null;
            if(finished) yield break;
            Canvas.ForceUpdateCanvases();
            var source=runtime.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
            if(runtime.Hud.ShownInventorySourceId!=source.id || source.items.Count==0 || source.items[0].definition!="caps" || source.items[0].count!=20)
            {Finish(false,"Нет исходной полной стопки20 в первой ячейке проверяемого E-окна.");yield break;}
            originalWorld=runtime.Session.World.worldId;originalItem=source.items[0].id;
            originalWindow=UiPointerProbe.ScreenBounds(window);originalSlot=UiPointerProbe.ScreenBounds(slot);originalPoint=originalSlot.center;
            firstAimAngle=runtime.Session.LocalActor.weaponAngle;
            var mouse=InputSystem.AddDevice<Mouse>("SessionProbeMouse");
            try
            {
                // Cross both sides of the world before the final gradual approach,
                // exercising actual turn/weapon artwork rather than a fixed aim.
                var waypoints=new[]{new Vector2(Screen.width*.1f,Screen.height*.25f),new Vector2(Screen.width*.9f,Screen.height*.75f),originalPoint};
                var from=(Vector2)Camera.main.WorldToScreenPoint(source.Position);
                for(int leg=0;leg<waypoints.Length;leg++)
                {
                    hoverStage="approach-"+leg;float start=Time.unscaledTime;
                    while(!finished)
                    {
                        float fraction=Mathf.Clamp01((Time.unscaledTime-start)/1.2f);var point=Vector2.Lerp(from,waypoints[leg],fraction);
                        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});
                        // Two ordinary ticks let input/aim and the preceding
                        // HUD LateUpdate settle without foreground render waits.
                        yield return null;yield return null;
                        if(finished || !VerifyOriginalCell(mouse.position.ReadValue(),true)) yield break;
                        if(fraction>=1) break;
                    }
                    from=waypoints[leg];
                }
                if(approach.Count<12 || aimCommands<12 || maxAimTurn<5)
                {Finish(false,$"Подход не проверил настоящий меняющийся aim: samples={approach.Count}, commands={aimCommands}, turn={maxAimTurn}deg.");yield break;}
                if(!RaycastOriginalSlot(slot)) yield break;
                approachVerified=true;hoverStage="all-peers-before-click";
                NetworkClient.Send(new ContainerWindowHoverReady {world=originalWorld,source=source.id,item=originalItem,count=20});
                while(!finished && (!clickReleased || DateTime.UtcNow.Ticks<clickAt))
                {
                    InputSystem.QueueStateEvent(mouse,new MouseState {position=originalPoint});
                    yield return null;yield return null;
                    if(finished || !VerifyOriginalCell(originalPoint,!clickReleased || DateTime.UtcNow.Ticks<clickAt)) yield break;
                }
                if(finished || !RaycastOriginalSlot(slot)) yield break;
                // Exactly one press/release at the ORIGINAL point. No relocation,
                // followTarget, direct button invocation or RequestClaim shortcut.
                hoverStage="single-whole-stack-click";stackClicks++;
                for(int frame=0;frame<2;frame++)
                {
                    InputSystem.QueueStateEvent(mouse,new MouseState {position=originalPoint}.WithButton(MouseButton.Left));
                    yield return null;yield return null;
                    if(finished || !VerifyOriginalCell(originalPoint,false)) yield break;
                }
                InputSystem.QueueStateEvent(mouse,new MouseState {position=originalPoint});
                yield return null;yield return null;
                if(finished || !VerifyOriginalCell(originalPoint,false)) yield break;
                hoverStage="claim-pending";
            }
            finally {InputSystem.RemoveDevice(mouse);}
        }
        private bool VerifyOriginalCell(Vector2 point,bool requireItem)
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null || actor.down || actor.level!="factory:0" || runtime.Session.World.worldId!=originalWorld ||
                !runtime.Hud.ContainerOpen || runtime.Hud.ShownInventorySourceId!=ContainerState.PrototypeId ||
                !runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy)
            {Finish(false,$"Окно/actor/world изменились во время подхода: actor={actor?.id}, level={actor?.level}, source={runtime.Hud.ShownInventorySourceId}.");return false;}
            var window=UiPointerProbe.ScreenBounds(runtime.Hud.ContainerWindowRect);var slot=UiPointerProbe.ScreenBounds(runtime.Hud.ContainerSlotRect(0));
            maxWindowDrift=Mathf.Max(maxWindowDrift,RectDrift(originalWindow,window));maxSlotDrift=Mathf.Max(maxSlotDrift,RectDrift(originalSlot,slot));
            maxAimTurn=Mathf.Max(maxAimTurn,Mathf.Abs(Mathf.DeltaAngle(firstAimAngle,actor.weaponAngle)));
            var camera=Camera.main;approach.Add(new ApproachSample {stage=hoverStage,time=Time.unscaledTime,pointer=point,aim=camera.ScreenToWorldPoint(point),
                window=window,slot=slot,weaponAngle=actor.weaponAngle,weaponFlip=actor.weaponFlipY,camera=camera.transform.position});
            if(maxWindowDrift>.05f || maxSlotDrift>.05f)
            {Finish(false,$"Окно убегает от исходной клетки при обычном aim: windowDrift={maxWindowDrift:F4}px, slotDrift={maxSlotDrift:F4}px, pointer={point}, stage={hoverStage}.");return false;}
            var source=runtime.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
            if(requireItem && (source.items.Count==0 || source.items[0].id!=originalItem || source.items[0].count!=20 || runtime.Session.World.pendingGrants.Count!=0))
            {Finish(false,"Исходный предмет изменился до согласованного единственного клика.");return false;}
            return true;
        }
        private bool RaycastOriginalSlot(RectTransform slot)
        {
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) {position=originalPoint},hits);
            if(hits.Count>0 && (hits[0].gameObject==slot.gameObject || hits[0].gameObject.transform.IsChildOf(slot))) return true;
            Finish(false,$"Исходная клетка недоступна для одного клика: point={originalPoint}, target={slot.name}, hit={(hits.Count==0?"нет":hits[0].gameObject.name)}.");return false;
        }
        private static float RectDrift(Rect original,Rect current)=>Mathf.Max(Mathf.Abs(original.xMin-current.xMin),Mathf.Abs(original.yMin-current.yMin),
            Mathf.Abs(original.xMax-current.xMax),Mathf.Abs(original.yMax-current.yMax));
        private void LateUpdate()
        {
            if(runtime.Options.Smoke.Contains("-frames-") && phase==2 && !captured.Contains(phase))
            {captured.Add(phase);NetworkSceneRecord.Save(runtime,phase);}
        }
        private void ObserveOpening(string source,string definition,AudioClip clip)
        {
            var voice=runtime.Audio.GetComponentsInChildren<AudioSource>().LastOrDefault(v=>v.clip==clip && v.volume>0);
            if(source!=ContainerState.PrototypeId || !runtime.ContainerAudio.TryGet(definition,out var expected,out _) ||
                clip!=expected || voice==null || !voice.isPlaying)
            {Finish(false,"Открывание не создало настоящий голос назначенного банка: "+source);return;}
            openings.Add(new OpeningWitness {source=source,definition=definition,clip=clip.name,voice=voice.GetInstanceID(),
                channels=clip.channels,frequency=clip.frequency,samples=clip.samples,volume=voice.volume,playing=voice.isPlaying,muted=voice.mute});
        }
        private void Finish(bool success,string detail)
        {
            if(success && (openings.Count!=3 || runtime.ContainerAudio.Openings!=3))
            {success=false;detail="Три обычных E-открывания не дали ровно три звука: "+openings.Count;}
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,approachVerified=approachVerified,
                aimCommands=aimCommands,stackClicks=stackClicks,originalItem=originalItem,originalWindow=originalWindow,originalSlot=originalSlot,
                maxWindowDrift=maxWindowDrift,maxSlotDrift=maxSlotDrift,maxAimTurn=maxAimTurn,approach=approach.ToArray(),openings=openings.ToArray()},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy() {if(runtime!=null) runtime.ContainerAudio.Opened-=ObserveOpening;}
        [Serializable] private sealed class ApproachSample
        {public string stage;public float time,weaponAngle;public Vector2 pointer,aim;public Rect window,slot;public bool weaponFlip;public Vector3 camera;}
        [Serializable] private sealed class Report
        {public bool success,approachVerified;public string detail,originalItem;public int phase,aimCommands,stackClicks;public Rect originalWindow,originalSlot;
            public float maxWindowDrift,maxSlotDrift,maxAimTurn;public ApproachSample[] approach;public OpeningWitness[] openings;}
    }
}
