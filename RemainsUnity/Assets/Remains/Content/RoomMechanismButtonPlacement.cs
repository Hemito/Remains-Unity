using System;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    public sealed class RoomMechanismButtonPlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый исходный ID")] private string stableId;
        [SerializeField,InspectorName("ID комнаты (отдельно от уровня)")] private string roomId;
        [SerializeField,InspectorName("Группа внутри комнаты")] private string groupId;
        [SerializeField,InspectorName("Исходный ID связанной платформы")] private string platformId;
        [SerializeField,InspectorName("Определение кнопки")] private MechanismButtonAsset definition;
        [SerializeField,InspectorName("Начальное состояние ремонта")] private string repairState=MechanismRepairStates.NeedsRepair;
        public MechanismButtonAsset Definition=>definition;
        public void Configure(string id,string room,string group,string platform,MechanismButtonAsset type,string initialRepair)
        {stableId=id;roomId=room;groupId=group;platformId=platform;definition=type;repairState=initialRepair;}
        public MechanismButtonState Read(RoomAuthoring room,string level)
        {
            if(definition==null) throw new InvalidOperationException("Расстановка кнопки "+name+": нет определения "+stableId);
            definition.Validate();var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f) throw new InvalidOperationException("Расстановка кнопки "+name+": нужен единичный масштаб.");
            var result=new MechanismButtonState {id=level+":"+stableId,definition=definition.Id,level=level,roomId=roomId,groupId=groupId,
                platformId=level+":"+platformId,x=rect.x,y=rect.y,repairState=repairState,revision=0,pressed=false,removed=false};
            result.Validate();return result;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(groupId) ||
                string.IsNullOrWhiteSpace(platformId) || definition==null || !MechanismRepairStates.BuiltIn(repairState))
                Debug.LogWarning("Расстановка кнопки "+name+": нужны исходный ID, комната, группа, платформа, определение и состояние ремонта.",this);
        }
    }
}
