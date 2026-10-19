using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryToolStandChecks
    {
        public static void Run()
        {
            PropCatalog.Load();
            var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(FactoryToolStandAuthoring.Folder+"/tool-stand.asset");
            Require(asset!=null && asset.Prefab!=null,"Не сохранён ассет/префаб настенного контейнера.");
            var definition=asset.CreateDefinition();var sprite=asset.Prefab.Artwork.sprite;
            Require(definition.id=="instr2" && definition.mass==0 && !definition.movable && !definition.hasPhysicalBody &&
                definition.width==1.46f && definition.height==.98f && sprite!=null && sprite.texture.width==73 && sprite.texture.height==49 &&
                definition.interactionOffset==new Vector2(.01f,-.24f) &&
                sprite.pixelsPerUnit==50 && Vector2.Distance(sprite.pivot,new Vector2(36,12))<.0001f &&
                asset.Prefab.VisualAnchor==PropVisualAnchor.PlacementAnchor && !asset.Prefab.FitsStateWidth,"Изменена политика тела или исходная регистрация стенда.");
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite.texture));
            Require(importer.textureType==TextureImporterType.Sprite && importer.textureShape==TextureImporterShape.Texture2D &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None &&
                !importer.mipmapEnabled && !importer.alphaIsTransparency,"Импорт заменил исходный Sprite/размер/пиксели стенда.");
            foreach(var pair in new[]{(FactoryWorkshopDraft.Path,0f),(ConnectedFactoryAuthoring.Path,38.4f)})
            {
                var room=AssetDatabase.LoadAssetAtPath<GameObject>(pair.Item1).GetComponent<RoomAuthoring>();
                var layout=FactoryContentCheckFixtures.BeforeOffice(RoomPhysicalContent.Read(room,"factory:0"));var stand=layout.containers.Single(c=>c.definition=="instr2");
                Require(stand.id=="factory:0:"+FactoryToolStandAuthoring.SourceId && !stand.movable && !stand.hasPhysicalBody && stand.mass==0 &&
                    stand.anchorHeight==0 && Vector2.Distance(stand.Position,new Vector2(2.4f+pair.Item2,1.62f))<.0001f,"Стенд потерял исходную позицию/ID/политику при чтении комнаты.");
                Require(layout.containers.Length==(pair.Item2==0?7:15) && layout.expansion.earlierVersions.Length==1,"Потеряна расстановка или цепочка v1/v2→v3.");
                var record=room.GetComponentInChildren<RoomImportAudit>().Report.Records;
                Require(record.Length==24 && record.Single(r=>r.code==FactoryToolStandAuthoring.Code).installed &&
                    record.Where(r=>!r.installed).All(r=>r.kind=="vendor") && record.Count(r=>!r.installed)==1,"Исходный стенд не учтён в журнале или скрыта другая запись.");
                var world=new WorldState {worldId="tool-stand-check"};PhysicalContentInstaller.Apply(world,layout);
                var actual=world.containers.Single(c=>c.id==stand.id);InventorySource source=actual;
                Require(actual.items.Any(i=>i.definition=="pin") && actual.items.Any(i=>i.definition=="rep") && source.Mass==null &&
                    Near(source.Bounds,new Rect(stand.x-.72f,1.38f,1.46f,.98f)),"Нет общего начального содержимого или авторское смещение курсорных границ потеряно.");
                var view=new PropArtwork().CreateContainerView(actual);
                try
                {
                    view.DrawMotion(actual,null,false,0);var art=view.Artwork.bounds;
                    Require(Near(source.Bounds,new Rect(art.min.x,art.min.y,art.size.x,art.size.y)),"Общая область взаимодействия не покрывает весь рисунок стенда.");
                    Require(source.Bounds.Contains(new Vector2(art.center.x,art.min.y+.02f)),"Нижняя часть стенда не доступна курсору.");
                    Require(Vector2.Distance(new Vector2(art.min.x,art.min.y),new Vector2(stand.x-.72f,1.38f))<.0001f &&
                        Vector2.Distance(new Vector2(art.max.x,art.max.y),new Vector2(stand.x+.74f,2.36f))<.0001f,"Рисунок принял нижний pivot/физическое смещение вместо исходной регистрации.");
                    Require(view.Order==ActorVisualLayers.RestingObjectOrder,"Закреплённый стенд перекрывает слой актёров.");
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
                var restored=StateJson.Copy(world);restored.containers.Single(c=>c.id==stand.id).items.Clear();
                InventorySource savedStand=restored.containers.Single(c=>c.id==stand.id);
                Require(Near(savedStand.Bounds,source.Bounds),"Сохранённый стенд потерял общую область взаимодействия.");
                string before=StateJson.ToJson(restored);
                Require(!PhysicalContentInstaller.Apply(restored,layout) && StateJson.ToJson(restored)==before,"Повторный вход пополнил пустой стенд.");
                CheckExpansion(world,layout,false);CheckExpansion(world,layout,true);
            }
            var icons=new InventoryArtwork();foreach(string id in new[]{"pin","rep","metal_comp"}) Require(icons.Require(id)!=null,"Нет исходной иконки компонента: "+id);
            Debug.Log("Стенд цеха: PASS — исходный PNG/registration, авторская область взаимодействия покрывает весь рисунок и сохраняется после загрузки, общий лут, неподвижность/отсутствие физического тела, v1/v2→v3, удалённые старые объекты и пустое сохранение без пополнения; vendor отложен.");
        }
        private static void CheckExpansion(WorldState installed,PhysicalLayout layout,bool earliest)
        {
            var world=StateJson.Copy(installed);world.contentPatches.Remove(layout.patch);
            world.contentPatches.Add(earliest?layout.expansion.earlierVersions[0].previousPatch:layout.expansion.previousPatch);
            world.containers.RemoveAll(c=>c.definition=="instr2");
            var deleted=world.containers.First().id;world.containers.RemoveAll(c=>c.id==deleted);
            foreach(var container in world.containers) container.items.Clear();
            var ids=world.containers.Select(c=>c.id).ToArray();
            if(earliest) world.props.RemoveAll(p=>p.definition=="bigbox");
            PhysicalContentInstaller.Apply(world,layout);
            Require(!world.containers.Any(c=>c.id==deleted) && ids.All(id=>world.containers.Single(c=>c.id==id).items.Count==0) &&
                world.containers.Count(c=>c.definition=="instr2")==1 && world.props.Count(p=>p.definition=="bigbox")==2,
                "Добавление стенда возродило удалённый старый объект/лут или потеряло bigbox-дополнение.");
        }
        private static bool Near(Rect a,Rect b)=>Mathf.Abs(a.x-b.x)<.0001f && Mathf.Abs(a.y-b.y)<.0001f &&
            Mathf.Abs(a.width-b.width)<.0001f && Mathf.Abs(a.height-b.height)<.0001f;
        private static void Require(bool condition,string detail) {if(!condition) throw new InvalidOperationException("Стенд цеха: "+detail);}
    }
}
