using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomSurfaceAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Ключ оформления")] private string texture;
        [SerializeField,InspectorName("Набор звуков шагов"),Tooltip("Пустое значение: использовать настройку комнаты.")] private string movementSound;
        public void SetMovementSound(string bank)=>movementSound=bank;
        [SerializeField,InspectorName("Проходимая снизу платформа")] private bool oneWay;
        [SerializeField,InspectorName("Пропускает выстрелы")] private bool shotsPassThrough;
        public void Configure(RoomSurface value)
        {
            texture=value.texture;movementSound=value.movementSound;oneWay=value.oneWay;shotsPassThrough=value.shotsPassThrough;
            transform.localPosition=new Vector3(value.x,value.y,0);transform.localScale=new Vector3(value.width,value.height,1);
        }
        public RoomSurface Read(RoomAuthoring root)
        {
            var rect=RoomAuthoring.ReadRect(transform,root.transform);
            return new RoomSurface {x=rect.x,y=rect.y,width=rect.width,height=rect.height,texture=texture,movementSound=movementSound,oneWay=oneWay,shotsPassThrough=shotsPassThrough};
        }
        private void OnDrawGizmos()
        {
            var previous=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=oneWay?Color.yellow:Color.cyan;
            Gizmos.DrawWireCube(new Vector3(.5f,.5f,0),new Vector3(1,1,0));Gizmos.matrix=previous;
        }
    }
}
