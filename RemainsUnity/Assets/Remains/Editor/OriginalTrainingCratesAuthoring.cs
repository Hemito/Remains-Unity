using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class OriginalTrainingCratesAuthoring
    {
        private const string Draft="Assets/Remains/RoomDrafts";
        public static void Import()
        {
            const string path=Draft+"/OriginalTrainingRoom.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(root.GetComponentsInChildren<RoomContainerPlacement>(true).Length!=0)
                    throw new InvalidOperationException("Ящики уже размещены; импорт не должен стирать ручные правки.");
                var wood=CreateType("woodbox","Деревянный ящик",15,60,"8");
                var heavy=CreateType("mcrate4","Тяжёлый металлический ящик",1000,70,"52");
                var types=new[]{wood,heavy}.Concat(new[]{"mcrate1","mcrate2","mcrate3"}.Select(id=>
                    AssetDatabase.LoadAssetAtPath<PropAsset>("Assets/Remains/Resources/Content/props/"+id+".asset")))
                    .ToDictionary(a=>a.CreateDefinition().id);
                var source=OriginalTrainingRoomSource.Read();
                var objects=source.Elements("obj").Where(o=>types.ContainsKey((string)o.Attribute("id"))).ToArray();
                if(objects.Length!=5) throw new InvalidOperationException("Ожидаются пять исходных учебных ящиков.");
                var group=new GameObject("Учебные ящики — расстановка, игровая интеграция ещё не подключена");
                group.transform.SetParent(root.transform,false);
                foreach(var item in objects)
                {
                    var asset=types[(string)item.Attribute("id")];var definition=asset.CreateDefinition();
                    var part=new GameObject($"{definition.name} · {definition.mass} кг");part.transform.SetParent(group.transform,false);
                    part.transform.localPosition=OriginalTrainingRoomSource.Position(source,item);
                    part.AddComponent<RoomContainerPlacement>().Configure("training:original:"+(string)item.Attribute("code"),asset);
                    var preview=new GameObject("Предпросмотр");preview.transform.SetParent(part.transform,false);
                    preview.transform.localPosition=new Vector3(0,definition.height/2,0);
                    var view=preview.AddComponent<SpriteRenderer>();view.sprite=asset.Prefab.Artwork.sprite;
                    view.sharedMaterial=asset.Prefab.Artwork.sharedMaterial;view.sortingOrder=20;
                }
                Validate(root);PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Verify();
        }
        private static PropAsset CreateType(string id,string title,float mass,int size,string symbol)
        {
            string texturePath="Assets/Remains/Resources/Original/Props/"+id+".png";
            if(File.Exists(texturePath) || File.Exists(Draft+"/"+id+".asset"))
                throw new InvalidOperationException("Определение уже существует: "+id);
            File.Copy(Path.Combine(OriginalTrainingRoomSource.Research,$"training-crates-native/DefineSprite_{symbol}_vis{id}/1.png"),texturePath);
            AssetDatabase.ImportAsset(texturePath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture.width!=size || texture.height!=size) throw new InvalidOperationException($"Размер исходного изображения {id} не совпадает с ожидаемым {size}.");
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),50,0,SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite,Draft+"/"+id+"-sprite.asset");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,Draft+"/"+id+".mat");
            var root=new GameObject(title);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;renderer.sortingOrder=20;
                root.AddComponent<PropView>().SetArtwork(renderer);
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,Draft+"/"+id+".prefab");
                var asset=ScriptableObject.CreateInstance<PropAsset>();asset.SetPrefab(prefab.GetComponent<PropView>());
                asset.SetDefinition(new PropDefinition {id=id,name=title,mass=mass,width=size/50f,height=size/50f,resource="Original/Props/"+id});
                AssetDatabase.CreateAsset(asset,Draft+"/"+id+".asset");return asset;
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Validate(GameObject root)
        {
            var room=root.GetComponent<RoomAuthoring>();var entries=root.GetComponentsInChildren<RoomContainerPlacement>();
            if(entries.Length!=5 || entries.Select(p=>p.Read(room).id).Distinct().Count()!=5 ||
                !entries.Select(p=>p.Definition.CreateDefinition().mass).OrderBy(m=>m).SequenceEqual(new[]{15f,25f,75f,120f,1000f}))
                throw new InvalidOperationException("Расстановка потеряла ящики, уникальные ID или исходные массы.");
        }
        public static void Verify()
        {
            Validate(AssetDatabase.LoadAssetAtPath<GameObject>(Draft+"/OriginalTrainingRoom.prefab"));
            OriginalTrainingTargetsAuthoring.RenderSaved();
            Debug.Log("Исходные учебные ящики проверены: 15/25/75/120/1000 кг, пять уникальных ID и сохранённые определения. Пока не подключены к игровому хабу.");
        }
    }
}
