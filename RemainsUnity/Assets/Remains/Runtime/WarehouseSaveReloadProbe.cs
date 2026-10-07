using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Domain;
using Remains.Content;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    // A new OS process loads a copied, complete ordinary save. This probe never
    // creates a world, relocates actors, disables enemies or edits saved content.
    [DefaultExecutionOrder(10000)]
    public sealed class WarehouseSaveReloadProbe : MonoBehaviour
    {
        private const string Level="factory:0",Prefix=Level+":original:plant:40:";
        private const string AdditionPrefix=Level+":original:plant:27:";
        private static readonly string[] PreviousPatches={Level+":base:factory-connected-46-18:physical-v8",Level+":base:factory-connected-46-18:doors-v5",Level+":base:factory-connected-46-18:npcs-v5"};
        private static readonly string[] CurrentPatches={Level+":base:factory-connected-46-18:physical-v9",Level+":base:factory-connected-46-18:doors-v6",Level+":base:factory-connected-46-18:npcs-v6"};
        [Serializable] private sealed class Expected {public int schema;public string world,snapshot,character;}
        [Serializable] private sealed class FrameEvidence
        {
            public string path,sha256,actor,clip,lightingRoom;
            public int nativeFrame,width,height,drawnFrame,drawnParts,visibleRenderers,cameraInstanceId,cameraMask,foregroundInstanceId,backgroundInstanceId;
            public int distinctColors,distinctColorLimit,differentPixels,rangeR,rangeG,rangeB,actorPixels,actorDistinctColors,actorDifferentPixels,actorRangeR,actorRangeG,actorRangeB;
            public long lightingRevision;
            public bool presentationReady,hudVisible,actorFullyFramed,pixelsVerified;
            public Vector3 cameraPosition,minimum,maximum;
            public float cameraSize;
            public Vector3[] viewport;
        }
        [Serializable] private sealed class Report
        {
            public bool success,selectedSnapshotVerified,sourceContentVerified,loadedContentVerified,bodyVerified;
            public bool sourceWasPrevious,additivePreviewVerified;
            public bool compatibilityCompared,compatibilityAcceptanceRequired,compatibilityAccepted,worldAbsentBeforeConsent,sourceImmutableVerified;
            public bool characterCompatibilityCompared,characterAcceptanceRequired,characterAccepted,characterAbsentBeforeConsent,characterSourcePreserved;
            public bool presentationVerified,frameVerified;
            public int presentationWaitFrames;
            public float presentationWaitSeconds;
            public string presentationWaitReason;
            public FrameEvidence frame;
            public string detail,world,snapshot,actor,owner,level;
            public string sourceArchive,sourceManifestSha256,currentManifestSha256,characterSourceSha256,characterSourceArchive,compatibilityPhase;
            public ContentDifference[] compatibilityRows,characterCompatibilityRows;
            public int compatibilityDifferenceCount,characterDifferenceCount;
            public int generation,props,containers,doors,npcs;
            public int sourceProps,sourceContainers,sourceDoors,sourceNpcs;
            public float sourceHealth,loadedHealth,finalHealth;
            public Vector2 sourcePosition,loadedPosition,finalPosition;
        }
        private PrototypeRuntime runtime;private Expected witness;private WorldState source,prepared;private ActorState expected;
        private Report report;private bool finished,captured;private float deadline,entered=-1;
        private RecoverySnapshot selectedSource;private string sourceManifest,characterSource;
        private ContentDifference[] worldRows,characterRows;private Button worldConfirm,characterConfirm;
        private Canvas canvas;private CombatHudView combatHud;private PonyVisual captureVisual;private MeshRenderer[] actorRenderers;
        private readonly Vector3[] viewportCorners=new Vector3[4];
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;
            if(value.Options.Role!="host" || value.Options.ProbePeers!=1 || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Загрузка склада требует копию сохранения и один скрытый хост.");
            string path=Path.Combine(value.Session.Saves.WorldDirectoryPath,"warehouse-reload-expected.json");
            witness=StateJson.FromJson<Expected>(File.ReadAllText(path));
            var selected=value.Session.Saves.SelectedSnapshot();source=value.Session.Saves.LoadWorld(false);selectedSource=selected;
            if(witness?.schema!=1 || selected==null || source==null || witness.character!=value.Options.Character ||
                selected.Id!=witness.snapshot || selected.WorldId!=witness.world || source.worldId!=witness.world)
                throw new InvalidOperationException("Загрузка склада выбрала другой исходный снимок или профиль; новый мир не создаётся.");
            expected=source.actors.Single(a=>a.player && a.character==witness.character);
            bool previous=SourceIsPrevious(source);prepared=PrepareExpected(source,previous);
            if(expected.level!=Level || expected.down || expected.health<=0)
                throw new InvalidOperationException("Исходная копия не содержит живого игрока в рейде или полный сохранённый склад.");
            report=new Report {world=witness.world,snapshot=witness.snapshot,actor=expected.id,
                selectedSnapshotVerified=true,sourceContentVerified=true,sourceWasPrevious=previous,additivePreviewVerified=true,
                sourceProps=source.props.Count(p=>p.level==Level),sourceContainers=source.containers.Count(c=>c.level==Level),
                sourceDoors=source.doors.Count(d=>d.level==Level),sourceNpcs=source.actors.Count(a=>!a.player && a.level==Level),
                sourceHealth=expected.health,sourcePosition=expected.Position};
            if(value.Hud?.NetworkLobby==null || value.Hud.CharacterContent==null)
                throw new InvalidOperationException("Склад reload: отсутствует штатный интерфейс сравнения контента.");
            canvas=value.Hud.GetComponent<Canvas>();combatHud=value.Hud.GetComponentInChildren<CombatHudView>(true);
            if(canvas==null || combatHud==null) throw new InvalidOperationException("Склад reload: отсутствует настоящий Canvas/HUD для кадра.");
            sourceManifest=StateJson.ToJson(source.weaponContent);
            var current=WeaponCatalog.ManifestCopy();
            report.sourceManifestSha256=Hash(sourceManifest);report.currentManifestSha256=Hash(StateJson.ToJson(current));
            report.compatibilityDifferenceCount=ContentComparison.Compare(source.weaponContent,current).Count;
            report.compatibilityAcceptanceRequired=report.compatibilityDifferenceCount>0;
            worldRows=ContentComparison.Compare(source.weaponContent,current,report.compatibilityAcceptanceRequired).ToArray();
            report.compatibilityRows=worldRows;
            report.sourceArchive=Path.Combine(value.Session.Saves.WorldDirectoryPath,"Worlds",selected.WorldId,"Snapshots",selected.Id+".json");
            VerifySource(true);
            characterSource=value.Session.Saves.ReadCharacterSource();
            var character=characterSource==null?null:StateJson.FromJson<ActorState>(characterSource);
            if(character==null || character.id!=expected.id || character.character!=witness.character)
                throw new InvalidOperationException("Склад reload: исходный профиль отсутствует или не совпадает с сохранённым телом.");
            report.characterSourceSha256=Hash(characterSource);
            report.characterDifferenceCount=ContentComparison.Compare(character.weaponContent,current).Count;
            report.characterAcceptanceRequired=report.characterDifferenceCount>0;
            characterRows=ContentComparison.Compare(character.weaponContent,current,report.characterAcceptanceRequired).ToArray();
            report.characterCompatibilityRows=characterRows;
            value.Session.ConfigureHostStartup(SessionStartupIntent.Continue(selected));
            value.CommandSource=()=>default;
            value.Session.Lobby.Changed+=LobbyChanged;
        }

        private IEnumerator Start()=>Drive(Run());
        private IEnumerator Run()
        {
            yield return ConfirmCompatibility();
            while(!finished)
            {
                if(entered>=0 && Time.unscaledTime-entered>=3 && FrameReady(out var bounds))
                {
                    // Observe a completed ordinary drawing before the explicit capture command.
                    yield return null;
                    if(finished) yield break;
                    if(FrameReady(out bounds)) {Capture(bounds);yield break;}
                }
                report.presentationWaitFrames++;
                report.presentationWaitSeconds=entered<0?0:Time.unscaledTime-entered;
                yield return null;
            }
        }
        private IEnumerator ConfirmCompatibility()
        {
            report.compatibilityPhase="character-comparison";
            if(report.characterAcceptanceRequired)
            {
                while(!finished && runtime.Hud.CharacterContent.ConfirmRect==null) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null || runtime.Session.LocalActor!=null || runtime.Session.Saves.ReadCharacterSource()!=characterSource ||
                    StateJson.ToJson(runtime.Session.CharacterContent.Differences)!=StateJson.ToJson(characterRows))
                    throw new InvalidOperationException("Склад reload: сравнение персонажа подменено или тело загружено до согласия.");
                report.characterCompatibilityCompared=true;report.characterAbsentBeforeConsent=true;
                VerifySource(true);
                characterConfirm=runtime.Hud.CharacterContent.ConfirmRect.GetComponent<Button>();
                if(characterConfirm==null) throw new InvalidOperationException("Склад reload: подтверждение персонажа не является штатной кнопкой.");
                characterConfirm.onClick.AddListener(CharacterConfirmed);
                yield return UiPointerProbe.Click(runtime.Hud.CharacterContent.ConfirmRect);
                if(!report.characterAccepted) throw new InvalidOperationException("Склад reload: штатная кнопка не подтвердила сравнение персонажа.");
            }
            else
            {
                while(!finished && !runtime.Session.Lobby.ClientWaiting && runtime.Session.ServerWorld==null) yield return null;
                if(finished) yield break;
                if(runtime.Session.CharacterContent.Differences.Count!=0 || runtime.Hud.CharacterContent.ConfirmRect!=null)
                    throw new InvalidOperationException("Склад reload: совпадающий профиль неожиданно требует подтверждения.");
                report.characterCompatibilityCompared=true;report.characterSourcePreserved=true;
            }
            report.compatibilityPhase="world-comparison";
            if(report.compatibilityAcceptanceRequired)
            {
                while(!finished && runtime.Hud.NetworkLobby.ContentConfirmRect==null) yield return null;
                if(finished) yield break;
                var lobby=runtime.Session.Lobby;
                if(!lobby.ClientContentPending || !lobby.Waiting || !lobby.Busy || runtime.Session.ServerWorld!=null || runtime.Session.LocalActor!=null ||
                    runtime.Hud.NetworkLobby.CopyPickerVisible || runtime.Hud.NetworkLobby.StartButtonRect!=null ||
                    StateJson.ToJson(lobby.ContentDifferences)!=StateJson.ToJson(worldRows))
                    throw new InvalidOperationException("Склад reload: штатное сравнение мира отсутствует/подменено или мир запущен до согласия.");
                VerifyStartup();VerifySource(true);
                report.compatibilityCompared=true;report.worldAbsentBeforeConsent=true;
                worldConfirm=runtime.Hud.NetworkLobby.ContentConfirmRect.GetComponent<Button>();
                if(worldConfirm==null) throw new InvalidOperationException("Склад reload: подтверждение мира не является штатной кнопкой.");
                worldConfirm.onClick.AddListener(WorldConfirmed);
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ContentConfirmRect);
                if(!report.compatibilityAccepted) throw new InvalidOperationException("Склад reload: штатная кнопка не подтвердила сравнение мира.");
            }
            else
            {
                while(!finished && runtime.Session.ServerWorld==null) yield return null;
                if(finished) yield break;
                if(runtime.Session.Lobby.ContentDifferences.Count!=0 || runtime.Hud.NetworkLobby.ContentConfirmRect!=null)
                    throw new InvalidOperationException("Склад reload: совпадающий мир неожиданно требует подтверждения.");
                report.compatibilityCompared=true;ValidateLoaded();
            }
            report.compatibilityPhase="loaded";
        }
        private void CharacterConfirmed()
        {
            if(finished) return;
            try
            {
                if(runtime.Session.CharacterContent.Differences.Count!=0)
                    throw new InvalidOperationException("Склад reload: штатное подтверждение не сняло сравнение персонажа.");
                // CharacterContentGate preserves the exact pre-consent source before preparing it.
                string recovery=Path.Combine(runtime.Session.Saves.CharacterDirectoryPath,"Recovery");
                report.characterSourceArchive=Directory.Exists(recovery)?Directory.GetFiles(recovery,"character-*.json")
                    .FirstOrDefault(path=>File.ReadAllText(path)==characterSource):null;
                if(report.characterSourceArchive==null)
                    throw new InvalidOperationException("Склад reload: штатная подготовка не сохранила исходный профиль с прежним манифестом.");
                report.characterSourcePreserved=true;report.characterAccepted=true;
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void WorldConfirmed()
        {
            if(finished) return;
            try
            {
                if(runtime.Session.Lobby.Waiting || runtime.Session.Lobby.Busy || runtime.Session.ServerWorld==null)
                    throw new InvalidOperationException("Склад reload: штатное подтверждение не запустило выбранный мир.");
                report.compatibilityAccepted=true;VerifySource(false);ValidateLoaded();
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void LobbyChanged()
        {
            if(finished || runtime.Session.ServerWorld==null || report.loadedContentVerified ||
                report.compatibilityAcceptanceRequired && !report.compatibilityAccepted) return;
            try {ValidateLoaded();}
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void VerifyStartup()
        {
            var startup=runtime.Session.Lobby.Startup;
            if(startup.Mode!=SessionStartupMode.ContinueSelected || startup.Selected?.Id!=selectedSource.Id || startup.Selected.Json!=selectedSource.Json)
                throw new InvalidOperationException("Склад reload: запуск потерял выбранную полную копию до подтверждения.");
        }
        private void VerifySource(bool selectedBeforeLoad)
        {
            if(selectedSource==null || selectedSource.Id!=witness.snapshot || selectedSource.WorldId!=witness.world ||
                StateJson.ToJson(source.weaponContent)!=sourceManifest || !File.Exists(report.sourceArchive) ||
                File.ReadAllText(report.sourceArchive)!=selectedSource.Json)
                throw new InvalidOperationException("Склад reload: исходный архив/манифест изменён; явное согласие не разрешает переписывать исходник.");
            if(selectedBeforeLoad)
            {
                var selected=runtime.Session.Saves.SelectedSnapshot();
                if(selected?.Id!=selectedSource.Id || selected.Json!=selectedSource.Json)
                    throw new InvalidOperationException("Склад reload: выбранная исходная копия изменена до загрузки.");
            }
        }
        private void ValidateLoaded()
        {
            if(report.loadedContentVerified) return;
            VerifyStartup();var loaded=runtime.Session.ServerWorld;
            File.WriteAllText(runtime.Options.Report+".loaded-start-world.json",StateJson.ToJson(loaded,true));
            // Lobby confirmation includes the ordinary Join. Derive its transport-only
            // reset from production ownership rules, rather than skipping those fields.
            var transport=StateJson.Copy(prepared);var ownership=new OwnershipService();ownership.BeginSession(transport);
            var players=loaded.actors.Where(a=>a.player && a.connected).ToArray();
            foreach(var player in players)
            {
                int index=transport.actors.FindIndex(a=>a.id==player.id);
                if(index<0) throw new InvalidOperationException("Склад reload: вход создал новое тело вместо сохранённого: "+player.id);
                transport.actors[index]=player.Copy();
            }
            ownership.Reassign(transport,players.Select(a=>a.owner).ToArray());
            if(loaded.worldId!=witness.world || !Counts(loaded,true) || !Preserved(transport,loaded) || !RoomContentProbePersistence.HasCompleteAuthoredContent(loaded,Level))
                throw new InvalidOperationException("Подготовка мира заменила сохранённое содержимое склада.");
            report.loadedContentVerified=true;
        }
        private IEnumerator Drive(IEnumerator routine)
        {
            var pending=new Stack<IEnumerator>();pending.Push(routine);
            try
            {
                while(!finished && pending.Count>0)
                {
                    object next=null;bool advanced=false;Exception failure=null;
                    try {advanced=pending.Peek().MoveNext();if(advanced) next=pending.Peek().Current;}
                    catch(Exception error) {failure=error;}
                    if(failure!=null) {Finish(false,failure.Message);yield break;}
                    if(!advanced) {(pending.Pop() as IDisposable)?.Dispose();continue;}
                    if(next is IEnumerator child) pending.Push(child);else yield return next;
                }
            }
            finally {while(pending.Count>0) (pending.Pop() as IDisposable)?.Dispose();}
        }
        private static string Hash(string json)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
        }
        private static bool Counts(WorldState world,bool current)=>world.props.Count(p=>p.level==Level)==(current?36:30) &&
            world.containers.Count(c=>c.level==Level)==(current?70:55) && world.doors.Count(d=>d.level==Level)==(current?24:19) &&
            world.actors.Count(a=>!a.player && a.level==Level)==(current?35:22) &&
            world.props.Count(p=>p.id.StartsWith(Prefix,StringComparison.Ordinal))==18 &&
            world.containers.Count(c=>c.id.StartsWith(Prefix,StringComparison.Ordinal))==19 &&
            world.doors.Count(d=>d.id.StartsWith(Prefix,StringComparison.Ordinal))==4 &&
            world.actors.Count(a=>a.id.StartsWith(Prefix,StringComparison.Ordinal))==9;
        private static bool SourceIsPrevious(WorldState world)
        {
            bool previous=PreviousPatches.All(world.contentPatches.Contains) && !CurrentPatches.Any(world.contentPatches.Contains) && Counts(world,false);
            bool current=CurrentPatches.All(world.contentPatches.Contains) && Counts(world,true);
            if(!previous && !current) throw new InvalidOperationException("Склад reload: исходник не является полной известной стадией v8/v5/v5 или v9/v6/v6.");
            return previous;
        }
        private static WorldState PrepareExpected(WorldState input,bool previous)
        {
            // The selected source remains immutable. Only the production installer decides additions.
            var batch=RoomContentBatch.Read(RoomCatalogAsset.Load().Resolve(WorldRooms.Definition(input,Level)),Level);
            if(batch.physical.patch!=CurrentPatches[0] || batch.doors.patch!=CurrentPatches[1] || batch.npcs.patch!=CurrentPatches[2])
                throw new InvalidOperationException("Склад reload: каталог не содержит согласованную текущую стадию27.");
            var copy=StateJson.Copy(input);var baseline=StateJson.Copy(input);string before=StateJson.ToJson(copy);bool changed=RoomContentInstaller.Apply(copy,batch);
            if(!Counts(copy,true) || changed!=previous || !previous && StateJson.ToJson(copy)!=before)
                throw new InvalidOperationException("Склад reload: production установка не дала ожидаемую добавочную стадию либо изменила current no-op.");
            void Verify<T>(System.Collections.Generic.IEnumerable<T> oldEntries,System.Collections.Generic.IEnumerable<T> newEntries,Func<T,string> id,int additions)
            {
                var old=oldEntries.ToArray();var next=newEntries.ToArray();
                foreach(var entry in old)
                    if(StateJson.ToJson(entry)!=StateJson.ToJson(next.Single(e=>id(e)==id(entry))))
                        throw new InvalidOperationException("Склад reload: preview изменил прежнее состояние/неизвестные поля: "+id(entry));
                var added=next.Where(e=>!old.Any(o=>id(o)==id(e))).ToArray();
                if(added.Length!=(previous?additions:0) || added.Any(e=>!id(e).StartsWith(AdditionPrefix,StringComparison.Ordinal)))
                    throw new InvalidOperationException("Склад reload: preview добавил/восстановил объект вне exact source27.");
            }
            Verify(baseline.props,copy.props,p=>p.id,6);Verify(baseline.containers,copy.containers,c=>c.id,15);
            Verify(baseline.doors,copy.doors,d=>d.id,5);Verify(baseline.actors,copy.actors,a=>a.id,13);
            var addedPatches=copy.contentPatches.Where(p=>!input.contentPatches.Contains(p)).ToArray();
            if(copy.revision!=input.revision+(previous?3:0) || !addedPatches.OrderBy(p=>p,StringComparer.Ordinal).SequenceEqual((previous?CurrentPatches:Array.Empty<string>()).OrderBy(p=>p,StringComparer.Ordinal)) ||
                !input.contentPatches.All(copy.contentPatches.Contains))
                throw new InvalidOperationException("Склад reload: preview изменил предшественников/ревизию вне трёх additions.");
            return copy;
        }
        private static bool Preserved(WorldState before,WorldState after)
        {
            foreach(var container in before.containers.Where(c=>c.level==Level))
                if(!PreservedProp(container,after.containers.Single(c=>c.id==container.id))) return false;
            foreach(var prop in before.props.Where(p=>p.level==Level))
                if(!PreservedProp(prop,after.props.Single(p=>p.id==prop.id))) return false;
            foreach(var door in before.doors.Where(d=>d.level==Level))
                if(StateJson.ToJson(door)!=StateJson.ToJson(after.doors.Single(d=>d.id==door.id))) return false;
            foreach(var npc in before.actors.Where(a=>!a.player && a.level==Level))
            {
                var loaded=after.Find(npc.id);
                if(loaded==null || loaded.actorType!=npc.actorType || loaded.anatomy!=npc.anatomy || loaded.health!=npc.health ||
                    loaded.mana!=npc.mana || loaded.lifecycleSequence!=npc.lifecycleSequence ||
                    StateJson.ToJson(loaded.inventory)!=StateJson.ToJson(npc.inventory) ||
                    StateJson.ToJson(loaded.injuries)!=StateJson.ToJson(npc.injuries)) return false;
            }
            return true;
        }
        private static bool PreservedProp<T>(T before,T after) where T:PropState
        {
            // 'before' includes the exact detached BeginSession + Reassign outcome.
            // All fields, including transport identity, inventory and extensions, agree.
            return StateJson.ToJson(before)==StateJson.ToJson(after);
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException($"Таймаут загрузки сохранённого склада: presentation={runtime.LevelPresentationReady}, preparing={runtime.IsLevelPreparing}, wait={report.presentationWaitReason}, cameraMask={runtime.WorldCamera?.cullingMask}, frames={report.presentationWaitFrames}.");
                var actor=runtime.Session.LocalActor;if(actor==null || !report.loadedContentVerified) return;
                if(entered<0)
                {
                    if(actor.id!=expected.id || actor.character!=expected.character || actor.level!=Level || actor.down ||
                        actor.health!=expected.health || Vector2.Distance(actor.Position,expected.Position)>.001f ||
                        StateJson.ToJson(actor.inventory)!=StateJson.ToJson(expected.inventory))
                        throw new InvalidOperationException($"Вход изменил живое тело: actor={actor.id}, level={actor.level}, HP={actor.health}/{expected.health}, position={actor.Position}/{expected.Position}.");
                    report.bodyVerified=true;report.owner=actor.owner;report.generation=actor.generation;
                    report.loadedHealth=actor.health;report.loadedPosition=actor.Position;entered=Time.unscaledTime;
                }
                if(actor.id!=report.actor || actor.owner!=report.owner || actor.generation!=report.generation ||
                    actor.level!=Level || actor.down || actor.health<=0)
                    throw new InvalidOperationException("После загрузки живого игрока сменили, лишили управления или отправили в хаб.");
                // Beyond the party-defeat cadence. Genuine enemy simulation remains active.
                if(Time.unscaledTime-entered>=5 && captured) Finish(true,"Новый процесс загрузил исходный рейд, сохранил живое тело и содержимое склада; NPC не отключались, возврата в хаб нет.");
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private bool FrameReady(out Bounds bounds)
        {
            bounds=default;
            var actor=runtime.Session.LocalActor;var camera=runtime.WorldCamera;var lighting=runtime.ActiveRoomLighting;
            if(actor==null || actor.level!=Level || !runtime.LevelPresentationReady || runtime.IsLevelPreparing || runtime.Hud.RoomPresentationPending)
            {report.presentationWaitReason="Ожидание штатной готовности локации";return false;}
            if(lighting==null || !lighting.IsReady || lighting.IsUpdating || lighting.AppliedOcclusionRevision!=runtime.Geometry.OcclusionRevision(Level))
            {report.presentationWaitReason="Ожидание актуальной опубликованной пары света";return false;}
            if(camera==null || !camera.enabled || !camera.gameObject.activeInHierarchy || camera.cullingMask==0)
            {report.presentationWaitReason="Ожидание видимой штатной камеры";return false;}
            if(!canvas.isActiveAndEnabled || !canvas.gameObject.activeInHierarchy || !combatHud.Vitals.activeInHierarchy ||
                !combatHud.Health.isActiveAndEnabled || combatHud.Health.sprite==null || !combatHud.HealthText.isActiveAndEnabled || string.IsNullOrEmpty(combatHud.HealthText.text))
            {report.presentationWaitReason="Ожидание настоящего HUD игрока";return false;}
            if(runtime.FindActorVisual(actor.id) is not PonyVisual view || !view.gameObject.activeInHierarchy || view.ActorId!=actor.id ||
                view.DrawnClip!=actor.pose || view.DrawnFrame<0 || view.DrawnPartCount<=0 || !view.TryGetDrawnBounds(out bounds))
            {report.presentationWaitReason="Ожидание завершённого рисунка сохранённого игрока";return false;}
            if(captureVisual!=view) {captureVisual=view;actorRenderers=view.GetComponentsInChildren<MeshRenderer>(true);}
            float z=bounds.center.z;var minimum=bounds.min;var maximum=bounds.max;
            viewportCorners[0]=camera.WorldToViewportPoint(new Vector3(minimum.x,minimum.y,z));
            viewportCorners[1]=camera.WorldToViewportPoint(new Vector3(maximum.x,minimum.y,z));
            viewportCorners[2]=camera.WorldToViewportPoint(new Vector3(maximum.x,maximum.y,z));
            viewportCorners[3]=camera.WorldToViewportPoint(new Vector3(minimum.x,maximum.y,z));
            foreach(var point in viewportCorners)
            {
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                    throw new InvalidOperationException("Склад reload: неконечные actual границы игрока в камере: "+actor.id);
                if(point.x<0 || point.x>1 || point.y<0 || point.y>1 || point.z<camera.nearClipPlane || point.z>camera.farClipPlane)
                {report.presentationWaitReason="Ожидание полного игрока в штатном кадре камеры";return false;}
            }
            if(VisibleRenderers(camera)==0)
            {report.presentationWaitReason="Ожидание actual renderers игрока в слоях камеры";return false;}
            report.presentationWaitReason="Готовы локация, свет, камера, игрок и HUD";return true;
        }
        private int VisibleRenderers(Camera camera)=>actorRenderers.Count(renderer=>renderer!=null && renderer.enabled && !renderer.forceRenderingOff &&
            renderer.gameObject.activeInHierarchy && (camera.cullingMask&(1<<renderer.gameObject.layer))!=0);
        private void Capture(Bounds bounds)
        {
            var lighting=runtime.ActiveRoomLighting;var camera=runtime.WorldCamera;var actor=runtime.Session.LocalActor;
            RoomLightingGpuEvidence.EnsurePublished(lighting,runtime.Geometry.Get(Level),runtime.Geometry.OcclusionRevision(Level),lighting.ForegroundMap,lighting.BackgroundMap,lighting.Bounds);
            var evidence=new FrameEvidence {path=runtime.Options.Report+".warehouse-reloaded.png",actor=actor.id,clip=captureVisual.DrawnClip,
                nativeFrame=Time.frameCount,drawnFrame=captureVisual.DrawnFrame,drawnParts=captureVisual.DrawnPartCount,visibleRenderers=VisibleRenderers(camera),
                cameraInstanceId=camera.GetInstanceID(),cameraMask=camera.cullingMask,cameraPosition=camera.transform.position,cameraSize=camera.orthographicSize,
                minimum=bounds.min,maximum=bounds.max,viewport=viewportCorners.ToArray(),lightingRoom=lighting.Room.id,lightingRevision=lighting.AppliedOcclusionRevision,
                foregroundInstanceId=lighting.ForegroundMap.GetInstanceID(),backgroundInstanceId=lighting.BackgroundMap.GetInstanceID(),
                presentationReady=runtime.LevelPresentationReady,hudVisible=true,actorFullyFramed=true,distinctColorLimit=64};
            report.frame=evidence;
            NetworkFrameCapture.Save(camera,evidence.path,canvas,1920,1080);
            byte[] png=File.ReadAllBytes(evidence.path);
            using(var sha=SHA256.Create()) evidence.sha256=BitConverter.ToString(sha.ComputeHash(png)).Replace("-","").ToLowerInvariant();
            var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!ImageConversion.LoadImage(decoded,png,false)) throw new InvalidOperationException("Склад reload: сохранённый actual PNG не декодируется.");
                evidence.width=decoded.width;evidence.height=decoded.height;
                if(decoded.width!=1920 || decoded.height!=1080) throw new InvalidOperationException("Склад reload: неверный actual размер кадра.");
                var pixels=decoded.GetPixels32();
                int left=Math.Clamp(Mathf.FloorToInt(viewportCorners.Min(v=>v.x)*decoded.width),0,decoded.width-1);
                int right=Math.Clamp(Mathf.CeilToInt(viewportCorners.Max(v=>v.x)*decoded.width),left+1,decoded.width);
                int bottom=Math.Clamp(Mathf.FloorToInt(viewportCorners.Min(v=>v.y)*decoded.height),0,decoded.height-1);
                int top=Math.Clamp(Mathf.CeilToInt(viewportCorners.Max(v=>v.y)*decoded.height),bottom+1,decoded.height);
                MeasurePixels(pixels,decoded.width,0,0,decoded.width,decoded.height,evidence.distinctColorLimit,
                    out _,out evidence.distinctColors,out evidence.differentPixels,out evidence.rangeR,out evidence.rangeG,out evidence.rangeB);
                MeasurePixels(pixels,decoded.width,left,bottom,right,top,evidence.distinctColorLimit,
                    out evidence.actorPixels,out evidence.actorDistinctColors,out evidence.actorDifferentPixels,out evidence.actorRangeR,out evidence.actorRangeG,out evidence.actorRangeB);
                if(evidence.distinctColors<8 || evidence.differentPixels<1000 || Math.Max(evidence.rangeR,Math.Max(evidence.rangeG,evidence.rangeB))<16 ||
                    evidence.actorPixels<16 || evidence.actorDistinctColors<4 || evidence.actorDifferentPixels<16 ||
                    Math.Max(evidence.actorRangeR,Math.Max(evidence.actorRangeG,evidence.actorRangeB))<8)
                    throw new InvalidOperationException($"Склад reload: actual кадр/область игрока пусты или одноцветны: colors={evidence.distinctColors}/{evidence.actorDistinctColors}, different={evidence.differentPixels}/{evidence.actorDifferentPixels}, ranges={evidence.rangeR},{evidence.rangeG},{evidence.rangeB}/{evidence.actorRangeR},{evidence.actorRangeG},{evidence.actorRangeB}.");
                evidence.pixelsVerified=true;report.presentationVerified=true;report.frameVerified=true;captured=true;
                report.presentationWaitSeconds=Time.unscaledTime-entered;
                File.WriteAllText(runtime.Options.Report+".warehouse-reloaded.frame.json",StateJson.ToJson(evidence,true));
            }
            finally {Destroy(decoded);}
        }
        private static void MeasurePixels(Color32[] pixels,int stride,int left,int bottom,int right,int top,int colorLimit,
            out int count,out int distinct,out int different,out int rangeR,out int rangeG,out int rangeB)
        {
            var colors=new HashSet<uint>();var first=pixels[bottom*stride+left];count=different=0;
            int minR=255,minG=255,minB=255,maxR=0,maxG=0,maxB=0;
            for(int y=bottom;y<top;y++) for(int x=left;x<right;x++)
            {
                var pixel=pixels[y*stride+x];count++;
                if(pixel.r!=first.r || pixel.g!=first.g || pixel.b!=first.b) different++;
                if(colors.Count<colorLimit) colors.Add((uint)(pixel.r | pixel.g<<8 | pixel.b<<16 | pixel.a<<24));
                minR=Math.Min(minR,pixel.r);minG=Math.Min(minG,pixel.g);minB=Math.Min(minB,pixel.b);
                maxR=Math.Max(maxR,pixel.r);maxG=Math.Max(maxG,pixel.g);maxB=Math.Max(maxB,pixel.b);
            }
            distinct=colors.Count;rangeR=maxR-minR;rangeG=maxG-minG;rangeB=maxB-minB;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            report??=new Report();
            if(success)
            {
                try
                {
                    VerifySource(false);report.sourceImmutableVerified=true;
                    if(!report.compatibilityCompared || report.compatibilityAccepted!=report.compatibilityAcceptanceRequired ||
                        !report.characterCompatibilityCompared || report.characterAccepted!=report.characterAcceptanceRequired ||
                        !report.characterSourcePreserved || report.compatibilityAcceptanceRequired && !report.worldAbsentBeforeConsent ||
                        report.characterAcceptanceRequired && !report.characterAbsentBeforeConsent)
                        throw new InvalidOperationException("Склад reload: отсутствует явное сравнение/согласие или исходная копия не сохранена.");
                    if(!report.presentationVerified || !report.frameVerified || report.frame?.pixelsVerified!=true)
                        throw new InvalidOperationException("Склад reload: отсутствует непустой actual кадр готовой локации/игрока/HUD.");
                }
                catch(Exception error) {success=false;detail=error.Message;}
            }
            finished=true;Detach();report.success=success;report.detail=detail;
            var actor=runtime.Session.LocalActor;var world=runtime.Session.World;
            report.level=actor?.level;report.finalHealth=actor?.health??0;report.finalPosition=actor?.Position??Vector2.zero;
            report.props=world?.props.Count(p=>p.level==Level)??0;report.containers=world?.containers.Count(c=>c.level==Level)??0;
            report.doors=world?.doors.Count(d=>d.level==Level)??0;report.npcs=world?.actors.Count(a=>!a.player && a.level==Level)??0;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));Debug.Log("Склад reload: "+success+", "+detail);
            StartCoroutine(Quit(success));
        }
        private void Detach()
        {
            if(runtime?.Session!=null) runtime.Session.Lobby.Changed-=LobbyChanged;
            if(worldConfirm!=null) worldConfirm.onClick.RemoveListener(WorldConfirmed);
            if(characterConfirm!=null) characterConfirm.onClick.RemoveListener(CharacterConfirmed);
        }
        private void OnDestroy()=>Detach();
        private IEnumerator Quit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
    }
}
