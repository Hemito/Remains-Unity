using System;
using System.IO;
using System.Security.Cryptography;
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
            using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(SpritePath))).Replace("-","").ToLowerInvariant()!=
                    "f44fef1687d155ef119503e9d049c80717af9ea84cfefdc6e27b9d91566624eb")
                    throw new InvalidOperationException("Исходная гильза изменилась: "+SpritePath);
            var importer=AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            if(importer==null) throw new InvalidOperationException("Нет импортёра гильзы: "+SpritePath);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=200;importer.spritePivot=new Vector2(16.6f/34,6.2f/11);
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spritePivot=importer.spritePivot;
            settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
            importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.alphaIsTransparency=false;
            importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if(sprite==null || sprite.texture.width!=34 || sprite.texture.height!=11 || Vector2.Distance(sprite.pivot,new Vector2(16.6f,6.2f))>.0001f)
                throw new InvalidOperationException("Нарушен размер или registration гильзы.");
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
            Debug.Log("Отдача и гильза установлены: независимые профили pistol/energy; исходный ресурс 34×11, PPU200. Параметры Unity не являются исходными единицами recoil5/2.");
        }
        public static void InstallAndBuild() {Install();PrototypeBuild.Build();}
        private static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++) {string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }
    }
}
