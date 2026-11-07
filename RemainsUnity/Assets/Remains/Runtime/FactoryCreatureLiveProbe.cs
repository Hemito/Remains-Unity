using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in live run of the factory's robots and zombies (2026-10-30) on a new seed104 world: the host puts the test
    // player near one placed creature of each kind at a time and records what the ordinary simulation, transport and
    // host self-parse make of it — noticing, attacks, digging out, radiation, the death blast — then shoots it with the
    // pistol. Neither the creatures nor their rules are scripted; only the player is placed, and its health and
    // radiation are restored between stations (logged). Frames of each station go next to the report.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryCreatureLiveProbe : MonoBehaviour
    {
        private const string Level="factory:0";
        private const float WatchSeconds=20,KillSeconds=15;
        private const int Attempts=3;
        private sealed class Station
        {
            public string name;public Func<ActorState,ActorTypeDefinition,bool> matches;
            public bool radiation,buried,blast,naturalHit;
            public float watch=WatchSeconds;
        }
        private static readonly Station[] Stations=
        {
            // A spritebot hovers above its floor, often beyond its 6 m sight: it may have to drift close on its patrol.
            new() {name="спрайтбот",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.SpriteBot,watch=40},
            new() {name="мина-паук",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.SpiderMine,blast=true},
            new() {name="роллер",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Roller},
            new() {name="робомозг с лазером",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Robobrain && a.actorType.Contains("robolaser")},
            new() {name="робомозг с искрой",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Robobrain && a.actorType.Contains("robospark")},
            new() {name="zombie0",matches=(a,t)=>a.actorType=="zombie0" && !ActorPresence.Hidden(a)},
            new() {name="zombie1",matches=(a,t)=>a.actorType=="zombie1" && !ActorPresence.Hidden(a)},
            new() {name="zombie2",matches=(a,t)=>a.actorType=="zombie2" && !ActorPresence.Hidden(a)},
            new() {name="zombie3",matches=(a,t)=>a.actorType=="zombie3" && !ActorPresence.Hidden(a)},
            // Seed104's glowing ghouls may all be buried: either digs out to the player first.
            new() {name="zombie4",matches=(a,t)=>a.actorType=="zombie4",radiation=true},
            new() {name="закопанный зомби",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Zombie && ActorPresence.Hidden(a),buried=true},
            // Sapient raiders on PonyBrain (2026-10-31): a shooter, a weapon in the teeth (the body strike), a levitated weapon.
            new() {name="рейдер-стрелок",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Raider && t.raider is {robot:false,zombie:false,attacker:RaiderAttacker.Ranged}},
            new() {name="рейдер, оружие в зубах",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Raider && t.raider is {robot:false,zombie:false,attacker:RaiderAttacker.Teeth}},
            new() {name="рейдер, оружие телекинезом",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Raider && t.raider is {robot:false,zombie:false,attacker:RaiderAttacker.Levitated}},
            new() {name="попадание слизня",matches=(a,t)=>t.behavior==NpcCreatureBehaviours.Slime,naturalHit=true},
        };
        [Serializable] private sealed class StationResult
        {
            public string station,actor,actorType,detail,modes,poses;
            public int attempt,attacks,explosions,naturalBursts,replayedNaturalAttacks;
            public bool passed,noticed,emerged,hiddenAtStart,killed,playerDown;
            public float playerDamage,radiation,moved,firstAttackSeconds,killSeconds;
            public string[] frames;
        }
        [Serializable] private sealed class LogEntry {public string type,message;public int count;}
        [Serializable] private sealed class Report
        {
            public bool success;public string detail;public int creatures,errors;
            public StationResult[] stations;public LogEntry[] log;
        }

        private PrototypeRuntime runtime;
        private bool traveled,finished;
        private int station=-1,attempt,phase,exitCode;
        private readonly HashSet<string> tried=new(StringComparer.Ordinal);
        private readonly HashSet<string> attacks=new(StringComparer.Ordinal);
        private readonly Dictionary<string,int> naturalBursts=new(StringComparer.Ordinal);
        private readonly List<StationResult> results=new();
        private readonly Dictionary<string,LogEntry> log=new(StringComparer.Ordinal);
        private readonly HashSet<string> modes=new(StringComparer.Ordinal),poses=new(StringComparer.Ordinal);
        private readonly List<string> frames=new();
        private StationResult current;
        private string npcId,capture;
        private float started,deadline,nextShot,firstAttack=-1,killStarted;
        private float startHealth;
        private int startExplosions;
        private bool met;
        private string broken;
        private float nextTrace;
        private int shots;
        private float closeAt;
        private HashSet<int> selected;
        private Vector2 npcStart;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+1500;value.CommandSource=Command;
            // --live-stations 0,1,10: only these stations (indices of Stations), to repeat a few.
            string only=Seed104CpuBaselineProbe.Argument("--live-stations",null);
            selected=only==null?null:new HashSet<int>(only.Split(',').Select(int.Parse));
            if(value.Options.Role!="host" || value.Options.ProbePeers!=1 || string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Живой прогон существ требует одиночного хоста, отдельного save-dir и report.");
            if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Живой прогон существ не перезаписывает существующий мир.");
            var world=new WorldState {seed=104};
            FactoryPreparation.Create(world,GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            value.Session.Saves.SaveWorld(world);
            var player=new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,level="hub",player=true,weaponContent=WeaponCatalog.ManifestCopy()};
            PlayerStarterInventory.Give(player);InventoryStacks.Add(player,SharedInventory.Create("p10",300));
            value.Session.Saves.SaveCharacter(player);
            value.Session.AttackReceived+=Attack;
            value.NaturalHits.Shown+=NaturalHit;
            // Every hit the player takes, with its attack: one blast of 100 or two of 60 read differently.
            value.Combat.DamageApplied+=hit=>{if(hit.actor==value.Session.LocalActorId) Debug.Log($"Живой прогон, попадание: {hit.attack} — {hit.amount:0.##} (станция {(current==null?"-":current.station)}).");};
            Application.logMessageReceived+=Log;
        }
        private void OnDestroy()
        {
            Application.logMessageReceived-=Log;
            if(runtime!=null) runtime.NaturalHits.Shown-=NaturalHit;
        }
        private void Log(string message,string stack,LogType type)
        {
            if(type==LogType.Log) return;
            string line=message.Split('\n')[0];if(line.Length>300) line=line.Substring(0,300);
            string key=type+":"+line;
            if(log.TryGetValue(key,out var entry)) {entry.count++;return;}
            if(log.Count<200) log[key]=new LogEntry {type=type.ToString(),message=type==LogType.Exception?line+" | "+stack.Split('\n').FirstOrDefault():line,count=1};
            // A dropped connection ends the run: the session cannot recover by itself.
            if(line.StartsWith("Disconnecting connection",StringComparison.Ordinal)) broken=line;
        }
        private void Attack(AttackEvent attack)
        {
            if(attack.attacker==runtime.Session.LocalActorId) shots++;
            if(current==null || npcId==null || attack.attacker!=npcId || !attacks.Add(attack.id)) return;
            current.attacks++;
            if(firstAttack<0) {firstAttack=Time.unscaledTime;current.firstAttackSeconds=firstAttack-started;capture="атака";}
            if(Stations[station].naturalHit && NpcNaturalAttack.IsNatural(attack))
            {
                current.replayedNaturalAttacks++;
                NetworkServer.SendToAll(Remains.Networking.AttackMessage.From(attack));
            }
        }
        private void NaturalHit(DamageResult result)
        {
            if(current==null || !Stations[station].naturalHit || result.actor!=runtime.Session.LocalActorId) return;
            naturalBursts[result.attack]=naturalBursts.GetValueOrDefault(result.attack)+1;
            capture="частицы попадания";
        }
        private ActorState Npc=>npcId==null?null:runtime.Session.ServerWorld?.Find(npcId);

        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;var npc=runtime.Session.World?.Find(npcId??"");
            if(finished || phase!=2 || actor==null || actor.down || npc==null || npc.down || ActorPresence.Hidden(npc)) return default;
            var hull=Anatomy.Movement(npc);
            var command=new PlayerCommand {weapon="pistol",hasAim=true,aim=npc.Position+Vector2.up*(hull.height*.5f)};
            if(ProbeWeaponSelection.Magazine(actor,"pistol").rounds==0) {command.reload=true;return command;}
            if(Time.unscaledTime>=nextShot) {nextShot=Time.unscaledTime+.3f;command.fire=true;}
            return command;
        }
        private void Update()
        {
            if(finished) return;
            try {Tick();}
            catch(Exception error) {Debug.LogException(error);Finish(false,"Сбой проверки: "+error.Message);}
        }
        private void Tick()
        {
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: станция {station}, фаза {phase}.");return;}
            if(broken!=null) {if(current!=null) {current.detail="разрыв соединения";results.Add(current);}Finish(false,"Соединение разорвано: "+broken);return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null) return;
            // A downed lone player loses the group (world-structure): everyone returns to the hub. The station ends; the
            // run goes back to the factory and goes on.
            if(phase!=0 && actor.level!=Level && !session.Travel.Pending)
            {current.playerDown=true;Close(phase==3?null:"игрок сбит, группа вернулась в хаб");traveled=false;return;}
            if(!traveled) {traveled=true;session.Travel.RequestFactory();return;}
            if(actor.level!=Level || session.Travel.Pending || !runtime.LevelPresentationReady || runtime.ReleasingUnusedAssets) return;
            if(phase==0) {NextStation();return;}
            var npc=Npc;var player=session.ServerWorld.Find(actor.id);
            if(npc==null) {Close("существо исчезло из мира");return;}
            if(npc.creature?.mode is {} mode) modes.Add(mode);
            if(!string.IsNullOrEmpty(npc.pose)) poses.Add(npc.pose);
            current.moved=Mathf.Max(current.moved,Vector2.Distance(npc.Position,npcStart));
            // Radiation caps health at 100 − rad (PlayerRadiation): only the loss below that cap is a hit.
            current.playerDamage=Mathf.Max(current.playerDamage,startHealth-player.radiation-player.health);
            current.radiation=Mathf.Max(current.radiation,player.radiation);
            current.emerged|=current.hiddenAtStart && !ActorPresence.Hidden(npc);
            current.explosions=session.DeathBlasts.Explosions-startExplosions;
            current.playerDown|=player.down;
            float now=Time.unscaledTime;
            if(now>=nextTrace)
            {
                // The owner's copy decides; both copies, the player on both sides and its shots show where a station stalls.
                nextTrace=now+2;var local=session.World.Find(npc.id);
                Debug.Log($"Живой прогон, след: {npc.id} хост owner={npc.owner} mode={npc.creature?.mode} target={npc.target} pose={npc.pose} facing={npc.facing} pos={npc.Position}; " +
                    $"клиент owner={local?.owner} mode={local?.creature?.mode} target={local?.target} pose={local?.pose} awake={(local!=null && NpcActivity.Awake(session.World,local))} " +
                    $"paused={session.Pause.IsPaused(Level)}; игрок хост={player.Position} клиент={actor.Position} owner={actor.owner}/{session.LocalOwner} hp={actor.health}; выстрелов {shots}.");
            }
            if(phase==3) {if(now>=closeAt) Close(null);return;}
            if(phase==1)
            {
                if(now-started>1.5f && !met) {met=true;capture="встреча";}
                var spec=Stations[station];
                if(npc.down) {Dying(now);return;}
                bool attacked=Attacked;
                bool naturalHit=!spec.naturalHit || naturalBursts.Keys.Any(attacks.Contains);
                bool done=attacked && naturalHit && (!spec.radiation || current.radiation>0) && (!spec.buried && !current.hiddenAtStart || current.emerged) && now-firstAttack>1 ||
                    player.down || now-started>spec.watch;
                if(!done) return;
                current.noticed=current.moved>.3f || attacked || modes.Count>1 || current.emerged;
                if((!current.noticed || !naturalHit) && !npc.down && attempt<Attempts && Place())
                {Debug.Log($"Живой прогон: {Stations[station].name} {npc.id} не заметил игрока; взят другой экземпляр.");return;}
                if(ActorPresence.Hidden(npc)) {Close(null);return;}
                Restore(player,"перед стрельбой");phase=2;killStarted=now;return;
            }
            if(phase==2)
            {
                if(npc.down) {current.killed=true;current.killSeconds=now-killStarted;Dying(now);return;}
                if(player.down) {Close("игрок сбит при стрельбе");return;}
                if(now-killStarted>KillSeconds) Close("не убит пистолетом за "+KillSeconds+" с");
            }
        }
        private bool Attacked=>current.attacks>0 || current.playerDamage>.5f || current.explosions>0;
        // A death is watched 1.5 s longer: its frame, and a death blast's damage that reaches the player after it.
        private void Dying(float now) {capture="гибель";closeAt=now+1.5f;phase=3;}
        private void NextStation()
        {
            station++;attempt=0;tried.Clear();
            while(selected!=null && station<Stations.Length && !selected.Contains(station)) station++;
            if(station>=Stations.Length) {Complete();return;}
            if(!Place())
            {
                results.Add(new StationResult {station=Stations[station].name,detail=tried.Count==0?"на заводе нет таких существ":"нет места для игрока в прямой видимости"});
                Debug.LogWarning($"Живой прогон: станция {Stations[station].name} пропущена: {results[^1].detail} (проверено {tried.Count}).");
                NextStation();
            }
        }
        // The next untried creature of the station with a place for the player; false when none is left.
        private bool Place()
        {
            var session=runtime.Session;var world=session.ServerWorld;var spec=Stations[station];
            var player=world.actors.Single(a=>a.player && a.connected);var room=runtime.Geometry.Get(Level);var hull=Anatomy.Movement(player);
            var candidates=world.actors.Where(a=>a.level==Level && !a.player && !a.down && a.health>0 && !tried.Contains(a.id) &&
                spec.matches(a,ActorTypes.Get(a.actorType))).OrderBy(a=>a.id,StringComparer.Ordinal).ToArray();
            // A creature with room in front of it within its sight first; behind it, then beyond its sight, only when none has.
            foreach(var (front,near) in new[]{(true,true),(false,true),(true,false)})
                foreach(var npc in candidates)
                {
                    if(!Spot(room,hull,npc,front,near,out var position)) continue;
                    tried.Add(npc.id);
                    if(current!=null) results.Add(Abandoned(current));
                    attempt++;Begin(npc,player,position);return true;
                }
            foreach(var npc in candidates) tried.Add(npc.id);
            return false;
        }
        private StationResult Abandoned(StationResult result)
        {result.detail="не заметил игрока, взят другой экземпляр";result.poses=string.Join(",",poses);result.modes=string.Join(",",modes);return result;}
        // A free, supported place 2–8 m from the creature, at its height or below it (a flyer's floor), in its line of
        // sight and (near) within 0.85 of its sight distance; in front of it, or behind it — NpcPerception does not see
        // behind beyond its close distance.
        private static bool Spot(RoomLayout room,MovementHull hull,ActorState npc,bool front,bool near,out Vector2 position)
        {
            var eye=npc.Position+Vector2.up*(Anatomy.Movement(npc).height*.6f);
            float side=(npc.facing<0?-1:1)*(front?1:-1),sight=ActorTypes.Get(npc.actorType).perception.sightDistance*.85f;
            foreach(float dx in new[]{5f,4f,6f,3f,8f,2f})
                for(float dy=.5f;dy>=-12;dy-=.25f)
                {
                    position=npc.Position+new Vector2(dx*side,dy);
                    if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position)) continue;
                    var seen=position+Vector2.up*hull.height*.7f;
                    if((!near || Vector2.Distance(eye,seen)<=sight) && RoomVisibility.Clear(room,eye,seen)) return true;
                    break;
                }
            position=default;return false;
        }
        private void Begin(ActorState npc,ActorState player,Vector2 position)
        {
            npcId=npc.id;npcStart=npc.Position;modes.Clear();poses.Clear();frames.Clear();firstAttack=-1;met=false;
            attacks.Clear();naturalBursts.Clear();
            current=new StationResult {station=Stations[station].name,actor=npc.id,actorType=npc.actorType,attempt=attempt,hiddenAtStart=ActorPresence.Hidden(npc)};
            player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.pose="idle";player.poseTime=0;
            Restore(player,"на станции "+current.station);
            startHealth=player.health;startExplosions=runtime.Session.DeathBlasts.Explosions;
            started=Time.unscaledTime;phase=1;
            Debug.Log($"Живой прогон: {current.station} — {npc.id} ({npc.actorType}) в {npc.Position}, игрок в {position}, закопан={current.hiddenAtStart}.");
        }
        // Probe-only: the test player's health and radiation are put back so one station does not decide the next.
        private void Restore(ActorState player,string reason)
        {
            if(player.health<100 || player.radiation>0 || player.down)
                Debug.LogWarning($"Живой прогон: здоровье игрока восстановлено {reason} ({player.health}, рад {player.radiation}, сбит={player.down}).");
            player.health=100;player.radiation=0;player.down=false;player.generation++;player.revision++;
            runtime.Session.CommitWorld();
        }
        private void Close(string problem)
        {
            var spec=Stations[station];
            current.modes=string.Join(",",modes.OrderBy(m=>m,StringComparer.Ordinal));
            current.poses=string.Join(",",poses.OrderBy(p=>p,StringComparer.Ordinal));
            current.frames=frames.ToArray();
            var missing=new List<string>();
            if(!Attacked) missing.Add("не напал");
            if(spec.radiation && current.radiation<=0) missing.Add("не облучил");
            if((spec.buried || current.hiddenAtStart) && !current.emerged) missing.Add("не выкопался");
            if(spec.blast && current.explosions==0) missing.Add("не взорвался");
            if(spec.naturalHit)
            {
                var counts=naturalBursts.Where(pair=>attacks.Contains(pair.Key)).Select(pair=>pair.Value).ToArray();
                current.naturalBursts=counts.Sum();
                if(counts.Length==0 || counts.Any(count=>count!=1) || current.replayedNaturalAttacks==0)
                    missing.Add("нет однократного эффекта подтверждённой атаки при повторной доставке");
            }
            current.passed=missing.Count==0;
            current.detail=string.Join("; ",missing.Append(problem).Where(s=>s!=null));
            results.Add(current);
            Debug.Log($"Живой прогон: {current.station} {(current.passed?"PASS":"FAIL")}: атак {current.attacks}, урон {current.playerDamage}, рад {current.radiation}, сдвиг {current.moved:0.0} м, режимы [{current.modes}], позы [{current.poses}], убит={current.killed}. {current.detail}");
            current=null;npcId=null;phase=0;
        }
        private void LateUpdate()
        {
            if(capture==null || current==null) return;
            var actor=runtime.Session.LocalActor;var npc=runtime.Session.World?.Find(npcId??"");
            if(actor==null || npc==null || runtime.Hud==null && !Stations[station].naturalHit) {capture=null;return;}
            var camera=Camera.main;var saved=camera.transform.position;
            string path=$"{runtime.Options.Report}.{station:00}-{frames.Count}.png";
            try
            {
                var middle=(actor.Position+npc.Position)*.5f;
                camera.transform.position=new Vector3(middle.x,middle.y+1.5f,saved.z);
                NetworkFrameCapture.Save(camera,path,runtime.Hud?.GetComponent<Canvas>(),1280,720);
                frames.Add(Path.GetFileName(path)+" "+capture);
            }
            finally {camera.transform.position=saved;capture=null;}
        }
        private void Complete()
        {
            int errors=log.Values.Where(e=>e.type is "Error" or "Exception" or "Assert").Sum(e=>e.count);
            int failed=results.Count(r=>!r.passed);
            Finish(failed==0 && errors==0,$"Станций {results.Count}, не прошли {failed}; ошибок в журнале {errors}.");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            int creatures=runtime.Session.ServerWorld?.actors.Count(a=>a.level==Level && !a.player)??0;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,creatures=creatures,
                errors=log.Values.Where(e=>e.type is "Error" or "Exception" or "Assert").Sum(e=>e.count),
                stations=results.ToArray(),log=log.Values.OrderByDescending(e=>e.count).ToArray()},true));
            Debug.Log("Живой прогон существ: "+(success?"PASS":"FAIL")+"; "+detail);
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private void Exit()=>Application.Quit(exitCode);
    }
}
