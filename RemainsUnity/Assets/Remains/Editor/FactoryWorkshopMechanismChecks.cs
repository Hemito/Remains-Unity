using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWorkshopMechanismChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();
            var catalog=MechanismCatalogAsset.Load();
            Require(catalog.Platforms.Length==1 && catalog.Buttons.Length==1,"Состав каталога механизмов");
            var platform=catalog.Platform(FactoryWorkshopMechanismAuthoring.PlatformTypeId);var type=platform.CreateDefinition();
            var button=catalog.Button(FactoryWorkshopMechanismAuthoring.ButtonTypeId);var control=button.CreateDefinition();
            Require(type.width==2.4f && type.height==.78f && type.durationSeconds==6 && type.graphicOffsetX==-1.2f &&
                type.graphicOffsetY==.1f && type.graphicWidth==2.4f && type.graphicHeight==.7f && !type.oneWay && !type.shotsPassThrough && type.surfaceTexture=="floor" && type.movementSound=="metal","Исходный размер/смещение/номинальный маршрут платформы");
            Require(control.repairDurationSeconds==FactoryWorkshopMechanismAuthoring.TemporaryRepairDuration &&
                control.interactionRadius==FactoryWorkshopMechanismAuthoring.TemporaryInteractionRadius,"Временные Unity-настройки ремонта должны быть явными");
            CheckArtwork(platform.Prefab,"platform",new Rect(-1.2f,.1f,2.4f,.7f),120,35);
            CheckArtwork(button.ClosedPrefab,"button-closed",new Rect(-.24f,0,.48f,.8f),24,40);
            CheckArtwork(button.OpenedPrefab,"button-open",new Rect(-.24f,0,.48f,.8f),192,320);
            var workshop=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path).GetComponent<RoomAuthoring>();
            var connected=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            CheckRoom(workshop,0);CheckRoom(connected,38.4f);
            var records=workshop.GetComponent<RoomImportAudit>().Report.Records;
            Require(records.Length==24 && records.Count(r=>!r.installed)==1 &&
                records.Where(r=>r.kind=="platform2" || r.kind=="knop3").All(r=>r.installed) &&
                records.Where(r=>!r.installed).All(r=>r.kind=="vendor"),"Журнал должен содержать ровно отложенного vendor");
            var main=RoomContentBatch.Read(connected,"factory:0");var six=RoomContentBatch.Read(connected,"factory:6");var minus=RoomContentBatch.Read(connected,"factory:-27");
            var world=new WorldState {mechanismVersion=1};
            RoomContentInstaller.Apply(world,main);RoomContentInstaller.Apply(world,six);RoomContentInstaller.Apply(world,minus);
            Require(world.movingPlatforms.Count==3 && world.mechanismButtons.Count==6 &&
                world.movingPlatforms.Select(p=>p.id).Concat(world.mechanismButtons.Select(b=>b.id)).Distinct().Count()==9,
                "Три отдельных factory instances не должны разделять IDs/состояние механизмов");
            var fingerprint=StateJson.ToJson(main);
            var changed=StateJson.Copy(main.mechanisms);changed.buttonDefinitions[0].interactionRadius+=.25f;
            Require(StateJson.ToJson(changed)!=StateJson.ToJson(main.mechanisms),"Радиус должен участвовать в gameplay manifest через RoomContentBatch");
            changed=StateJson.Copy(main.mechanisms);changed.platformDefinitions[0].durationSeconds+=1;
            Require(StateJson.ToJson(changed)!=StateJson.ToJson(main.mechanisms) && fingerprint.Contains("repairDurationSeconds"),"Physics/repair definition параметры входят в RoomContentBatch");
            CheckPreservation(main.mechanisms);
            Debug.Log("Механизмы цеха: PASS — три исходных ID, room-scoped d2, saved PNG/mesh/prefabs, отдельные0/6/-27, сохранение removed/repair/midsegment/mod data; vendor отложен.");
        }
        public static void CheckRoom(RoomAuthoring room,float offset)
        {
            var layout=RoomMechanismContent.Read(room,"factory:0");
            Require(layout!=null && layout.movingPlatforms.Length==1 && layout.mechanismButtons.Length==2,"Расстановка платформы/двух кнопок в "+room.name);
            var platform=layout.movingPlatforms.Single();
            Require(platform.id=="factory:0:"+FactoryWorkshopMechanismAuthoring.SourcePrefix+FactoryWorkshopMechanismAuthoring.PlatformCode &&
                platform.definition==FactoryWorkshopMechanismAuthoring.PlatformTypeId && platform.definitionVersion==1 &&
                platform.roomId==FactoryWorkshopMechanismAuthoring.RoomId && platform.groupId=="d2" &&
                Near(platform.startX,offset+10.8f) && Near(platform.startY,9.62f) && Near(platform.endX,offset+27.6f) && Near(platform.endY,9.62f) &&
                platform.endpoint==0 && platform.segment==null && !platform.removed,"Исходная регистрация/маршрут платформы в "+room.name);
            for(int index=0;index<2;index++)
            {
                var button=layout.mechanismButtons.Single(b=>b.id=="factory:0:"+FactoryWorkshopMechanismAuthoring.SourcePrefix+FactoryWorkshopMechanismAuthoring.ButtonCodes[index]);
                button.ValidateLink(platform);
                Require(Near(button.x,offset+(index==0?7.6f:29.2f)) && Near(button.y,11.22f) &&
                    button.definition==FactoryWorkshopMechanismAuthoring.ButtonTypeId && button.repairState==MechanismRepairStates.NeedsRepair && !button.pressed && !button.removed,
                    "Регистрация/начальное состояние кнопки "+button.id);
            }
            var platformMarkers=room.GetComponentsInChildren<RoomMovingPlatformPlacement>(true).Where(room.Included).ToArray();
            var buttonMarkers=room.GetComponentsInChildren<RoomMechanismButtonPlacement>(true).Where(room.Included).ToArray();
            Require(platformMarkers.Length==1 && buttonMarkers.Length==2 &&
                platformMarkers[0].GetComponentsInChildren<MeshRenderer>(true).Length==1 &&
                buttonMarkers.All(b=>b.GetComponentsInChildren<MeshRenderer>(true).Length==1),"Нет однократной сохранённой preview-графики; нельзя удваивать glow");
            Require(platformMarkers[0].GetComponentsInChildren<Collider2D>(true).Length==0 && buttonMarkers.All(b=>b.GetComponentsInChildren<Collider2D>(true).Length==0),
                "Коллизия не должна браться из preview-графики");
        }
        public static void CheckPreservation(MechanismLayout layout)
        {
            var world=new WorldState {mechanismVersion=1};Require(MechanismContentInstaller.Apply(world,layout),"Первичная установка механизмов");
            var platform=world.movingPlatforms.Single();
            platform.revision=7;platform.segment=new MovingPlatformSegment {sequence=7,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=10,durationSeconds=6};
            platform.extensions=Extension("mod:platform","{\"branches\":[1,2],\"note\":\"сохранить\"}");
            platform.segment.extensions=Extension("mod:segment","{\"phase\":\"исходная\"}");
            var button=world.mechanismButtons[0];button.repairState=MechanismRepairStates.Ready;button.pressed=true;button.removed=true;button.revision=9;
            button.extensions=Extension("mod:button","{\"nested\":{\"value\":17}}");
            string savedPlatform=StateJson.ToJson(platform),savedButton=StateJson.ToJson(button);
            world.mechanismButtons.RemoveAt(1);world=StateJson.Copy(world);string before=StateJson.ToJson(world);
            Require(!MechanismContentInstaller.Apply(world,layout) && StateJson.ToJson(world)==before,"Повторный вход не восстанавливает удалённую кнопку");
            Require(StateJson.ToJson(world.movingPlatforms.Single())==savedPlatform && StateJson.ToJson(world.mechanismButtons.Single())==savedButton,
                "Midsegment save/reload изменил ремонт, revision или неизвестные поля");
            var upgraded=StateJson.Copy(layout);upgraded.patch=layout.patch+":additive-v2";
            var extra=StateJson.Copy(layout.mechanismButtons[1]);extra.id=layout.level+":mod:new-button";
            upgraded.mechanismButtons=upgraded.mechanismButtons.Concat(new[]{extra}).ToArray();
            upgraded.expansion=new ContentExpansion {previousPatch=layout.patch,additions=new[]{extra.id}};
            Require(MechanismContentInstaller.Apply(world,upgraded) && world.mechanismButtons.Count==2 &&
                world.mechanismButtons.All(b=>b.id!=layout.mechanismButtons[1].id) &&
                StateJson.ToJson(world.movingPlatforms.Single())==savedPlatform && StateJson.ToJson(world.mechanismButtons.First())==savedButton,
                "Добавочная версия вернула пропавшую кнопку/обнулила ремонт или ход");
            world.movingPlatforms[0].removed=true;var removed=StateJson.ToJson(world.movingPlatforms[0]);world=StateJson.Copy(world);
            Require(!MechanismContentInstaller.Apply(world,upgraded) && StateJson.ToJson(world.movingPlatforms[0])==removed,"Removed платформа стала живой");
            var bad=StateJson.Copy(layout);bad.mechanismButtons[0].groupId="other-room-group";
            before=StateJson.ToJson(world);Reject(()=>MechanismContentInstaller.Apply(world,bad));Require(StateJson.ToJson(world)==before,"Ошибка связей частично меняет мир");
            var conflict=new WorldState {mechanismVersion=1};conflict.actors.Add(new ActorState {id=layout.movingPlatforms[0].id});
            before=StateJson.ToJson(conflict);Reject(()=>MechanismContentInstaller.Apply(conflict,layout));Require(StateJson.ToJson(conflict)==before,"Конфликт IDs частично меняет мир");
            var legacy=new WorldState();Reject(()=>MechanismContentInstaller.Apply(legacy,layout));Require(legacy.mechanismVersion==0,"Установщик тайно мигрирует world schema");
            var overflow=new WorldState {mechanismVersion=1,revision=long.MaxValue};before=StateJson.ToJson(overflow);
            Reject(()=>MechanismContentInstaller.Apply(overflow,layout));Require(StateJson.ToJson(overflow)==before,"Переполнение revision частично меняет мир");
        }
        private static IDictionary<string,JToken> Extension(string id,string json)=>new Dictionary<string,JToken>{{id,JToken.Parse(json)}};
        private static void CheckArtwork(GameObject prefab,string key,Rect expected,int width,int height)
        {
            Require(prefab!=null && AssetDatabase.Contains(prefab),"Нет cold saved prefab "+key);
            var renderers=prefab.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length==1 && prefab.GetComponentsInChildren<Collider2D>(true).Length==0,"Префаб должен содержать один bitmap renderer без физики: "+key);
            var renderer=renderers[0];var texture=renderer.sharedMaterial.mainTexture as Texture2D;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
            var origin=renderer.transform.localPosition;
            Require(texture!=null && texture.name==key && texture.width==width && texture.height==height && texture.wrapModeU==TextureWrapMode.Clamp &&
                texture.wrapModeV==TextureWrapMode.Clamp && AssetDatabase.Contains(mesh) && AssetDatabase.Contains(renderer.sharedMaterial) &&
                Near(origin.x,expected.x) && Near(origin.y,expected.y) && Near(mesh.bounds.min.x,0) && Near(mesh.bounds.min.y,0) &&
                Near(mesh.bounds.size.x,expected.width) && Near(mesh.bounds.size.y,expected.height) && mesh.uv.Length==4 &&
                mesh.uv[0]==Vector2.zero && mesh.uv[1]==Vector2.right && mesh.uv[2]==Vector2.up && mesh.uv[3]==Vector2.one,
                "Cold prefab/Clamp/UV/bounds не соответствуют исходнику: "+key);
        }
        private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.001f;
        private static void Reject(Action action) {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Неверная расстановка не отклонена");}
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Механизмы цеха: "+reason);}
    }
}
