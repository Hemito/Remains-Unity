using System;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomContainerPlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID контейнера")] private string stableId;
        [SerializeField,InspectorName("Определение объекта и массы")] private PropAsset definition;
        public PropAsset Definition=>definition;
        public void Configure(string id,PropAsset asset) {stableId=id;definition=asset;}
        public TrainingPlacement Read(RoomAuthoring room)
        {
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(string.IsNullOrWhiteSpace(stableId) || definition==null ||
                Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f)
                throw new InvalidOperationException($"Расстановка {name}: нужны ID, определение объекта и единичный масштаб.");
            var data=definition.CreateDefinition();PropCatalog.Validate(data);
            return new TrainingPlacement {id=stableId,type=data.id,x=rect.x,y=rect.y};
        }
    }
}
