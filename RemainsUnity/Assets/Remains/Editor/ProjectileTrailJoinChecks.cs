using System;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class ProjectileTrailJoinChecks
    {
        public static void Run()
        {
            var wait=new ProjectileTrailJoin();
            var tail=wait.Tail(Vector2.zero,Vector2.right,.1f,0,3,false,new Vector2(.5f,.2f));
            Require(!ProjectileTrailJoin.IsForward(tail,new Vector2(.1f,0),Vector2.right),"След нарисован назад до дула");
            tail=wait.Tail(Vector2.zero,Vector2.right,1,0,3,false,new Vector2(.6f,.3f));
            Require(tail==new Vector2(.6f,.3f),"Ожидание сохранило устаревшее дуло");
            Require(wait.Tail(Vector2.zero,Vector2.right,7,4,3,false,Vector2.one*10)==new Vector2(4,0),"След не вернулся на каноническую линию");
            var late=new ProjectileTrailJoin();
            Require(late.Tail(Vector2.zero,Vector2.right,5,2,3,false,Vector2.one)==new Vector2(2,0),"Поздний след растянут обратно к дулу");
            var root=new GameObject("Проверка соединения следа");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();var observer=new ActorState {id="observer",owner="local",player=true,connected=true,level="hub"};
                var actor=new ActorState {id="source",owner="remote",player=true,connected=true,level="hub",weapon="pistol"};
                world.actors.Add(observer);world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,observer.id);
                typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,observer.owner);
                actor.Position+=new Vector2(.3f,.2f)-WeaponPose.Muzzle(actor);
                var launch=new AttackEvent {id="join-check",attacker=actor.id,sourceOwner=actor.owner,weapon="pistol",level="hub",endX=10};
                var flight=new ProjectileFlight {state=new ProjectileState {launch=launch},path=new LinearProjectilePath(Vector2.zero,Vector2.right,90,10)};
                ShotArtwork.Prepare();ShotArtwork.TryGet("pistol",out var artwork);
                var obj=new GameObject("След");obj.transform.SetParent(root.transform);
                var view=obj.AddComponent<ProjectileVisual>();view.Initialize(flight,artwork,session);view.JoinRemoteMuzzle();
                void Draw()=>typeof(ProjectileVisual).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                flight.path.Advance(1f/144,(_,_)=>null);Draw();
                Require(Vector2.Distance(view.TrailStart,WeaponPose.Muzzle(actor))<.0001f && view.TrailEnd==flight.path.Position,"Начало не у дула или передний край изменил путь");
                Vector2 renderedTail=view.transform.TransformPoint(new Vector3(artwork.trace.bounds.min.x,artwork.trace.bounds.center.y));
                Require(Vector2.Distance(renderedTail,view.TrailStart)<.0001f,"Рендерер не использует рассчитанное начало следа");
                var frozenStart=view.TrailStart;Draw();Require(view.TrailStart==frozenStart,"Повтор кадра продвинул след");
                flight.state.generation++;Draw();
                Require(view.TrailStart==Vector2.zero,"Восстановленный полёт сохранил соединение со старым дулом");
                actor.x+=2;flight.path.Advance(.09f,(_,_)=>null);Draw();
                Require(Vector2.Distance(view.TrailStart,new Vector2(flight.path.Traveled-artwork.length,0))<.0001f,"Поздний след тянется за персонажем");
                flight.path.Advance(.02f,(_,_)=>.5f);flight.contact=true;Draw();
                Require((Vector2)view.transform.position==flight.path.Position && view.GetComponent<SpriteRenderer>().sprite==artwork.impact[0] &&
                    Quaternion.Angle(view.transform.rotation,Quaternion.identity)<.001f,"Попадание смещено или повёрнуто косметическим соединением");
                Require(launch.originX==0 && launch.originY==0 && launch.endX==10,"Представление переписало исходную атаку");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Соединение следа: PASS — дуло, физический передний край, отсутствие обратного следа, завершение и неизменная точка попадания.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
