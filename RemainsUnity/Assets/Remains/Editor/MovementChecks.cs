using System;
using System.IO;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class MovementChecks
    {
        public static void Run()
        {
            var factory=RoomLayout.Load();var movement=new MovementController(factory);
            var actor=new ActorState {x=0,y=0};movement.Step(actor,Vector2.zero,true,.05f);
            Require(actor.y>0 && actor.vy>0,"Прыжок от пола");
            actor=new ActorState();movement.Step(actor,Vector2.right,false,.02f);
            Require(actor.pose=="walk" && actor.vx==2.5f,"По умолчанию ходьба");
            movement.Step(actor,Vector2.right,false,.02f,true);
            Require(actor.pose=="run" && actor.vx==4,"Бег включается отдельно");
            actor.x=3;actor.y=4;actor.vy=-10;movement.Step(actor,Vector2.zero,false,.1f);
            Require(Mathf.Abs(actor.y-3.25f)<.001f && actor.pose=="idle","Приземление на верхнюю платформу");

            foreach(float dt in new[]{1f/60,.1f,.33f,.9f})
            {
                actor=new ActorState {id="ladder-check",x=4};
                for(int i=0;i<300;i++) movement.Step(actor,Vector2.up,false,dt);
                Require(Mathf.Abs(actor.y-3.25f)<.001f,"Верхний предел лестницы");
                for(int i=0;i<300;i++)
                {
                    movement.Step(actor,Vector2.down,false,dt);
                    Require(actor.y>=0,"Спуск не проходит сквозь нижний пол");
                }
                Require(actor.y==0 && actor.vy==0 && RoomSupport.IsSupported(factory,actor.Position),"Нижний предел лестницы устойчив");
            }

            var hub=RoomLayout.Load("hub");var hubMovement=new MovementController(hub);
            Require(hub.artwork.Length>0 && hub.ladders.Length==3,"Геометрия хаба");
            for(int slot=0;slot<4;slot++)
            {
                actor=new ActorState {id="hub-spawn-"+slot};CharacterPlacement.SpawnInHub(actor,slot);
                float initialY=actor.y;
                Require(actor.level=="hub" && RoomSupport.IsSupported(hub,actor.Position),"Точка появления на опоре хаба");
                for(int i=0;i<1200;i++) hubMovement.Step(actor,Vector2.zero,false,1f/60);
                Require(Mathf.Abs(actor.y-initialY)<.001f && actor.pose=="idle","Стояние в хабе без проваливания");
                for(int i=0;i<1200;i++) hubMovement.Step(actor,Vector2.right,false,1f/60);
                Require(RoomSupport.TryLowestFloor(hub,actor.x,out float floor) && actor.y>=floor,"Движение по хабу остаётся над нижним полом");
            }
            actor=new ActorState {id="old-hub",level="hub",x=-1.04f,y=0,health=1};
            Require(ActorPositionRecovery.Repair(actor,hub,"регрессия старого хаба") && actor.health==1 &&
                Math.Abs(actor.x-hub.spawnX)<.001f && Math.Abs(actor.y-hub.spawnY)<.001f,"Восстановление старой точки появления");

            actor=new ActorState {id="fallen-body",x=3.650094f,y=-5116.46f,vy=-452.2f,health=16,down=true};
            actor.inventory.Add(SharedInventory.Create("heal",7));actor.injuries.Add(new InjuryState{id="fracture"});
            var world=new WorldState();world.actors.Add(actor);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/movement-recovery",Guid.NewGuid().ToString("N")));
            var saves=new SaveStore(directory);saves.SaveWorld(world);string source=saves.PreserveRecoverySource();
            Require(ActorPositionRecovery.Repair(actor,factory,"регрессия провалившегося тела") && actor.y==0 &&
                actor.vy==0 && actor.health==16 && actor.down && actor.inventory[0].count==7 && actor.injuries.Count==1,
                "Восстановление позиции сохраняет последствия, ранения и предметы");
            saves.SaveWorld(world);
            var preserved=JsonUtility.FromJson<WorldState>(File.ReadAllText(source));
            Require(preserved.actors[0].y< -5000 && saves.LoadWorld().actors[0].y==0,"Исходный сейв остаётся в Recovery после исправления");
            Debug.Log("Проверки движения: PASS — появление в хабе, опоры, лестницы, восстановление старых позиций без потери состояния.");
        }

        private static void Require(bool condition,string message)
        {
            if(!condition) throw new InvalidOperationException("Проверка движения: "+message);
        }
    }
}
