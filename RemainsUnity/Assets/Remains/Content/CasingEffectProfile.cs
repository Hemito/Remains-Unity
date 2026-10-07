using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Эффект гильзы",fileName="CasingEffect")]
    public sealed class CasingEffectProfile : ScriptableObject
    {
        [SerializeField,InspectorName("Исходный спрайт гильзы")] private Sprite sprite;
        [SerializeField,InspectorName("Socket относительно общей позы оружия (м)")] private Vector2 socket;
        [SerializeField,Min(.001f),InspectorName("Время эффекта (с)")] private float lifetime=.65f;
        [SerializeField,Min(0),InspectorName("Скорость назад от ствола (м/с)")] private float horizontalSpeed=1.2f;
        [SerializeField,Min(0),InspectorName("Скорость вверх (м/с)")] private float verticalSpeed=2.8f;
        [SerializeField,Min(0),InspectorName("Декоративная гравитация (м/с²)")] private float gravity=20;
        [SerializeField,InspectorName("Скорость вращения (град/с)")] private float rotationSpeed=400;
        public Sprite Sprite=>sprite;
        public Vector2 Socket=>socket;
        public float Lifetime=>lifetime;
        public float HorizontalSpeed=>horizontalSpeed;
        public float VerticalSpeed=>verticalSpeed;
        public float Gravity=>gravity;
        public float RotationSpeed=>rotationSpeed;
        public void Configure(Sprite value,Vector2 offset,float lifetime=.65f,float horizontalSpeed=1.2f,float verticalSpeed=2.8f,float gravity=20,float rotationSpeed=400)
        {sprite=value;socket=offset;this.lifetime=lifetime;this.horizontalSpeed=horizontalSpeed;this.verticalSpeed=verticalSpeed;this.gravity=gravity;this.rotationSpeed=rotationSpeed;Validate("Configure "+name);}
        public void Validate(string context)
        {
            if(this==null)throw new InvalidOperationException("Назначенный профиль гильзы уничтожен: "+context);
            if(sprite==null || sprite.texture==null || !float.IsFinite(sprite.pixelsPerUnit) || sprite.pixelsPerUnit<=0 || sprite.rect.width<=0 || sprite.rect.height<=0 ||
                !float.IsFinite(socket.x) || !float.IsFinite(socket.y) || !float.IsFinite(lifetime) || lifetime<=0 ||
                !float.IsFinite(horizontalSpeed) || horizontalSpeed<0 || !float.IsFinite(verticalSpeed) || verticalSpeed<0 ||
                !float.IsFinite(gravity) || gravity<0 || !float.IsFinite(rotationSpeed))
                throw new InvalidOperationException($"Некорректный эффект гильзы: {context}, profile={name}, sprite={sprite?.name}, socket={socket}, lifetime={lifetime}, horizontal={horizontalSpeed}, vertical={verticalSpeed}, gravity={gravity}, rotation={rotationSpeed}.");
        }
        private void OnValidate()
        {try {Validate("ассет "+name);}catch(InvalidOperationException error){Debug.LogWarning(error.Message,this);}}
    }
}
