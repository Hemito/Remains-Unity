using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit migration of the existing saved Sprite objects. PNG transfer/import
    // is a separate reviewed operation; this authorer never replaces textures,
    // references, names, prefabs, or assets and never invokes a build.
    public static class HudVectorReserveAuthoring
    {
        public const string ManifestPath=VectorArtworkReserveManifest.HudPath;
        public const string HudPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
        private const string TextureFolder="Assets/Remains/Resources/Original/Hud/";
        private const string SpriteFolder="Assets/Remains/Resources/Presentation/CombatHudArtwork/";

        public static ReserveManifest ReadManifest()
        {
            var manifest=VectorArtworkReserveManifest.Load(ManifestPath);
            Require(manifest.entries.Length==20,"Ожидались ровно20 прежних рисунков HUD.");
            for(int i=0;i<20;i++)
            {
                string id=i==0?"health":i<18?"health-warning-"+(i+1):i==18?"mana":"health-frame";
                string leaf=i==0?"health.png":i<18?"HealthWarning/"+(i+1)+".png":i==18?"mana.png":"health-frame.png";
                string saved=i==0?SpriteFolder+"hud-0.asset":i<18?SpriteFolder+"hud-"+(i+2)+".asset":
                    i==18?SpriteFolder+"hud-1.asset":"Assets/Remains/Resources/Presentation/health-frame.asset";
                var entry=manifest.entries[i];
                Require(entry.id==id && entry.texturePath==TextureFolder+leaf && entry.sprites.Length==1 && entry.sprites[0].assetPath==saved,
                    "Изменены coverage/порядок/пути прежнего HUD: index="+i+", id="+entry.id);
                var sprite=entry.sprites[0];
                int width=i==18?1600:i==19?808:800,height=i==19?104:96;
                float priorPpu=i==19?100:200,priorBorder=i==19?4:0;
                float[] sourceBounds=i==18?new float[] {0,0,200,12}:i==19?new float[] {-50.5f,-6.5f,50.5f,6.5f}:new float[] {0,0,100,12};
                Require(entry.pixelWidth==width && entry.pixelHeight==height && entry.requiredMaxTextureSize==2048 &&
                    Exact(entry.sourceBoundsPixelsYDown,sourceBounds) && Exact(entry.sourceOriginPixelsYDown,new float[] {0,0}) &&
                    sprite.localId==21300000 && sprite.priorPixelsPerUnit==priorPpu && sprite.pixelsPerUnit==priorPpu*2 &&
                    Exact(sprite.priorRect,new float[] {0,0,width/2f,height/2f}) && Exact(sprite.rect,new float[] {0,0,width,height}) &&
                    Exact(sprite.priorPivotPixelsBottomLeft,new float[] {width/4f,height/4f}) &&
                    Exact(sprite.pivotPixelsBottomLeft,new float[] {width/2f,height/2f}) &&
                    Exact(sprite.priorBorder,new float[] {priorBorder,priorBorder,priorBorder,priorBorder}) &&
                    Exact(sprite.border,new float[] {priorBorder*2,priorBorder*2,priorBorder*2,priorBorder*2}),
                    "Изменена согласованная геометрия/плотность HUD: id="+id);
            }
            return manifest;
        }

        [MenuItem("Remains/Графика/Установить векторный HUD8× с сохранением ссылок")]
        public static void Install()
        {
            var manifest=ReadManifest();
            string prefabHash=VectorArtworkReserveManifest.Hash(AssetFile(HudPath));
            var prepared=new List<(Sprite current,Texture2D texture,ReserveSprite declaration,bool update)>();
            var metadataHashes=new Dictionary<string,string>(StringComparer.Ordinal);
            // Complete validation precedes the first CopySerialized. A differing
            // manual binding refuses the entire install rather than being replaced.
            foreach(var entry in manifest.entries)
            {
                var texture=CheckTexture(entry);
                string meta=AssetFile(entry.texturePath)+".meta";
                metadataHashes.Add(meta,VectorArtworkReserveManifest.Hash(meta));
                var declaration=entry.sprites[0];
                var current=CheckIdentity(entry,declaration,texture);
                bool target=Matches(current,declaration,false),prior=Matches(current,declaration,true);
                Require(target || prior,"Прежний Sprite содержит несовместимую ручную геометрию: id="+entry.id+", asset="+declaration.assetPath);
                prepared.Add((current,texture,declaration,!target));
            }
            CheckHudReferences(manifest);
            int changed=0;
            foreach(var item in prepared)
            {
                if(!item.update) continue;
                var d=item.declaration;
                var rect=RectOf(d.rect);
                var pivot=new Vector2(d.pivotPixelsBottomLeft[0]/rect.width,d.pivotPixelsBottomLeft[1]/rect.height);
                var fresh=Sprite.Create(item.texture,rect,pivot,d.pixelsPerUnit,0,SpriteMeshType.FullRect,VectorOf(d.border));
                Require(fresh!=null,"Unity не создала проверенный Sprite: "+d.assetPath);
                try
                {
                    fresh.name=item.current.name;
                    EditorUtility.CopySerialized(fresh,item.current);
                    EditorUtility.SetDirty(item.current);
                    changed++;
                }
                finally {UnityEngine.Object.DestroyImmediate(fresh);}
            }
            AssetDatabase.SaveAssets();
            Check();
            Require(VectorArtworkReserveManifest.Hash(AssetFile(HudPath))==prefabHash,"Установка изменила префаб/раскладку/ссылки HUD.");
            foreach(var pair in metadataHashes)
                Require(VectorArtworkReserveManifest.Hash(pair.Key)==pair.Value,"Установка изменила metadata текстуры: "+pair.Key);
            Debug.Log($"Векторный HUD8×: установлен20рисунков, обновлено Sprite={changed}; GUID/localID, размеры, рамка, раскладка и ссылки сохранены.");
        }

        public static void Check()
        {
            var manifest=ReadManifest();
            foreach(var entry in manifest.entries)
            {
                var texture=CheckTexture(entry);
                var declaration=entry.sprites[0];
                var current=CheckIdentity(entry,declaration,texture);
                Require(Matches(current,declaration,false),"Не установлена точная8× геометрия Sprite: id="+entry.id+", asset="+declaration.assetPath);
                var bounds=current.bounds;
                float[] actual={bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y};
                for(int i=0;i<4;i++) Require(Mathf.Abs(actual[i]-declaration.worldBounds[i])<=.000001f,
                    "Изменён фактический мировой размер/привязка Sprite: id="+entry.id+", edge="+i);
            }
            CheckHudReferences(manifest);
        }

        private static Texture2D CheckTexture(ReserveEntry entry)
        {
            VectorArtworkReserveManifest.VerifyStaged(entry);
            Require(VectorArtworkReserveManifest.Hash(VectorArtworkReserveManifest.RepositoryPath(entry.rawPng))==entry.pngSha256,
                "Подготовленный PNG отличается от прямого SWF export: id="+entry.id);
            Require(VectorArtworkReserveManifest.Hash(AssetFile(entry.texturePath))==entry.pngSha256,
                "Сначала перенесите точный подготовленный8× PNG без замены .meta: id="+entry.id+", texture="+entry.texturePath);
            Require(AssetDatabase.AssetPathToGUID(entry.texturePath)==entry.textureGuid,"Изменён GUID Texture: id="+entry.id);
            var importer=AssetImporter.GetAtPath(entry.texturePath) as TextureImporter;
            Require(importer!=null && importer.textureCompression==TextureImporterCompression.Uncompressed && !importer.mipmapEnabled &&
                importer.npotScale==TextureImporterNPOTScale.None && importer.filterMode==FilterMode.Bilinear &&
                importer.wrapMode==TextureWrapMode.Clamp && importer.sRGBTexture && importer.alphaIsTransparency && importer.maxTextureSize>=entry.requiredMaxTextureSize,
                "Импорт меняет качество/цвет/canvas8×: id="+entry.id+", texture="+entry.texturePath);
            foreach(string platform in new[] {"Standalone","DefaultTexturePlatform"})
            {
                var settings=importer.GetPlatformTextureSettings(platform);
                Require(!settings.overridden || (settings.maxTextureSize>=entry.requiredMaxTextureSize &&
                    settings.textureCompression==TextureImporterCompression.Uncompressed && !settings.crunchedCompression),
                    "Платформенный импорт уменьшает/сжимает8×: id="+entry.id+", platform="+platform);
            }
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.texturePath);
            Require(texture!=null && texture.width==entry.pixelWidth && texture.height==entry.pixelHeight,
                "Фактический импорт имеет другие размеры: id="+entry.id+", texture="+entry.texturePath);
            return texture;
        }

        private static Sprite CheckIdentity(ReserveEntry entry,ReserveSprite declaration,Texture2D texture)
        {
            var current=AssetDatabase.LoadAssetAtPath<Sprite>(declaration.assetPath);
            Require(current!=null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(current,out string guid,out long localId) &&
                guid==declaration.guid && localId==declaration.localId,
                "Нет прежнего Sprite с точным GUID/localID: id="+entry.id+", asset="+declaration.assetPath);
            Require(current.texture==texture,"Прежний Sprite привязан к другой текстуре: id="+entry.id+", asset="+declaration.assetPath);
            return current;
        }

        private static void CheckHudReferences(ReserveManifest manifest)
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            var view=root!=null?root.GetComponent<CombatHudView>():null;
            Require(view!=null,"Нет сохранённого CombatHudView: "+HudPath);view.Validate();
            Sprite Expected(int index)=>AssetDatabase.LoadAssetAtPath<Sprite>(manifest.entries[index].sprites[0].assetPath);
            Require(view.Health.sprite==Expected(0) && view.Mana.sprite==Expected(18) && view.HealthWarningFrames.Length==17,
                "Изменены ссылки/число кадров основных полос HUD.");
            for(int i=0;i<17;i++) Require(view.HealthWarningFrames[i]==Expected(i+1),"Изменён порядок предупреждения HP: index="+i);
            var frame=view.Health.transform.parent.Find("Рамка здоровья");
            var image=frame!=null?frame.GetComponent<Image>():null;
            Require(image!=null && image.sprite==Expected(19) && image.type==Image.Type.Sliced && image.pixelsPerUnitMultiplier==4,
                "Изменена ссылка/тип/масштаб рамки здоровья.");
        }

        private static bool Matches(Sprite sprite,ReserveSprite d,bool prior)
        {
            float[] r=prior?d.priorRect:d.rect,p=prior?d.priorPivotPixelsBottomLeft:d.pivotPixelsBottomLeft,b=prior?d.priorBorder:d.border;
            float ppu=prior?d.priorPixelsPerUnit:d.pixelsPerUnit;
            return sprite.rect==RectOf(r) && sprite.pivot==new Vector2(p[0],p[1]) && sprite.border==VectorOf(b) && sprite.pixelsPerUnit==ppu;
        }
        private static bool Exact(float[] actual,float[] expected)=>actual!=null && actual.SequenceEqual(expected);
        private static Rect RectOf(float[] value)=>new Rect(value[0],value[1],value[2],value[3]);
        private static Vector4 VectorOf(float[] value)=>new Vector4(value[0],value[1],value[2],value[3]);
        private static string AssetFile(string path)=>VectorArtworkReserveManifest.RepositoryPath("RemainsUnity/"+path);
        private static void Require(bool condition,string message)=>VectorArtworkReserveManifest.Require(condition,"HUD: "+message);
    }
}
