using System;
using UnityEngine;

namespace Remains.Content
{
    // Placement data is independent of the preview and of network actor instances.
    public sealed class RoomActorPlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID объекта")] private string stableId;
        [SerializeField,InspectorName("ID типа персонажа")] private string actorType;
        [SerializeField,InspectorName("Направление (−1 влево, 1 вправо)")] private int facing=1;
        [SerializeField,InspectorName("Закреплённая мишень (исходное свойство)")] private bool fixedPlacement;
        [SerializeField,InspectorName("Предпросмотр, не игровой персонаж")] private SpriteRenderer preview;
        public bool FixedPlacement=>fixedPlacement;
        public void Configure(string id,string type,int direction,bool anchored,SpriteRenderer view)
        {stableId=id;actorType=type;facing=direction;fixedPlacement=anchored;preview=view;RefreshPreview();}
        public TrainingPlacement Read(RoomAuthoring room)
        {
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(actorType) || Math.Abs(facing)!=1 ||
                Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f)
                throw new InvalidOperationException($"Расстановка {name}: нужны ID, направление ±1 и единичный масштаб.");
            return new TrainingPlacement {id=stableId,type=actorType,x=rect.x,y=rect.y,facing=facing};
        }
        private void RefreshPreview() {if(preview!=null) preview.flipX=facing<0;}
        private void OnValidate()
        {
            RefreshPreview();
            if(Math.Abs(facing)!=1) Debug.LogWarning($"Расстановка {name}: неверное направление {facing}; ожидается −1 или 1.",this);
        }
    }
}
