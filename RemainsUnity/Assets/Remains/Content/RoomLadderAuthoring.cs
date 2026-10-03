using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomLadderAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Направление тела при хвате")] private LadderFacing facing=LadderFacing.Right;
        [SerializeField,InspectorName("Смещение линии движения")] private float movementOffset;
        [SerializeField,InspectorName("Смещение изображения")] private float artworkOffset;
        [SerializeField,InspectorName("Ключ текстуры")] private string texture;
        [SerializeField,InspectorName("Ключ изображения лестницы")] private string artwork;
        [SerializeField,InspectorName("Исходный масштаб текстуры")] private bool nativeScale;
        public void Configure(RoomLadder value)
        {
            facing=value.facing;movementOffset=value.movementOffset;artworkOffset=value.artworkOffset;texture=value.texture;artwork=value.artwork;nativeScale=value.nativeScale;
            transform.localPosition=new Vector3(value.x,value.bottom,0);transform.localScale=new Vector3(1,value.top-value.bottom,1);
        }
        public RoomLadder Read(RoomAuthoring root)
        {
            var rect=RoomAuthoring.ReadRect(transform,root.transform);
            if(facing!=LadderFacing.Left && facing!=LadderFacing.Right)
                throw new System.InvalidOperationException($"Лестница {name}: неверное направление хвата {(int)facing}.");
            return new RoomLadder {x=rect.x,bottom=rect.y,top=rect.yMax,facing=facing,movementOffset=movementOffset,artworkOffset=artworkOffset,texture=texture,artwork=artwork,nativeScale=nativeScale};
        }
        private void OnDrawGizmos()
        {
            var previous=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=Color.green;
            Gizmos.DrawLine(Vector3.zero,Vector3.up);Gizmos.matrix=previous;
        }
    }
}
