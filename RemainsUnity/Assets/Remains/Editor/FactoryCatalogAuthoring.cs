using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryCatalogAuthoring
    {
        public const string AssetPath="Assets/Remains/Resources/Content/FactoryCatalog.asset";
        private const string Source="Assets/Remains/Rooms/Original";

        [MenuItem("Remains/Комнаты/Подготовить каталог игрового завода")]
        public static void Generate()
        {
            var entries=AssetDatabase.FindAssets("t:"+nameof(RoomTemplateAsset),new[]{Source+"/plant"})
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p,StringComparer.Ordinal).Select(path=>
                {
                    var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(path);
                    string stem=Path.GetFileName(path).Replace(".room.json","");
                    RoomTemplateInstance Prefab(bool mirror)
                    {
                        string prefabPath=$"{Source}/Prefabs/plant/{stem}{(mirror?"-m":"")}.prefab";
                        var root=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        return root==null?throw new InvalidOperationException("Нет префаба "+prefabPath):
                            root.GetComponent<RoomTemplateInstance>()??throw new InvalidOperationException("Нет метаданных префаба "+prefabPath);
                    }
                    return new FactoryCatalogAsset.Entry{template=template,normal=Prefab(false),mirrored=Prefab(true)};
                }).ToArray();
            TextAsset Text(string name)=>AssetDatabase.LoadAssetAtPath<TextAsset>(Source+"/"+name+".json")
                ??throw new InvalidOperationException("Нет исходных данных завода "+name);
            var art=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(RoomTileArtAuthoring.AssetPath)
                ??throw new InvalidOperationException("Нет подготовленной графики комнат.");
            // Validate a detached candidate before changing an existing asset.
            var candidate=ScriptableObject.CreateInstance<FactoryCatalogAsset>();
            try
            {
                candidate.Configure(entries,Text("definitions"),Text("lands"),Text("room-lighting"),art);
                var existing=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(AssetPath);
                if(existing==null) {AssetDatabase.CreateAsset(candidate,AssetPath);candidate=null;}
                else {EditorUtility.CopySerialized(candidate,existing);EditorUtility.SetDirty(existing);}
                AssetDatabase.SaveAssets();
                Debug.Log($"Каталог игрового завода: {entries.Length} шаблонов, {entries.Length*2} префабов; {AssetPath}.");
            }
            finally {if(candidate!=null) UnityEngine.Object.DestroyImmediate(candidate);}
        }

        public static void RunBatch()
        {
            int code=0;
            try {Generate();Check();}
            catch(Exception error) {Debug.LogError("Подготовка игрового завода прервана: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static void Check()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(AssetPath);
            var world=new WorldState{seed=104};
            var record=FactoryPreparation.Create(world,catalog,0);
            string saved=record.layout;
            if(Remains.RoomFormat.FactoryLayoutReader.Read(saved,"проверка сохранённой раскладки").Write()!=saved)
                throw new InvalidOperationException("Раскладка не сохраняет поля при чтении/записи.");
            var copied=StateJson.Copy(world);
            var prepared=FactoryPreparation.Prepare(copied,catalog,"factory:0");
            if(copied.factories[0].layout!=saved || prepared.Grid.Rooms.Count!=84 ||
                prepared.Physics.platforms.Length==0 || prepared.Physics.ladders.Length==0)
                throw new InvalidOperationException("Завод не сохранил раскладку/геометрию при копировании состояния.");
            if(EntryPreparationContext.Fingerprint(world,"factory:0")!=EntryPreparationContext.Fingerprint(copied,"factory:0"))
                throw new InvalidOperationException("Отпечаток подготовки входа изменился после копирования мира.");
            bool rejected=false;
            try {FactoryPreparation.Create(copied,catalog,0);} catch(InvalidOperationException) {rejected=true;}
            if(!rejected) throw new InvalidOperationException("Повторная генерация существующего завода не отклонена.");
            copied.seed++;
            rejected=false;
            try {FactoryPreparation.Prepare(copied,catalog,"factory:0");} catch(System.IO.InvalidDataException) {rejected=true;}
            if(!rejected) throw new InvalidOperationException("Несовпадающий seed мира не отклонён.");
            Debug.Log($"Сохранённый завод: PASS; 4×21, сетка {prepared.Grid.Level.Width}×{prepared.Grid.Level.Height}, поверхностей {prepared.Physics.platforms.Length}, лестниц {prepared.Physics.ladders.Length}; состояние сохранено, повторная генерация и другой seed отклонены.");
        }
    }
}
