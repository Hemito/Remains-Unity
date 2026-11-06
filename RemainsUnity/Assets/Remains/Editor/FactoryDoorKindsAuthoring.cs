using System;
using System.IO;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The door kinds of the original that the megafactory did not place (user 2026-11-06). The level view reads a door's
    // definition as remains:obj:<id>, so each kind has its own asset with the source id; the checked office assets give
    // their art, sounds and strength. hatch2 and grate have no such asset: they come from the room object art.
    public static class FactoryDoorKindsAuthoring
    {
        private const string Folder="Assets/Remains/Resources/Presentation/Doors/";
        private const string Objects="Assets/Remains/Rooms/Original/Tiles/objects/";
        private const string Root="Assets/Remains/Resources/Content/FactoryLevelRoot.prefab";
        public static void BuildBatch()
        {
            int code=0;
            try
            {
                Copy("door1a",Folder+"office-door1a");Copy("door1b",Folder+"office-door1b");
                Copy("septum",Folder+"office-septum");Copy("window1",Folder+"office-window1");
                Copy("platform1","Assets/Remains/RoomDrafts/Continuation27/continuation-platform1",Folder+"continuation-platform1.prefab");
                var metal=Require(Folder+"office-door1b.asset");var large=Require(Folder+"door3.asset");
                Create("hatch2",new Vector2(1.6f,.8f),new Vector2(1.6f,1),true,1000,metal);
                Create("grate",new Vector2(.8f,2.4f),new Vector2(.8f,2.4f),false,1000,large);
                AssetDatabase.SaveAssets();
                var root=PrefabUtility.LoadPrefabContents(Root);
                try
                {
                    root.GetComponent<FactoryDoorAdapter>().ConfigureKinds(Kinds());
                    PrefabUtility.SaveAsPrefabAsset(root,Root,out bool saved);
                    if(!saved) throw new IOException("Не записан корень завода: "+Root);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
                AssetDatabase.SaveAssets();
                Debug.Log("Виды дверей завода: PASS — записаны определения, префабы и адаптер.");
            }
            catch(Exception error) {Debug.LogError("Виды дверей завода: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static FactoryDoorKind[] Kinds()=>new[]
        {
            Kind("door1a","door:wood",false),Kind("door1b","floor",false),Kind("septum","door:wood",false),Kind("window1","door:glass",false),
            Kind("hatch2","floor",true),Kind("grate","floor",false),Kind("platform1","floor",true),
        };
        private static FactoryDoorKind Kind(string id,string surface,bool horizontal)=>new()
        {
            type=id,surface=surface,horizontal=horizontal,
            prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+id+".prefab")?.GetComponent<DoorView>()
                ??throw new InvalidOperationException("Нет префаба двери: "+id),
        };
        private static DoorAsset Require(string path)=>AssetDatabase.LoadAssetAtPath<DoorAsset>(path)
            ??throw new InvalidOperationException("Нет определения двери: "+path);
        private static DoorAsset Own(string id)
        {
            var asset=AssetDatabase.LoadAssetAtPath<DoorAsset>(Folder+id+".asset");
            if(asset==null) {asset=ScriptableObject.CreateInstance<DoorAsset>();AssetDatabase.CreateAsset(asset,Folder+id+".asset");}
            asset.name=id;return asset;
        }
        private static void Copy(string id,string source,string prefabPath=null)
        {
            var original=Require(source+".asset");var asset=Own(id);
            asset.ConfigureBarrier(id,original.Size,original.Closed,original.Opened,original.Destroyed,
                original.OpenSound,original.CloseSound,original.DestroySound,original.Interactive,original.MaxHealth,original.InteractionSize);
            EditorUtility.SetDirty(asset);
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath??source+".prefab");
            try
            {
                prefab.name=id;
                var view=prefab.GetComponent<DoorView>();view.Configure(asset,view.Artwork);
                PrefabUtility.SaveAsPrefabAsset(prefab,Folder+id+".prefab",out bool saved);
                if(!saved) throw new IOException("Не сохранён префаб двери: "+id);
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
        }
        private static void Create(string id,Vector2 size,Vector2 interaction,bool interactive,float health,DoorAsset sounds)
        {
            Sprite Sprite(string state,int frame)
            {
                string path=$"{Folder}{id}-{state}.asset";
                var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if(existing!=null) return existing;
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>($"{Objects}{id}-f{frame}.png")
                    ??throw new InvalidOperationException($"Нет рисунка {id}, кадр {frame}.");
                var sprite=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),
                    (float)Remains.RoomFormat.RoomSpriteLayout.SourcePixelsPerMeter,0,SpriteMeshType.FullRect);
                sprite.name=$"{id}-{state}";AssetDatabase.CreateAsset(sprite,path);return sprite;
            }
            var asset=Own(id);
            asset.ConfigureBarrier(id,size,Sprite("closed",1),Sprite("open",2),Sprite("die",3),
                sounds.OpenSound,sounds.CloseSound,sounds.DestroySound,interactive,health,interaction);
            EditorUtility.SetDirty(asset);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+id+".mat");
            if(material==null) {material=WorldArtworkMaterial.Create(asset.Closed.texture);AssetDatabase.CreateAsset(material,Folder+id+".mat");}
            var root=new GameObject(id);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                root.AddComponent<DoorView>().Configure(asset,renderer);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+id+".prefab",out bool saved);
                if(!saved) throw new IOException("Не сохранён префаб двери: "+id);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
