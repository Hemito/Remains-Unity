using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Authors the mine weapons (WeaponAsset) from the exported original data (weapon:<id>): explosion, sensing zone,
    // delay, size, artwork and sounds. Geometry uses the world scale (50 px per metre, 30 frames per second); the blast
    // (damage, radius) keeps the original's ratio to the current Unity grenade, whose numbers are themselves preliminary
    // (combat.md). Only mines with a plain explosion (tipdam 4, as the grenade) are authored; cryo/plasma/impulse kinds
    // need their damage types first.
    //   batch -executeMethod Remains.Editor.MineContentAuthoring.RunBatch
    public static class MineContentAuthoring
    {
        public static readonly string[] Mines={"hmine","mine"};
        private const string Folder="Assets/Remains/Resources/Content/weapons";
        private const string CatalogPath="Assets/Remains/Resources/Content/WeaponCatalog.asset";
        private const float PixelsPerMetre=50,FramesPerSecond=30,OriginalVisibility=300;

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var exported=ExportedContentCatalog.Load();
            var catalog=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(CatalogPath)??throw new InvalidOperationException("Нет каталога оружия "+CatalogPath);
            var grenadeAsset=catalog.AllAssets().Single(asset=>asset.Id=="grenade");
            var grenade=grenadeAsset.CreateDefinition();
            var grenadeChar=Char(exported.Require("weapon:grenade"));
            float damageScale=grenade.damage/Number(grenadeChar,"damexpl"),radiusScale=grenade.range/Number(grenadeChar,"expl");
            var serialized=new SerializedObject(catalog);var list=serialized.FindProperty("weapons");
            foreach(string id in Mines)
            {
                var entry=exported.Require("weapon:"+id);
                var root=XElement.Parse(entry.rawXml);var character=root.Element("char")??throw new InvalidOperationException("Нет char у "+id);
                if(Number(character,"tipdam")!=Number(grenadeChar,"tipdam"))
                    throw new InvalidOperationException($"Мина {id}: тип урона {Number(character,"tipdam")} не совпадает с гранатой; нужен свой тип урона.");
                var definition=new WeaponDefinition
                {
                    id=id,explosive=true,damage=Number(character,"damexpl")*damageScale,range=Number(character,"expl")*radiusScale,
                    cooldown=Number(character,"rapid")/FramesPerSecond,holdingArea=new HoldingArea(),
                    mine=new MineProfile
                    {
                        sensitivity=Number(character,"sens")/PixelsPerMetre,delaySeconds=Number(character,"time")/FramesPerSecond,
                        visibility=OriginalVisibility/PixelsPerMetre,width=Number(root,"sX")/PixelsPerMetre,height=Number(root,"sY")/PixelsPerMetre,
                        artwork=Binding(entry.graphics,"item-image-grade-0",id),beepSound=Binding(entry.sounds,"sens",id),
                        defuseSound=Binding(entry.sounds,"dem",id),placeSound=Binding(entry.sounds,"shoot",id)
                    }
                };
                string path=$"{Folder}/{id}.asset";
                var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(path);
                if(asset==null) {asset=ScriptableObject.CreateInstance<WeaponAsset>();asset.SetDefinition(definition);AssetDatabase.CreateAsset(asset,path);}
                else {asset.SetDefinition(definition);EditorUtility.SetDirty(asset);}
                bool listed=false;
                for(int i=0;i<list.arraySize;i++) listed|=list.GetArrayElementAtIndex(i).objectReferenceValue==asset;
                if(!listed) {list.arraySize++;list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=asset;}
                Debug.Log($"Мина {id}: урон {definition.damage:F1}, радиус {definition.range:F2} м, зона ±{definition.mine.sensitivity:F2} м, задержка {definition.mine.delaySeconds:F2} с, графика {definition.mine.artwork}.");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }

        private static XElement Char(ExportedItemEntry entry)=>XElement.Parse(entry.rawXml).Element("char")??throw new InvalidOperationException("Нет char у "+entry.key);
        private static float Number(XElement element,string name)
        {
            string value=(string)element.Attribute(name)??throw new InvalidOperationException($"Нет атрибута {name} в {element.Name}.");
            return float.Parse(value,CultureInfo.InvariantCulture);
        }
        private static string Binding(ExportedBinding[] bindings,string role,string id)
            =>bindings?.FirstOrDefault(binding=>binding.role==role)?.resourceId is {Length:>0} resource?resource:
                throw new InvalidOperationException($"Мина {id}: нет ресурса роли {role} в выгрузке.");
    }
}
