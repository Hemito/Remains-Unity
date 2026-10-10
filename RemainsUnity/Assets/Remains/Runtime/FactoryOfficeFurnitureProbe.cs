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
using UnityEngine.InputSystem;

namespace Remains.Runtime
{
    public struct FurnitureProbeFixture : NetworkMessage {public string json;}
    public struct FurnitureProbeStep : NetworkMessage {public int phase;public double at;}
    public struct FurnitureProbeObserved : NetworkMessage {public string actor,source,detail;public int generation,phase;public bool valid;}
    public struct FurnitureProbeFinish : NetworkMessage {public bool success;public string detail,snapshot;}

    // Opt-in initial player fixture; authored furniture/NPCs and every subsequent
    // movement, attack, E interaction and UI claim remain production operations.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryOfficeFurnitureProbe : MonoBehaviour
    {
        private const string Level="factory:0",Prefix=Level+":original:plant:53:";
        private sealed class Expected
        {
            public readonly string code,type;public readonly float x,y,width,height,mass;public readonly bool container,body;
            public Expected(string code,string type,float x,float y,float width,float height,float mass,bool container=true,bool body=true)
            {this.code=code;this.type=type;this.x=x;this.y=y;this.width=width;this.height=height;this.mass=mass;this.container=container;this.body=body;}
            public string Id=>Prefix+code;
        }
        private static readonly Expected[] declarations={
            new("no27T8YFjTx8ZVz4","table",96.8f,7.2f,1.24f,.8f,9.92f),
            new("Qyc8bJzHANJlny9s","table",111.2f,7.2f,1.24f,.8f,9.92f),
            new("B37xGIBuhSjLOfSc","table",100.8f,11.2f,1.24f,.8f,9.92f),
            new("HaoIW4PYZAYl0YKb","trash",92.4f,11.2f,.5f,.8f,4),
            new("HLHr8wbz490ZTI9b","bookcase",113.6f,11.2f,1.6f,2.4f,0,true,false),
            new("D08uoqM6f1EaCJfJ","table1",106.4f,7.2f,1.34f,.8f,10.72f,false),
            new("TGISVy91GeyHglBk","table1",107.2f,11.2f,1.34f,.8f,10.72f,false),
            new("mA7CG0NX9srOOsJv","table1",96.8f,11.2f,1.34f,.8f,10.72f,false)};
        [Serializable] private sealed class PlayerBaseline {public string id,owner;public int generation;public int[] counts;public Vector2 fixturePosition;}
        [Serializable] private sealed class Fixture
        {public string source,firstItem;public ItemStack[] items;public PlayerBaseline[] players;public int firstCount;}
        [Serializable] private sealed class OpeningVoice
        {
            public string source,definition,resource,clip;public int phase,voice,channels,frequency,samples,timeSamples;
            public float gain,volume,expectedVolume,mixVolume,attenuation;public bool playing,muted;
        }
        [Serializable] private sealed class Report
        {
            public bool success,explicitInitialFixture,closedVerified,appearanceVerified,closedWindowVerified,opened,empty,reopened,policiesVerified,stableRecipientsVerified,durableVerified,savedReloadVerified,reinstallVerified,raceSkipped,pointerCaptureVerified;
            public string detail,type,source,snapshot,progress;public int phase,claimsAccepted,claimsDenied,uiRaceAttempts,combatShots,layoutCaptures;public PlayerBaseline[] players;
            public ItemStack[] initialItems;
            public int actualOpenings,totalOpenings,expectedOpenings;public OpeningVoice[] openingVoices;
        }
        private PrototypeRuntime runtime;private Expected expected;private Fixture fixture;private PlayerBaseline baseline;
        private readonly HashSet<string> accepted=new(StringComparer.Ordinal),denied=new(StringComparer.Ordinal);
        private readonly HashSet<string> barrier=new(StringComparer.Ordinal);
        private readonly List<OpeningVoice> openingVoices=new();
        private string OpeningResource=>expected.type=="trash"?"Original/Audio/container-small-open":
            expected.type=="table"?"Original/Room/ContinuationObjects/Audio/wood_open":null;
        private int phase=-1,serverPhase=-1,combatShots,shotsAtOpen,roundsAtOpen,uiRaceAttempts,layoutCaptures;
        private bool traveled,installed,finished,closedVerified,closedWindowVerified,opened,reopened,empty,reported,clickDone,clicking,raceSkipped,edge,pointerCaptureVerified;
        private float deadline,nextHeal,fixtureAt,quietSince=-1;private double raceAt;
        private string capture,lastCapture,snapshot,aimedThreat;private RoomLayout initialRoom;private string[] actorIds;private int[][] initialCounts;
        private Rect? pinnedWindow;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private ContainerState Target=>runtime.Session.World?.containers.SingleOrDefault(c=>c.id==expected.Id);
        private string Progress=>$"phase={phase}/{serverPhase}, source={expected?.Id}, actor={Actor?.id}, position={Actor?.Position}, HP={Actor?.health}, down={Actor?.down}, life={Actor?.lifecycleSequence}, generation={Actor?.generation}/{baseline?.generation}, target={Target?.Position}, items={Target?.items.Count}, pending={runtime.Session.World?.pendingGrants.Count}, UI={runtime.Hud.ContainerOpen}/{runtime.Hud.ShownInventorySourceId}, claims={accepted.Count}/{denied.Count}, clicks={uiRaceAttempts}/{clickDone}/{raceSkipped}, barrier={barrier.Count}, shots={combatShots}";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+180;
            string smoke=value.Options.Smoke??"";var parts=smoke.Split('-');
            if(!smoke.StartsWith("office-furniture-",StringComparison.Ordinal) || parts.Count(p=>p is "table" or "trash" or "bookcase")!=1)
                throw new InvalidOperationException("Неоднозначный/неподдерживаемый режим мебели: "+smoke);
            expected=parts.Contains("table")?declarations[0]:
                parts.Contains("trash")?declarations[3]:
                parts.Contains("bookcase")?declarations[4]:
                throw new InvalidOperationException("Не задан режим мебели: "+smoke);
            if(value.Options.ProbePeers is <1 or >2 || (string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report)))
                throw new InvalidOperationException("Мебель требует отдельный каталог сохранений и одного/двух участников.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка мебели не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.ContainerAudio.Opened+=ObserveOpening;
            value.Session.AttackReceived+=attack=>{if(attack.attacker==value.Session.LocalActorId) combatShots++;};
            value.Session.ClaimPrepared+=(transaction,valid)=>{if(fixture!=null) (valid?accepted:denied).Add(transaction);};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<FurnitureProbeFixture>(message=>
                {
                    fixture=JsonUtility.FromJson<Fixture>(message.json);
                    baseline=fixture.players.Single(p=>p.id==value.Session.LocalActorId);
                    initialRoom=runtime.Geometry.Get(Level);CacheBaseline();phase=-1;fixtureAt=Time.unscaledTime;
                });
                NetworkClient.RegisterHandler<FurnitureProbeStep>(message=>
                {
                    phase=message.phase;raceAt=message.at;reported=false;clickDone=false;edge=false;
                    if(phase==3 && Host) StartCoroutine(Drain());
                });
                NetworkClient.RegisterHandler<FurnitureProbeFinish>(message=>Finish(message.success,message.detail,message.snapshot));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FurnitureProbeObserved>(Observe);
        }
        private PlayerCommand Command()
        {
            try {return ChooseCommand();} catch(Exception error) {Finish(false,error.Message+": "+Progress);return default;}
        }
        private PlayerCommand ChooseCommand()
        {
            var actor=Actor;if(actor==null || finished || fixture==null || actor.generation!=baseline.generation || actor.level!=Level) return default;
            var command=new PlayerCommand {hasAim=true,aim=((InventorySource)Target).Bounds.center};
            if(runtime.Hud.ContainerOpen)
            {
                var mouse=Mouse.current;
                if(mouse?.name=="SessionProbeMouse") {command.fire=mouse.leftButton.isPressed;command.hasPointer=true;command.pointer=mouse.position.ReadValue();}
                if(phase==4 && !edge) {edge=true;command.interact=true;}
                return command;
            }
            if(actor.health<75 && InventoryJournal.Count(actor,"heal")>0 && Time.unscaledTime>=nextHeal)
            {nextHeal=Time.unscaledTime+1.5f;command.heal=true;}
            // Fight only a real attack/target on this player; no broad proximity purge.
            var threat=runtime.Session.World.actors.Where(a=>!a.player && !a.down && a.health>0 && a.level==Level &&
                (a.target==actor.id || a.attackBusy && a.attackTarget==actor.id)).OrderBy(a=>(a.Position-actor.Position).sqrMagnitude).FirstOrDefault();
            if(threat!=null)
            {
                var pistol=actor.inventory.FirstOrDefault(i=>i.definition=="pistol" && i.weaponState!=null);
                if(pistol==null) throw new InvalidOperationException("У проверочного игрока нет настоящего пистолета: "+actor.id);
                command.weaponItem=actor.equippedWeaponItem==pistol.id?null:pistol.id;
                command.aim=threat.Position+Vector2.up*(Anatomy.Movement(threat).height*.5f);
                command.reload=pistol.weaponState.rounds==0 && InventoryJournal.Count(actor,"p10")>0;
                var muzzle=WeaponPose.Muzzle(actor);
                bool ally=runtime.Session.World.actors.Any(a=>a.player && a.id!=actor.id && a.connected && a.level==Level &&
                    SegmentIntersectsActor(muzzle,command.aim,a));
                command.fire=aimedThreat==threat.id && actor.equippedWeaponItem==pistol.id && pistol.weaponState.rounds>0 && !ally;
                aimedThreat=threat.id;
                quietSince=-1;return command;
            }
            aimedThreat=null;if(quietSince<0) quietSince=Time.unscaledTime;
            if((phase==0 || phase==5 && lastCapture=="ui-closed") && Time.unscaledTime-quietSince>.35f && !edge)
            {
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,command.aim);
                if(target.kind!=InteractionKind.Container || target.id!=expected.Id)
                    throw new InvalidOperationException($"Мебель: E выбрал {target.kind}/{target.id}, ожидался {expected.Id}, cursor={command.aim}.");
                edge=true;command.interact=true;
            }
            return command;
        }
        private static bool SegmentIntersectsActor(Vector2 start,Vector2 end,ActorState ally)
        {
            var hull=Anatomy.Movement(ally);var middle=ally.Position+Vector2.up*(hull.height*.5f);
            var direction=end-start;float t=direction.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(middle-start,direction)/direction.sqrMagnitude);
            return Vector2.Distance(start+direction*t,middle)<Mathf.Max(hull.width*.5f,hull.height*.5f)+.2f;
        }
        private void Update()
        {
            if(finished) return;
            try {Tick();} catch(Exception error) {Finish(false,error.Message+": "+Progress);}
        }
        private void Tick()
        {
            if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут мебели: "+Progress);
            var session=runtime.Session;var actor=Actor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!traveled) {traveled=true;session.Travel.RequestFactory();}
            if(Host && !installed && !session.Travel.Pending && session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level) &&
                session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level)) Setup();
            if(fixture==null || actor.generation!=baseline.generation) return;
            if(actor.id!=baseline.id || actor.owner!=baseline.owner || actor.owner!=session.LocalOwner || !actor.connected || actor.level!=Level ||
                session.Travel.Pending || !ReferenceEquals(initialRoom,runtime.Geometry.Get(Level))) throw new InvalidOperationException("Мебель потеряла игрока, владение, локацию или геометрию");
            if(actor.down || actor.health<=0) throw new InvalidOperationException("Игрок потерял сознание у мебели");
            ValidatePolicies(session.World,false);
            if(opened) ContainerArtworkProbeEvidence.RequireState(Target,true);
            if(opened && phase!=5 && runtime.Hud.ContainerOpen && Magazine(actor)!=roundsAtOpen)
                throw new InvalidOperationException("Клик окна мебели выстрелил или потратил патрон");
            // All observers record the closed frame before either player's E.
            if(phase==-1 && Time.unscaledTime-fixtureAt>=3)
            {ContainerArtworkProbeEvidence.RequireState(Target,false);capture="closed";phase=-10;}
            if(phase==-10 && lastCapture=="closed" && !reported) SendObserved(0,true);
            if(phase==0 && runtime.Hud.ContainerOpen && Target.appearance.opened)
            {
                ContainerArtworkProbeEvidence.RequireState(Target,true);ValidateWindow();opened=true;shotsAtOpen=combatShots;roundsAtOpen=Magazine(actor);capture="open";phase=1;
            }
            if(phase==1 && lastCapture=="open" && !reported) SendObserved(1,true);
            if(phase==2 && NetworkTime.time>=raceAt && !clickDone && !clicking) {clicking=true;StartCoroutine(Race());}
            if(phase==2 && clickDone && RaceValid(session.World) && lastCapture=="claimed" && !reported) SendObserved(2,true);
            if(phase==3 && Target.items.Count==0 && session.World.pendingGrants.Count==0 && AmountsValid(session.World,true) &&
                accepted.All(session.World.completedTransactions.Contains) && !clicking)
            {empty=true;capture="empty";phase=30;}
            if(phase==30 && lastCapture=="empty" && !reported) SendObserved(3,true);
            if(phase==4 && !runtime.Hud.ContainerOpen) {ContainerArtworkProbeEvidence.RequireState(Target,true);capture="ui-closed";phase=5;edge=false;quietSince=-1;}
            if(phase==5 && runtime.Hud.ContainerOpen)
            {ValidateWindow();if(Target.items.Count!=0) throw new InvalidOperationException("E повторно наполнил мебель");shotsAtOpen=combatShots;roundsAtOpen=Magazine(actor);reopened=true;capture="reopened";phase=6;}
            if(phase==6 && lastCapture=="reopened" && !clicking && !clickDone) {clicking=true;StartCoroutine(EmptyClick());}
            if(phase==6 && clickDone && lastCapture=="reopened" && !reported) SendObserved(4,AmountsValid(session.World,true));
        }
        private void ValidateWindow()
        {
            if(runtime.Hud.ShownInventorySourceId!=expected.Id || runtime.Hud.InventoryOpen || runtime.Hud.BlocksGameplayInput)
                throw new InvalidOperationException("E мебели открыл другой источник/заблокировал управление");
            pinnedWindow=null;
        }
        private void ValidateLayout()
        {
            var window=runtime.Hud.ContainerWindowRect;
            if(!window.gameObject.activeInHierarchy) throw new InvalidOperationException("В проверочном разрешении окно мебели не показано: "+expected.Id);
            var bounds=UiPointerProbe.ScreenBounds(window);
            if(bounds.xMin<-.1f || bounds.yMin<-.1f || bounds.xMax>Screen.width+.1f || bounds.yMax>Screen.height+.1f)
                throw new InvalidOperationException($"Окно мебели вышло за экран: source={expected.Id}, bounds={bounds}, screen={Screen.width}×{Screen.height}.");
            if(pinnedWindow.HasValue && Vector2.Distance(bounds.position,pinnedWindow.Value.position)>.1f)
                throw new InvalidOperationException($"Окно мебели сдвинулось после открытия: source={expected.Id}, initial={pinnedWindow.Value}, current={bounds}.");
            pinnedWindow=bounds;
            var layout=window.GetComponent<ContainerWindowView>();
            if(!layout.PlacementPinned) throw new InvalidOperationException("Окно мебели не закреплено: "+expected.Id);
            // Reservations describe the moment of placement. Current artwork and
            // transient notices may move, but must not move an accessible slot.
            for(int i=0;i<layout.ReservationCount;i++)
                if(OverlapsPixels(bounds,CanvasScreenBounds(window,layout.ReservedCanvasBounds(i))))
                    throw new InvalidOperationException($"Исходное окно мебели перекрыло защищённую область: source={expected.Id}, index={i}, bounds={bounds}.");
            if(OverlapsPixels(bounds,CanvasScreenBounds(window,layout.SourceCanvasBounds)))
                throw new InvalidOperationException("Исходное окно мебели перекрыло источник: "+expected.Id);
            layoutCaptures++;
        }
        private static Rect CanvasScreenBounds(RectTransform window,Rect area)
        {
            var lower=RectTransformUtility.WorldToScreenPoint(null,window.parent.TransformPoint(new Vector3(area.xMin,area.yMin,0)));
            var upper=RectTransformUtility.WorldToScreenPoint(null,window.parent.TransformPoint(new Vector3(area.xMax,area.yMax,0)));
            return Rect.MinMaxRect(lower.x,lower.y,upper.x,upper.y);
        }
        private static bool OverlapsPixels(Rect first,Rect second)=>
            Mathf.Min(first.xMax,second.xMax)-Mathf.Max(first.xMin,second.xMin)>.1f &&
            Mathf.Min(first.yMax,second.yMax)-Mathf.Max(first.yMin,second.yMin)>.1f;
        private void Setup()
        {
            var session=runtime.Session;var world=session.ServerWorld;ValidatePolicies(world,true);
            var target=world.containers.Single(c=>c.id==expected.Id);var items=target.items.Select(i=>i.Copy()).ToArray();
            ContainerArtworkProbeEvidence.RequireState(target,false);
            var required=expected.type=="table"?new[]{("caps",3),("pin",1)}:expected.type=="trash"?new[]{("metal_comp",1)}:new[]{("lbook",1)};
            if(items.Length!=required.Length || required.Any(r=>items.Count(i=>i.definition==r.Item1 && i.count==r.Item2)!=1))
                throw new InvalidOperationException("У мебели нет согласованного authored содержимого: "+expected.Id);
            var room=runtime.Geometry.Get(Level);var players=world.actors.Where(a=>a.player && a.connected).ToArray();var baselines=new List<PlayerBaseline>();
            foreach(var player in players)
            {
                float x=expected.type=="table"?(player.character=="session-host"?95.1f:95.4f):
                    expected.type=="trash"?(player.character=="session-host"?90.6f:90.9f):(player.character=="session-host"?112.2f:112.3f);
                var position=new Vector2(x,expected.y);var hull=Anatomy.Movement(player);var bounds=new Rect(x-hull.width*.5f,position.y,hull.width,hull.height);
                if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position))
                    throw new InvalidOperationException("Начальная fixture мебели не имеет свободной опоры: "+position);
                foreach(var prop in WorldProps.All(world)) if(prop.hasPhysicalBody && prop.level==Level && InteriorOverlap(bounds,PropBounds(prop)))
                    throw new InvalidOperationException($"Начальная fixture пересекает тело мебели: actor={player.id}, position={position}, prop={prop.id}, bounds={PropBounds(prop)}.");
                foreach(var npc in world.actors) if(!npc.player && npc.level==Level && InteriorOverlap(bounds,InventorySource.BodyBounds(npc)))
                    throw new InvalidOperationException($"Начальная fixture пересекает NPC: actor={player.id}, npc={npc.id}, bounds={InventorySource.BodyBounds(npc)}.");
                player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.crouched=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
                baselines.Add(new PlayerBaseline {id=player.id,owner=player.owner,generation=player.generation,fixturePosition=position,counts=items.Select(i=>InventoryJournal.Count(player,i.definition)).ToArray()});
            }
            fixture=new Fixture {source=expected.Id,items=items,firstItem=items[0].id,firstCount=items[0].count,players=baselines.ToArray()};
            baseline=fixture.players.Single(p=>p.id==session.LocalActorId);CacheBaseline();initialRoom=room;installed=true;session.CommitWorld();NetworkServer.SendToAll(new FurnitureProbeFixture {json=JsonUtility.ToJson(fixture)});
            Debug.Log($"Изолированная fixture мебели: source={expected.Id}, type={expected.type}; изменено только исходное положение игроков, далее настоящий E/UI/бой без подгонки мира.");
        }
        private static Rect PropBounds(PropState prop)=>new(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height);
        private static bool InteriorOverlap(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private static void ValidatePolicies(WorldState world,bool initial)
        {
            foreach(var spec in declarations)
            {
                var found=WorldProps.All(world).Where(p=>p.id==spec.Id).ToArray();
                if(found.Length!=1) throw new InvalidOperationException("Мебель отсутствует/продублирована: "+spec.Id);
                var prop=found[0];bool container=world.containers.Contains(prop as ContainerState);
                if(prop.definition!=spec.type || prop.level!=Level || container!=spec.container || prop.hasPhysicalBody!=spec.body || prop.movable!=spec.body ||
                    Mathf.Abs(prop.width-spec.width)>.0001f || Mathf.Abs(prop.height-spec.height)>.0001f || Mathf.Abs(prop.mass-spec.mass)>.0001f ||
                    (initial || !spec.body) && (Mathf.Abs(prop.x-spec.x)>.0001f || Mathf.Abs(prop.y-spec.y)>.0001f))
                    throw new InvalidOperationException($"Разошёлся контракт мебели: id={spec.Id}, type={prop.definition}, position={prop.Position}, size={prop.width}x{prop.height}, body={prop.hasPhysicalBody}, movable={prop.movable}, mass={prop.mass}.");
                if(!spec.container && InventorySource.Resolve(world,spec.Id)!=null) throw new InvalidOperationException("Обычный стол получил ложный контейнер E: "+spec.Id);
            }
        }
        private bool RaceValid(WorldState world)=>!Target.items.Any(i=>i.id==fixture.firstItem) && world.pendingGrants.Count==0 && AmountsValid(world,false) && accepted.All(world.completedTransactions.Contains);
        private void CacheBaseline()
        {actorIds=fixture.players.Select(p=>p.id).ToArray();initialCounts=fixture.players.Select(p=>p.counts).ToArray();}
        private bool AmountsValid(WorldState world,bool all)=>VerifyAmounts(world,fixture.items,fixture.firstItem,actorIds,initialCounts,all);
        // Pure probe evidence, including merged-stack aliases. It does not grant loot.
        internal static bool VerifyAmounts(WorldState world,ItemStack[] items,string firstItem,string[] players,int[][] counts,bool all)
        {
            for(int index=0;index<items.Length;index++)
            {
                var item=items[index];int expectedCount=all || item.id==firstItem?item.count:0,total=0,recipients=0;
                for(int player=0;player<players.Length;player++)
                {
                    var actor=world.Find(players[player]);if(actor==null) return false;
                    int delta=InventoryJournal.Count(actor,item.definition)-counts[player][index];if(delta<0 || delta>expectedCount) return false;total+=delta;
                    if(InventoryStacks.Find(actor.inventory,actor.inventoryAliases,item.id)!=null) recipients++;
                }
                if(total!=expectedCount || recipients!=(expectedCount==0?0:1)) return false;
            }
            return true;
        }
        private IEnumerator SafeClick(RectTransform rect)=>SafePointer(UiPointerProbe.Click(rect,followTarget:true));
        private IEnumerator SafePointer(IEnumerator click)
        {
            try
            {
                while(!finished)
                {
                    bool next;try {next=click.MoveNext();} catch(Exception error) {Finish(false,error.Message+": "+Progress);yield break;}
                    if(!next) yield break;yield return click.Current;
                }
            }
            finally {(click as IDisposable)?.Dispose();}
        }
        private IEnumerator Race()
        {
            uiRaceAttempts++;int index=Target.items.FindIndex(i=>i.id==fixture.firstItem);
            if(index<0) raceSkipped=true;else yield return SafeClick(runtime.Hud.ContainerSlotRect(index));
            clicking=false;clickDone=true;
            while(!finished && !RaceValid(runtime.Session.World)) yield return null;
            if(!finished) capture="claimed";
        }
        private IEnumerator Drain()
        {
            clicking=true;
            while(!finished && Target.items.Count>0)
            {
                string id=Target.items[0].id;yield return SafeClick(runtime.Hud.ContainerSlotRect(0));
                while(!finished && (Target.items.Any(i=>i.id==id) || runtime.Session.World.pendingGrants.Count>0)) yield return null;
            }
            clicking=false;
        }
        private IEnumerator EmptyClick()
        {
            yield return SafeClick(runtime.Hud.ContainerSlotRect(8));
            Rect heldBounds=default;
            yield return SafePointer(UiPointerProbe.HoldOutside(runtime.Hud.ContainerSlotRect(8),()=>
            {
                heldBounds=UiPointerProbe.ScreenBounds(runtime.Hud.ContainerWindowRect);
                runtime.Session.ShowNotice("Проверка удерживаемого клика контейнера");
            },point=>
            {
                if(!runtime.Hud.BlocksPointerGameplay(point) || UiPointerProbe.ScreenBounds(runtime.Hud.ContainerWindowRect)!=heldBounds || Magazine(Actor)!=roundsAtOpen)
                    throw new InvalidOperationException("Удерживаемая ЛКМ вне сетки потеряла захват, сдвинула окно или потратила патрон.");
            },point=>
            {
                if(runtime.Hud.BlocksPointerGameplay(point)) throw new InvalidOperationException("Отпускание ЛКМ оставило устаревшую блокировку вне окна.");
                pointerCaptureVerified=true;
            }));
            clicking=false;clickDone=true;
        }
        private void SendObserved(int observedPhase,bool valid)
        {reported=true;NetworkClient.Send(new FurnitureProbeObserved {actor=Actor.id,source=expected.Id,generation=Actor.generation,phase=observedPhase,valid=valid,detail=Progress});}
        private void Observe(NetworkConnectionToClient connection,FurnitureProbeObserved message)
        {
            if(finished) return;var session=runtime.Session;var actor=session.ConnectedActor(connection);
            if(actor==null || actor.id!=message.actor || actor.generation!=message.generation || actor.level!=Level || message.source!=expected.Id || message.phase!=serverPhase+1) return;
            if(!message.valid) {NetworkServer.SendToAll(new FurnitureProbeFinish {detail="Наблюдение мебели разошлось: "+message.detail});return;}
            barrier.Add(actor.id);if(barrier.Count!=runtime.Options.ProbePeers) return;barrier.Clear();serverPhase++;
            if(serverPhase<4) {NetworkServer.SendToAll(new FurnitureProbeStep {phase=serverPhase==0?0:serverPhase==1?2:serverPhase==2?3:4,at=NetworkTime.time+1.5});return;}
            try
            {
                var world=session.ServerWorld;ValidatePolicies(world,false);
                if(world.containers.Single(c=>c.id==expected.Id).items.Count!=0 || world.pendingGrants.Count!=0 || !AmountsValid(world,true))
                    throw new InvalidOperationException("Координатор не подтвердил пустую мебель/единую выдачу");
                ContainerArtworkProbeEvidence.RequireState(world.containers.Single(c=>c.id==expected.Id),true);
                session.CommitWorld();session.FlushSaveWrites();var saved=session.Saves.LoadWorld(false);ValidatePolicies(saved,false);
                ContainerArtworkProbeEvidence.RequireState(saved.containers.Single(c=>c.id==expected.Id),true);
                if(saved.containers.Single(c=>c.id==expected.Id).items.Count!=0 || !AmountsValid(saved,true) || !RoomContentProbePersistence.UnchangedOnReinstall(saved,Level))
                    throw new InvalidOperationException("Disk reload или повторная установка восстановили взятый лут мебели");
                NetworkServer.SendToAll(new FurnitureProbeFinish {success=true,snapshot=session.Saves.SelectedSnapshot().Id,
                    detail="Авторская мебель: закрытый кадр до E у всех наблюдателей; подтверждённое открытие/revision1; E и плавающее окно, одновременные просмотры/UI попытки, единственные стабильные получатели, подтверждённый журнал, пустое повторное E, закрытие UI сохраняет открытый рисунок, disk reload и отсутствие пополнения при повторной установке. Только начальная fixture игроков; полный путь/перезапуск процесса здесь не проверяются."});
            }
            catch(Exception error) {NetworkServer.SendToAll(new FurnitureProbeFinish {detail=error.Message});}
        }
        private static int Magazine(ActorState actor)=>actor.inventory.FirstOrDefault(i=>i.definition=="pistol")?.weaponState?.rounds??-1;
        private void ObserveOpening(string source,string definition,AudioClip clip)
        {
            if(source!=expected.Id || finished) return;
            try
            {
                string resource=OpeningResource;
                if(resource==null || definition!=expected.type || Target?.definition!=definition ||
                    !runtime.ContainerAudio.TryGet(definition,out var mapped,out float gain) || mapped!=clip ||
                    Resources.Load<AudioClip>(resource)!=clip || gain!=.5f || phase is not (0 or 5))
                    throw new InvalidOperationException($"Нет исходного банка/фазы E мебели: source={source}, type={definition}, phase={phase}.");
                var voice=runtime.Audio.GetComponentsInChildren<AudioSource>().LastOrDefault(v=>v.clip==clip &&
                    !openingVoices.Any(observed=>observed.voice==v.GetInstanceID()));
                float attenuation=GameplayAudio.Attenuation(Vector2.Distance(Target.Position,Actor.BodyPosition));
                float expectedVolume=Mathf.Clamp01(runtime.Audio.Volume)*gain*attenuation;
                if(voice==null || !voice.isPlaying || voice.volume<=0 ||
                    Mathf.Abs(voice.volume-expectedVolume)>.00001f || voice.mute!=Application.isBatchMode ||
                    clip.samples<=0 || clip.frequency<=0 || clip.channels<=0 ||
                    expected.type=="trash" && (clip.samples!=35712 || clip.frequency!=44100 || clip.channels!=2))
                    throw new InvalidOperationException("E мебели не создал новый настоящий голос исходного native банка: "+source);
                openingVoices.Add(new OpeningVoice {source=source,definition=definition,resource=resource,clip=clip.name,phase=phase,
                    voice=voice.GetInstanceID(),channels=clip.channels,frequency=clip.frequency,samples=clip.samples,timeSamples=voice.timeSamples,
                    gain=gain,volume=voice.volume,expectedVolume=expectedVolume,mixVolume=runtime.Audio.Volume,attenuation=attenuation,
                    playing=voice.isPlaying,muted=voice.mute});
            }
            catch(Exception error) {Finish(false,error.Message+": "+Progress);}
        }
        private void LateUpdate()
        {
            if(capture==null) return;
            try
            {
                if(runtime.Options.Smoke.Contains("-frames-",StringComparison.Ordinal))
                {
                    if(runtime.Hud.ContainerOpen) ValidateLayout();
                    ContainerArtworkProbeEvidence.Save(runtime,Target,capture,runtime.Options.Report+".furniture-"+expected.type+"-"+capture+".frame.json");
                    NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".furniture-"+expected.type+"-"+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    NetworkSceneRecord.Save(runtime,(capture=="closed"?700:capture=="ui-closed"?750:800+phase)+Array.IndexOf(declarations,expected)*10);
                }
                if(capture=="closed") closedVerified=true;
                if(capture=="ui-closed") closedWindowVerified=true;
                lastCapture=capture;capture=null;
            }
            catch(Exception error) {capture=null;Finish(false,error.Message+": "+Progress);}
        }
        private void Finish(bool success,string detail,string durableSnapshot=null)
        {
            if(finished) return;bool reload=false,reinstall=false;
            try
            {
                if(success)
                {
                    int expectedCount=OpeningResource==null?0:2;
                    if(openingVoices.Count!=expectedCount || runtime.ContainerAudio.Openings!=expectedCount ||
                        expectedCount==2 && (openingVoices[0].phase!=0 || openingVoices[1].phase!=5) ||
                        expectedCount==0 && runtime.ContainerAudio.TryGet(expected.type,out _,out _))
                        throw new InvalidOperationException($"Неверное число исходных голосов двух E мебели: type={expected.type}, actual={openingVoices.Count}/{runtime.ContainerAudio.Openings}, expected={expectedCount}.");
                    // This opt-in disk check needs the completed recovery head,
                    // not the previous head while its ordered write is pending.
                    runtime.Session.FlushSaveWrites();
                    var selected=runtime.Session.Saves.SelectedSnapshot();snapshot=selected?.Id;
                    if(snapshot!=durableSnapshot) throw new InvalidOperationException($"Реплика мебели не соответствует подтверждённому snapshot: actual={snapshot}, expected={durableSnapshot}.");
                    var saved=runtime.Session.Saves.LoadWorld(false);ValidatePolicies(saved,false);
                    ContainerArtworkProbeEvidence.RequireState(saved.containers.Single(c=>c.id==expected.Id),true);
                    reload=saved.containers.Single(c=>c.id==expected.Id).items.Count==0 && AmountsValid(saved,true);
                    reinstall=RoomContentProbePersistence.UnchangedOnReinstall(saved,Level);
                    if(!reload || !reinstall || !accepted.All(saved.completedTransactions.Contains)) throw new InvalidOperationException("Реплика мебели потеряла выдачу/журнал или повторно создала лут");
                    if(runtime.Options.Smoke.Contains("-frames-",StringComparison.Ordinal))
                    {ContainerArtworkProbeEvidence.Save(runtime,Target,"saved",runtime.Options.Report+".furniture-"+expected.type+"-saved.frame.json");NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".furniture-"+expected.type+"-saved.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);NetworkSceneRecord.Save(runtime,900+Array.IndexOf(declarations,expected));}
                }
            }
            catch(Exception error) {success=false;detail=error.Message;}
            finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,type=expected.type,source=expected.Id,phase=phase,
                explicitInitialFixture=true,players=fixture?.players,initialItems=fixture?.items,opened=opened,empty=empty,reopened=reopened,
                closedVerified=closedVerified,appearanceVerified=success && opened && closedVerified,closedWindowVerified=closedWindowVerified,
                policiesVerified=success,stableRecipientsVerified=success,durableVerified=success,savedReloadVerified=reload,reinstallVerified=reinstall,
                snapshot=snapshot,claimsAccepted=accepted.Count,claimsDenied=denied.Count,uiRaceAttempts=uiRaceAttempts,raceSkipped=raceSkipped,combatShots=combatShots,layoutCaptures=layoutCaptures,pointerCaptureVerified=pointerCaptureVerified,progress=Progress,
                actualOpenings=openingVoices.Count,totalOpenings=runtime.ContainerAudio.Openings,expectedOpenings=OpeningResource==null?0:2,openingVoices=openingVoices.ToArray()},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy() {if(runtime?.ContainerAudio!=null) runtime.ContainerAudio.Opened-=ObserveOpening;}
    }
}
