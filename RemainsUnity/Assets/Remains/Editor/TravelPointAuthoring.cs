using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class TravelPointAuthoring
    {
        private const string Folder="Assets/Remains/Resources/Presentation/Travel";
        public const string MapId="original:hub:worldMap";
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();Anatomy.Load();
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(string name in new[]{"world-map","hub-map","exit"})
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath("Assets/Remains/Resources/Original/Travel/"+name+".png");
                if(importer==null) throw new InvalidOperationException("Нет прямого экспорта ресурса перехода: "+name);
                importer.textureType=TextureImporterType.Default;importer.npotScale=TextureImporterNPOTScale.None;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;
                importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            }
            MapWindow();RemoveShortcuts();
            AddPoint("Assets/Remains/Resources/Levels/hub.prefab","hub",true);
            AddPoint("Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab","hub",true);
            foreach(var entry in RoomCatalogAsset.Load().Entries.Where(e=>e.id!="base:hub"))
                AddPoint(AssetDatabase.GetAssetPath(entry.room),"factory:0",false);
            AssetDatabase.SaveAssets();TravelPointChecks.Run();ContractChecks.Run();
            Debug.Log("Переходы: сохранены карта хаба, выходные двери и окно карты; кнопки телепорта удалены из HUD и Escape.");
        }
        private static Sprite Artwork(string name,bool world)
        {
            string path=Folder+"/"+name+".asset";var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if(sprite!=null) return sprite;
            var texture=Resources.Load<Texture2D>("Original/Travel/"+name);
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,world?0:.5f),world?200:100,0,SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite,path);return sprite;
        }
        private static void AddPoint(string path,string level,bool map)
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                var existing=root.GetComponentsInChildren<RoomTravelPointAuthoring>(true);
                if(existing.Length!=0)
                {
                    foreach(var point in existing)
                    {
                        var artwork=point.GetComponent<SpriteRenderer>();
                        if(artwork==null) throw new InvalidOperationException("Точка перехода не имеет изображения: "+point.name);
                        artwork.sortingOrder=ActorVisualLayers.WallFixtureOrder;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);return;
                }
                var position=map?new Vector2(18,8):new Vector2(layout.spawnX-1,layout.spawnY);
                var sprite=Artwork(map?"hub-map":"exit",true);var node=new GameObject(map?"Карта Пустоши · worldMap":"Дверь выхода в хаб");
                node.transform.SetParent(root.transform,false);node.transform.localPosition=position;
                var renderer=node.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sortingOrder=ActorVisualLayers.WallFixtureOrder;
                string materialPath=Folder+"/"+(map?"hub-map":"exit")+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null) {material=WorldArtworkMaterial.Create(sprite.texture);if(map) material.SetFloat("_Emission",1);AssetDatabase.CreateAsset(material,materialPath);}
                renderer.sharedMaterial=material;
                var hull=Anatomy.Movement(new ActorState());
                float floor=layout.platforms.Where(s=>RoomSupport.ContainsX(s,position.x) && RoomSupport.Top(s,position.x,hull.width)<=position.y+.02f)
                    .Select(s=>RoomSupport.Top(s,position.x,hull.width)).Where(y=>ActorTerrainMotion.IsClear(layout,hull,new Vector2(position.x,y)))
                    .OrderByDescending(y=>y).First();
                node.AddComponent<RoomTravelPointAuthoring>().Configure(map?MapId:"base:"+layout.id+":hub-exit",
                    map?TravelPointKind.Map:TravelPointKind.Exit,map?"Карта Пустоши":"Выход в хаб",map?"factory:0":"hub",
                    map?null:MapId,sprite.bounds.size,new Vector2(position.x,floor));
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void MapWindow()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldMap.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null) return;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=ui.Panel(null,"Карта Пустоши",Vector2.zero,new Vector2(900,610));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
                var view=root.AddComponent<WorldMapView>();ui.Label(root.transform,"Карта Пустоши",new Vector2(18,-14),new Vector2(820,32)).fontSize=24;
                var image=new GameObject("Карта оригинала",typeof(RectTransform),typeof(Image));image.transform.SetParent(root.transform,false);
                view.Map=image.GetComponent<Image>();HudElements.Rect(view.Map.rectTransform,new Vector2(18,-62),new Vector2(520,520));
                view.Map.sprite=Artwork("world-map",false);view.Map.raycastTarget=false;
                // Content position of random_plant in the selected original map, independent of its scripts.
                view.Factory=ui.Button(root.transform,"Завод · 0",new Vector2(18+164.9f/550*520-80,-62-330f/550*520),new Vector2(160,36),()=>{});
                view.Description=ui.Label(root.transform,"Выберите место на карте.",new Vector2(566,-92),new Vector2(310,320));view.Description.fontSize=20;
                view.Travel=ui.Button(root.transform,"Отправиться",new Vector2(566,-450),new Vector2(310,48),()=>{});
                view.Close=ui.Button(root.transform,"Закрыть · Esc",new Vector2(566,-514),new Vector2(310,48),()=>{});
                foreach(var button in new[]{view.Factory,view.Travel,view.Close})
                    button.GetComponent<Image>().sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/Presentation/MainMenuArtwork/button-glow.asset");
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void RemoveShortcuts()
        {
            const string hudPath="Assets/Remains/Resources/Presentation/SessionHud.prefab";
            var root=PrefabUtility.LoadPrefabContents(hudPath);
            try
            {
                foreach(var button in root.GetComponentsInChildren<Button>(true).Where(b=>b.GetComponentInChildren<Text>(true)?.text=="На завод 0").ToArray())
                    UnityEngine.Object.DestroyImmediate(button.gameObject);
                var view=root.GetComponent<SessionHudView>();
                HudElements.Rect(view.Inventory.GetComponent<RectTransform>(),new Vector2(8,-29),new Vector2(104,26));
                HudElements.Rect(view.Menu.GetComponent<RectTransform>(),new Vector2(118,-29),new Vector2(104,26));
                root.GetComponent<RectTransform>().sizeDelta=new Vector2(230,62);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,hudPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            const string pausePath="Assets/Remains/Resources/Presentation/PauseMenu.prefab";
            root=PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                var view=root.GetComponent<PauseMenuView>();
                foreach(var button in root.GetComponentsInChildren<Button>(true).Where(b=>b.GetComponentInChildren<Text>(true)?.text=="Вернуться в хаб").ToArray())
                {
                    var rect=button.GetComponent<RectTransform>();float y=rect.anchoredPosition.y,shift=rect.sizeDelta.y+6;
                    foreach(RectTransform child in rect.parent) if(child!=rect && child.anchoredPosition.y<y) child.anchoredPosition+=Vector2.up*shift;
                    UnityEngine.Object.DestroyImmediate(button.gameObject);
                }
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,pausePath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
