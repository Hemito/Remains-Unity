using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class GrenadeTelekinesisChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {left=0,right=10,bottom=0,top=10,platforms=Array.Empty<RoomSurface>()};
            var world=new WorldState();
            var source=new ActorState {id="source",owner="a",player=true,connected=true,level="hub"};
            var catcher=new ActorState {id="catcher",owner="b",player=true,connected=true,level="hub",mana=100};
            world.actors.Add(source);world.actors.Add(catcher);
            var grenade=new GrenadeState {id="g",attacker=source.id,level=source.level,owner=source.owner,generation=1,
                Position=new Vector2(9.7f,5),Velocity=new Vector2(3,1),rotationVersion=1,angularVelocity=5,remaining=2};
            world.grenades.Add(grenade);
            GrenadeRules.Hold(grenade,catcher);
            Require(grenade.holder==catcher.id && grenade.owner==catcher.owner && grenade.generation==2 &&
                grenade.Velocity==Vector2.zero && grenade.angularVelocity==0,"Захват не остановил полёт и не сменил владельца.");
            GrenadeRules.Step(grenade,room,.25f);
            Require(Mathf.Abs(grenade.remaining-1.75f)<.001f && grenade.Position==new Vector2(9.7f,5),"Запал или положение при удержании.");
            GrenadeRules.MoveHeld(grenade,room,new Vector2(100,5),.1f);
            Require(grenade.x>9.7f && grenade.x<=10 && grenade.Velocity.magnitude<=10.001f,"Захват прошёл сквозь стену или превысил скорость.");
            GrenadeRules.MoveHeld(grenade,room,new Vector2(7,5),.1f);
            var release=grenade.Position;
            GrenadeRules.ReleaseHold(grenade);
            GrenadeRules.Step(grenade,room,.1f);
            Require(grenade.holder==null && grenade.generation==3 && grenade.x<release.x && grenade.y<release.y,"Отпущенная граната не вернулась к баллистике.");
            GrenadeRules.Hold(grenade,catcher);
            var resumed=new WorldState();
            resumed.grenades.Add(grenade.Copy());
            GrenadeRules.BeginSession(resumed);
            var restored=resumed.grenades[0];
            Require(restored.holder==null && restored.owner==null && restored.remaining==grenade.remaining &&
                restored.Position==grenade.Position && restored.generation==grenade.generation+1,"Вход из сохранения изменил запал или сохранил устаревший захват.");
            catcher.connected=false;
            Require(GrenadeRules.ReassignAndReportChanges(world,new[]{"a"}) && grenade.holder==null && grenade.owner==source.owner &&
                grenade.remaining==restored.remaining,"Уход владельца не передал живую гранату.");
            catcher.connected=true;
            GrenadeRules.Hold(grenade,catcher);
            for(int frame=0;frame<120 && grenade.remaining>0;frame++) GrenadeRules.Step(grenade,room,1f/60);
            Require(grenade.remaining==0 && GrenadeRules.Detonate(world,grenade,Array.Empty<string>())!=null &&
                GrenadeRules.Detonate(world,grenade,Array.Empty<string>())==null,"Запал в руке должен завершиться одним взрывом.");
        }

        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException(message);}
    }
}
