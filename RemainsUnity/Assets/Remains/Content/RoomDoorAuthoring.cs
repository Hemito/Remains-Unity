using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomDoorAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID размещения")] private string stableId;
        [SerializeField,InspectorName("Начальное состояние: открыта")] private bool initiallyOpen;
        [SerializeField,InspectorName("Ключ поверхности для попаданий")] private string surfaceTexture;
        public void Configure(string id,bool open,string surface)
        {stableId=id;initiallyOpen=open;surfaceTexture=surface;GetComponent<DoorView>().Show(open);}
        public DoorState Read(RoomAuthoring room,string level)
        {
            var view=GetComponent<DoorView>();
            if(view==null) throw new InvalidOperationException($"Размещение двери {name}: нет DoorView.");
            var rect=RoomAuthoring.ReadRect(transform,room.transform);var size=view.Definition.Size;
            var result=new DoorState {id=stableId,definition=view.Definition.Id,level=level,
                x=rect.x-size.x*rect.width*.5f,y=rect.y,width=size.x*rect.width,height=size.y*rect.height,
                surfaceTexture=surfaceTexture,open=initiallyOpen};
            result.Validate();return result;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(surfaceTexture))
                Debug.LogWarning($"Размещение двери {name}: задайте устойчивый ID и ключ поверхности.",this);
            if(TryGetComponent<DoorView>(out var view) && view.Definition!=null) view.Show(initiallyOpen);
        }
    }
}
