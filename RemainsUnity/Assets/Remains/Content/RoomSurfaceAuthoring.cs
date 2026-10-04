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
        [SerializeField,Range(-1,1),InspectorName("Наклон: −1 влево, 0 плоская, 1 вправо")] private int slopeDirection;
        public void Configure(RoomSurface value)
        {
            texture=value.texture;movementSound=value.movementSound;oneWay=value.oneWay;shotsPassThrough=value.shotsPassThrough;
            slopeDirection=value.slopeDirection;
            transform.localPosition=new Vector3(value.x,value.y,0);transform.localScale=new Vector3(value.width,value.height,1);
        }
        public RoomSurface Read(RoomAuthoring root)
        {
            var rect=RoomAuthoring.ReadRect(transform,root.transform);
            if(slopeDirection!=0 && (!oneWay || !shotsPassThrough) || Mathf.Abs(slopeDirection)>1)
                throw new System.InvalidOperationException($"Наклонная опора {name}: нужны направление ±1 и проходимость снизу/для выстрелов.");
            return new RoomSurface {x=rect.x,y=rect.y,width=rect.width,height=rect.height,texture=texture,movementSound=movementSound,oneWay=oneWay,shotsPassThrough=shotsPassThrough,slopeDirection=slopeDirection};
        }
        private void OnValidate()
        {slopeDirection=Mathf.Clamp(slopeDirection,-1,1);if(slopeDirection!=0) oneWay=shotsPassThrough=true;}
        private void OnDrawGizmos()
        {
            var previous=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=oneWay?Color.yellow:Color.cyan;
            if(slopeDirection==0) Gizmos.DrawWireCube(new Vector3(.5f,.5f,0),new Vector3(1,1,0));
            else Gizmos.DrawLine(new Vector3(0,slopeDirection>0?0:1,0),new Vector3(1,slopeDirection>0?1:0,0));
            Gizmos.matrix=previous;
        }
    }
}
