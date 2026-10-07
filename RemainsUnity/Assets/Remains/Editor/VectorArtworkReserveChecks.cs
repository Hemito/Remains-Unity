using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit authoring evidence only. No runtime asset scans or gameplay changes.
    public static class VectorArtworkReserveChecks
    {
        public const string EvidenceFolder=".reference/tests/vector-artwork-r59";
        public const string RegistrationBaselineFile="Tools/Research/VectorArtworkReserve/native-registration-baseline.json";
        private const string Prefix="Assets/Remains/Resources/";
        private const string HudPath=Prefix+"Presentation/CombatHud.prefab";
        private const string Research="Tools/Research/VectorArtworkReserve/";
        private sealed class TextureEvidence
        {
            public string path,guid,sha256,importerSpriteId;
            public int sourceWidth,sourceHeight,width,height,type,spriteMode,maxSize,npot,compression,filter,wrap,standaloneMaxSize,standaloneCompression,standaloneFormat;
            public bool mipmaps,alphaIsTransparency,srgb,standaloneOverride;
            public float importerPixelsPerUnit;
            public SpriteEvidence[] sprites;
        }
        private sealed class SpriteEvidence
        {public string path,guid,name,textureGuid;public long localId;public float[] rect,pivot,border,bounds;public float ppu;}
        private sealed class FileEvidence {public string path,sha256;}
        private sealed class Baseline
        {public int schema=1;public string capturedUtc,hudGuid,hudLayout;public TextureEvidence[] textures;public FileEvidence[] protectedFiles;}
        private sealed class RegistrationBaseline
        {public int schema;public string provenance,sourceCapture,originalCaptureSha256,capturedUtc;public TextureEvidence[] textures;}
        private sealed class VerificationEvidence
        {public int schema=1;public bool success;public string verifiedUtc,baselineSha256;public int textures,sprites,protectedFiles,rejectionCases;public FileEvidence[] manifests;}
        private sealed class HudElement
        {
            public string path,spriteGuid;public long spriteLocalId;
            public float[] anchors,pivot,position,size,scale,rotation,rect,color;
            public int type,fillMethod,fillOrigin;public bool fillClockwise,preserveAspect,raycast;public float fillAmount,pixelsPerUnitMultiplier;
        }
        private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Root,path));
        private static string BaselinePath=>Absolute(EvidenceFolder+"/native-baseline.json");
        private static string[] SelectedTextures()=>new[]{Prefix+"Original/Hud/health.png",Prefix+"Original/Hud/mana.png",Prefix+"Original/Hud/health-frame.png"}
            .Concat(Enumerable.Range(2,17).Select(i=>Prefix+"Original/Hud/HealthWarning/"+i+".png"))
            .Concat(new[]{Prefix+"Original/Weapons/spear.png",Prefix+"Original/Weapons/ShotEffects/casing.png"}).ToArray();

        public static void CaptureBaseline()
        {
            var baseline=Capture();
            if(File.Exists(BaselinePath))
            {
                var previous=ReadBaseline();
                Require(StateJson.ToJson(previous.textures)==StateJson.ToJson(baseline.textures) && previous.hudGuid==baseline.hudGuid &&
                    previous.hudLayout==baseline.hudLayout && StateJson.ToJson(previous.protectedFiles)==StateJson.ToJson(baseline.protectedFiles),
                    "Базовый захват уже существует и отличается от текущих данных; перезапись после замены запрещена: "+BaselinePath);
                Debug.Log("Векторная графика: прежний baseline подтверждён без перезаписи: "+BaselinePath);return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            using(var file=new FileStream(BaselinePath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(file,new System.Text.UTF8Encoding(false))) writer.Write(StateJson.ToJson(baseline,true));
            Debug.Log($"Векторная графика: baseline — {baseline.textures.Length} текстуры/{baseline.textures.Sum(t=>t.sprites.Length)} Sprite, {baseline.protectedFiles.Length} защищённых файлов; {BaselinePath}.");
        }
        private static Baseline Capture()
        {
            var paths=SelectedTextures();var view=HudView();view.Validate();
            var sprites=view.GetComponentsInChildren<Image>(true).Select(i=>i.sprite).Concat(view.HealthWarningFrames)
                .Concat(new[]{Asset<Sprite>("Assets/Remains/Prefabs/Weapons/spear-sprite.asset"),Asset<Sprite>(Prefix+"Original/Weapons/ShotEffects/casing.png")})
                .Where(s=>s!=null && paths.Contains(AssetDatabase.GetAssetPath(s.texture),StringComparer.Ordinal)).Distinct().ToArray();
            var result=paths.Select(path=>ReadTexture(path,sprites.Where(s=>AssetDatabase.GetAssetPath(s.texture)==path))).ToArray();
            Require(result.Length==22 && result.All(t=>t.sprites.Length==1),"Нужны ровно 22 выбранные текстуры с одной реальной Sprite-привязкой: "+StateJson.ToJson(result));
            return new Baseline {capturedUtc=DateTime.UtcNow.ToString("O"),textures=result,hudGuid=AssetDatabase.AssetPathToGUID(HudPath),
                hudLayout=HudLayout(view),protectedFiles=ProtectedPaths().Select(p=>new FileEvidence {path=p,sha256=Hash(Absolute(p))}).ToArray()};
        }
        private static TextureEvidence ReadTexture(string path,IEnumerable<Sprite> sprites)
        {
            var texture=Asset<Texture2D>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer!=null,"Нет TextureImporter: "+path);
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            var platform=importer.GetPlatformTextureSettings("Standalone");
            var serialized=new SerializedObject(importer);var spriteId=serialized.FindProperty("m_SpriteSheet.m_SpriteID");
            return new TextureEvidence {path=path,guid=AssetDatabase.AssetPathToGUID(path),sha256=Hash(Absolute("RemainsUnity/"+path)),
                sourceWidth=width,sourceHeight=height,width=texture.width,height=texture.height,type=(int)importer.textureType,
                spriteMode=(int)importer.spriteImportMode,maxSize=importer.maxTextureSize,npot=(int)importer.npotScale,compression=(int)importer.textureCompression,
                filter=(int)importer.filterMode,wrap=(int)importer.wrapMode,mipmaps=importer.mipmapEnabled,alphaIsTransparency=importer.alphaIsTransparency,
                srgb=importer.sRGBTexture,standaloneOverride=platform.overridden,standaloneMaxSize=platform.maxTextureSize,
                standaloneCompression=(int)platform.textureCompression,standaloneFormat=(int)platform.format,
                importerPixelsPerUnit=importer.spritePixelsPerUnit,importerSpriteId=spriteId?.stringValue,
                sprites=sprites.Select(ReadSprite).OrderBy(s=>s.path,StringComparer.Ordinal).ThenBy(s=>s.localId).ToArray()};
        }
        private static SpriteEvidence ReadSprite(Sprite sprite)
        {
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long id),"Sprite не имеет постоянной ссылки: "+sprite.name);
            return new SpriteEvidence {path=AssetDatabase.GetAssetPath(sprite),guid=guid,localId=id,name=sprite.name,
                textureGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)),rect=Rect(sprite.rect),pivot=V2(sprite.pivot),
                ppu=sprite.pixelsPerUnit,border=new[]{sprite.border.x,sprite.border.y,sprite.border.z,sprite.border.w},
                bounds=new[]{sprite.bounds.min.x,sprite.bounds.min.y,sprite.bounds.max.x,sprite.bounds.max.y}};
        }
        private static string HudLayout(CombatHudView view)
        {
            var entries=view.GetComponentsInChildren<RectTransform>(true).Select(rect=>
            {
                var image=rect.GetComponent<Image>();var item=new HudElement {path=Hierarchy(rect,view.transform),
                    anchors=new[]{rect.anchorMin.x,rect.anchorMin.y,rect.anchorMax.x,rect.anchorMax.y},pivot=V2(rect.pivot),
                    position=V3(rect.anchoredPosition3D),size=V2(rect.sizeDelta),scale=V3(rect.localScale),
                    rotation=new[]{rect.localRotation.x,rect.localRotation.y,rect.localRotation.z,rect.localRotation.w},rect=Rect(rect.rect)};
                if(image!=null)
                {
                    item.color=new[]{image.color.r,image.color.g,image.color.b,image.color.a};item.type=(int)image.type;
                    item.fillMethod=(int)image.fillMethod;item.fillOrigin=image.fillOrigin;item.fillClockwise=image.fillClockwise;item.fillAmount=image.fillAmount;
                    item.preserveAspect=image.preserveAspect;item.raycast=image.raycastTarget;item.pixelsPerUnitMultiplier=image.pixelsPerUnitMultiplier;
                    if(image.sprite!=null)
                    {Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image.sprite,out item.spriteGuid,out item.spriteLocalId),"Непостоянная HUD Sprite: "+item.path);}
                }
                return item;
            }).OrderBy(e=>e.path,StringComparer.Ordinal).ToArray();
            return StateJson.ToJson(entries);
        }
        private static string Hierarchy(Transform item,Transform root)
        {var names=new List<string>();for(var p=item;p!=root;p=p.parent) names.Add(p.GetSiblingIndex()+":"+p.name);names.Reverse();return string.Join("/",names);}
        private static string[] ProtectedPaths()
        {
            var directories=new[]{"RemainsUnity/Assets/Remains/Resources/Content","RemainsUnity/Assets/Remains/Resources/Original",
                "RemainsUnity/Assets/Remains/Resources/Presentation","RemainsUnity/Assets/Remains/RoomDrafts",
                "RemainsUnity/Assets/Remains/Prefabs/Props","RemainsUnity/Assets/Remains/Prefabs/Actors","RemainsUnity/Assets/Remains/Prefabs/Weapons"};
            foreach(string dir in directories) Require(Directory.Exists(Absolute(dir)),"Нет защищённой папки: "+dir);
            var selected=new HashSet<string>(SelectedTextures().SelectMany(p=>new[]{"RemainsUnity/"+p,"RemainsUnity/"+p+".meta"}),StringComparer.Ordinal);
            selected.Add("RemainsUnity/Assets/Remains/Prefabs/Weapons/spear-sprite.asset");
            selected.Add("RemainsUnity/Assets/Remains/Resources/Presentation/health-frame.asset");
            foreach(int frame in new[]{0,1}.Concat(Enumerable.Range(3,17))) selected.Add("RemainsUnity/Assets/Remains/Resources/Presentation/CombatHudArtwork/hud-"+frame+".asset");
            return directories.SelectMany(dir=>Directory.GetFiles(Absolute(dir),"*",SearchOption.AllDirectories))
                .Select(p=>p.Substring(Root.Length+1).Replace('\\','/')).Where(p=>!selected.Contains(p)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        }
        private static Baseline ReadBaseline()
        {
            Require(File.Exists(BaselinePath),"Нет baseline до замены PNG: "+BaselinePath);
            var result=StateJson.FromJson<Baseline>(File.ReadAllText(BaselinePath));
            Require(result?.schema==1 && result.textures?.Length==22 && result.protectedFiles?.Length>0 && !string.IsNullOrEmpty(result.hudLayout),"Неполный baseline: "+BaselinePath);return result;
        }
        private static Baseline ReadRegistrationBaseline()
        {
            string path=Absolute(RegistrationBaselineFile);
            Require(File.Exists(path),"Нет сохранённого исходного registration baseline: "+RegistrationBaselineFile);
            var registration=StateJson.FromJson<RegistrationBaseline>(File.ReadAllText(path));
            ValidateRegistrationBaseline(registration);
            return new Baseline {textures=registration.textures,capturedUtc=registration.capturedUtc};
        }
        private static void ValidateRegistrationBaseline(RegistrationBaseline baseline)
        {
            Require(baseline?.schema==1 && !string.IsNullOrWhiteSpace(baseline.provenance) &&
                baseline.sourceCapture==EvidenceFolder+"/native-baseline.json" && IsHex(baseline.originalCaptureSha256,64) &&
                DateTimeOffset.TryParse(baseline.capturedUtc,System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,out _),"Повреждён provenance исходного registration baseline: "+RegistrationBaselineFile);
            Require(baseline.textures?.Length==22 && baseline.textures.All(t=>t!=null) &&
                baseline.textures.Select(t=>t.path).OrderBy(p=>p,StringComparer.Ordinal).SequenceEqual(SelectedTextures().OrderBy(p=>p,StringComparer.Ordinal)),
                "Исходный registration baseline должен содержать ровно 22 утверждённых пути: "+RegistrationBaselineFile);
            foreach(var texture in baseline.textures)
            {
                string context=RegistrationBaselineFile+"/"+texture.path;
                Require(IsHex(texture.guid,32) && IsHex(texture.sha256,64) && texture.width>0 && texture.height>0 &&
                    texture.sourceWidth==texture.width && texture.sourceHeight==texture.height &&
                    float.IsFinite(texture.importerPixelsPerUnit) && texture.importerPixelsPerUnit>0 &&
                    texture.sprites?.Length==1,"Неполные исходные native данные текстуры: "+context);
                var sprite=texture.sprites[0];
                Require(sprite!=null && !string.IsNullOrWhiteSpace(sprite.path) && !string.IsNullOrWhiteSpace(sprite.name) &&
                    IsHex(sprite.guid,32) && sprite.localId==21300000 && sprite.textureGuid==texture.guid &&
                    Coordinates(sprite.rect,4) && Coordinates(sprite.pivot,2) && Coordinates(sprite.border,4) && Coordinates(sprite.bounds,4) &&
                    sprite.rect[0]>=0 && sprite.rect[1]>=0 && sprite.rect[2]>0 && sprite.rect[3]>0 &&
                    sprite.rect[0]+sprite.rect[2]<=texture.width && sprite.rect[1]+sprite.rect[3]<=texture.height &&
                    float.IsFinite(sprite.ppu) && sprite.ppu>0,"Неполные исходные Sprite данные: "+context);
                Require(texture.spriteMode==(int)SpriteImportMode.Single?IsHex(texture.importerSpriteId,32):texture.importerSpriteId=="",
                    "Отсутствует исходный importer Sprite ID: "+context);
            }
        }
        // Normal clean-clone/build contract. It deliberately has no dependency on
        // ignored migration evidence, historical HUD layout or unrelated resources.
        public static void Run()=>VerifyCurrent();
        public static void VerifyCurrent()
        {
            var baseline=ReadRegistrationBaseline();var entries=VectorArtworkReserveManifest.Entries();
            VerifyEntries(entries,baseline);
            Debug.Log("Векторная графика: PASS — текущие 22 native ресурса/постоянные Sprite/мировая привязка; tracked baseline="+RegistrationBaselineFile);
        }
        private static void VerifyEntries(ReserveEntry[] entries,Baseline baseline)
        {
            Require(entries.Select(e=>e.texturePath).OrderBy(p=>p,StringComparer.Ordinal).SequenceEqual(SelectedTextures().OrderBy(p=>p,StringComparer.Ordinal)),
                "Manifest не содержит ровно утверждённые 22 текстуры.");
            foreach(var entry in entries) VerifyEntry(entry,baseline);
        }
        // Explicit one-time before/after migration audit; only this entrypoint
        // compares the original capture's 6910 protected files and HUD layout.
        public static void Verify()
        {
            var baseline=ReadBaseline();var entries=VectorArtworkReserveManifest.Entries();
            VerifyEntries(entries,baseline);
            Require(AssetDatabase.AssetPathToGUID(HudPath)==baseline.hudGuid && HudLayout(HudView())==baseline.hudLayout,"Изменены реальные HUD layout/color/fill/border multiplier/ссылки.");
            foreach(var file in baseline.protectedFiles) Require(File.Exists(Absolute(file.path)) && Hash(Absolute(file.path))==file.sha256,"Изменён защищённый gameplay/pony/props файл: "+file.path);
            Remains.Content.WeaponCatalog.Load();
            CombatHudPrefabChecks.Run();WeaponPrefabChecks.Run();
            int rejectionCases=CheckRejections(baseline,entries);
            var evidence=new VerificationEvidence {success=true,verifiedUtc=DateTime.UtcNow.ToString("O"),baselineSha256=Hash(BaselinePath),textures=entries.Length,
                sprites=baseline.textures.Sum(t=>t.sprites.Length),protectedFiles=baseline.protectedFiles.Length,rejectionCases=rejectionCases,
                manifests=new[]{Research+"hud-manifest.json",Research+"weapon-manifest.json"}.Select(p=>new FileEvidence {path=p,sha256=Hash(Absolute(p))}).ToArray()};
            File.WriteAllText(Absolute(EvidenceFolder+"/native-verified.json"),StateJson.ToJson(evidence,true));
            Debug.Log("Векторная графика: PASS — 22 native текстуры, постоянные Sprite/мировая привязка/HUD и protected baseline; "+StateJson.ToJson(evidence));
        }
        public static void VerifyEntry(string id)
        {
            VerifyEntry(VectorArtworkReserveManifest.Find(id),ReadRegistrationBaseline());
        }
        private static void VerifyEntry(ReserveEntry entry,Baseline baseline)
        {
            var before=baseline.textures.SingleOrDefault(t=>t.path==entry.texturePath);
            Require(before!=null,"Текстура не входит в baseline: "+entry.id+"/"+entry.texturePath);
            ValidateDeclaration(entry,before);
            ValidateNative(entry,before,ReadTexture(before.path,before.sprites.Select(FindSprite)));
        }
        private static void ValidateDeclaration(ReserveEntry entry,TextureEvidence before)
        {
            VectorArtworkReserveManifest.Validate(entry);VectorArtworkReserveManifest.VerifyStaged(entry);
            string context=before.path;
            Require(entry.productionBeforeSha256==before.sha256 && entry.textureGuid==before.guid,"Неверный baseline hash/GUID: "+context);
            string png=entry.png;Require(png.StartsWith(Research,StringComparison.Ordinal),"PNG вне prepared пакета: "+png);
            var bytes=File.ReadAllBytes(Absolute(png));
            Require(bytes.Length>=33 && bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && PngInt(bytes,16)==entry.pixelWidth &&
                PngInt(bytes,20)==entry.pixelHeight && bytes[24]==8 && bytes[25]==6,"PNG canvas/RGBA расходятся с manifest: "+png);
            Require(entry.sprites.Length==before.sprites.Length,"Неполные Sprite declarations: "+context);
            foreach(var old in before.sprites)
            {
                var matches=entry.sprites.Where(s=>s.assetPath==old.path && s.localId==old.localId).ToArray();
                Require(matches.Length==1,"Подменён/повторён Sprite ID: "+old.path);var item=matches[0];
                Require(item.guid==old.guid && Same(item.priorRect,old.rect) && Near(item.priorPivotPixelsBottomLeft,old.pivot,.0001f) &&
                    item.priorPixelsPerUnit==old.ppu && Same(item.priorBorder,old.border) && Near(item.worldBounds,old.bounds),"Baseline Sprite registration не совпала: "+old.path);
                Require(item.pixelsPerUnit==old.ppu*2 && Same(item.rect,old.rect.Select(v=>v*2).ToArray()) &&
                    Near(item.pivotPixelsBottomLeft,old.pivot.Select(v=>v*2).ToArray(),.0001f) && Same(item.border,old.border.Select(v=>v*2).ToArray()),
                    "8× compatibility canvas не сохраняет прежнюю мировую привязку: "+old.path);
            }
        }
        private static void ValidateNative(ReserveEntry entry,TextureEvidence before,TextureEvidence actual)
        {
            Require(actual.guid==before.guid && actual.importerSpriteId==before.importerSpriteId,"Изменены texture GUID/importer Sprite ID: "+before.path);
            Require(actual.sha256==entry.pngSha256 && actual.width==entry.pixelWidth && actual.height==entry.pixelHeight &&
                actual.sourceWidth==actual.width && actual.sourceHeight==actual.height,"Native импорт уменьшил/заменил PNG: "+before.path+", "+StateJson.ToJson(actual));
            Require(actual.maxSize>=entry.requiredMaxTextureSize && actual.npot==(int)TextureImporterNPOTScale.None && !actual.mipmaps &&
                actual.compression==(int)TextureImporterCompression.Uncompressed && actual.filter==(int)FilterMode.Bilinear && actual.wrap==(int)TextureWrapMode.Clamp &&
                actual.type==before.type && actual.spriteMode==before.spriteMode && actual.srgb==before.srgb &&
                actual.alphaIsTransparency==before.alphaIsTransparency &&
                (!actual.standaloneOverride || actual.standaloneMaxSize>=Math.Max(actual.width,actual.height) && actual.standaloneCompression==(int)TextureImporterCompression.Uncompressed),
                "Native importer не сохраняет утверждённое качество: "+before.path+", "+StateJson.ToJson(actual));
            Require(actual.sprites.Length==before.sprites.Length,"Изменено число native Sprite: "+before.path);
            foreach(var old in before.sprites)
            {
                var declared=entry.sprites.Single(s=>s.assetPath==old.path && s.localId==old.localId);
                var now=actual.sprites.SingleOrDefault(s=>s.path==old.path && s.localId==old.localId);
                Require(now!=null && now.guid==old.guid && now.name==old.name && now.textureGuid==old.textureGuid,"Изменена постоянная Sprite-ссылка: "+old.path);
                // Sprite.Create stores a normalized float pivot: 1e-4 pixel covers
                // native multiply/rounding only; world registration is tighter.
                Require(Same(now.rect,declared.rect) && Near(now.pivot,declared.pivotPixelsBottomLeft,.0001f) && now.ppu==declared.pixelsPerUnit &&
                    Same(now.border,declared.border) && Near(now.bounds,old.bounds),"Sprite rect/pivot/PPU/border/world bounds не совпали: "+old.path+", "+StateJson.ToJson(now));
            }
        }
        private static int CheckRejections(Baseline baseline,ReserveEntry[] entries)
        {
            int count=0;
            foreach(var entry in entries)
            {
                var before=baseline.textures.Single(t=>t.path==entry.texturePath);
                var valid=ReadTexture(before.path,before.sprites.Select(FindSprite));ValidateNative(entry,before,valid);
                void RejectNative(Action<TextureEvidence> change,string reason)
                {
                    var invalid=StateJson.Copy(valid);change(invalid);
                    Reject(()=>ValidateNative(entry,before,invalid),reason+"/"+entry.id);count++;
                }
                RejectNative(t=>t.width=Math.Max(1,t.width/2),"Фактический импорт уменьшен");
                RejectNative(t=>t.maxSize=32,"Повторный normalizer уменьшает reserve");
                RejectNative(t=>t.guid=new string('0',32),"Подменён texture GUID");
                RejectNative(t=>t.importerSpriteId="changed", "Подменён importer Sprite ID");
                RejectNative(t=>t.sha256=before.sha256,"Остался старый PNG");
                RejectNative(t=>t.sprites[0].localId++,"Подменён Sprite fileID");
                RejectNative(t=>t.sprites[0].rect=before.sprites[0].rect,"Остался старый Sprite rect");
                RejectNative(t=>t.sprites[0].ppu=before.sprites[0].ppu,"Остался старый PPU");
                RejectNative(t=>t.sprites[0].bounds[0]+=.001f,"Сдвинута мировая привязка");
                var wrong=StateJson.Copy(entry);wrong.productionBeforeSha256=new string('0',64);
                Reject(()=>ValidateDeclaration(wrong,before),"Неверный source baseline/"+entry.id);count++;
            }
            Debug.Log("Векторная графика: "+count+" отрицательных проверок по захваченным native данным; assets не изменялись.");return count;
        }
        private static void Reject(Action action,string reason)
        {try {action();} catch(InvalidOperationException error) when(error.Message.StartsWith("Векторная графика:",StringComparison.Ordinal)){return;}
            throw new InvalidOperationException("Векторная графика: ошибочные native данные приняты: "+reason);}
        private static Sprite FindSprite(SpriteEvidence evidence)
        {
            var sprites=AssetDatabase.LoadAllAssetsAtPath(evidence.path).OfType<Sprite>().Where(sprite=>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long id) && guid==evidence.guid && id==evidence.localId).ToArray();
            Require(sprites.Length==1,"Нет прежнего Sprite GUID/localID: "+evidence.path+"/"+evidence.localId);return sprites[0];
        }
        private static T Asset<T>(string path) where T:UnityEngine.Object
        {var result=AssetDatabase.LoadAssetAtPath<T>(path);Require(result!=null,"Нет native asset: "+path);return result;}
        private static CombatHudView HudView()
        {var view=Asset<GameObject>(HudPath).GetComponent<CombatHudView>();Require(view!=null,"Нет CombatHudView на сохранённом префабе: "+HudPath);return view;}
        private static string Hash(string path)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static int PngInt(byte[] bytes,int start)=>checked((int)((uint)bytes[start]<<24|(uint)bytes[start+1]<<16|(uint)bytes[start+2]<<8|bytes[start+3]));
        private static bool Same(float[] a,float[] b)=>a!=null && b!=null && a.SequenceEqual(b);
        private static bool IsHex(string value,int count)=>value?.Length==count && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static bool Coordinates(float[] value,int count)=>value?.Length==count && value.All(float.IsFinite);
        private static bool Near(float[] a,float[] b,float tolerance=.000001f)=>a!=null && b!=null && a.Length==b.Length && a.Zip(b,(x,y)=>float.IsFinite(x) && float.IsFinite(y) && Mathf.Abs(x-y)<=tolerance).All(v=>v);
        private static float[] V2(Vector2 v)=>new[]{v.x,v.y};
        private static float[] V3(Vector3 v)=>new[]{v.x,v.y,v.z};
        private static float[] Rect(Rect v)=>new[]{v.x,v.y,v.width,v.height};
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Векторная графика: "+message);}
    }
}
