using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Authored, non-blocking doorway beside the canonical arrival marker.
    public sealed class FactoryEntrance : MonoBehaviour
    {
        [SerializeField,InspectorName("Дверь выхода")] private RoomTravelPointAuthoring doorway;
        public void Configure(RoomTravelPointAuthoring point) => doorway=point;
        public void Prepare(FactoryLevelRoot root)
        {
            if(doorway==null || doorway.transform.parent!=root.transform)
                throw new InvalidOperationException("Вход завода: отсутствует сериализованная дочерняя дверь выхода.");
            var image=doorway.GetComponent<SpriteRenderer>();
            if(image==null || image.sprite==null)
                throw new InvalidOperationException("Вход завода: отсутствует изображение двери выхода.");
            var room=root.Prepared.Physics;
            var arrival=new Vector2(room.spawnX,room.spawnY);
            var position=arrival+Vector2.left*2;
            var size=image.sprite.bounds.size;
            string rejection=FactoryArrival.Rejection(room,new MovementHull{width=size.x,height=size.y},position);
            if(rejection!=null) throw new InvalidOperationException($"Вход завода {root.Prepared.Level}: дверь у исходного маркера недоступна. {rejection}");
            doorway.transform.localPosition=position;
            doorway.Configure(root.Prepared.Level+":entrance:hub-exit",TravelPointKind.Exit,
                "На выход","hub","original:hub:worldMap",image.sprite.bounds.size,arrival);
        }
    }
}
