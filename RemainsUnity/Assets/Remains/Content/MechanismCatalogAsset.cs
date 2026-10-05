using System;
using System.Linq;
using UnityEngine;
namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Каталог механизмов",fileName="mechanisms")]
    public sealed class MechanismCatalogAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Платформы")] private MovingPlatformAsset[] platforms=Array.Empty<MovingPlatformAsset>();
        [SerializeField,InspectorName("Кнопки")] private MechanismButtonAsset[] buttons=Array.Empty<MechanismButtonAsset>();
        public MovingPlatformAsset[] Platforms=>(MovingPlatformAsset[])platforms.Clone();
        public MechanismButtonAsset[] Buttons=>(MechanismButtonAsset[])buttons.Clone();
        public void Configure(MovingPlatformAsset[] moving,MechanismButtonAsset[] controls)
        {platforms=(MovingPlatformAsset[])moving.Clone();buttons=(MechanismButtonAsset[])controls.Clone();Validate();}
        public void Validate()
        {
            if(platforms==null || buttons==null || platforms.Any(p=>p==null) || buttons.Any(b=>b==null))
                throw new InvalidOperationException("Каталог механизмов "+name+": пустой список или определение.");
            foreach(var type in platforms) type.Validate();
            foreach(var type in buttons) type.Validate();
            var ids=platforms.Select(p=>p.Id).Concat(buttons.Select(b=>b.Id)).ToArray();
            if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length) throw new InvalidOperationException("Каталог механизмов "+name+": повтор ID определения.");
        }
        public MovingPlatformAsset Platform(string id)=>platforms.SingleOrDefault(p=>p.Id==id) ??
            throw new InvalidOperationException("Каталог механизмов "+name+": нет платформы "+id);
        public MechanismButtonAsset Button(string id)=>buttons.SingleOrDefault(b=>b.Id==id) ??
            throw new InvalidOperationException("Каталог механизмов "+name+": нет кнопки "+id);
        public static MechanismCatalogAsset Load()
        {
            var catalog=Resources.Load<MechanismCatalogAsset>("Content/mechanisms");
            if(catalog==null) throw new InvalidOperationException("Нет каталога механизмов Content/mechanisms.");
            catalog.Validate();return catalog;
        }
        private void OnValidate() {try {Validate();}catch(InvalidOperationException error){Debug.LogWarning(error.Message,this);}}
    }
}
