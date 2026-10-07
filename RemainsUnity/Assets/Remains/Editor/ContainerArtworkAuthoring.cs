using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit bounded mapping for the existing r31 catalog, never infer an open path at runtime.
    public static class ContainerArtworkAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/Containers/ArtworkProfiles";
        private sealed class Entry
        {
            public string id,openResource;public Rect openRect;public Vector2 openPivot;public float ppu;
            public Entry(string id,string resource=null,Rect rect=default,Vector2 pivot=default,float ppu=0)
            {this.id=id;openResource=resource;openRect=rect;openPivot=pivot;this.ppu=ppu;}
            public bool Alternate=>openResource!=null;
            public bool SeparateSprite=>ppu>0;
        }
        private static readonly Entry[] entries=
        {
            new("case","Original/Containers/case-open",new Rect(0,0,160,120),new Vector2(80,60),200),
            new("chest","Original/Containers/chest-open",new Rect(0,0,240,160),new Vector2(120,80),200),
            new("ammobox","Original/Containers/ammobox-open",new Rect(0,0,160,160),new Vector2(80,78),200),
            new("explbox","Original/Containers/OfficeLoot/explbox-open"),
            new("weapbox","Original/Containers/OfficeLoot/weapbox-open"),
            new("medbox","Original/Containers/OfficeLoot/medbox-open"),
            new("instr1","Original/Containers/OfficeLoot/instr1-open"),
            new("trash","Original/Containers/OfficeFurniture/trash-open"),
            new("filecab"),new("table"),new("table1"),new("bookcase"),new("instr2"),new("bigbox"),
            new("box"),new("mcrate1"),new("mcrate2"),new("mcrate3"),new("mcrate4"),new("woodbox"),new("prototype-crate")
        };
        public static string ProfilePath(string id)=>Folder+"/"+id+".asset";
        private static bool Exists(string path)=>File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath,"..",path)));
        private static string SpritePath(Entry entry)=>Folder+"/"+entry.id+"-opened.asset";
        private static Sprite Opened(Entry entry)=>entry.SeparateSprite?AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath(entry)):
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/"+entry.openResource+".png");
        private static PropView Prefab(EntityAssetCatalog catalog,Entry entry)
        {
            var definition=catalog.CreateProps().SingleOrDefault(value=>value.id==entry.id);
            if(definition==null || !catalog.TryGetPropPrefab(definition.id,definition.resource,out var prefab))
                throw new InvalidOperationException("Нет исходного сохранённого префаба контейнера/объекта: "+entry.id);
            return prefab;
        }
        private static void CheckEntry(EntityAssetCatalog catalog,Entry entry,bool required)
        {
            var prefab=Prefab(catalog,entry);var profile=AssetDatabase.LoadAssetAtPath<ContainerArtworkProfileAsset>(ProfilePath(entry.id));
            if(entry.Alternate)
            {
                string path="Assets/Remains/Resources/"+entry.openResource+".png";
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture==null) throw new InvalidOperationException("Нет подтверждённого open PNG: "+path);
                if(entry.SeparateSprite && (texture.width!=entry.openRect.width || texture.height!=entry.openRect.height))
                    throw new InvalidOperationException("Open canvas изменён: "+entry.id);
                if(!entry.SeparateSprite && Opened(entry)==null) throw new InvalidOperationException("Не импортирован родной open Sprite: "+entry.id);
            }
            if(profile==null)
            {
                if(required || prefab.ContainerArtwork!=null || Exists(ProfilePath(entry.id)+".meta") ||
                    entry.SeparateSprite && (AssetDatabase.LoadMainAssetAtPath(SpritePath(entry))!=null || Exists(SpritePath(entry)+".meta")))
                    throw new InvalidOperationException("Неполный/другой набор профиля контейнера: "+entry.id);
                return;
            }
            profile.Validate(entry.id);
            if(profile.Closed!=prefab.Artwork.sprite || profile.Mode!=(entry.Alternate?ContainerArtworkMode.ClosedOpened:ContainerArtworkMode.Static) ||
                profile.Opened!=(entry.Alternate?Opened(entry):null) || prefab.ContainerArtwork!=profile)
                throw new InvalidOperationException("Профиль не совпал с явно подтверждённой графикой/префабом: "+entry.id);
            if(entry.SeparateSprite && (profile.Opened.rect!=entry.openRect || profile.Opened.pixelsPerUnit!=entry.ppu ||
                Vector2.Distance(profile.Opened.pivot,entry.openPivot)>.0001f))
                throw new InvalidOperationException("Open registration/canvas изменён: "+entry.id);
        }
        [MenuItem("Remains/Контент/Подготовить состояния графики контейнеров")]
        public static void Install()
        {
            var catalog=EntityAssetCatalog.Load();
            foreach(var definition in catalog.CreateProps())
                if(!entries.Any(entry=>entry.id==definition.id))
                    throw new InvalidOperationException("Нет явного решения состояния графики для типа каталога: "+definition.id);
            foreach(var entry in entries) CheckEntry(catalog,entry,false);
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts/Containers","ArtworkProfiles");
            foreach(var entry in entries)
            {
                if(AssetDatabase.LoadAssetAtPath<ContainerArtworkProfileAsset>(ProfilePath(entry.id))!=null) continue;
                if(entry.SeparateSprite)
                {
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Remains/Resources/"+entry.openResource+".png");
                    var sprite=Sprite.Create(texture,entry.openRect,entry.openPivot/entry.openRect.size,entry.ppu,0,SpriteMeshType.FullRect);
                    sprite.name=entry.id+"-opened";AssetDatabase.CreateAsset(sprite,SpritePath(entry));
                }
                var prefab=Prefab(catalog,entry);
                var profile=ScriptableObject.CreateInstance<ContainerArtworkProfileAsset>();profile.name=entry.id+"-artwork";
                profile.Configure(entry.id,entry.Alternate?ContainerArtworkMode.ClosedOpened:ContainerArtworkMode.Static,prefab.Artwork.sprite,
                    entry.Alternate?Opened(entry):null);AssetDatabase.CreateAsset(profile,ProfilePath(entry.id));
                string path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
                try {root.GetComponent<PropView>().ConfigureContainerArtwork(profile);PrefabUtility.SaveAsPrefabAsset(root,path);}
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Check();
        }
        public static void Check()
        {
            var catalog=EntityAssetCatalog.Load();foreach(var entry in entries) CheckEntry(catalog,entry,true);
            Debug.Log("Базовые профили контейнеров: PASS — 8 исходных close/open пар и 13 явных статических рисунков; closed Sprite/GUID и тела сохранены. Новая мебель проверяется своим ресурсным авторингом.");
        }
    }
}
