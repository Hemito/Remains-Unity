using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class ProjectilePathChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {platforms=new[]{new RoomSurface {x=5,y=-1,width=.01f,height=2}}};
            foreach(int rate in new[]{30,60,144})
            {
                var flight=new LinearProjectilePath(Vector2.zero,Vector2.right,90,20);
                int contacts=0;float duration=0;
                for(int i=0;i<rate && !flight.Finished;i++)
                {
                    var step=flight.Advance(1f/rate,(a,b)=>ShotObstacles.FirstContact(room,a,b));
                    if(step.contact) contacts++;duration+=step.duration;
                }
                Require(contacts==1 && Mathf.Abs(flight.Position.x-5)<.0001f && Mathf.Abs(duration-5f/90)<.0001f,"Частота кадров меняет контакт тонкой стены.");
                Require(!flight.Advance(1,(_,_)=>throw new Exception("Повторная проверка законченного полёта.")).advanced,"Завершённый снаряд движется.");
            }
            var large=new LinearProjectilePath(Vector2.zero,Vector2.right,1200,20);
            Require(large.Advance(.5f,(a,b)=>ShotObstacles.FirstContact(room,a,b)).contact && Mathf.Abs(large.Position.x-5)<.0001f,"Быстрый снаряд пропустил стену.");
            var boundary=new LinearProjectilePath(Vector2.zero,Vector2.right,5,5);
            Require(boundary.Advance(1,(a,b)=>ShotObstacles.FirstContact(room,a,b)).contact,"Контакт ровно в конце пути принят за отсутствие препятствия.");
            Require(ShotObstacles.FirstContact(room,Vector2.zero,Vector2.right)==null,"Свободный отрезок создаёт контакт.");
            var pending=new LinearProjectilePath(Vector2.zero,Vector2.right,10,20);
            int queries=0;
            Require(!pending.Advance(0,(a,b)=>{queries++;return 0;}).advanced && queries==0,"Пауза проверяет будущие контакты.");
            var early=pending.Advance(.2f,(a,b)=>HitGeometry.CircleEntry(new Vector2(5,0),.2f,a,b));
            Require(!early.contact && pending.Position.x==2,"Контакт произошёл до прихода снаряда.");
            // Target moves away after launch; the resolver sees the current position.
            Require(!pending.Advance(.4f,(a,b)=>HitGeometry.CircleEntry(new Vector2(5,3),.2f,a,b)).contact,"Сохранено попадание по прежней позиции цели.");
            Require(pending.Advance(.2f,(a,b)=>HitGeometry.CircleEntry(new Vector2(7,0),.2f,a,b)).contact,"Не замечена вошедшая в путь цель.");
            var original=new LinearProjectilePath(Vector2.zero,Vector2.right,10,20);
            original.Advance(.3f,(_,_)=>null);
            var restored=new LinearProjectilePath(original.Origin,original.Direction,original.Speed,original.Range,original.Traveled,original.Finished);
            original.Advance(.4f,(_,_)=>null);restored.Advance(.4f,(_,_)=>null);
            Require(original.Position==restored.Position,"Восстановление прогресса меняет траекторию.");
            restored.Advance(100,(_,_)=>null);Require(restored.Finished && restored.Position.x==20,"Полёт выходит за конечную дальность.");
            bool invalid=false;
            try {new LinearProjectilePath(Vector2.zero,Vector2.right,float.NaN,20);} catch(ArgumentException) {invalid=true;}
            Require(invalid,"Не отклонена некорректная скорость.");
            Debug.Log("Траектория снаряда: PASS — время прихода, подвижная цель, тонкая стена, пауза, восстановление, 30/60/144 Гц.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
