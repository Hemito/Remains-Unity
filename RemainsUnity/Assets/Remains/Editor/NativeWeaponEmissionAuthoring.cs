using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Only presentation metadata: existing shot clips + shared original screen-blend sprites.
    // Does not rebuild WeaponCatalog, prefabs, gameplay definitions or projectile/tracer art.
    public static class NativeWeaponEmissionAuthoring
    {
        private const string Root="Assets/Remains/Resources/Original/Weapons/Muzzle/Native/";
        private static string Manifest=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/weapon-emission-20261106/emissions.json"));
        private static readonly Dictionary<string,float> Gains=new(StringComparer.Ordinal)
        {
            // Unity tuning by calibre/family; these are not original damage or lighting formulae.
            ["p9"]=6,["p10"]=8,["p32"]=6,["p44"]=12,["p556"]=10,["p308"]=14,["p127"]=16,["p50mg"]=18,
            ["p145"]=20,["p20"]=14,["p12"]=12,["p375"]=14,["batt"]=5,["energ"]=6,["bcell"]=6,["crystal"]=8,
            ["pcryo"]=5,["fuel"]=5,["acid"]=4,["scrap"]=5,["dart"]=3,["spikenail"]=3,["saw"]=3,["not"]=4
        };
        private static readonly Dictionary<string,float> WeaponGains=new(StringComparer.Ordinal)
        {["npc-shot"]=8,["weapon:rech:grade:0"]=6,["weapon:rechr:grade:0"]=6};
        public static void RunBatch()
        {try{Run();Check();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void CheckBatch()
        {try{Check();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            var original=JObject.Parse(File.ReadAllText(Manifest));
            if((string)original["matrixBasis"]!="Unity-y-up: m10=-rotateSkew0,m01=-rotateSkew1; translation=twips/1000")
                throw new InvalidDataException("Устаревшая база матриц вспышек; обновите Tools/Research/WeaponEmission с --metadata-only.");
            if((string)original["colorBasis"]!="SWF-byte-channel: multiply/256, add bytes, clamp RGBA")
                throw new InvalidDataException("В метаданных вспышек нет исходных цветовых преобразований; обновите WeaponEmission --metadata-only.");
            var masks=new Dictionary<(int Symbol,int Frame),Sprite>();
            foreach(var row in ((JArray)original["masks"]).OfType<JObject>())
            {
                string path=Root+(string)row["file"];
                if(!File.Exists(path))throw new FileNotFoundException("Нет исходной маски вспышки: "+path);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var sourceImporter=AssetImporter.GetAtPath(path) as TextureImporter??throw new InvalidOperationException("Нет импортёра "+path);
                sourceImporter.GetSourceTextureWidthAndHeight(out int width,out int height);
                var pivot=new Vector2((float)row["originX"]*8/width,1-(float)row["originY"]*8/height);
                var sprite=ImportMask(path,pivot);
                masks.Add(((int)row["symbol"],(int)row["frame"]),sprite);
            }
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var definitions=((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="weapon").ToDictionary(r=>(string)r["id"],StringComparer.Ordinal);
            var resources=((JArray)catalog["resources"]).OfType<JObject>().ToDictionary(r=>(string)r["id"],StringComparer.Ordinal);
            var appearances=((JArray)original["appearances"]).OfType<JObject>().ToDictionary(r=>(int)r["symbol"]);
            var tinted=new Dictionary<string,Sprite>(StringComparer.Ordinal);var tintReport=new JArray();
            var weaponCatalog=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>("Assets/Remains/Resources/Content/WeaponCatalog.asset")??throw new InvalidOperationException("Нет каталога оружия.");
            var report=new JArray();var profiles=new Dictionary<string,MuzzleFlashCatalog.Profile>(StringComparer.Ordinal)
                {["p10"]=new(){ammo="p10",gain=8}};int configured=0,lit=0;
            foreach(var asset in weaponCatalog.AnimatedWeapons())
            {
                if(!OriginalId(asset.Id,out string id,out int grade))continue;
                if(!definitions.TryGetValue(id,out var row))throw new InvalidDataException("Нет исходного оружия "+asset.Id);
                string appearance=((JArray)row["graphics"]).OfType<JObject>().SingleOrDefault(g=>(string)g["role"]=="appearance-grade-"+grade)?["resource"]?.ToString();
                if(appearance==null)throw new InvalidDataException("Нет исходного вида "+asset.Id);
                var resource=resources[appearance];int first=(int?)resource["labels"]?["shoot"]??throw new InvalidDataException("Нет метки shoot "+asset.Id);
                var clip=asset.ShotAnimation;var sprites=new Sprite[clip.FrameCount];var transforms=Enumerable.Repeat(Matrix4x4.identity,clip.FrameCount).ToArray();
                int lightFrames=0;
                if(appearances.TryGetValue((int)resource["symbol"],out var state))
                {
                    var frames=(JArray)state["frames"];
                    for(int i=0;i<clip.FrameCount;i++)
                    {
                        int sourceFrame=first-1+i;
                        if(sourceFrame>=frames.Count)throw new InvalidDataException($"Кадр {sourceFrame+1} за исходным видом {asset.Id}.");
                        if(frames[sourceFrame] is not JObject emission)continue;
                        var key=((int)emission["symbol"],(int)emission["frame"]);
                        if(!masks.TryGetValue(key,out var sprite))throw new InvalidDataException("Нет маски "+key+", weapon="+asset.Id);
                        var m=((JArray)emission["matrix"]).Values<float>().ToArray();if(m.Length!=6)throw new InvalidDataException("Неверная матрица "+asset.Id);
                        var matrix=Matrix4x4.identity;matrix.m00=m[0];matrix.m10=m[1];matrix.m01=m[2];matrix.m11=m[3];matrix.m03=m[4];matrix.m13=m[5];
                        sprites[i]=Tint(sprite,emission,tinted,tintReport);transforms[i]=matrix;lightFrames++;
                    }
                }
                clip.ConfigureEmission(sprites,transforms);EditorUtility.SetDirty(clip);configured++;
                if(lightFrames>0)
                {
                    string ammo=MuzzleFlashCatalog.AmmoKey(asset.CreateDefinition().ammo),key=MuzzleFlashCatalog.ProfileKey(asset.Id,ammo);
                    if(string.IsNullOrEmpty(ammo))
                    {
                        if(!WeaponGains.TryGetValue(asset.Id,out float gain))throw new InvalidDataException("Нет явного профиля света оружия без патрона: "+asset.Id);
                        profiles[key]=new MuzzleFlashCatalog.Profile{weapon=asset.Id,gain=gain};
                    }
                    else
                    {
                        if(!Gains.TryGetValue(ammo,out float gain))throw new InvalidDataException($"Нет явного профиля света: weapon={asset.Id}, ammo={ammo}.");
                        profiles[key]=new MuzzleFlashCatalog.Profile{ammo=ammo,gain=gain};
                    }
                    lit++;
                }
                report.Add(new JObject{["weapon"]=asset.Id,["appearance"]=appearance,["firstFrame"]=first,["frames"]=clip.FrameCount,["emittingFrames"]=lightFrames});
            }
            var lightCatalog=AssetDatabase.LoadAssetAtPath<MuzzleFlashCatalog>("Assets/Remains/Resources/Content/MuzzleFlashCatalog.asset")??throw new InvalidOperationException("Нет MuzzleFlashCatalog.");
            lightCatalog.Configure(profiles.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value).ToArray());EditorUtility.SetDirty(lightCatalog);
            AssetDatabase.SaveAssets();
            string output=Path.GetDirectoryName(Manifest);File.WriteAllText(Path.Combine(output,"installed.json"),new JObject{["configuredWeapons"]=configured,["weaponsWithNativeEmission"]=lit,["sharedMasks"]=masks.Count,["tintedMasks"]=tinted.Count,["tints"]=tintReport,["weapons"]=report}.ToString());
            Debug.Log($"Исходные вспышки: настроено оружия={configured}, со светом={lit}, общих масок={masks.Count}, вариантов CXFORM={tinted.Count}; без изменения игровых определений.");
        }
        private static Sprite ImportMask(string path,Vector2 pivot)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter??throw new InvalidOperationException("Нет импортёра "+path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=400;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteAlignment=(int)SpriteAlignment.Custom;
            settings.spriteMeshType=SpriteMeshType.FullRect;settings.spritePivot=pivot;importer.SetTextureSettings(settings);
            importer.spritePivot=pivot;importer.mipmapEnabled=false;importer.isReadable=true;importer.alphaIsTransparency=true;
            importer.sRGBTexture=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;
            importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path)??throw new InvalidOperationException("Не импортирована маска "+path);
        }
        private static Sprite Tint(Sprite source,JObject emission,Dictionary<string,Sprite> cache,JArray report)
        {
            var multiply=((JArray)emission["colorMultiply"]).Values<float>().ToArray();var add=((JArray)emission["colorAdd"]).Values<float>().ToArray();
            Require(multiply.Length==4 && add.Length==4 && multiply.Concat(add).All(float.IsFinite),"Неверный CXFORM исходной вспышки.");
            if(multiply.All(v=>v==1) && add.All(v=>v==0))return source;
            Require(add[3]==0,"AlphaAdd вспышки требует исходных векторных контуров; прямоугольную подстановку не создаём.");
            string sourcePath=AssetDatabase.GetAssetPath(source),key=sourcePath+"|"+emission["colorMultiply"].ToString(Newtonsoft.Json.Formatting.None)+"|"+emission["colorAdd"].ToString(Newtonsoft.Json.Formatting.None);
            if(cache.TryGetValue(key,out var found))return found;
            using var sha=SHA256.Create();string suffix=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,16).ToLowerInvariant();
            string folder=Root+"Tinted/";Directory.CreateDirectory(folder);string path=folder+Path.GetFileNameWithoutExtension(sourcePath)+"-"+suffix+".png";
            var image=new Texture2D(source.texture.width,source.texture.height,TextureFormat.RGBA32,false);
            try
            {
                var pixels=source.texture.GetPixels32();for(int i=0;i<pixels.Length;i++)pixels[i]=Transform(pixels[i],multiply,add);
                image.SetPixels32(pixels);image.Apply();var bytes=image.EncodeToPNG();
                if(!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))File.WriteAllBytes(path,bytes);
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
            var result=ImportMask(path,new Vector2(source.pivot.x/source.rect.width,source.pivot.y/source.rect.height));cache.Add(key,result);
            report.Add(new JObject{["source"]=sourcePath,["path"]=path,["multiply"]=new JArray(multiply),["add"]=new JArray(add)});return result;
        }
        private static Color32 Transform(Color32 pixel,float[] multiply,float[] add)
        {
            byte Channel(byte value,int index)=>(byte)Mathf.Clamp(Mathf.RoundToInt(value*multiply[index]+add[index]),0,255);
            return new Color32(Channel(pixel.r,0),Channel(pixel.g,1),Channel(pixel.b,2),Channel(pixel.a,3));
        }
        private static bool OriginalId(string id,out string original,out int grade)
        {
            grade=0;original=id is "pistol" or "npc-shot"?"p10mm":id=="energy"?"lasp":null;
            if(original!=null)return true;
            var parts=id.Split(':');if(parts.Length!=4 || parts[0]!="weapon" || parts[2]!="grade" || !int.TryParse(parts[3],out grade))return false;
            original=parts[1];return true;
        }
        public static void Check()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>("Assets/Remains/Resources/Content/WeaponCatalog.asset");
            var profiles=AssetDatabase.LoadAssetAtPath<MuzzleFlashCatalog>("Assets/Remains/Resources/Content/MuzzleFlashCatalog.asset").Read();int guns=0,frames=0;
            foreach(var asset in catalog.AnimatedWeapons())
            {
                var clip=asset.ShotAnimation;var masks=clip.EmissionFrames;if(masks==null || !masks.Any(m=>m!=null))continue;
                Require(profiles.TryGetValue(MuzzleFlashCatalog.ProfileKey(asset.Id,asset.CreateDefinition().ammo),out float gain) && gain>0,"Нет положительного профиля "+asset.Id);
                for(int i=0;i<masks.Length;i++)if(masks[i]!=null)
                {
                    Require(Mathf.Abs(masks[i].pixelsPerUnit-400)<.001f,"Маска потеряла исходный масштаб: "+asset.Id);
                    var local=clip.EmissionTransform(i);var weapon=Matrix4x4.TRS(new Vector3(3,4,0),Quaternion.Euler(0,0,73),new Vector3(1,-1,1));
                    var point=new Vector3(.17f,-.11f,0);
                    Require(Vector3.Distance((weapon*local).MultiplyPoint3x4(point),weapon.MultiplyPoint3x4(local.MultiplyPoint3x4(point)))<.00001f,"Поворот/зеркальность маски: "+asset.Id);frames++;
                }
                guns++;
            }
            Require(guns>40 && frames>80,$"Неполное покрытие вспышек: guns={guns}, frames={frames}.");
            Require(MuzzleFlashCatalog.AmmoKey("item:p10")==MuzzleFlashCatalog.AmmoKey("p10"),"Короткий и экспортный ammo ID расходятся.");
            var installed=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Manifest),"installed.json")));int tints=0;
            foreach(var tint in ((JArray)installed["tints"]).OfType<JObject>())
            {
                var source=AssetDatabase.LoadAssetAtPath<Texture2D>((string)tint["source"]);var image=AssetDatabase.LoadAssetAtPath<Texture2D>((string)tint["path"]);
                Require(source!=null && image!=null && source.width==image.width && source.height==image.height,"CXFORM изменил холст маски.");
                var multiply=((JArray)tint["multiply"]).Values<float>().ToArray();var add=((JArray)tint["add"]).Values<float>().ToArray();var a=source.GetPixels32();var b=image.GetPixels32();
                // alphaIsTransparency may dilate RGB in fully transparent texels; alpha and visible RGB must remain exact.
                for(int i=0;i<a.Length;i++){var expected=Transform(a[i],multiply,add);Require(expected.a==b[i].a && (expected.a==0 || expected.r==b[i].r && expected.g==b[i].g && expected.b==b[i].b),"CXFORM не совпал с исходной формулой: "+tint["path"]+", pixel="+i);}
                tints++;
            }
            var pistol=AssetDatabase.LoadAssetAtPath<WeaponAsset>("Assets/Remains/Resources/Content/weapons/pistol.asset");
            Require(Vector3.Distance(pistol.ShotAnimation.EmissionTransform(0).MultiplyPoint3x4(Vector3.zero),new Vector3(.602f,.122f,0))<.00001f,"Исходная опора пистолета изменилась.");
            Debug.Log($"NativeWeaponEmissionChecks PASS: оружия={guns}, светящихся кадров={frames}, CXFORM-вариантов={tints}, исходный масштаб, матрицы, ammo aliases.");
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Исходные вспышки: "+message);}
    }
}
