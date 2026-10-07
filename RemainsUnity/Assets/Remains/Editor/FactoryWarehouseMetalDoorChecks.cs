using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWarehouseMetalDoorChecks
    {
        public static void Run()
        {
            string legacy=LegacyBytes();var definition=FactoryWarehouseMetalDoorAuthoring.Verify();
            Require(FactoryWarehouseMetalDoorAuthoring.Prepare()==definition && LegacyBytes()==legacy,
                "Повторная подготовка изменила legacy door2 или создала другой профиль.");
            ActorTypes.Load();Anatomy.Load();
            int placements=CheckRegistration(definition);
            CheckBarrierAndPresentation(definition);
            CheckMissingResources();
            CheckInterruptedJournal();
            Debug.Log($"Металлическая дверь склада: PASS — {placements} source anchors, native close/open/die40×80/50PPU, неизменная legacy door2, строгий manifest; shared E/занятый объём/HP/dedup/reload, sweep/shot/NPC barrier, 3 однократных sound transitions и silent initial reload. Реальные аудиоголоса и ordinary route проверяются отдельной runtime пробой; threshold100/замки/original runtime не подтверждены.");
        }
        private static int CheckRegistration(DoorAsset definition)
        {
            var roomObject=new GameObject("Регистрация металлической двери");
            try
            {
                var room=roomObject.AddComponent<RoomAuthoring>();room.Configure(new RoomLayout {id="warehouse-metal-registration",left=0,right=153.6f,top=20});
                int count=0;
                foreach(var source in new[]{(record:64,x:30,y:15,code:"e3o3yL9JmJjPLZ1t"),(record:71,x:36,y:23,code:"rd8DsY9aYG3qkDra")})
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseMetalDoorAuthoring.PrefabPath);
                    var marker=UnityEngine.Object.Instantiate(prefab,roomObject.transform);
                    try
                    {
                        Vector2 anchor=FactorySourceCoordinates.Placement(source.x,source.y,1)+Vector2.right*115.2f;
                        marker.transform.localPosition=anchor;
                        string id="factory:0:original:plant:40:"+source.code;
                        var authoring=marker.AddComponent<RoomDoorAuthoring>();authoring.Configure(id,false,FactoryWarehouseMetalDoorAuthoring.SurfaceTexture);
                        var state=authoring.Read(room,"factory:0");var view=marker.GetComponent<DoorView>();
                        Require(state.id==id && state.definition==FactoryWarehouseMetalDoorAuthoring.DefinitionId && !state.open && state.CanInteract && state.Blocks &&
                            state.integrity?.health==1000 && state.integrity.maxHealth==1000 && state.integrity.resolvedAttacks.Count==0 && state.revision==0 &&
                            state.Bounds==state.InteractionBounds && Near(state.Bounds.xMin,anchor.x-.4f) && Near(state.Bounds.yMin,anchor.y) &&
                            Near(state.Bounds.width,.8f) && Near(state.Bounds.height,1.6f),"Read изменил source registration/capability: record="+source.record);
                        foreach(var sprite in new[]{definition.Closed,definition.Opened,definition.Destroyed})
                        {
                            view.Artwork.sprite=sprite;var actual=view.Artwork.bounds;
                            // At x139.6, binary32 (anchor+.4) and ((anchor-.4)+.8) differ by one ULP.
                            // Validate each source independently: Read's lower-left rect above, actual native
                            // vertices here, then the renderer's centre/XY size at that same bottom-centre anchor.
                            var points=sprite.vertices;var low=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
                            var high=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
                            foreach(var point in points) {low=Vector2.Min(low,point);high=Vector2.Max(high,point);}
                            var expectedCenter=marker.transform.TransformPoint(new Vector3(0,definition.Size.y*.5f,0));
                            Require(points.Length==4 && sprite.pivot==new Vector2(20,0) && sprite.pixelsPerUnit==50 &&
                                Near(low.x,-definition.Size.x*.5f) && Near(high.x,definition.Size.x*.5f) && Near(low.y,0) && Near(high.y,definition.Size.y) &&
                                points.Distinct().Count()==4 && points.All(p=>(Near(p.x,-definition.Size.x*.5f)||Near(p.x,definition.Size.x*.5f)) && (Near(p.y,0)||Near(p.y,definition.Size.y))) &&
                                Near(actual.center.x,expectedCenter.x) && Near(actual.center.y,expectedCenter.y) && Near(actual.center.z,expectedCenter.z) &&
                                Near(actual.size.x,definition.Size.x) && Near(actual.size.y,definition.Size.y),
                                $"Native рисунок не соответствует одной bottom-centre регистрации: record={source.record}, sprite={sprite.name}, "+
                                $"anchor={anchor.ToString("R")}, verticesMin={low.ToString("R")}, verticesMax={high.ToString("R")}, "+
                                $"actualCenter={actual.center.ToString("R")}, expectedCenter={expectedCenter.ToString("R")}, actualSize={actual.size.ToString("R")}, "+
                                $"physicalMin={state.Bounds.min.ToString("R")}, physicalMax={state.Bounds.max.ToString("R")}, "+
                                $"edgeDelta={new Vector4(actual.min.x-state.Bounds.xMin,actual.min.y-state.Bounds.yMin,actual.max.x-state.Bounds.xMax,actual.max.y-state.Bounds.yMax).ToString("R")}.");
                        }
                        count++;
                    }
                    finally {UnityEngine.Object.DestroyImmediate(marker);}
                }
                return count;
            }
            finally {UnityEngine.Object.DestroyImmediate(roomObject);}
        }
        private static void CheckBarrierAndPresentation(DoorAsset definition)
        {
            var root=new GameObject("Переходы металлической двери");
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseMetalDoorAuthoring.PrefabPath).GetComponent<DoorView>();
                var visuals=root.AddComponent<DoorVisuals>();visuals.SetOrders(new WorldVisualOrderTable());visuals.RegisterPrefab(prefab);
                var door=new DoorState {id="metal-check",definition=definition.Id,level="factory:0",surfaceTexture=FactoryWarehouseMetalDoorAuthoring.SurfaceTexture,
                    x=0,y=0,width=definition.Size.x,height=definition.Size.y,interactive=definition.Interactive,
                    integrity=new DoorIntegrity {maxHealth=definition.MaxHealth,health=definition.MaxHealth},
                    interaction=new DoorInteractionVolume {width=definition.InteractionSize.x,height=definition.InteractionSize.y}};
                var world=new WorldState();world.doors.Add(door);
                var actor=new ActorState {id="metal-door-user",player=true,connected=true,level=door.level,x=-.6f,y=0};
                var npc=new ActorState {id="metal-door-npc",actorType="raider",anatomy="raider",owner="fixture-owner",level=door.level,x=-.6f,y=0};
                var floor=new RoomSurface {x=-3,y=-.2f,width=7,height=.2f,texture="floor"};
                var room=new RoomLayout {id="metal-door-barrier",left=-3,right=4,bottom=-.2f,top=4,ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()};
                void RefreshGeometry()=>room.platforms=DoorGeometry.Compose(new[]{floor},world.doors,door.level);
                void Draw()=>visuals.Reconcile(world,door.level);
                RefreshGeometry();Draw();var actual=visuals.Find(door.id);
                var position=actual.transform.position;var scale=actual.transform.localScale;
                Require(actual.Artwork.sprite==definition.Closed && visuals.SoundChanges==0,"Закрытая initial load изменила рисунок или проиграла sound transition.");
                var hull=new MovementHull {width=.8f,height=1.4f};
                var start=new Vector2(-.6f,0);var across=new Vector2(2.4f,0);
                Require(ActorTerrainMotion.Supported(room,hull,start) && ActorTerrainMotion.Move(room,hull,start,across,false).x<0 &&
                    ShotObstacles.FirstContact(room,new Vector2(-1,.7f),new Vector2(2,.7f),out var contact).HasValue && contact.sourceDoorId==door.id &&
                    NpcDoorIntent.Select(npc,new Vector2(3,0),world.doors,room)==door,"Закрытая actual-size дверь не блокирует sweep/shot либо AI потерял stable contact ID.");
                var access=new NearbyDoorAccess();
                Require(DoorOperations.SetOpen(door,actor,door.revision,true,access,false,out _) == DoorChange.Changed,"Обычный доступ не открыл незапертую interactive дверь.");
                RefreshGeometry();Draw();
                Require(actual.Artwork.sprite==definition.Opened && visuals.SoundChanges==1 &&
                    ActorTerrainMotion.Move(room,hull,start,across,false).x>1 && !ShotObstacles.FirstContact(room,new Vector2(-1,.7f),new Vector2(2,.7f)).HasValue &&
                    NpcDoorIntent.Select(npc,new Vector2(3,0),world.doors,room)==null,"Открытая дверь не освободила барьер/AI или не дала один sound transition.");
                Draw();visuals.Reconcile(world.Copy(),door.level);
                Require(visuals.SoundChanges==1,"Повторный event/снимок повторил звук открытия.");
                string opened=StateJson.ToJson(door);
                Require(DoorOperations.SetOpen(door,actor,door.revision,false,access,true,out _) == DoorChange.Rejected && StateJson.ToJson(door)==opened,
                    "Отказ занятого проёма изменил дверь.");
                Require(DoorOperations.SetOpen(door,actor,door.revision,false,access,false,out _) == DoorChange.Changed,"Свободная дверь не закрылась.");
                RefreshGeometry();Draw();Require(actual.Artwork.sprite==definition.Closed && visuals.SoundChanges==2,"Закрытие не восстановило один closed transition.");
                Require(DoorDamageOperations.Apply(door,"metal-hit",100,out _) == DoorChange.Changed && door.integrity.health==900 && door.Blocks,"Нелетальный удар не оставил настоящий барьер.");
                Draw();Require(actual.Artwork.sprite==definition.Closed && visuals.SoundChanges==2,"HP без разрушения повторил sound/frame.");
                string damaged=StateJson.ToJson(door);
                Require(DoorDamageOperations.Apply(door,"metal-hit",100,out _) == DoorChange.Unchanged && StateJson.ToJson(door)==damaged,"Один attackID списал HP повторно.");
                Require(DoorDamageOperations.Apply(door,"metal-break",900,out _) == DoorChange.Changed && door.Destroyed && !door.Blocks && !door.CanInteract,"Разрушение не освободило capability/barrier.");
                RefreshGeometry();Draw();
                Require(actual.Artwork.sprite==definition.Destroyed && visuals.SoundChanges==3 && actual.transform.position==position && actual.transform.localScale==scale &&
                    ActorTerrainMotion.Move(room,hull,start,across,false).x>1 && !ShotObstacles.FirstContact(room,new Vector2(-1,.7f),new Vector2(2,.7f)).HasValue &&
                    NpcDoorIntent.Select(npc,new Vector2(3,0),world.doors,room)==null,"Destroyed рисунок/привязка/geometry/AI расходятся.");
                string destroyed=StateJson.ToJson(door);var restored=StateJson.Copy(door);
                Require(StateJson.ToJson(restored)==destroyed && DoorDamageOperations.Apply(restored,"metal-break",900,out _) == DoorChange.Unchanged &&
                    DoorDamageOperations.Apply(restored,"after-break",100,out _) == DoorChange.Rejected && StateJson.ToJson(restored)==destroyed &&
                    DoorOperations.SetOpen(restored,actor,restored.revision,true,access,false,out _) == DoorChange.Rejected,"Reload/dedup восстановили дверь или допустили новое действие после разрушения.");
                Draw();visuals.Reconcile(world.Copy(),door.level);Require(visuals.SoundChanges==3,"Повтор destroyed snapshot повторил звук разрушения.");
                visuals.Reconcile(world,"hub");visuals.Reconcile(world,door.level);
                Require(visuals.Find(door.id).Artwork.sprite==definition.Destroyed && visuals.SoundChanges==3,"Возврат в уровень проиграл initial destroy sound или восстановил closed рисунок.");
                var noninteractive=StateJson.Copy(door);noninteractive.integrity=new DoorIntegrity {health=1000,maxHealth=1000};noninteractive.interactive=false;
                room.platforms=DoorGeometry.Compose(new[]{floor},new[]{noninteractive},door.level);
                Require(NpcDoorIntent.Select(npc,new Vector2(3,0),new[]{noninteractive},room)==null,"AI обошёл CanInteract=false.");
                var loadedWorld=new WorldState();loadedWorld.doors.Add(restored);
                var initial=StateJson.Copy(restored);initial.integrity=new DoorIntegrity {health=1000,maxHealth=1000};initial.revision=0;initial.open=false;
                var content=new DoorLayout {level=door.level,patch="metal-check:doors-v1",doors=new[]{initial}};
                DoorContentInstaller.Apply(loadedWorld,content);string kept=StateJson.ToJson(loadedWorld.doors[0]);
                Require(kept==destroyed && !DoorContentInstaller.Apply(loadedWorld,content) && StateJson.ToJson(loadedWorld.doors[0])==destroyed,
                    "Повторный installer пополнил прочность или потерял receipts разрушенной двери.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckMissingResources()
        {
            var manifest=FactoryWarehouseMetalDoorAuthoring.ReadManifest();var invalid=StateJson.Copy(manifest);
            invalid.states[0].sha256=new string('0',64);bool rejected=false;
            try {FactoryWarehouseMetalDoorAuthoring.NativeSprite(invalid,invalid.states[0].label);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Изменённый SHA256 ресурса принят как тот же исходный рисунок.");
        }
        private static void CheckInterruptedJournal()
        {
            var room=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>();
            var source=room.GetComponent<RoomImportAudit>().Report.Records;string before=StateJson.ToJson(source);
            var ids=FactoryWarehouseMetalDoorsPlacement.Records.Select(r=>r.RecordId).ToArray();
            foreach(bool installed in new[]{false,true})
            {
                var projected=FactoryWarehouseMetalDoorsPlacement.RepairDecisions(source,installed);
                Require(StateJson.ToJson(source)==before && StateJson.ToJson(projected.Where(r=>!ids.Contains(r.recordId)).ToArray())==
                    StateJson.ToJson(source.Where(r=>!ids.Contains(r.recordId)).ToArray()),"Восстановление журнала изменило исходник либо одну из остальных55records.");
                foreach(var id in ids)
                {
                    var prior=source.Single(r=>r.recordId==id);var actual=projected.Single(r=>r.recordId==id);
                    Require(actual.installed==installed && actual.code==prior.code && actual.kind==prior.kind && actual.sourceData==prior.sourceData &&
                        actual.sourceX==prior.sourceX && actual.sourceY==prior.sourceY && !string.IsNullOrWhiteSpace(actual.reason),"Восстановление flags изменило source payload: "+id);
                }
            }
            void Reject(RoomSourceRecord[] data,string context)
            {bool failed=false;try {FactoryWarehouseMetalDoorsPlacement.RepairDecisions(data,true);} catch(InvalidOperationException) {failed=true;}Require(failed,"Журнал молча исправил "+context);}
            Reject(source.Skip(1).ToArray(),"неполный source пакет");
            var mixed=StateJson.Copy(source);mixed.Single(r=>r.recordId==ids[0]).installed=false;mixed.Single(r=>r.recordId==ids[1]).installed=true;
            Reject(mixed,"смешанные target flags");
            var wrong=StateJson.Copy(source);wrong.Single(r=>r.recordId==ids[0]).kind="door1";Reject(wrong,"чужой target type");
            wrong=StateJson.Copy(source);wrong.Single(r=>r.recordId==ids[0]).recordId="unknown-record";Reject(wrong,"подменённый target ID при57records");
        }
        private static string LegacyBytes()
        {
            using var sha=SHA256.Create();string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",FactoryWarehouseMetalDoorAuthoring.Folder));
            return string.Join("|",new[]{"door2.asset","door2.prefab","door2.mat","door2-closed.asset","door2-open.asset"}.SelectMany(p=>new[]{p,p+".meta"})
                .Select(p=>p+":"+Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(Path.Combine(folder,p))))));
        }
        private static bool Near(float a,float b)=>Mathf.Abs(a-b)<=.00001f;
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Проверка металлической двери склада: "+reason);}
    }
}
