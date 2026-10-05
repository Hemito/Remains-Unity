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
    public static class FactoryBigBoxChecks
    {
        public static void Run()
        {
            PropCatalog.Load();var data=PropCatalog.Get("bigbox");
            Require(data.mass==175 && data.width==2 && data.height==1.6f && data.resource==FactoryBigBoxAuthoring.Resource,"Определение расходится с исследовательской спецификацией.");
            var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(FactoryBigBoxAuthoring.Folder+"/bigbox.asset");
            Require(asset!=null && asset.Prefab!=null && asset.Texture.width==100 && asset.Texture.height==80,"Потеряны префаб или исходное разрешение.");
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset.Texture));
            Require(importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None,"Импорт уменьшает или повторно сжимает исходник.");
            var view=new PropArtwork().CreateView("bigbox","Проверка большого ящика",data.width);
            try
            {
                var state=new PropState {id="bigbox-check",definition="bigbox",x=3,y=2,width=data.width,height=data.height,mass=data.mass,anchorHeight=0};
                view.DrawMotion(state,"local",false,0);var bounds=view.Artwork.bounds;
                Require(Mathf.Abs(bounds.min.y-state.y)<.001f && Mathf.Abs(bounds.center.x-state.x)<.001f &&
                    Mathf.Abs(bounds.size.x-state.width)<.001f && Mathf.Abs(bounds.size.y-state.height)<.001f,"Рисунок расходится с физическими границами.");
                Require(view.Order==ActorVisualLayers.RestingObjectOrder,"Лежащий ящик перекрывает персонажей.");
                var actor=new ActorState {level=state.level,Position=state.Position};
                Require(new MassLimitedTelekinesisRule(TelekinesisSettings.Load()).Rejection(actor,state,false)?.Contains("175")==true,"Начальный профиль не объясняет отказ из-за 175 кг.");
                state.held=true;view.DrawMotion(state,"local",false,0);
                Require(view.Order==ActorVisualLayers.WorldObjectOrder && view.HeldGlowVisible,"Удерживаемый ящик не переходит на передний слой.");
            }
            finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            var settings=JsonUtility.FromJson<PropContactAudio.Settings>(Resources.Load<TextAsset>("Presentation/prop-audio").text);
            var sound=settings.entries.Single(e=>e.definition=="bigbox");
            Require(sound.clip=="Original/Audio/crate-metal-fall" && Resources.Load<AudioClip>(sound.clip)?.samples>0,"Нет исходного банка падения металла.");
            var room=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);
            var layout=RoomPhysicalContent.Read(room,"factory:0");var boxes=layout.props.Where(p=>p.definition=="bigbox").OrderBy(p=>p.x).ToArray();
            Require(boxes.Length==2 && Mathf.Abs(boxes[0].x-52)<.001f && Mathf.Abs(boxes[1].x-55.2f)<.001f && boxes.All(p=>Mathf.Abs(p.y-.8f)<.001f),"Потеряны две исходные расстановки или смещение цеха.");
            foreach(var box in boxes)
            {
                var hull=new MovementHull {width=box.width,height=box.height};
                Require(ActorTerrainMotion.IsClear(room.CreateLayout(),hull,box.Position),"Ящик установлен внутри стены: "+box.id);
            }
            CheckUpgrade(layout);
            Debug.Log("Большие ящики завода: PASS — исходник 100×80 без сжатия, 175 кг, совпадение рисунка/физики, слои, звук, два ID и добавочное обновление старого мира без возврата удалённых объектов/лута.");
        }
        private static void CheckUpgrade(PhysicalLayout current)
        {
            Require(current.expansion!=null && current.expansion.additions.Length==2,"Не объявлено добавочное обновление.");
            var old=StateJson.Copy(current);old.patch=current.expansion.previousPatch;old.expansion=null;
            old.props=old.props.Where(p=>p.definition!="bigbox").ToArray();
            var world=new WorldState {worldId="bigbox-upgrade-check"};PhysicalContentInstaller.Apply(world,old);
            var removedProp=world.props[0].id;world.props.RemoveAt(0);
            var removedContainer=world.containers[0].id;world.containers.RemoveAt(0);
            var moved=world.props[0];moved.x+=3;moved.held=true;moved.owner="saved-owner";moved.generation=7;
            var emptied=world.containers[0];emptied.items.Clear();emptied.y+=2;
            string propsBefore=StateJson.ToJson(world.props),containersBefore=StateJson.ToJson(world.containers);
            world=StateJson.Copy(world);
            Require(PhysicalContentInstaller.Apply(world,current),"Новая расстановка не установлена.");
            Require(world.props.Count(p=>p.definition=="bigbox")==2 && !world.props.Any(p=>p.id==removedProp) && !world.containers.Any(c=>c.id==removedContainer),"Возвращён удалённый объект или потеряно дополнение.");
            Require(StateJson.ToJson(world.props.Where(p=>p.definition!="bigbox").ToList())==propsBefore && StateJson.ToJson(world.containers)==containersBefore,"Изменено прежнее положение, удержание или содержимое.");
            world=StateJson.Copy(world);string before=StateJson.ToJson(world);
            Require(!PhysicalContentInstaller.Apply(world,current) && StateJson.ToJson(world)==before,"Повторная загрузка изменила состояние.");
            var bad=StateJson.Copy(current);bad.patch+="-invalid";bad.expansion.additions=new[]{"missing-id"};bool rejected=false;
            try {PhysicalContentInstaller.Apply(world,bad);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(world)==before,"Ошибочная миграция частично изменила мир.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Большой ящик: "+reason);}
    }
}
