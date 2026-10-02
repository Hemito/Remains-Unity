using System;
using System.IO;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponAssetMigration
    {
        public static void Run()
        {
            const string folder="Assets/Remains/Resources/Content";
            const string target=folder+"/WeaponCatalog.asset";
            if(AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(target)!=null)
                throw new InvalidOperationException("Каталог уже перенесён; существующие настройки не заменены.");
            var original=File.ReadAllText(folder+"/weapons.json");
            var source=StateJson.FromJson<WeaponList>(original);
            if(!AssetDatabase.IsValidFolder(folder+"/Weapons")) AssetDatabase.CreateFolder(folder,"Weapons");
            var assets=new WeaponAsset[source.weapons.Length];
            for(int i=0;i<assets.Length;i++)
            {
                var definition=source.weapons[i];var asset=ScriptableObject.CreateInstance<WeaponAsset>();
                asset.SetDefinition(definition);assets[i]=asset;
                AssetDatabase.CreateAsset(asset,folder+"/Weapons/"+definition.id+".asset");
            }
            var catalog=ScriptableObject.CreateInstance<WeaponAssetCatalog>();catalog.SetAssets(assets);
            if(catalog.CreateJson()!=StateJson.ToJson(source)) throw new InvalidOperationException("Перенос изменил определения оружия.");
            AssetDatabase.CreateAsset(catalog,target);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../.reference/migration");
            File.WriteAllText("../.reference/migration/weapons-before-so.json",original);
            if(!AssetDatabase.DeleteAsset(folder+"/weapons.json")) throw new InvalidOperationException("Не удалось убрать прежний источник оружия.");
            ContractChecks.Run();
            Debug.Log($"Оружие перенесено в ScriptableObject: {assets.Length}, определения совпадают с исходными.");
        }
    }
}
