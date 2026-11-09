using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Small original utility graphics, generated once in Editor; never at runtime.
    public static class LightTestItemsAuthoring
    {
        private const string Folder="Assets/Remains/Resources/LightTests";
        public static void RunBatch(){Run();EditorApplication.Exit(0);}
        public static void Run()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var catalog=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>("Assets/Remains/Resources/Content/WeaponCatalog.asset")
                ??throw new InvalidOperationException("Нет каталога оружия для тестовых источников света.");
            var assets=catalog.AllAssets().ToList();
            var template=assets.FirstOrDefault(a=>a.Id=="grenade")??throw new InvalidOperationException("Нет базовой гранаты для тестовых метательных предметов.");
            var material=template.GrenadeFlight?.sharedMaterial??throw new InvalidOperationException("У базовой гранаты нет материала полёта.");
            var ball=Draw("ball",0);var stick=Draw("stick",1);var flashlight=Draw("flashlight",2);
            var shapes=AssetDatabase.LoadAssetAtPath<DroppedItemShapeCatalog>("Assets/Remains/Resources/Content/DroppedItemShapes.asset")
                ??throw new InvalidOperationException("Нет каталога форм брошенных предметов.");
            var shapeEntries=shapes.CreateLookup();
            AddShape(LightTestItems.Ball,ball);AddShape(LightTestItems.Stick,stick);AddShape(LightTestItems.Flashlight,flashlight);
            shapes.Configure(shapeEntries.Values.ToArray());EditorUtility.SetDirty(shapes);DroppedItemShapeCatalog.Reset();
            AddThrowable(LightTestItems.Ball,"ball",ball,new Color(.3f,3,1.8f,1),3.5f,true,.85f,.95f);
            AddThrowable(LightTestItems.Stick,"stick",stick,new Color(.2f,2,.35f,1),3,false,.12f,.55f);
            catalog.SetAssets(assets.ToArray());EditorUtility.SetDirty(catalog);
            var icons=AssetDatabase.LoadAssetAtPath<InventoryIconAsset>("Assets/Remains/Resources/Original/Inventory/icons.asset")
                ??throw new InvalidOperationException("Нет каталога иконок тестовых предметов.");
            var serialized=new SerializedObject(icons);var entries=serialized.FindProperty("entries");
            AddIcon(LightTestItems.Ball,ball);AddIcon(LightTestItems.Stick,stick);AddIcon(LightTestItems.Flashlight,flashlight);
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(icons);
            string textPath="Assets/Remains/Resources/Content/item-text.json";
            var texts=JObject.Parse(File.ReadAllText(textPath));var list=(JArray)texts["items"];
            Text(LightTestItems.Ball,"Светящийся попрыгунчик","Резиновый светящийся мячик для проверки света и теней. Отскакивает от поверхностей; попадание наносит 1 единицу базового урона и убирает мячик. Светит 120 секунд активного времени локации.");
            Text(LightTestItems.Stick,"ХИС-палочка","Химический источник зелёного света для проверки освещения. Бросьте и оставьте на полу: светит 120 секунд активного времени локации.");
            Text(LightTestItems.Flashlight,"Фонарик","Тестовый ручной фонарик. Возьмите в телекинез и направляйте курсором. Прямой свет без отражённого заполнения.");
            File.WriteAllText(textPath,texts.ToString(Newtonsoft.Json.Formatting.Indented));
            AssetDatabase.ImportAsset(textPath);AssetDatabase.SaveAssets();
            Debug.Log("Тестовые источники света подготовлены: попрыгунчик, ХИС-палочка и фонарик; выдача через Esc → DEBUG.");

            void AddThrowable(string id,string file,Sprite sprite,Color radiance,float reach,bool contact,float bounce,float retention)
            {
                var definition=template.CreateDefinition();definition.id=id;definition.explosive=false;definition.damage=contact?1:0;
                definition.speedDamage=definition.maxSpeedDamage=0;definition.damageType=DamageTypes.Physical;definition.fire=null;
                definition.grenadeFuseSeconds=120;definition.grenadeRestitution=bounce;definition.grenadeGroundRetention=retention;
                definition.grenadeAngularRetention=.92f;definition.range=1;definition.cooldown=.4f;
                definition.stopAtFirstActor=true;definition.stopAtTerrain=true;
                definition.throwableLight=new ThrowableLightProfile{radiance=radiance,reach=reach,width=.2f,contactProjectile=contact};
                WeaponCatalogLoader.Validate(definition,"авторинг тестового света");
                string prefabPath=Folder+"/"+file+".prefab";
                var node=new GameObject(id);WeaponView prefab;
                try
                {
                    var renderer=node.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                    var view=node.AddComponent<WeaponView>();view.Configure(id,renderer,null);
                    view.ConfigureRest(CatalogSpriteAuthoring.Reference(sprite));
                    prefab=PrefabUtility.SaveAsPrefabAsset(node,prefabPath).GetComponent<WeaponView>();
                }
                finally{UnityEngine.Object.DestroyImmediate(node);}
                string path=Folder+"/"+file+".asset";var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(path);bool fresh=asset==null;
                if(fresh)asset=ScriptableObject.CreateInstance<WeaponAsset>();
                asset.SetDefinition(definition);asset.SetPresentation(prefab);asset.SetGrenadeFlight(prefab.Artwork);
                if(fresh)AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
                int found=assets.FindIndex(a=>a.Id==id);if(found<0)assets.Add(asset);else assets[found]=asset;
            }
            void AddShape(string id,Sprite sprite)
            {
                var shape=new DroppedItemShape{id=id,opaqueBounds=SpriteOpaqueBoundsBaker.Read(sprite)};
                shape.ConfigureReference(CatalogSpriteAuthoring.Reference(sprite));shapeEntries[id]=shape;
            }
            void AddIcon(string id,Sprite sprite)
            {
                int at=-1;for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue==id){at=i;break;}
                if(at<0){at=entries.arraySize;entries.arraySize++;}
                var entry=entries.GetArrayElementAtIndex(at);entry.FindPropertyRelative("Id").stringValue=id;entry.FindPropertyRelative("Sprite").objectReferenceValue=sprite;
            }
            void Text(string id,string title,string description)
            {
                var old=list.OfType<JObject>().FirstOrDefault(x=>(string)x["id"]==id);
                if(old!=null)old.Remove();list.Add(new JObject{["id"]=id,["name"]=title,["description"]=description});
            }
        }
        private static Sprite Draw(string name,int kind)
        {
            const int size=64;var image=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=x-31.5f,dy=y-31.5f;Color c=Color.clear;
                if(kind==0 && dx*dx+dy*dy<24*24)
                {
                    c=dx*dx+dy*dy>21*21?new Color(.06f,.25f,.21f):new Color(.12f,.8f,.65f);
                    if(Mathf.Abs(dy-dx*.45f)<2 || Mathf.Abs(dx+dy*.4f)<2)c=new Color(.04f,.38f,.32f);
                    if((dx+8)*(dx+8)+(dy-8)*(dy-8)<20)c=new Color(.65f,1,.85f);
                }
                if(kind==1 && x>=25 && x<=38 && y>=8 && y<=54)
                    c=x<=27||x>=36||y<=11||y>=51?new Color(.1f,.3f,.12f):new Color(.3f,1,.35f);
                if(kind==1 && x>=29&&x<=33&&y>=56&&y<=59)c=new Color(.15f,.3f,.15f);
                if(kind==2 && x>=8&&x<=44&&y>=24&&y<=39)c=new Color(.22f,.26f,.27f);
                if(kind==2 && x>=13&&x<=38&&y>=26&&y<=37)c=new Color(.34f,.4f,.4f);
                if(kind==2 && x>=38&&x<=55&&y>=20&&y<=43)c=new Color(.14f,.19f,.2f);
                if(kind==2 && x>=50&&x<=56&&y>=23&&y<=40)c=new Color(.9f,.94f,.72f);
                if(kind==2 && x>=21&&x<=29&&y>=39&&y<=43)c=new Color(.4f,.65f,.3f);
                pixels[y*size+x]=c;
            }
            image.SetPixels32(pixels);image.Apply();string path=Folder+"/"+name+".png";
            File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;
            importer.spritePixelsPerUnit=kind==0?240:120;importer.spritePivot=new Vector2(.5f,.5f);
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path)??throw new InvalidOperationException("Не создан рисунок тестового света: "+path);
        }
    }
}
