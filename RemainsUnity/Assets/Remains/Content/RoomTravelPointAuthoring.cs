using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomTravelPointAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID")] private string stableId;
        [SerializeField,InspectorName("Назначение")] private TravelPointKind kind;
        [SerializeField,InspectorName("Название для игрока")] private string title;
        [SerializeField,InspectorName("Локация назначения")] private string destination;
        [SerializeField,InspectorName("ID точки прибытия (пусто — вход уровня)")] private string arrivalPoint;
        [SerializeField,InspectorName("Размер области взаимодействия")] private Vector2 size;
        [SerializeField,Min(.1f),InspectorName("Дальность взаимодействия")] private float range=1.3f;
        [SerializeField,InspectorName("Место прибытия к этой точке")] private Vector2 arrival;
        public void Configure(string id,TravelPointKind role,string label,string target,string targetPoint,Vector2 bounds,Vector2 spawn)
        {stableId=id;kind=role;title=label;destination=target;arrivalPoint=targetPoint;size=bounds;arrival=spawn;}
        public TravelPoint Read(RoomAuthoring room,string level)
        {
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            var result=new TravelPoint {id=stableId,kind=kind,title=title,level=level,destination=destination,arrivalPoint=arrivalPoint,
                x=rect.x-size.x*rect.width*.5f,y=rect.y,width=size.x*rect.width,height=size.y*rect.height,range=range,
                arrivalX=arrival.x,arrivalY=arrival.y};
            result.Validate();return result;
        }
        private void OnValidate()
        {
            range=Mathf.Max(.1f,range);
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(destination) || size.x<=0 || size.y<=0)
                Debug.LogWarning($"Точка перехода {name}: задайте ID, назначение и положительный размер.",this);
        }
        public static TravelPoint[] ReadAll(RoomAuthoring room,string level)
        {
            var points=new List<TravelPoint>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var component in room.GetComponentsInChildren<RoomTravelPointAuthoring>(true))
            {
                if(!room.Included(component)) continue;
                var point=component.Read(room,level);
                if(!ids.Add(point.id)) throw new InvalidOperationException("Повтор точки перехода: "+point.id);
                points.Add(point);
            }
            return points.ToArray();
        }
    }
}
