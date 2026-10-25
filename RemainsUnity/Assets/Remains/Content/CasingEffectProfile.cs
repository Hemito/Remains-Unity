using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Эффект гильзы",fileName="CasingEffect")]
    public sealed class CasingEffectProfile : ScriptableObject
    {
        [SerializeField,FormerlySerializedAs("sprite"),InspectorName("Спрайт гильзы вне каталога")] private Sprite legacySprite;
        [SerializeField,InspectorName("Гильза по ключу")] private CatalogSpriteReference spriteReference;
        [SerializeField,InspectorName("Socket относительно общей позы оружия (м)")] private Vector2 socket;
        [SerializeField,Min(.001f),InspectorName("Время эффекта (с)")] private float lifetime=.65f;
        [SerializeField,Min(0),InspectorName("Скорость назад от ствола (м/с)")] private float horizontalSpeed=1.2f;
        [SerializeField,Min(0),InspectorName("Скорость вверх (м/с)")] private float verticalSpeed=2.8f;
        [SerializeField,Min(0),InspectorName("Декоративная гравитация (м/с²)")] private float gravity=20;
        [SerializeField,InspectorName("Скорость вращения (град/с)")] private float rotationSpeed=400;
        public Sprite Sprite=>spriteReference!=null && spriteReference.IsConfigured?spriteReference.Resolve():legacySprite;
        public CatalogSpriteReference Reference=>spriteReference!=null && spriteReference.IsConfigured?spriteReference:legacySprite==null?null:CatalogSpriteReference.FromSprite(legacySprite);
        public void ConfigureReference(CatalogSpriteReference value)
        {spriteReference=value??throw new ArgumentNullException(nameof(value));legacySprite=null;Validate("ключ гильзы "+name);}
        public Vector2 Socket=>socket;
        public float Lifetime=>lifetime;
        public float HorizontalSpeed=>horizontalSpeed;
        public float VerticalSpeed=>verticalSpeed;
        public float Gravity=>gravity;
        public float RotationSpeed=>rotationSpeed;
        public void Configure(Sprite value,Vector2 offset,float lifetime=.65f,float horizontalSpeed=1.2f,float verticalSpeed=2.8f,float gravity=20,float rotationSpeed=400)
        {legacySprite=value;spriteReference=null;socket=offset;this.lifetime=lifetime;this.horizontalSpeed=horizontalSpeed;this.verticalSpeed=verticalSpeed;this.gravity=gravity;this.rotationSpeed=rotationSpeed;Validate("Configure "+name);}
        public void Validate(string context)
        {
            if(this==null)throw new InvalidOperationException("Назначенный профиль гильзы уничтожен: "+context);
            var source=Reference;source?.Validate(context);
            if(source==null || source.Rect.width<=0 || source.Rect.height<=0 ||
                !float.IsFinite(socket.x) || !float.IsFinite(socket.y) || !float.IsFinite(lifetime) || lifetime<=0 ||
                !float.IsFinite(horizontalSpeed) || horizontalSpeed<0 || !float.IsFinite(verticalSpeed) || verticalSpeed<0 ||
                !float.IsFinite(gravity) || gravity<0 || !float.IsFinite(rotationSpeed))
                throw new InvalidOperationException($"Некорректный эффект гильзы: {context}, profile={name}, key={source?.Key}, socket={socket}, lifetime={lifetime}, horizontal={horizontalSpeed}, vertical={verticalSpeed}, gravity={gravity}, rotation={rotationSpeed}.");
        }
        private void OnValidate()
        {try {Validate("ассет "+name);}catch(InvalidOperationException error){Debug.LogWarning(error.Message,this);}}
    }
}
