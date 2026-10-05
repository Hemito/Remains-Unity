using System;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    public sealed class RoomMovingPlatformPlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый исходный ID")] private string stableId;
        [SerializeField,InspectorName("ID комнаты (отдельно от уровня)")] private string roomId;
        [SerializeField,InspectorName("Группа внутри комнаты")] private string groupId;
        [SerializeField,InspectorName("Определение платформы")] private MovingPlatformAsset definition;
        [SerializeField,InspectorName("Смещение второго конца маршрута")] private Vector2 routeDelta;
        public MovingPlatformAsset Definition=>definition;
        public void Configure(string id,string room,string group,MovingPlatformAsset type,Vector2 delta)
        {stableId=id;roomId=room;groupId=group;definition=type;routeDelta=delta;}
        public MovingPlatformState Read(RoomAuthoring room,string level)
        {
            if(definition==null) throw new InvalidOperationException("Расстановка платформы "+name+": нет определения "+stableId);
            definition.Validate();var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f) throw new InvalidOperationException("Расстановка платформы "+name+": нужен единичный масштаб.");
            var type=definition.CreateDefinition();
            var result=new MovingPlatformState {id=level+":"+stableId,definition=type.id,definitionVersion=type.version,level=level,roomId=roomId,groupId=groupId,
                startX=rect.x,startY=rect.y,endX=rect.x+routeDelta.x,endY=rect.y+routeDelta.y,endpoint=0,revision=0,removed=false,segment=null};
            result.Validate(type);return result;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(groupId) || definition==null ||
                !float.IsFinite(routeDelta.x) || !float.IsFinite(routeDelta.y) || routeDelta==Vector2.zero)
                Debug.LogWarning("Расстановка платформы "+name+": нужны исходный ID, комната, группа, определение и конечный маршрут.",this);
        }
    }
}
