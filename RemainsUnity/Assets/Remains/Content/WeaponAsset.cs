using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Оружие",fileName="Weapon")]
    public sealed class WeaponAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение оружия")]
        private WeaponDefinition definition=new();
        [SerializeField,InspectorName("Префаб оружия")] private Remains.Presentation.WeaponView presentation;
        public Remains.Presentation.WeaponView Presentation=>presentation;
        [SerializeField,InspectorName("Анимация выстрела (необязательно)")] private WeaponShotClip shotAnimation;
        public WeaponShotClip ShotAnimation=>shotAnimation;
        [SerializeField,Min(0),InspectorName("Множитель света дульной вспышки")] private float muzzleFlashGain=1;
        public float MuzzleFlashGain=>muzzleFlashGain;
        [SerializeField,InspectorName("Анимация перезарядки (необязательно)")] private WeaponReloadClip reloadAnimation;
        public WeaponReloadClip ReloadAnimation=>reloadAnimation;
        [SerializeField,InspectorName("Эффект гильзы (пусто — намеренно отсутствует)")] private CasingEffectProfile casingProfile;
        public CasingEffectProfile CasingProfile=>casingProfile;
        public void SetCasingProfile(CasingEffectProfile value)=>casingProfile=value;
        [SerializeField,InspectorName("Префаб летящей гранаты")] private SpriteRenderer grenadeFlight;
        public SpriteRenderer GrenadeFlight=>grenadeFlight;
        public void SetGrenadeFlight(SpriteRenderer value)=>grenadeFlight=value;
        public string Id=>definition.id;
        public void SetShotAnimation(WeaponShotClip value)=>shotAnimation=value;
        public void SetReloadAnimation(WeaponReloadClip value)=>reloadAnimation=value;
        public void SetPresentation(Remains.Presentation.WeaponView value)=>presentation=value;
        public WeaponDefinition CreateDefinition()
        {
            var result=StateJson.FromJson<WeaponDefinition>(StateJson.ToJson(definition));
            // Unity serializes absent arrays as empty arrays; both mean no authored melee motion.
            if(result.meleeMotion?.Length==0) result.meleeMotion=null;
            return result;
        }
        public void SetDefinition(WeaponDefinition value)=>definition=StateJson.FromJson<WeaponDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            if(!float.IsFinite(muzzleFlashGain) || muzzleFlashGain<0)
            {Debug.LogWarning($"Оружие {name}: неверный множитель света вспышки {muzzleFlashGain}.",this);muzzleFlashGain=0;}
            if(definition==null || string.IsNullOrWhiteSpace(definition.id))
            {Debug.LogWarning($"Оружие {name}: задайте стабильный ID определения.",this);return;}
            try {WeaponCatalogLoader.Validate(CreateDefinition(),"ассет "+name);}
            catch(System.IO.InvalidDataException error) {Debug.LogWarning(error.Message,this);}
            if(!ReferenceEquals(casingProfile,null))
            {
                try
                {
                    casingProfile.Validate("оружие "+definition.id);
                    if(definition.projectileSpeed<=0)throw new System.InvalidOperationException("Гильза задана не для дальнего выстрела: weapon="+definition.id);
                }
                catch(System.InvalidOperationException error){Debug.LogWarning(error.Message,this);}
            }
            if(reloadAnimation!=null)
            {
                try
                {
                    if(string.IsNullOrEmpty(definition.ammo) || definition.reloadSeconds<=0)
                        throw new System.InvalidOperationException($"Оружие {definition.id}: анимация перезарядки задана без боеприпаса/положительного времени.");
                    reloadAnimation.ValidateAgainst(presentation==null?(CatalogSpriteReference)null:presentation.RestReference);
                }
                catch(System.InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
            }
        }
    }
}
