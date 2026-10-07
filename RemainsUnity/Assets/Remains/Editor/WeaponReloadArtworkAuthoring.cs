using System;
using System.IO;
using System.Security.Cryptography;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponReloadArtworkAuthoring
    {
        public const string ManifestPath="../Tools/Research/WeaponReloadResources/manifest.json";
        public const string AssetFolder="Assets/Remains/Prefabs/Weapons/Reload";
        [Serializable] private sealed class Export {public int schema;public float pixelsPerUnit;public Entry[] clips;}
        [Serializable] private sealed class Entry {public string weapon;public int firstFrame,finalFrame;public float[] originPixels;public Frame[] frames;}
        [Serializable] private sealed class Frame {public int sourceFrame,width,height;public string resource,sha256;}
        public static string ClipPath(string weapon)=>AssetFolder+"/"+weapon+"-reload.asset";
        public static void Install()
        {
            var export=JsonUtility.FromJson<Export>(File.ReadAllText(ManifestPath));
            if(export?.schema!=1 || export.pixelsPerUnit!=200 || export.clips?.Length!=2)
                throw new InvalidOperationException("Некорректный манифест кадров перезарядки.");
            foreach(var entry in export.clips)
            {
                int last=entry.weapon=="pistol"?40:entry.weapon=="energy"?60:0;
                if(entry.firstFrame!=10 || entry.finalFrame!=last || entry.frames?.Length!=last-9 || entry.originPixels?.Length!=2)
                    throw new InvalidOperationException("Неверная последовательность перезарядки: "+entry.weapon);
                var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>("Assets/Remains/Resources/Content/weapons/"+entry.weapon+".asset");
                if(asset==null || asset.Presentation?.Artwork.sprite==null) throw new InvalidOperationException("Нет исходного оружия: "+entry.weapon);
                var expected=AssetDatabase.LoadAssetAtPath<WeaponReloadClip>(ClipPath(entry.weapon));
                if(asset.ReloadAnimation!=null && asset.ReloadAnimation!=expected)
                    throw new InvalidOperationException("Сохранена пользовательская анимация перезарядки; замена запрещена: "+entry.weapon);
                Vector2 origin=new(entry.originPixels[0],entry.originPixels[1]);var rest=asset.Presentation.Artwork.sprite;
                if(rest.pixelsPerUnit!=export.pixelsPerUnit || Vector2.Distance(rest.rect.position+rest.pivot,origin)>.001f)
                    throw new InvalidOperationException("Ресурс перезарядки не совпадает с текущей привязкой оружия: "+entry.weapon);
                for(int i=0;i<entry.frames.Length;i++)
                {
                    var frame=entry.frames[i];string path="Assets/Remains/Resources/"+frame.resource+".png";
                    if(frame.sourceFrame!=i+10 || !File.Exists(path) || Hash(File.ReadAllBytes(path))!=frame.sha256)
                        throw new InvalidOperationException("Изменён исходный кадр перезарядки: "+path);
                }
            }
            EnsureFolder(AssetFolder);
            foreach(var entry in export.clips)
            {
                var frames=new Sprite[entry.frames.Length];
                for(int i=0;i<frames.Length;i++)
                {
                    var frame=entry.frames[i];string path="Assets/Remains/Resources/"+frame.resource+".png";
                    var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                    if(importer==null) throw new InvalidOperationException("Нет импортёра кадра перезарядки: "+path);
                    importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.sRGBTexture=true;
                    importer.alphaIsTransparency=true;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
                    importer.maxTextureSize=4096;importer.SaveAndReimport();
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if(texture==null || texture.width!=frame.width || texture.height!=frame.height)
                        throw new InvalidOperationException("Изменён размер кадра перезарядки: "+path);
                    string target=AssetFolder+"/"+entry.weapon+"-reload-"+frame.sourceFrame.ToString("00")+".asset";
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(target);Vector2 pivot=new(entry.originPixels[0],entry.originPixels[1]);
                    if(sprite==null)
                    {
                        sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivot.x/texture.width,pivot.y/texture.height),200,0,SpriteMeshType.FullRect);
                        sprite.name=entry.weapon+"-reload-"+frame.sourceFrame.ToString("00");AssetDatabase.CreateAsset(sprite,target);
                    }
                    else if(sprite.texture!=texture || sprite.rect!=new Rect(0,0,texture.width,texture.height) || sprite.pixelsPerUnit!=200 || Vector2.Distance(sprite.pivot,pivot)>.001f)
                        throw new InvalidOperationException("Кадр уже изменён вручную; замена запрещена: "+target);
                    frames[i]=sprite;
                }
                var clip=AssetDatabase.LoadAssetAtPath<WeaponReloadClip>(ClipPath(entry.weapon));
                if(clip==null)
                {clip=ScriptableObject.CreateInstance<WeaponReloadClip>();clip.Configure(frames);AssetDatabase.CreateAsset(clip,ClipPath(entry.weapon));}
                else
                {
                    clip.Validate();if(clip.FrameCount!=frames.Length) throw new InvalidOperationException("Изменена длина анимации: "+clip.name);
                    for(int i=0;i<frames.Length;i++) if(clip.Frame(i)!=frames[i]) throw new InvalidOperationException("Изменён кадр анимации: "+clip.name+"/"+i);
                }
                var weapon=AssetDatabase.LoadAssetAtPath<WeaponAsset>("Assets/Remains/Resources/Content/weapons/"+entry.weapon+".asset");
                clip.ValidateAgainst(weapon.Presentation.Artwork.sprite);
                if(weapon.ReloadAnimation==null) {weapon.SetReloadAnimation(clip);EditorUtility.SetDirty(weapon);}
                else if(weapon.ReloadAnimation!=clip)
                    throw new InvalidOperationException("Сохранена другая анимация перезарядки; установка остановлена: "+entry.weapon);
            }
            AssetDatabase.SaveAssets();Debug.Log("Графика перезарядки установлена: pistol31 + energy51, прежние GUID/дула/правила сохранены.");
        }
        private static string Hash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void EnsureFolder(string path)
        {
            string[] pieces=path.Split('/');string current=pieces[0];
            for(int i=1;i<pieces.Length;i++) {string next=current+"/"+pieces[i];if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,pieces[i]);current=next;}
        }
    }
}
