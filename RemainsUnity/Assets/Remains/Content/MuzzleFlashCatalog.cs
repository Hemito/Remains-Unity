using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Свет боеприпасов",fileName="MuzzleFlashCatalog")]
    public sealed class MuzzleFlashCatalog : ScriptableObject
    {
        [Serializable] public sealed class Profile
        {
            [InspectorName("ID патрона")] public string ammo;
            [InspectorName("ID оружия без патрона (вместо ID патрона)")] public string weapon;
            [Min(0),InspectorName("Сила свечения исходных пикселей")] public float gain;
        }
        [SerializeField] private Profile[] profiles=Array.Empty<Profile>();
        public static string AmmoKey(string ammo)=>ammo!=null && ammo.StartsWith("item:",StringComparison.Ordinal)?ammo.Substring(5):ammo;
        public static string ProfileKey(string weapon,string ammo)=>string.IsNullOrEmpty(ammo)?"emitter:"+weapon:AmmoKey(ammo);
        public void Configure(Profile[] values) {profiles=values;Read();}
        public Dictionary<string,float> Read()
        {
            var result=new Dictionary<string,float>(StringComparer.Ordinal);
            if(profiles==null) throw new InvalidOperationException($"Каталог {name}: отсутствует список профилей света патронов.");
            foreach(var profile in profiles)
                if(profile==null || string.IsNullOrWhiteSpace(profile.ammo)==string.IsNullOrWhiteSpace(profile.weapon) || !float.IsFinite(profile.gain) || profile.gain<0 || !result.TryAdd(ProfileKey(profile.weapon,profile.ammo),profile.gain))
                    throw new InvalidOperationException($"Каталог {name}: неверный или повторный профиль света патрона {profile?.ammo}.");
            return result;
        }
        private void OnValidate() {try {Read();}catch(InvalidOperationException e){Debug.LogWarning(e.Message,this);}}
    }
}
