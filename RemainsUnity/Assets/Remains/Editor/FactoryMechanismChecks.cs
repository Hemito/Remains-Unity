using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Mechanisms of the megafactory (user 2026-11-06): the rooms of the plant get their platforms, buttons and door groups,
    // a saved world gains them once, a button swaps its group and an area opens its own, once.
    public static class FactoryMechanismChecks
    {
        public static void RunBatch()
        {
            try {Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            string counts="";int platforms=0,buttons=0,areas=0,driven=0;
            try
            {
                foreach(int seed in new[]{104,105,106,107})
                {
                    var world=NewGameWorld.Create(seed);const string level="factory:0";
                    MechanismPersistence.Prepare(world,()=>{});
                    FactoryContentSources.Prepare(world,level);
                    var batch=RoomContentBatch.Read(RoomAuthoring.Load(level,world),level);
                    RoomContentInstaller.Apply(world,batch);
                    MechanismPersistence.ValidateWorld(world);
                    foreach(var platform in world.movingPlatforms)
                    {
                        Require(platform.level==level && world.mechanismButtons.Any(b=>b.platformId==platform.id),"платформа без кнопки: "+platform.id);
                        var route=platform.End-platform.Start;
                        Require(Mathf.Abs(Mathf.Abs(route.x)-16.8f)<.001f && route.y==0 || route.x==0 && Mathf.Abs(route.y+8.8f)<.001f,$"маршрут не по оригиналу: {platform.id} {route}");
                    }
                    foreach(var button in world.mechanismButtons.Where(b=>b.DrivesDoors))
                        foreach(string id in button.doors) Require(world.doors.Exists(d=>d.id==id && d.definition=="platform1"),$"{button.id}: нет двери {id}");
                    platforms+=world.movingPlatforms.Count;buttons+=world.mechanismButtons.Count(b=>!b.IsArea);areas+=world.mechanismButtons.Count(b=>b.IsArea);
                    driven+=world.mechanismButtons.Count(b=>b.DrivesDoors);
                    counts+=$" seed {seed}: платформ {world.movingPlatforms.Count}, кнопок {world.mechanismButtons.Count(b=>!b.IsArea)}, областей {world.mechanismButtons.Count(b=>b.IsArea)};";
                    Debug.Log("Механизмы завода:"+counts);
                    int before=world.mechanismButtons.Count;
                    RoomContentInstaller.Apply(world,batch);
                    Require(world.mechanismButtons.Count==before,"повторная установка удвоила механизмы");
                    Operate(world);
                }
                Require(platforms>0 && buttons>0 && driven>0,"за 4 мира нет механизмов:"+counts);
                Debug.Log("Механизмы завода: PASS —"+counts);
            }
            finally {FactoryContentSources.Clear();}
        }
        private static void Operate(WorldState world)
        {
            var operations=new MechanismOperations(world);
            var swap=world.mechanismButtons.FirstOrDefault(b=>b.DrivesDoors && !b.IsArea);
            if(swap!=null)
            {
                var doors=swap.doors.Select(id=>world.doors.Find(d=>d.id==id)).ToArray();bool[] before=doors.Select(d=>d.open).ToArray();
                var actor=new ActorState {id="mech-check",player=true,connected=true,level=swap.level,x=swap.x,y=swap.y,health=100};world.actors.Add(actor);
                var result=operations.TryToggleDoors(actor,swap,1.6f,swap.revision,"mechanism:check-"+swap.id);
                Require(result.Outcome==MechanismOperationOutcome.Changed,"кнопка дверей отклонена: "+result.Reason);
                Require(doors.Select(d=>d.open).SequenceEqual(before.Select(o=>!o)),"кнопка не переключила группу");
                world.actors.Remove(actor);
            }
            var area=world.mechanismButtons.FirstOrDefault(b=>b.IsArea);
            if(area!=null)
            {
                Require(operations.OpenArea(area) && area.doors.All(id=>world.doors.Find(d=>d.id==id).open),"область не открыла группу");
                Require(!operations.OpenArea(area),"область сработала дважды");
            }
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Механизмы завода: "+message);}
    }
}
