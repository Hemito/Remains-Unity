using System;
using System.IO;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class EntityAssetMigration
    {
        public static void Run()
        {
            const string folder="Assets/Remains/Resources/Content";
            const string target=folder+"/EntityCatalog.asset";
            if(AssetDatabase.LoadAssetAtPath<EntityAssetCatalog>(target)!=null) throw new InvalidOperationException("Каталог сущностей уже существует; перенос отменён.");
            var actorsJson=File.ReadAllText(folder+"/actors.json");var propsJson=File.ReadAllText(folder+"/props.json");
            var actors=StateJson.FromJson<ActorTypeList>(actorsJson);var props=StateJson.FromJson<PropDefinitionList>(propsJson);
            if(!AssetDatabase.IsValidFolder(folder+"/Actors")) AssetDatabase.CreateFolder(folder,"Actors");
            if(!AssetDatabase.IsValidFolder(folder+"/Props")) AssetDatabase.CreateFolder(folder,"Props");
            var actorAssets=new ActorTypeAsset[actors.actors.Length];var propAssets=new PropAsset[props.props.Length];
            for(int i=0;i<actorAssets.Length;i++)
            {var asset=ScriptableObject.CreateInstance<ActorTypeAsset>();asset.SetDefinition(actors.actors[i]);actorAssets[i]=asset;AssetDatabase.CreateAsset(asset,folder+"/Actors/"+actors.actors[i].id+".asset");}
            for(int i=0;i<propAssets.Length;i++)
            {var asset=ScriptableObject.CreateInstance<PropAsset>();asset.SetDefinition(props.props[i]);propAssets[i]=asset;AssetDatabase.CreateAsset(asset,folder+"/Props/"+props.props[i].id+".asset");}
            var catalog=ScriptableObject.CreateInstance<EntityAssetCatalog>();catalog.SetAssets(actorAssets,propAssets);
            if(catalog.ActorsJson()!=StateJson.ToJson(actors) || catalog.PropsJson()!=StateJson.ToJson(props)) throw new InvalidOperationException("Перенос изменил определения сущностей.");
            AssetDatabase.CreateAsset(catalog,target);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../.reference/migration");File.WriteAllText("../.reference/migration/actors-before-so.json",actorsJson);File.WriteAllText("../.reference/migration/props-before-so.json",propsJson);
            if(!AssetDatabase.DeleteAsset(folder+"/actors.json") || !AssetDatabase.DeleteAsset(folder+"/props.json")) throw new InvalidOperationException("Не удалось убрать прежние определения сущностей.");
            ContractChecks.Run();Debug.Log($"Перенесены типы персонажей: {actorAssets.Length}, физические объекты: {propAssets.Length}. Значения сохранены.");
        }
    }
}
