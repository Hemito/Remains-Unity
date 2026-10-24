using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Diagnostics;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in diagnostic driver: real commands, transport, simulation and host self-parse.
    // No teleportation, fabricated hits, paused AI, altered damage or live-library writes.
    public sealed class Seed104CpuBaselineProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private string scenario,revision,assemblyHash,fixtureHash;
        private int warmFrames,sampleFrames,readyFrame=-1,phase,shots,warmShots;
        private bool finished,createdPlayerFixture;
        private double deadline,nextShot,started,nextDiagnostic;
        private readonly List<PayloadSample> payloads=new();
        private readonly List<MemorySample> memory=new();
        private double nextMemory;
        private readonly HashSet<string> attacks=new(StringComparer.Ordinal);
        private int initialNpcs,initialContainers,initialMines,initialHazards;
        private PropState bodyProbeProp,bodyProbeContainer;
        private float bodyProbePropX,bodyProbeContainerX;
        private int bodyChanges;
        private Seed104LightWindowRoute lightRoute;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;scenario=Argument("--baseline-scenario","idle");revision=Argument("--baseline-revision","unknown");
            if(Environment.GetCommandLineArgs().Contains("--baseline-create-fixture"))
                CreatePlayerFixture(value.Options);
            fixtureHash=File.ReadAllText(Path.Combine(value.Options.SaveDirectory,"baseline-input.sha256"));
            // Unity's Editor may load assemblies from bytes, leaving Assembly.Location empty.
            string assemblyPath=Application.isEditor?Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/ScriptAssemblies/Assembly-CSharp.dll")):
                typeof(PrototypeRuntime).Assembly.Location;
            using(var hash=SHA256.Create()) using(var input=File.OpenRead(assemblyPath))
                assemblyHash=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
            warmFrames=int.Parse(Argument("--baseline-warm-frames","600"));
            sampleFrames=int.Parse(Argument("--baseline-frames","900"));
            if(scenario!="idle" && scenario!="combat" && scenario!="inactive" && scenario!="bodies" && scenario!="light-windows" || warmFrames<60 || sampleFrames<120)
                throw new InvalidOperationException("CPU-замер: нужен idle/combat/inactive/bodies/light-windows, прогрев >=60 кадров и выборка >=120.");
            if(value.Options.Role!="host" || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("CPU-замер допускается только с изолированным хостом.");
            deadline=Time.realtimeSinceStartupAsDouble+Math.Max(480,(warmFrames+sampleFrames)/30d+180);
            value.CommandSource=Command;
            WorldReplication.PayloadMeasured+=Payload;
            value.Session.AttackReceived+=Attack;
            Debug.Log($"CPU seed104: сценарий={scenario}, прогрев={warmFrames}, выборка={sampleFrames}, исходник={revision}.");
        }
        private void CreatePlayerFixture(LaunchOptions options)
        {
            string directory=Argument("--baseline-create-fixture",null);
            if(Application.isEditor || Application.platform!=RuntimePlatform.WindowsPlayer || !Application.isBatchMode ||
                options.Role!="host" || options.Smoke!="seed104-baseline" || string.IsNullOrWhiteSpace(directory) ||
                directory.StartsWith("--",StringComparison.Ordinal) || string.IsNullOrWhiteSpace(options.SaveDirectory) || string.IsNullOrEmpty(options.Report))
                throw new InvalidOperationException("Создание Player fixture требует Windows Player batchmode, host, seed104-baseline, --baseline-create-fixture <dir>, пустого save-dir и report.");
            string fixtureDirectory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string saveDirectory=Path.GetFullPath(options.SaveDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(fixtureDirectory.Equals(saveDirectory,StringComparison.OrdinalIgnoreCase) ||
                fixtureDirectory.StartsWith(saveDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
                saveDirectory.StartsWith(fixtureDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Player fixture и save-dir должны быть отдельными, невложенными каталогами.");
            foreach(string path in new[]{fixtureDirectory,saveDirectory})
                if(File.Exists(path) || Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
                    throw new InvalidOperationException("Создание Player fixture запрещено: каталог не пуст или занят файлом: "+path);
            var world=new WorldState {seed=104,worldId="64ab04d6f71844dba014ce97fa6f347b",branchId="1b17fa9845504183a40af0e0151fbb7a",
                weaponContent=WeaponCatalog.ManifestCopy()};
            FactoryPreparation.Create(world,GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,new[]{"hub","factory:0"});
                foreach(string level in new[]{"hub","factory:0"})
                    RoomContentInstaller.Apply(world,RoomContentBatch.Read(RoomAuthoring.Load(level,world),level));
                var actor=new ActorState {id="player:"+options.Character,character=options.Character,level="hub",player=true,
                    weaponContent=WeaponCatalog.ManifestCopy()};
                PlayerStarterInventory.Give(actor);
                InventoryStacks.Add(actor,SharedInventory.Create("p10",300));
                string worldText=StateJson.ToJson(world),actorText=StateJson.ToJson(actor);
                Directory.CreateDirectory(fixtureDirectory);
                File.WriteAllText(Path.Combine(fixtureDirectory,"world.json"),worldText);
                File.WriteAllText(Path.Combine(fixtureDirectory,"character.json"),actorText);
                var saves=new SaveStore(saveDirectory);
                using(var hash=SHA256.Create())
                {
                    string digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(worldText+"\n"+actorText))).Replace("-","").ToLowerInvariant();
                    File.WriteAllText(Path.Combine(saveDirectory,"baseline-input.sha256"),digest);
                }
                saves.SaveWorld(StateJson.FromJson<WorldState>(worldText));
                saves.SaveCharacter(StateJson.FromJson<ActorState>(actorText));
                createdPlayerFixture=true;
                Debug.Log($"CPU seed104: создан новый Windows Player fixture: {fixtureDirectory}. Совпадение с hash прежнего Editor baseline3 не заявляется.");
            }
            finally {FactoryContentSources.Clear();}
        }
        public static string Argument(string key,string fallback)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) if(args[i]==key) return args[i+1];
            return fallback;
        }
        private void Attack(AttackEvent attack)
        {if(attack.attacker==runtime.Session.LocalActorId && attacks.Add(attack.id)) shots++;}
        private void Payload(string kind,int bytes)
        {
            if(!CpuBaselineCapture.Enabled) return;
            payloads.Add(new PayloadSample {kind=kind,bytes=bytes,frame=Time.frameCount});
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null || phase<2 || scenario!="combat" || finished) return default;
            var command=new PlayerCommand {hasAim=true,aim=actor.Position+new Vector2(12,2),weapon="pistol"};
            var selected=WeaponInventory.Selected(actor);
            if(selected?.definition!="pistol") return command;
            if(selected.weaponState.rounds==0) {command.reload=true;return command;}
            if(Time.unscaledTimeAsDouble>=nextShot)
            {nextShot=Time.unscaledTimeAsDouble+.35;command.fire=true;}
            return command;
        }
        private void Update()
        {
            if(finished) return;
            try {Tick();}
            catch(Exception error) {Debug.LogException(error);Finish(false,error.ToString());}
        }
        private void Tick()
        {
            if(Time.realtimeSinceStartupAsDouble>=nextMemory)
            {nextMemory=Time.realtimeSinceStartupAsDouble+10;memory.Add(new MemorySample {seconds=Time.realtimeSinceStartupAsDouble,managedBytes=GC.GetTotalMemory(false)});}
            if(Time.realtimeSinceStartupAsDouble>deadline) {Finish(false,$"Таймаут: фаза={phase}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if((phase<2 || phase==3 && CpuBaselineCapture.FrameCount==0) && Time.realtimeSinceStartupAsDouble>=nextDiagnostic)
            {
                nextDiagnostic=Time.realtimeSinceStartupAsDouble+10;
                Debug.Log($"CPU seed104 ожидание: фаза={phase}, actor={actor?.id}, level={actor?.level}, travel={session.Travel.Pending}, ready={runtime.LevelPresentationReady}, preparing={runtime.IsLevelPreparing}, unload={runtime.ReleasingUnusedAssets}, light={runtime.ActiveFactoryLighting?.IsReady}, capture={CpuBaselineCapture.Enabled}, frames={CpuBaselineCapture.FrameCount}, allocated={GC.GetAllocatedBytesForCurrentThread()}.");
            }
            if(actor==null) return;
            if(actor.down) {Finish(false,"Персонаж выведен из строя; сценарий перестал соответствовать базовой линии.");return;}
            if(phase==0)
            {
                if(session.World.seed!=104) throw new InvalidOperationException("Загружен другой seed.");
                initialNpcs=session.World.actors.Count(a=>!a.player && a.level=="factory:0");
                initialContainers=session.World.containers.Count(c=>c.level=="factory:0");
                initialMines=session.World.mines.Count(m=>m.level=="factory:0");
                initialHazards=session.World.hazards.Count(h=>h.level=="factory:0");
                if(initialNpcs<200 || initialContainers<1000 || initialMines<50 || initialHazards<30)
                    throw new InvalidOperationException($"Неполный завод: NPC={initialNpcs}, контейнеров={initialContainers}, мин={initialMines}, ловушек={initialHazards}.");
                session.Travel.RequestFactory();phase=1;return;
            }
            if(phase==1)
            {
                if(actor.level!="factory:0" || session.Travel.Pending || !runtime.LevelPresentationReady || runtime.ReleasingUnusedAssets) return;
                if(scenario=="inactive") {session.Travel.RequestHub();phase=4;return;}
                phase=2;readyFrame=Time.frameCount;return;
            }
            if(phase==4)
            {
                if(actor.level!="hub" || session.Travel.Pending || !runtime.LevelPresentationReady || runtime.ReleasingUnusedAssets) return;
                phase=2;readyFrame=Time.frameCount;return;
            }
            if(phase==2 && Time.frameCount-readyFrame>=warmFrames)
            {
                if(scenario=="bodies")
                {
                    var host=session.ServerWorld;
                    bodyProbeProp=host.props.FirstOrDefault(p=>p.level==actor.level && Vector2.Distance(p.Position,actor.Position)>64);
                    bodyProbeContainer=host.containers.FirstOrDefault(p=>p.level==actor.level && Vector2.Distance(p.Position,actor.Position)>64);
                    if(bodyProbeProp==null || bodyProbeContainer==null) throw new InvalidOperationException("Body-замер: нет дальнего prop/контейнера в полном fixture.");
                    bodyProbePropX=bodyProbeProp.x;bodyProbeContainerX=bodyProbeContainer.x;
                    Debug.LogWarning($"Body-замер: диагностическое смещение данных изолированного fixture, не игровой тест физики: prop={bodyProbeProp.id}, container={bodyProbeContainer.id}.");
                    InvokeRepeating(nameof(ChangeBodies),.1f,3f);
                }
                if(scenario=="light-windows") lightRoute=new Seed104LightWindowRoute(runtime);
                payloads.Clear();warmShots=shots;started=Time.realtimeSinceStartupAsDouble;
                Debug.Log($"CPU seed104: начало захвата, level={actor.level}, ready={runtime.LevelPresentationReady}, frame={Time.frameCount}.");
                FrameHitchLog.BeginCpuBaseline(sampleFrames);phase=3;return;
            }
            if(phase==3 && lightRoute!=null)
            {
                if(!CpuBaselineCapture.Enabled && CpuBaselineCapture.FrameCount==0) return;
                if(!CpuBaselineCapture.Enabled) {Finish(false,"Свет: исчерпан лимит кадров до окончания маршрута.");return;}
                if(lightRoute.Tick()) {Finish(true,"Свет: шесть соседних переходов и прямой возврат завершены; синтетические запросы, не приёмка движения камеры.");return;}
            }
            if(phase==3 && !CpuBaselineCapture.Enabled && CpuBaselineCapture.FrameCount>=sampleFrames)
            {
                // Names are persisted in the report; the real receive path is separately timed by FrameHitchLog.
                double seconds=Time.realtimeSinceStartupAsDouble-started;
                bool sustained=(scenario!="combat" || shots-warmShots>=Math.Max(3,(int)(seconds*.8))) &&
                    (scenario!="bodies" || bodyChanges>=Math.Max(3,(int)(seconds/3*.8)));
                Finish(sustained,sustained?"CPU-замер завершён; время Editor batch не является временем кадра Windows Player.":"Недостаточная частота событий сценария; замер недействителен.");
            }
        }
        private void ChangeBodies()
        {
            if(finished || phase!=3) return;
            bodyChanges++;
            float offset=bodyChanges%2==1?.25f:0;
            bodyProbeProp.x=bodyProbePropX+offset;bodyProbeContainer.x=bodyProbeContainerX+offset;
            var host=runtime.Session.ServerWorld;
            WorldChanges.Mark(host,bodyProbeProp);WorldChanges.Mark(host,bodyProbeContainer);host.revision++;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;FrameHitchLog.EndCpuBaseline();
            lightRoute?.Dispose();
            var summary=CpuBaselineCapture.Summarize();
            var report=new Report {success=success,detail=detail,scenario=scenario,sourceRevision=revision,
                assemblySha256=assemblyHash,allocationMetricAvailable=FrameHitchLog.BaselineAllocationsAvailable,
                fixtureSha256=fixtureHash,
                unity=Application.unityVersion,processor=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),editor=Application.isEditor,batch=Application.isBatchMode,
                width=Screen.width,height=Screen.height,vSync=QualitySettings.vSyncCount,targetFps=Application.targetFrameRate,
                warmFrames=warmFrames,sampleFrames=sampleFrames,seconds=started==0?0:Time.realtimeSinceStartupAsDouble-started,captureStartedSeconds=started,
                factoryNpcs=initialNpcs,containers=initialContainers,mines=initialMines,hazards=initialHazards,
                level=runtime.Session.LocalActor?.level,shots=shots-warmShots,bodyChanges=bodyChanges,payloads=payloads.ToArray(),cpu=summary,memory=memory.ToArray(),lightWindows=lightRoute?.Samples,lightWindowFrames=lightRoute?.Frames,actualSampleFrames=summary.frameCount,
                limitations="Секции вложены и считаются включительно. Allocations — глобальный счётчик Unity GC Allocated In Frame: все managed-потоки за интервал, включая диагностику; это не атрибуция выделений конкретной системе. Фоновое CPU-время не измерено. work=кадр−TimeUpdate, не чистое CPU-время; ожидания в других фазах остаются. Combat — стрельба из пистолета от входа, не нагрузочный бой четырёх игроков. Новые данные мира и кадры выстрела могут загружаться асинхронно."};
            if(createdPlayerFixture)
                report.limitations+=" Создан новый Windows Player fixture; его SHA-256 не объявляется совпадающим с прежним Editor baseline3. Последующие сравнения требуют этих же world.json/character.json и fixtureSha256.";
            if(scenario=="bodies") report.limitations+=" Bodies — смещение двух дальних записей host fixture каждые 3 секунды с dirty marks; это нагрузка snapshot body delta, не игровая проверка физики/владения.";
            if(lightRoute!=null) report.limitations+=" Light-windows: синтетические запросы окна без перемещения персонажа/камеры; FactoryLightFocus временно отключён. Шесть соседних окон и прямой возврат; не проверка camera lookahead. sampleFrames — предел, actualSampleFrames — фактический захват. Времена секций включительные; frame timestamps дают корреляцию, не атрибуцию всего кадра свету. RT — учтённые RenderTexture, не полная память драйвера. Нулевой GPU timing не доказывает 16,67 мс.";
            report.limitations+=" "+FrameHitchLog.SectionTimingScope;
            if(!report.allocationMetricAvailable) report.limitations+=" allocatedBytes/allocationsPerFrame недостоверны: счётчик сбросился; эти поля нельзя использовать. memory — GC.GetTotalMemory(false), не native/GPU память.";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(runtime.Options.Report)));
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(report,true));
            Debug.Log($"CPU seed104 {scenario}: success={success}, кадров={summary.frameCount}, work p95={summary.work.p95:F2}, p99={summary.work.p99:F2}, max={summary.work.max:F2} мс. {detail}");
            Application.Quit(success?0:1);
        }
        private void OnDestroy()
        {
            lightRoute?.Dispose();
            FrameHitchLog.EndCpuBaseline();
            if(runtime?.Session==null) return;
            WorldReplication.PayloadMeasured-=Payload;runtime.Session.AttackReceived-=Attack;
        }
        [Serializable] private sealed class PayloadSample {public string kind;public int bytes,frame;}
        [Serializable] private sealed class MemorySample {public double seconds;public long managedBytes;}
        [Serializable] private sealed class Report
        {
            public bool success,editor,batch,allocationMetricAvailable;public string detail,scenario,sourceRevision,assemblySha256,fixtureSha256,unity,processor,gpu,graphicsApi,level,limitations;
            public int width,height,vSync,targetFps,warmFrames,sampleFrames,factoryNpcs,containers,mines,hazards,shots,bodyChanges;
            public double seconds,captureStartedSeconds;public CpuBaselineSummary cpu;public PayloadSample[] payloads;
            public MemorySample[] memory;
            public int actualSampleFrames;public Seed104LightWindowRoute.Sample[] lightWindows;
            public Seed104LightWindowRoute.FrameSample[] lightWindowFrames;
        }
    }
}
