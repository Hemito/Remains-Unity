using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class ProjectilePersistenceChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var source=new ActorState {id="source",owner="a",player=true,connected=true,level="factory:0",loadoutVersion=1};
            var nearby=new ActorState {id="nearby",owner="b",player=true,connected=true,level="factory:0",x=4,loadoutVersion=1};
            var far=new ActorState {id="far",owner="host",player=true,connected=true,level="factory:0",x=18,loadoutVersion=1};
            world.actors.AddRange(new[]{source,nearby,far});
            var flight=new ProjectileState {launch=new AttackEvent {id="shot-save",attacker=source.id,sourceOwner="a",sourcePlayer=true,
                level=source.level,weapon="pistol",endX=20},owner="a",generation=1,flightSpeed=90,traveled=3};
            world.projectiles.Add(flight);
            source.connected=false;
            Require(ProjectileLifecycle.ReassignAndReportChanges(world,new[]{"b","host"}),"Передача снаряда не отмечена для сохранения");
            Require(!ProjectileLifecycle.ReassignAndReportChanges(world,new[]{"b","host"}),"Неизменный полёт требует повторного снимка");
            Require(flight.owner=="b" && flight.generation==2 && flight.launch.sourceOwner=="a" && flight.launch.attacker==source.id,"Передача полёта меняет автора или выбирает далёкого хоста.");
            Require(!ProjectileLifecycle.AcceptProgress(flight,"a",1,10,6,false),"Принят пакет прежнего владельца.");
            Require(ProjectileLifecycle.AcceptProgress(flight,"b",2,1,4,false),"Не принят прогресс нового владельца.");
            Require(!ProjectileLifecycle.AcceptProgress(flight,"b",2,1,5,false) && !ProjectileLifecycle.AcceptProgress(flight,"b",2,2,3,false),"Повтор/откат изменил полёт.");
            Require(!ProjectileLifecycle.AcceptProgress(flight,"b",2,2,float.NaN,false),"Принят NaN прогресса.");
            nearby.level="hub";far.level="hub";ProjectileLifecycle.Reassign(world,new[]{"b","host"});
            Require(flight.owner==null && flight.traveled==4 && !flight.finished,"Пустой уровень удаляет полёт или продолжает его на хосте.");
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/projectile-save-"+Guid.NewGuid().ToString("N")));
            var store=new SaveStore(directory);store.SaveWorld(world);var restored=store.LoadWorld();
            var saved=restored.projectiles[0];
            Require(saved.Path().Position.x==4 && saved.launch.id==flight.launch.id && saved.owner==null,"Сохранение потеряло идентичность/прогресс полёта.");
            ProjectileLifecycle.BeginSession(restored);
            var returning=restored.Find(nearby.id);returning.level="factory:0";returning.owner="new-b";
            ProjectileLifecycle.Reassign(restored,new[]{"new-b"});
            Require(saved.owner=="new-b" && saved.sequence==0 && saved.traveled==4,"Возвращение в уровень не продолжает прежний полёт.");
            var path=saved.Path();path.Advance(.1f,(_,_)=>null);
            Require(Mathf.Abs(path.Position.x-13)<.0001f,"Продолжение начинается с дула вместо сохранённой позиции.");
            Require(ProjectileLifecycle.AcceptProgress(saved,"new-b",saved.generation,1,13,true),"Не принято завершение полёта.");
            Require(!ProjectileLifecycle.AcceptProgress(saved,"new-b",saved.generation,2,14,false),"Завершённый полёт ожил от позднего пакета.");
            store.SaveWorld(restored);Require(store.LoadWorld().projectiles[0].finished,"Завершение не сохранено.");
            Debug.Log("Сохранение полёта: PASS — автор, ближайший владелец, отключение, пустой уровень, повтор/откат, продолжение, завершение. "+directory);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
