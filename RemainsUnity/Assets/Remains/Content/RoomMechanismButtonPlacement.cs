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
        [SerializeField,InspectorName("Двери группы вместо платформы")] private string[] doors=Array.Empty<string>();
        [SerializeField,InspectorName("Размер области (0 — обычная кнопка)")] private Vector2 area;
        public MechanismButtonAsset Definition=>definition;
        public void Configure(string id,string room,string group,string platform,MechanismButtonAsset type,string initialRepair)
        {stableId=id;roomId=room;groupId=group;platformId=platform;definition=type;repairState=initialRepair;}
        public void ConfigureDoors(string[] group,Vector2 size) {doors=group;area=size;}
        public MechanismButtonState Read(RoomAuthoring room,string level)
        {
            if(definition==null) throw new InvalidOperationException("Расстановка кнопки "+name+": нет определения "+stableId);
            definition.Validate();var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f) throw new InvalidOperationException("Расстановка кнопки "+name+": нужен единичный масштаб.");
            var result=new MechanismButtonState {id=level+":"+stableId,definition=definition.Id,level=level,roomId=roomId,groupId=groupId,
                platformId=doors.Length>0?null:level+":"+platformId,x=rect.x,y=rect.y,repairState=repairState,revision=0,pressed=false,removed=false,
                areaWidth=area.x,areaHeight=area.y};
            foreach(string door in doors) result.doors.Add(level+":"+door);
            result.Validate();return result;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(groupId) ||
                string.IsNullOrWhiteSpace(platformId) && doors.Length==0 || definition==null || !MechanismRepairStates.BuiltIn(repairState))
                Debug.LogWarning("Расстановка кнопки "+name+": нужны исходный ID, комната, группа, платформа, определение и состояние ремонта.",this);
        }
    }
}
