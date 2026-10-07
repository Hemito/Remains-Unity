using System;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponShotEffectsAuthoring
    {
        public const string SpritePath="Assets/Remains/Resources/Original/Weapons/ShotEffects/casing.png";
        public const string ProfilePath="Assets/Remains/Prefabs/Weapons/ShotEffects/pistol-casing.asset";
        public static void Install()
        {
            VectorArtworkReserveChecks.VerifyEntry("casing");
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            EnsureFolder("Assets/Remains/Prefabs/Weapons/ShotEffects");
            var profile=AssetDatabase.LoadAssetAtPath<CasingEffectProfile>(ProfilePath);
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<CasingEffectProfile>();profile.Configure(sprite,Vector2.zero);
                AssetDatabase.CreateAsset(profile,ProfilePath);
            }
            else
            {
                profile.Validate(ProfilePath);
                if(profile.Sprite!=sprite || profile.Socket!=Vector2.zero || profile.Lifetime!=.65f || profile.HorizontalSpeed!=1.2f ||
                    profile.VerticalSpeed!=2.8f || profile.Gravity!=20 || profile.RotationSpeed!=400)
                    throw new InvalidOperationException("Профиль гильзы изменён вручную; автоматическая замена запрещена: "+ProfilePath);
            }
            foreach(string id in new[]{"pistol","energy","npc-shot"})
            {
                var weapon=AssetDatabase.LoadAssetAtPath<WeaponAsset>("Assets/Remains/Resources/Content/weapons/"+id+".asset");
                if(weapon==null) throw new InvalidOperationException("Нет оружия: "+id);
                var definition=weapon.CreateDefinition();
                var calibration=id=="pistol"?WeaponRecoilProfile.PistolCalibration():id=="energy"?WeaponRecoilProfile.EnergyCalibration():WeaponRecoilProfile.Disabled();
                // Unity serializes a new absent inline class as its neutral all-zero instance.
                if(definition.recoil!=null && !definition.recoil.SameValues(WeaponRecoilProfile.Disabled()) && !definition.recoil.SameValues(calibration))
                    throw new InvalidOperationException("Профиль отдачи изменён вручную; автоматическая замена запрещена: "+id);
                definition.recoil=calibration;weapon.SetDefinition(definition);
                if(id=="pistol")
                {
                    if(weapon.CasingProfile!=null && weapon.CasingProfile!=profile)
                        throw new InvalidOperationException("Назначение гильзы изменено вручную: "+id);
                    weapon.SetCasingProfile(profile);
                }
                else if(weapon.CasingProfile!=null) throw new InvalidOperationException("У этого определения намеренно нет гильзы: "+id);
                EditorUtility.SetDirty(weapon);
            }
            AssetDatabase.SaveAssets();WeaponCatalog.Load();
            Debug.Log("Отдача и гильза установлены: независимые профили pistol/energy; исходный вектор гильзы8× с прежней мировой регистрацией. Параметры Unity не являются исходными единицами recoil5/2.");
        }
        public static void InstallAndBuild() {Install();PrototypeBuild.Build();}
        private static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++) {string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }
    }
}
