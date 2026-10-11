using System;
using System.IO;
using System.Security.Cryptography;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class MuzzleFlashAuthoring
    {
        public const string Root="Assets/Remains/Resources/Original/Weapons/Muzzle/";
        public static void RunBatch() {Install();}
        public static void Install()
        {
            for(int frame=1;frame<=2;frame++)
            {
                string path=Root+"pistol-flash-"+frame+".png";
                string expected=frame==1?"8EB04D52E9BEE34AC6C2C83363448800598512E46837D78984F7575EA2164D5B":"8F96D3AD518001869BD3D1B67380704A095B180A4475387271E3F0E83209CB6F";
                if(!File.Exists(path)) throw new InvalidOperationException("Нет исходной вспышки: "+path);
                using(var sha=SHA256.Create())
                    if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","")!=expected)
                        throw new InvalidOperationException("Вспышка изменена относительно исходного экспорта; автоматический импорт отменён: "+path);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null) throw new InvalidOperationException("Нет исходной вспышки: "+path);
                importer.textureType=TextureImporterType.Sprite;
                importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=400;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                settings.spriteAlignment=(int)SpriteAlignment.Custom;
                // Source union bounds: xmin=-10.2, ymin=-12.25; FFDec rounds the 8x canvas to293x203.
                importer.spritePivot=new Vector2(81.6f/293f,105f/203f);
                settings.spriteMeshType=SpriteMeshType.FullRect;
                settings.spritePivot=importer.spritePivot;
                importer.SetTextureSettings(settings);
                importer.mipmapEnabled=false;
                importer.alphaIsTransparency=true;
                importer.isReadable=true;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.sRGBTexture=true;
                importer.maxTextureSize=4096;
                importer.filterMode=FilterMode.Bilinear;
                importer.wrapMode=TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture==null || texture.width!=293 || texture.height!=203)
                    throw new InvalidOperationException("Неверный размер исходной вспышки8×: "+path);
            }
            var clip=AssetDatabase.LoadAssetAtPath<WeaponShotClip>("Assets/Remains/Prefabs/Weapons/pistol-shot.asset");
            if(clip==null || clip.CopyFrames().Length!=1)
                throw new InvalidOperationException("Для pistol-shot ожидался один исходный кадр выстрела.");
            var mask=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"pistol-flash-1.png");
            if(mask==null) throw new InvalidOperationException("Не импортирован спрайт маски дульной вспышки.");
            clip.ConfigureEmission(new[]{mask},new Vector2(.602f,.122f));
            EditorUtility.SetDirty(clip);
            const string catalogPath="Assets/Remains/Resources/Content/MuzzleFlashCatalog.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<MuzzleFlashCatalog>(catalogPath);
            if(catalog==null)
            {
                catalog=ScriptableObject.CreateInstance<MuzzleFlashCatalog>();
                AssetDatabase.CreateAsset(catalog,catalogPath);
            }
            catalog.Configure(new[]{new MuzzleFlashCatalog.Profile {ammo="p10",gain=8}});
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("Дульная вспышка p10: исходный символ1558, кадр1, вектор8×, PPU400; маска назначена pistol-shot. Сила8 — начальная настройка Unity.");
        }
    }
}
