using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class DroppedItemShapeChecks
    {
        public static void Run()
        {
            CheckSynthetic(false);CheckSynthetic(true);CheckGeometryWithoutArtworkLoad();CheckImportedArtworkPaths();CheckCreationAndMigration();CheckUnsupportedDrop();
            Debug.Log("Формы брошенных предметов: PASS — плоские/высокие изображения, прозрачные поля, pivot, отражение/повороты, опора и сохранённые тела.");
        }
        private static void CheckImportedArtworkPaths()
        {
            var catalog=GameAssets.Load<DroppedItemShapeCatalog>(DroppedItemShapeCatalog.Resource);
            var checkedKeys=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(var shape in catalog.CreateLookup().Values)
            {
                var key=shape.Reference?.Key;
                if(string.IsNullOrEmpty(key) || !checkedKeys.Add(key)) continue;
                var source=GameAssets.Load<Sprite>(key);
                Require(source!=null,"Нет Editor PNG для формы "+shape.id+", key="+key);
                Require(UnityEditor.AssetDatabase.GetAssetPath(source)==CatalogArtBuildSplit.SpritePath(key),
                    "Форма загружена из устаревшего расположения: "+shape.id+", key="+key);
                // Moved lazy sprites are canonical, not cropped adapters. Re-authoring must not change their pivot.
                var reference=CatalogSpriteAuthoring.Reference(source);
                Require(reference.Key==key && reference.Resolve()==source && reference.Bounds==source.bounds,
                    "Авторинг принял перемещённый спрайт за отдельную обрезку: "+shape.id+", key="+key);
            }
            Require(checkedKeys.Count>0,"Нет форм по ключам для проверки разделения Editor/player.");
        }
        private static void CheckGeometryWithoutArtworkLoad()
        {
            var texture=new Texture2D(20,10,TextureFormat.RGBA32,false);
            var sprite=Sprite.Create(texture,new Rect(0,0,20,10),new Vector2(.2f,.7f),10,0,SpriteMeshType.FullRect);
            var catalog=ScriptableObject.CreateInstance<DroppedItemShapeCatalog>();
            try
            {
                var direct=new DroppedItemShape {id="direct-geometry",sprite=sprite,opaqueBounds=new Rect(-.1f,-.2f,.8f,.3f)};
                var keyed=new DroppedItemShape {id="keyed-geometry",opaqueBounds=direct.opaqueBounds};
                // Deliberately absent: a physics lookup must not load or rasterize any artwork.
                keyed.ConfigureReference(CatalogSpriteReference.FromSprite(sprite,"Imported/Art/0000000000000000000000000000000000000000000000000000000000000000"));
                catalog.Configure(new[]{keyed});
                Require(catalog.CreateLookup()[keyed.id]==keyed,"Поиск формы требует загруженного изображения.");
                foreach(float angle in new[]{-135f,0,45,90,180})foreach(bool mirrored in new[]{false,true})
                    Require(keyed.WorldBounds(angle,mirrored)==direct.WorldBounds(angle,mirrored),"Ключ графики изменил подготовленную физическую форму.");
            }
            finally
            {UnityEngine.Object.DestroyImmediate(catalog);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static void CheckSynthetic(bool tall)
        {
            var texture=new Texture2D(20,20,TextureFormat.RGBA32,false);var pixels=new Color32[400];
            int minX=tall?8:2,maxX=tall?10:18,minY=tall?2:8,maxY=tall?18:10;
            for(int y=minY;y<maxY;y++)for(int x=minX;x<maxX;x++)pixels[y*20+x]=new Color32(255,255,255,1);
            texture.SetPixels32(pixels);texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,20,20),new Vector2(.2f,.7f),10,0,SpriteMeshType.FullRect);
            var bounds=SpriteOpaqueBoundsBaker.Measure(pixels,20,sprite.rect,sprite.pivot,sprite.pixelsPerUnit);
            Require(Mathf.Abs(bounds.width-(maxX-minX)/10f)<.00001f && Mathf.Abs(bounds.height-(maxY-minY)/10f)<.00001f,"Прозрачные поля вошли в физическое тело.");
            var shape=new DroppedItemShape {id=tall?"tall":"flat",sprite=sprite,opaqueBounds=bounds};shape.Validate();
            var root=new GameObject("Форма предмета");var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;
            var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigureDroppedArtwork(shape);
            try
            {
                foreach(float angle in new[]{-135f,0,45,90,180})foreach(bool mirrored in new[]{false,true})
                {
                    var box=shape.WorldBounds(angle,mirrored);
                    var body=new ContainerState {id="shape",definition="loose-item",presentationItem="shape",x=2,y=box.height*.5f,
                        width=box.width,height=box.height,anchorHeight=box.height*.5f,presentationAngle=angle,presentationMirrored=mirrored};
                    view.DrawMotion(body,null,false,0);
                    var drawn=OpaqueWorldBounds(renderer,bounds);
                    Require(Mathf.Abs(drawn.yMin)<.00001f && Mathf.Abs(drawn.xMin-(body.x-body.width*.5f))<.00001f &&
                        Mathf.Abs(drawn.width-body.width)<.00001f && Mathf.Abs(drawn.height-body.height)<.00001f,
                        $"Физика и непрозрачное изображение расходятся: tall={tall}, angle={angle}, mirror={mirrored}, bounds={drawn}.");
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static Rect OpaqueWorldBounds(SpriteRenderer renderer,Rect opaque)
        {
            var low=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var high=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<4;i++)
            {
                var point=new Vector3(i%2==0?opaque.xMin:opaque.xMax,i<2?opaque.yMin:opaque.yMax,0);
                if(renderer.flipY)point.y=-point.y;
                var world=(Vector2)renderer.transform.TransformPoint(point);low=Vector2.Min(low,world);high=Vector2.Max(high,world);
            }
            return Rect.MinMaxRect(low.x,low.y,high.x,high.y);
        }
        private static void CheckCreationAndMigration()
        {
            var actor=new ActorState {id="thrower",owner="owner",level="shape-check"};
            var artwork=new PropArtwork();
            foreach(string item in new[]{"p10","caps"})foreach(float angle in new[]{0f,45,90})foreach(bool mirrored in new[]{false,true})
            {
                var shape=DroppedItemShapeCatalog.Require(item);var origin=new Vector2(2,3);
                var body=DroppedItemTransfer.Preview(actor,"shape-check",item,origin,new Vector2(1,-2),angle,mirrored);
                var view=artwork.CreateContainerView(body);
                try
                {
                    view.DrawMotion(body,"owner",false,0);
                    Require(Vector2.Distance(view.transform.position,origin)<.00001f,"При броске изменился исходный pivot изображения.");
                    var drawn=OpaqueWorldBounds(view.Artwork,shape.opaqueBounds);
                    Require(Mathf.Abs(drawn.yMin-(body.y-body.anchorHeight))<.00001f && Mathf.Abs(drawn.width-body.width)<.00001f &&
                        Mathf.Abs(drawn.height-body.height)<.00001f,"Созданное тело не соответствует подготовленной графике.");
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            var saved=new ContainerState {id="saved-drop",definition="loose-item",presentationItem="p10",level=actor.level,x=7,y=1.15f,
                width=.35f,height=.3f,anchorHeight=.15f,presentationAngle=45,presentationMirrored=true,owner="kept",generation=7,vx=2,vy=-3};
            saved.items.Add(new ItemStack {id="kept-item",definition="p10",count=9});
            var world=new WorldState {worldId="shape-upgrade"};world.containers.Add(saved);
            var batch=new RoomContentBatch {physical=new PhysicalLayout {patch="saved-shape",level=actor.level}};world.contentPatches.Add(batch.physical.patch);
            float bottom=saved.y-saved.anchorHeight;string before=StateJson.ToJson(world);
            var plan=DroppedItemShapeUpgrade.Prepare(world,actor.level);
            Require(plan!=null && before==StateJson.ToJson(world),"Подготовка формы изменила сохранённое тело.");
            Require(RoomContentInstaller.NeedsInstall(world,batch) && RoomContentInstaller.Apply(world,batch) && ReferenceEquals(world.containers[0],saved),"Подготовка комнаты не обновила сохранённую форму или заменила ссылку.");
            Require(Mathf.Abs(saved.y-saved.anchorHeight-bottom)<.00001f && saved.x==7 && saved.owner=="kept" && saved.generation==7 &&
                saved.vx==2 && saved.vy==-3 && saved.presentationAngle==45 && saved.presentationMirrored && saved.items[0].id=="kept-item" &&
                saved.items[0].count==9 && WorldChanges.IsMarked(world,saved),"Обновление формы потеряло опору, позу, владение или предметы.");
            world=StateJson.Copy(world);before=StateJson.ToJson(world);
            Require(!RoomContentInstaller.NeedsInstall(world,batch) && !RoomContentInstaller.Apply(world,batch) && before==StateJson.ToJson(world),"Загрузка повторно сдвинула исправленный предмет.");
        }
        private static void CheckUnsupportedDrop()
        {
            var actor=new ActorState {id="unsupported-thrower",player=true,connected=true,health=100,level="shape-check",owner="owner"};
            actor.inventory.Add(new ItemStack {id="empty-graphic",definition="weapon:a_magic:grade:0",count=2});
            var world=new WorldState {worldId="unsupported-drop"};world.actors.Add(actor);string before=StateJson.ToJson(world);
            Require(!DroppedItemTransfer.HasPreparedShape(actor.inventory[0].definition),"Пустому исходному изображению подставлена форма");
            Require(!DroppedItemTransfer.Prepare(world,actor,"empty-drop",actor.inventory[0].id,1,Vector2.zero,Vector2.zero,new NearbyDepositRule(),out string reason) &&
                !string.IsNullOrEmpty(reason) && before==StateJson.ToJson(world),"Бросок без графической формы изменил мир или списал предмет");
            var saved=new ContainerState {id="saved-empty-graphic",definition="loose-item",presentationItem=actor.inventory[0].definition,
                level=actor.level,x=3,y=4,width=.35f,height=.3f,anchorHeight=.15f,owner="kept-owner",generation=7,vx=2,vy=-3};
            saved.items.Add(new ItemStack {id="saved-empty-item",definition=saved.presentationItem,count=2});world.containers.Add(saved);
            before=StateJson.ToJson(world);
            Require(!DroppedItemShapeUpgrade.NeedsApply(world,actor.level) && DroppedItemShapeUpgrade.Prepare(world,actor.level)==null &&
                before==StateJson.ToJson(world),"Неподдерживаемое изображение заблокировало подготовку сохранения или изменило физическое тело.");
            var view=new PropArtwork().CreateContainerView(saved);
            Require(view==null && before==StateJson.ToJson(world),"Неподдерживаемому сохранённому предмету подставлено изображение или изменён источник данных.");
        }
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException(reason);}
    }
}
