using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class TrainingGameplayPreparation
    {
        private const string Path="Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab";
        public static void Prepare()
        {
            var catalog=new SerializedObject(EntityAssetCatalog.Load());
            Append(catalog.FindProperty("actors"),AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(ArmoredTrainingTargetAuthoring.AssetPath));
            foreach(string id in new[]{"woodbox","mcrate4"}) Append(catalog.FindProperty("props"),AssetDatabase.LoadAssetAtPath<PropAsset>("Assets/Remains/RoomDrafts/"+id+".asset"));
            catalog.ApplyModifiedPropertiesWithoutUndo();
            var root=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                var targets=root.GetComponentsInChildren<RoomActorPlacement>().Where(m=>m.Read(room).type=="training-dummy").OrderBy(m=>m.Read(room).x).ToArray();
                if(targets.Length!=3) throw new InvalidOperationException("Ожидаются три обычные мишени для сохранения старых ID.");
                for(int i=0;i<targets.Length;i++)
                {
                    var marker=targets[i];var entry=marker.Read(room);
                    marker.Configure("training:hub:"+(i+1),entry.type,entry.facing,marker.FixedPlacement,marker.GetComponent<SpriteRenderer>());
                }
                foreach(var marker in root.GetComponentsInChildren<RoomContainerPlacement>())
                {
                    var entry=marker.Read(room);
                    if(entry.type=="mcrate1") marker.Configure("training:crate:1",marker.Definition);
                    if(entry.type=="mcrate3") marker.Configure("training:crate:3",marker.Definition);
                }
                PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Verify();
        }
        private static void Append(SerializedProperty list,UnityEngine.Object asset)
        {
            if(asset==null) throw new InvalidOperationException("Нет подготовленного определения сущности.");
            for(int i=0;i<list.arraySize;i++) if(list.GetArrayElementAtIndex(i).objectReferenceValue==asset) return;
            int index=list.arraySize;list.InsertArrayElementAtIndex(index);list.GetArrayElementAtIndex(index).objectReferenceValue=asset;
        }
        public static void Verify()
        {
            ActorTypes.Load();PropCatalog.Load();
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=RoomTrainingContent.Read(room,"check:complete-training-layout","hub");
                var world=new WorldState();TrainingContentInstaller.Apply(world,UnityEngine.JsonUtility.FromJson<TrainingLayout>(Resources.Load<TextAsset>("Content/hub-training").text));
                var old=world.Find("training:hub:1");old.resolvedAttacks.Add("kept-hit");
                TrainingContentInstaller.Apply(world,layout);
                if(world.actors.Count!=5 || world.containers.Count!=6 || world.actors.Count(a=>a.actorType=="training-armored-dummy")!=2 ||
                    !ReferenceEquals(world.Find(old.id),old) || !old.resolvedAttacks.Contains("kept-hit")) throw new InvalidOperationException("Полная расстановка потеряла старые объекты или создала дубликаты.");
                RoomTrainingContent.HidePreviews(room);
                if(root.GetComponentsInChildren<RoomActorPlacement>().Any(m=>m.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)) ||
                    root.GetComponentsInChildren<RoomContainerPlacement>().Any(m=>m.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)))
                    throw new InvalidOperationException("В игровом экземпляре остались редакторные копии сущностей.");
                if(!root.GetComponentsInChildren<RoomArtworkAuthoring>().All(a=>a.GetComponent<Renderer>().enabled))
                    throw new InvalidOperationException("Отключение предпросмотров скрыло оформление комнаты.");
                Debug.Log("Полная расстановка тира PASS: пять актёров, сохранение старых ID и журналов, шесть контейнеров старого мира, предпросмотры скрыты отдельно от геометрии.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            ArmoredTrainingTargetChecks.Run();
        }
    }
}
