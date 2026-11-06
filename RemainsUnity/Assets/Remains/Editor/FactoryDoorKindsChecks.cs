using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The further door kinds on the megafactory (user 2026-11-06): they stand as barriers, keep the source strength,
    // and a world saved before them gains exactly these doors without touching the ones it has.
    public static class FactoryDoorKindsChecks
    {
        private static bool Further(string id)=>id is "door1a" or "door1b" or "septum" or "window1" or "hatch2" or "grate" or "platform1";
        public static void RunBatch()
        {
            try {Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var world=NewGameWorld.Create(104);world.mechanismVersion=1;const string level="factory:0";
            try
            {
                FactoryContentSources.Prepare(world,level);
                var batch=RoomContentBatch.Read(RoomAuthoring.Load(level,world),level);
                RoomContentInstaller.Apply(world,batch);
                var doors=world.doors.Where(d=>d.level==level).ToArray();
                string counts=string.Join(", ",doors.GroupBy(d=>d.definition).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>$"{g.Key} {g.Count()}"));
                Debug.Log("Виды дверей завода: "+counts);
                foreach(var (id,health,interactive) in new[]{("door1a",300f,true),("door1b",400f,true),("septum",30f,false),("window1",10f,false),("hatch2",1000f,true),("grate",1000f,false),("platform1",5000f,false)})
                {
                    foreach(var door in doors.Where(d=>d.definition==id))
                    {
                        Require(door.integrity?.maxHealth==health && door.interactive==interactive,$"{door.id}: прочность или взаимодействие не по оригиналу");
                        Require(door.open || door.Blocks,$"{door.id}: закрытая преграда не преграждает");
                    }
                    var prefab=GameAssets.Load<Remains.Presentation.DoorView>("Presentation/Doors/"+id);
                    Require(prefab!=null && prefab.Definition.Id==id,"вид двери не находит префаб "+id);
                }
                Require(doors.Any(d=>d.definition=="door1a") && doors.Any(d=>d.definition=="septum") && doors.Any(d=>d.definition=="hatch2"),"на заводе нет частых видов: "+counts);
                // A world saved at doors-v3 gets the new doors only.
                var kept=doors.First(d=>d.definition=="door1");kept.open=true;
                string v4=world.contentPatches.Single(p=>p.StartsWith(level+":",StringComparison.Ordinal) && p.EndsWith(":doors-v4",StringComparison.Ordinal));
                int before=doors.Length,further=doors.Count(d=>Further(d.definition));
                world.doors.RemoveAll(d=>d.level==level && Further(d.definition));
                world.contentPatches.Remove(v4);world.contentPatches.Add(v4.Replace(":doors-v4",":doors-v3"));
                RoomContentInstaller.Apply(world,batch);
                Require(world.doors.Count(d=>d.level==level)==before,"сохранённый мир получил не те двери");
                Require(world.doors.Single(d=>d.id==kept.id).open,"обновление сбросило состояние старой двери");
                Debug.Log($"Виды дверей завода: PASS — {counts}; новых {further}, обновление старого мира добавляет только их.");
            }
            finally {FactoryContentSources.Clear();}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Виды дверей завода: "+message);}
    }
}
