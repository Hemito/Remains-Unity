using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace Remains.Editor
{
    // Explicit native capture precedes the density migration. No gameplay,
    // prefab, texture, Sprite, or importer is changed by CaptureBaseline.
    public static class ItemHubVectorReserveAuthoring
    {
        public const string ManifestPath="Tools/Research/ItemHubVectorReserve66/manifest.json";
        public const string BaselinePath=".reference/tests/item-hub-vector-r66/baseline.json";
        private sealed class Selection
        {
            public string id,texture,sprite;
            public int width,height;
            public Vector2 pivot;
            public Selection(string id,string texture,string sprite,int width,int height,Vector2 pivot)
            {this.id=id;this.texture=texture;this.sprite=sprite;this.width=width;this.height=height;this.pivot=pivot;}
        }
        private static readonly Selection[] selected={
            new("grenade-flight","Assets/Remains/Resources/Original/Props/grenade-flight.png",
                "Assets/Remains/Prefabs/Props/grenade-flight-sprite.asset",152,152,new Vector2(.5f,.5f)),
            new("training-dummy","Assets/Remains/Resources/Original/Training/dummy.png",
                "Assets/Remains/Prefabs/Actors/training-dummy-sprite.asset",251,354,new Vector2(.5f,0)),
            new("training-armored-dummy","Assets/Remains/Resources/Original/Training/armored-dummy.png",
                "Assets/Remains/RoomDrafts/ArmoredTrainingSprite.asset",251,354,new Vector2(.5f,0))};

        [Serializable] public sealed class Baseline
        {
            public int schema=1;
            public string classification="native Unity registration capture before 4x to 8x; original runtime unverified";
            public TextureBaseline[] textures;
            public FileBaseline[] protectedFiles;
            public ReferenceBaseline[] references;
        }
        [Serializable] public sealed class TextureBaseline
        {
            public string id,texturePath,textureGuid,pngSha256,rgbaSha256;
            public int width,height,textureType,maxTextureSize,npotScale,compression,filterMode,wrapMode;
            public bool mipmaps,sRgb,alphaIsTransparency;
            public ReserveSprite sprite;
            public float[] meshVertices;
            public int[] meshTriangles;
        }
        [Serializable] public sealed class FileBaseline {public string path,sha256;}
        [Serializable] public sealed class ReferenceBaseline {public string path,spriteGuid;public int occurrences;}
        [Serializable] private sealed class Package
        {public Input nativeBaseline;public ArtworkUpdate[] artworkUpdates;}
        [Serializable] private sealed class Input {public string path,sha256;}
        [Serializable] private sealed class ArtworkUpdate {public string path,beforeSha256,afterSha256,staged;}

        [MenuItem("Remains/Графика/Установить гранату и мишени8× с сохранением ссылок")]
        public static void Install()
        {
            var manifest=ReadManifest();var package=ReadPackage();var baseline=ReadBaseline(package);
            CheckProtectedFiles(baseline);
            // Every input and old/new registration is verified before the first
            // PNG or Sprite write. Native reference/mesh capture is authoritative.
            for(int i=0;i<selected.Length;i++)
            {
                var entry=manifest.entries[i];var before=baseline.textures[i];
                VectorArtworkReserveManifest.VerifyStaged(entry);
                string hash=VectorArtworkReserveManifest.Hash(entry.texturePath);
                Require(hash==entry.productionBeforeSha256 || hash==entry.pngSha256,"Ручные пиксели изменены; замена отменена: "+entry.id);
                VectorArtworkReserveAuthoring.RequireCompatible(entry);
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(entry.sprites[0].assetPath);
                Require(MeshMatches(sprite,before),"Изменена прежняя Sprite геометрия: "+entry.id);
                CheckImporter(entry,before);
            }
            foreach(var update in package.artworkUpdates)
            {
                Require(VectorArtworkReserveManifest.Hash(VectorArtworkReserveManifest.RepositoryPath(update.staged))==update.afterSha256,
                    "Изменена подготовленная artwork metadata: "+update.path);
                string hash=VectorArtworkReserveManifest.Hash(update.path);
                Require(hash==update.beforeSha256 || hash==update.afterSha256,"Ручные artwork metadata изменены; замена отменена: "+update.path);
            }
            foreach(var entry in manifest.entries)
                if(VectorArtworkReserveManifest.Hash(entry.texturePath)!=entry.pngSha256)
                    File.Copy(VectorArtworkReserveManifest.RepositoryPath(entry.png),entry.texturePath,true);
            foreach(var update in package.artworkUpdates)
                if(VectorArtworkReserveManifest.Hash(update.path)!=update.afterSha256)
                    File.Copy(VectorArtworkReserveManifest.RepositoryPath(update.staged),update.path,true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int changed=0;
            var replacements=new Sprite[selected.Length];
            try
            {
                // Prepare and verify every native replacement before modifying any
                // saved Sprite. A previous PNG-only or partial Sprite install is valid.
                for(int i=0;i<selected.Length;i++)
                {
                    var entry=manifest.entries[i];var declaration=entry.sprites[0];var before=baseline.textures[i];
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(declaration.assetPath);
                    var texture=CheckTexture(entry,before);
                    if(Matches(sprite,declaration)) {CheckNativeSprite(sprite,entry,before);continue;}
                    replacements[i]=PrepareSprite(texture,entry,before);
                    replacements[i].name=sprite.name;
                }
                for(int i=0;i<selected.Length;i++)
                {
                    if(replacements[i]==null) continue;
                    var entry=manifest.entries[i];var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(entry.sprites[0].assetPath);
                    EditorUtility.CopySerialized(replacements[i],sprite);
                    CheckNativeSprite(sprite,entry,baseline.textures[i]);
                    Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long localId) &&
                        guid==entry.sprites[0].guid && localId==entry.sprites[0].localId,
                        "CopySerialized изменил сохранённую Sprite identity: "+entry.id);
                    EditorUtility.SetDirty(sprite);changed++;
                }
            }
            finally {foreach(var replacement in replacements) if(replacement!=null) UnityEngine.Object.DestroyImmediate(replacement);}
            AssetDatabase.SaveAssets();Check();CheckProtectedFiles(baseline);
            Debug.Log($"Векторная графика8×: граната/две мишени установлены, Sprite обновлено={changed}; GUID/localID, pivot, mesh и мировой размер сохранены.");
        }

        public static void Validate()=>Check();
        public static void VerifyPreservation()
        {Check();CheckProtectedFiles(ReadBaseline(ReadPackage()));}
        public static void Check()
        {
            var manifest=ReadManifest();var package=ReadPackage();var baseline=ReadBaseline(package);
            // Routine content checks freeze only this resource contract. The
            // wider before/after prefab/file hashes belong to the one-time
            // migration audit, not to unrelated future hub authoring.
            foreach(var reference in baseline.references.Where(r=>!r.path.EndsWith("/Levels/hub.prefab",StringComparison.Ordinal)))
                Reference(VectorArtworkReserveManifest.RepositoryPath(reference.path),reference.spriteGuid,reference.occurrences);
            for(int i=0;i<selected.Length;i++)
            {
                var entry=manifest.entries[i];var before=baseline.textures[i];
                CheckTexture(entry,before);
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(entry.sprites[0].assetPath);
                Require(sprite!=null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long localId) &&
                    guid==entry.sprites[0].guid && localId==entry.sprites[0].localId &&
                    AssetDatabase.GetAssetPath(sprite.texture)==entry.texturePath && Matches(sprite,entry.sprites[0]) && MeshMatches(sprite,before),
                    "Изменены установленные Sprite ссылки/регистрация/mesh: "+entry.id);
                CheckNativeSprite(sprite,entry,before);
            }
            foreach(var update in package.artworkUpdates)
                Require(VectorArtworkReserveManifest.Hash(update.path)==update.afterSha256,"Не установлена точная8× artwork metadata: "+update.path);
        }

        private static ReserveManifest ReadManifest()
        {
            var manifest=VectorArtworkReserveManifest.Load(ManifestPath);
            Require(manifest.entries.Length==selected.Length,"Ожидались ровно3 согласованных рисунка.");
            for(int i=0;i<selected.Length;i++)
            {
                var entry=manifest.entries[i];var source=selected[i];var d=entry.sprites[0];
                Require(entry.id==source.id && entry.texturePath==source.texture && entry.sprites.Length==1 && d.assetPath==source.sprite &&
                    entry.pixelWidth==source.width*2 && entry.pixelHeight==source.height*2 && d.priorPixelsPerUnit==200 && d.pixelsPerUnit==400 &&
                    d.priorRect.SequenceEqual(new float[] {0,0,source.width,source.height}) &&
                    d.rect.SequenceEqual(new float[] {0,0,source.width*2,source.height*2}) &&
                    d.priorPivotPixelsBottomLeft.SequenceEqual(new[]{source.width*source.pivot.x,source.height*source.pivot.y}) &&
                    d.pivotPixelsBottomLeft.SequenceEqual(new[]{source.width*source.pivot.x*2,source.height*source.pivot.y*2}),
                    "Изменён согласованный canvas/pivot/coverage: "+source.id);
            }
            return manifest;
        }
        private static Package ReadPackage()
        {
            var package=JsonUtility.FromJson<Package>(File.ReadAllText(VectorArtworkReserveManifest.RepositoryPath(ManifestPath)));
            Require(package?.nativeBaseline?.path=="Tools/Research/ItemHubVectorReserve66/source/native-baseline.json" &&
                package.artworkUpdates?.Length==2 && package.artworkUpdates.Select(u=>u.path).SequenceEqual(new[]{
                    "Assets/Remains/Resources/Original/Training/artwork.json","Assets/Remains/Resources/Original/Training/armored-artwork.json"}),
                "Нет единственного frozen native baseline/artwork metadata.");
            return package;
        }
        private static Baseline ReadBaseline(Package package)
        {
            string path=VectorArtworkReserveManifest.RepositoryPath(package.nativeBaseline.path);
            Require(VectorArtworkReserveManifest.Hash(path)==package.nativeBaseline.sha256,"Изменён frozen native baseline.");
            var baseline=JsonUtility.FromJson<Baseline>(File.ReadAllText(path));
            Require(baseline?.schema==1 && baseline.textures?.Length==3 && baseline.protectedFiles?.Length>0 && baseline.references?.Length==6,
                "Повреждён frozen native baseline.");
            for(int i=0;i<selected.Length;i++) Require(baseline.textures[i].id==selected[i].id &&
                baseline.textures[i].texturePath==selected[i].texture && baseline.textures[i].sprite.assetPath==selected[i].sprite &&
                baseline.textures[i].meshVertices?.Length>=6 && baseline.textures[i].meshVertices.Length%2==0 && baseline.textures[i].meshTriangles?.Length>=3,
                "Изменена native baseline identity/геометрия: "+selected[i].id);
            return baseline;
        }
        private static void CheckProtectedFiles(Baseline baseline)
        {
            string[] allowed=selected.SelectMany(s=>new[]{"RemainsUnity/"+s.texture,"RemainsUnity/"+s.sprite}).Concat(new[]{
                "RemainsUnity/Assets/Remains/Resources/Original/Training/artwork.json",
                "RemainsUnity/Assets/Remains/Resources/Original/Training/armored-artwork.json"}).ToArray();
            foreach(var file in baseline.protectedFiles.Where(f=>!allowed.Contains(f.path,StringComparer.Ordinal)))
                Require(VectorArtworkReserveManifest.Hash(VectorArtworkReserveManifest.RepositoryPath(file.path))==file.sha256,
                    "Изменён защищённый prefab/metadata: "+file.path);
            foreach(var reference in baseline.references)
                Reference(VectorArtworkReserveManifest.RepositoryPath(reference.path),reference.spriteGuid,reference.occurrences);
        }
        private static void CheckImporter(ReserveEntry entry,TextureBaseline before)
        {
            var importer=AssetImporter.GetAtPath(entry.texturePath) as TextureImporter;
            Require(importer!=null && AssetDatabase.AssetPathToGUID(entry.texturePath)==before.textureGuid && entry.textureGuid==before.textureGuid &&
                (int)importer.textureType==before.textureType && (int)importer.textureCompression==before.compression &&
                (int)importer.npotScale==before.npotScale && (int)importer.filterMode==before.filterMode && (int)importer.wrapMode==before.wrapMode &&
                importer.mipmapEnabled==before.mipmaps && importer.sRGBTexture==before.sRgb && importer.alphaIsTransparency==before.alphaIsTransparency &&
                importer.maxTextureSize==before.maxTextureSize && importer.maxTextureSize>=entry.requiredMaxTextureSize,
                "Изменены GUID/sampler/color/import settings: "+entry.id);
            foreach(string platform in new[]{"Standalone","DefaultTexturePlatform"})
            {
                var settings=importer.GetPlatformTextureSettings(platform);
                Require(!settings.overridden || (settings.maxTextureSize>=Math.Max(entry.pixelWidth,entry.pixelHeight) &&
                    settings.textureCompression==TextureImporterCompression.Uncompressed && !settings.crunchedCompression),
                    "Платформенный импорт уменьшает/сжимает8×: "+entry.id+", platform="+platform);
            }
        }
        private static Texture2D CheckTexture(ReserveEntry entry,TextureBaseline before)
        {
            VectorArtworkReserveManifest.VerifyStaged(entry);
            Require(VectorArtworkReserveManifest.Hash(entry.texturePath)==entry.pngSha256,"Не установлены точные8× PNG: "+entry.id);
            CheckImporter(entry,before);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.texturePath);
            Require(texture!=null && texture.width==entry.pixelWidth && texture.height==entry.pixelHeight,"Unity изменила размер PNG: "+entry.id);
            return texture;
        }
        private static bool Matches(Sprite sprite,ReserveSprite declaration)=>sprite!=null &&
            sprite.rect==new Rect(declaration.rect[0],declaration.rect[1],declaration.rect[2],declaration.rect[3]) &&
            sprite.pixelsPerUnit==declaration.pixelsPerUnit && sprite.pivot==new Vector2(declaration.pivotPixelsBottomLeft[0],declaration.pivotPixelsBottomLeft[1]) &&
            sprite.border==new Vector4(declaration.border[0],declaration.border[1],declaration.border[2],declaration.border[3]);
        private static Sprite PrepareSprite(Texture2D texture,ReserveEntry entry,TextureBaseline before)
        {
            var declaration=entry.sprites[0];
            var rect=new Rect(declaration.rect[0],declaration.rect[1],declaration.rect[2],declaration.rect[3]);
            var pivot=new Vector2(declaration.pivotPixelsBottomLeft[0]/rect.width,declaration.pivotPixelsBottomLeft[1]/rect.height);
            // The captured grenade has a tight mesh; the two targets are full quads.
            // OverrideGeometry is rejected in this native Editor context and takes
            // rect-space pixels, not the captured pivot-relative metre positions.
            // SpriteDataAccess writes those exact float positions without a lossy
            // metre -> pixel -> metre roundtrip. Rect/PPU and full bounds come from
            // the explicit canvas declaration, not from the mesh's opaque extent.
            bool tight=entry.id=="grenade-flight";
            var fresh=Sprite.Create(texture,rect,pivot,declaration.pixelsPerUnit,0,
                tight?SpriteMeshType.Tight:SpriteMeshType.FullRect,
                new Vector4(declaration.border[0],declaration.border[1],declaration.border[2],declaration.border[3]));
            try
            {
                Require(fresh!=null,"Unity не создала Sprite: "+entry.id);
                fresh.name=entry.id+"-8x-prepared";
                if(tight)
                {
                    int count=before.meshVertices.Length/2;
                    NativeArray<Vector3> positions=default;
                    NativeArray<Vector2> uv=default;
                    NativeArray<ushort> indices=default;
                    try
                    {
                        positions=new NativeArray<Vector3>(count,Allocator.Temp);
                        uv=new NativeArray<Vector2>(count,Allocator.Temp);
                        indices=new NativeArray<ushort>(before.meshTriangles.Length,Allocator.Temp);
                        for(int i=0;i<count;i++)
                        {
                            positions[i]=new Vector3(before.meshVertices[i*2],before.meshVertices[i*2+1],0);
                            uv[i]=ExpectedUv(new Vector2(positions[i].x,positions[i].y),fresh);
                        }
                        for(int i=0;i<indices.Length;i++) indices[i]=checked((ushort)before.meshTriangles[i]);
                        fresh.SetVertexCount(count);
                        fresh.SetVertexAttribute(VertexAttribute.Position,positions);
                        fresh.SetVertexAttribute(VertexAttribute.TexCoord0,uv);
                        fresh.SetIndices(indices);
                        var actualUv=fresh.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
                        Require(actualUv.Length==count,"Unity изменила размер UV channel: "+entry.id);
                        for(int i=0;i<count;i++) Require(actualUv[i].x==uv[i].x && actualUv[i].y==uv[i].y,
                            "Unity изменила сохранённый UV channel: "+entry.id+", vertex="+i);
                    }
                    finally
                    {
                        if(indices.IsCreated) indices.Dispose();
                        if(uv.IsCreated) uv.Dispose();
                        if(positions.IsCreated) positions.Dispose();
                    }
                }
                CheckNativeSprite(fresh,entry,before);
                return fresh;
            }
            catch {if(fresh!=null) UnityEngine.Object.DestroyImmediate(fresh);throw;}
        }
        private static Vector2 ExpectedUv(Vector2 vertex,Sprite sprite)=>new Vector2(
            (vertex.x*sprite.pixelsPerUnit+sprite.pivot.x+sprite.rect.x)/sprite.texture.width,
            (vertex.y*sprite.pixelsPerUnit+sprite.pivot.y+sprite.rect.y)/sprite.texture.height);
        private static void CheckNativeSprite(Sprite sprite,ReserveEntry entry,TextureBaseline before)
        {
            Require(Matches(sprite,entry.sprites[0]),"Unity изменила Sprite rect/pivot/PPU/border: "+entry.id);
            Require(MeshMatches(sprite,before),"Unity изменила проверенную Sprite геометрию: "+entry.id+"; "+MeshDifference(sprite,before));
            var bounds=sprite.bounds;float[] actual={bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y};
            Require(actual.Select((value,index)=>Mathf.Abs(value-before.sprite.worldBounds[index])<=.000001f).All(v=>v),
                "Изменены реальные мировые bounds Sprite: "+entry.id+"; actual="+string.Join(",",actual.Select(Exact))+
                "; baseline="+string.Join(",",before.sprite.worldBounds.Select(Exact)));
            var vertices=sprite.vertices;var uv=sprite.uv;
            Require(uv.Length==vertices.Length,"Unity изменила число Sprite UV: "+entry.id);
            for(int i=0;i<uv.Length;i++)
            {
                var expected=ExpectedUv(vertices[i],sprite);
                // Native UV evaluation may multiply by reciprocal texture size.
                // Geometry/indices stay bit-exact; this separate mapping check is
                // bounded to subpixel (<.001px) error on the selected canvases.
                Require(float.IsFinite(uv[i].x) && float.IsFinite(uv[i].y) &&
                    Mathf.Abs(uv[i].x-expected.x)<=.000001f && Mathf.Abs(uv[i].y-expected.y)<=.000001f,
                    "Sprite UV не соответствует Texture8×/pivot/PPU: "+entry.id+", vertex="+i+
                    ", actual="+Exact(uv[i].x)+","+Exact(uv[i].y)+", expected="+Exact(expected.x)+","+Exact(expected.y));
            }
        }
        private static string Exact(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        private static string MeshDifference(Sprite sprite,TextureBaseline before)
        {
            if(sprite==null) return "Sprite=null";
            var vertices=sprite.vertices;var triangles=sprite.triangles;
            return "actual vertices=["+string.Join(",",vertices.SelectMany(v=>new[]{Exact(v.x),Exact(v.y)}))+
                "], triangles=["+string.Join(",",triangles)+"], baseline vertices=["+
                string.Join(",",before.meshVertices.Select(Exact))+"], triangles=["+string.Join(",",before.meshTriangles)+"]";
        }
        private static bool MeshMatches(Sprite sprite,TextureBaseline before)
        {
            if(sprite==null) return false;
            var vertices=sprite.vertices;var triangles=sprite.triangles;
            if(vertices.Length*2!=before.meshVertices.Length || triangles.Length!=before.meshTriangles.Length) return false;
            for(int i=0;i<vertices.Length;i++) if(vertices[i].x!=before.meshVertices[i*2] || vertices[i].y!=before.meshVertices[i*2+1]) return false;
            for(int i=0;i<triangles.Length;i++) if(triangles[i]!=before.meshTriangles[i]) return false;
            return true;
        }

        [MenuItem("Remains/Графика/Зафиксировать прежние ссылки гранаты и мишеней4×")]
        public static void CaptureBaseline()
        {
            string output=VectorArtworkReserveManifest.RepositoryPath(BaselinePath);
            Require(!File.Exists(output),"Baseline уже существует; историческое свидетельство не перезаписывается: "+output);
            var baseline=new Baseline {textures=selected.Select(Capture).ToArray()};
            var paths=selected.SelectMany(s=>new[]{s.texture,s.texture+".meta",s.sprite,s.sprite+".meta"}).Concat(new[]{
                "Assets/Remains/Resources/Original/Training/artwork.json",
                "Assets/Remains/Resources/Original/Training/armored-artwork.json",
                "Assets/Remains/Prefabs/Weapons/grenade.prefab",
                "Assets/Remains/Prefabs/Props/grenade-flight.prefab",
                "Assets/Remains/Prefabs/Actors/training-dummy.prefab",
                "Assets/Remains/RoomDrafts/training-armored-dummy.prefab",
                "Assets/Remains/Resources/Levels/hub.prefab"}).ToArray();
            baseline.protectedFiles=paths.Select(path=>new FileBaseline {
                path="RemainsUnity/"+path,sha256=VectorArtworkReserveManifest.Hash(path)}).ToArray();
            baseline.references=new[]{
                Reference("Assets/Remains/Prefabs/Weapons/grenade.prefab",baseline.textures[0].sprite.guid,1),
                Reference("Assets/Remains/Prefabs/Props/grenade-flight.prefab",baseline.textures[0].sprite.guid,1),
                Reference("Assets/Remains/Prefabs/Actors/training-dummy.prefab",baseline.textures[1].sprite.guid,1),
                Reference("Assets/Remains/RoomDrafts/training-armored-dummy.prefab",baseline.textures[2].sprite.guid,1),
                Reference("Assets/Remains/Resources/Levels/hub.prefab",baseline.textures[1].sprite.guid,3),
                Reference("Assets/Remains/Resources/Levels/hub.prefab",baseline.textures[2].sprite.guid,2)};
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(baseline,true)+"\n",new System.Text.UTF8Encoding(false));
            Debug.Log("Векторная графика: native baseline3рисунков4× сохранён; PNG, GUID/localID, world bounds, pivot и ссылки не изменены: "+output);
        }

        private static TextureBaseline Capture(Selection selection)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(selection.texture);
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(selection.sprite);
            var importer=AssetImporter.GetAtPath(selection.texture) as TextureImporter;
            Require(texture!=null && importer!=null && texture.width==selection.width && texture.height==selection.height,
                "Изменён прежний Texture canvas: "+selection.id+", path="+selection.texture);
            Require(sprite!=null && sprite.texture==texture && sprite.rect==new Rect(0,0,selection.width,selection.height) &&
                sprite.pixelsPerUnit==200 && sprite.pivot==new Vector2(selection.width*selection.pivot.x,selection.height*selection.pivot.y) &&
                sprite.border==Vector4.zero,"Изменена прежняя Sprite регистрация: "+selection.id+", path="+selection.sprite);
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long localId) && localId==21300000,
                "Не получены реальные GUID/localID Sprite: "+selection.sprite);
            var bounds=sprite.bounds;
            var declaration=new ReserveSprite {assetPath=selection.sprite,guid=guid,localId=localId,priorPixelsPerUnit=200,
                priorRect=new[]{sprite.rect.x,sprite.rect.y,sprite.rect.width,sprite.rect.height},
                priorPivotPixelsBottomLeft=new[]{sprite.pivot.x,sprite.pivot.y},priorBorder=new float[4],
                worldBounds=new[]{bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y}};
            return new TextureBaseline {id=selection.id,texturePath=selection.texture,textureGuid=AssetDatabase.AssetPathToGUID(selection.texture),
                pngSha256=VectorArtworkReserveManifest.Hash(selection.texture),width=texture.width,height=texture.height,
                textureType=(int)importer.textureType,maxTextureSize=importer.maxTextureSize,npotScale=(int)importer.npotScale,
                compression=(int)importer.textureCompression,filterMode=(int)importer.filterMode,wrapMode=(int)importer.wrapMode,
                mipmaps=importer.mipmapEnabled,sRgb=importer.sRGBTexture,alphaIsTransparency=importer.alphaIsTransparency,sprite=declaration,
                meshVertices=sprite.vertices.SelectMany(v=>new[]{v.x,v.y}).ToArray(),meshTriangles=sprite.triangles.Select(v=>(int)v).ToArray()};
        }
        private static ReferenceBaseline Reference(string path,string guid,int expected)
        {
            string text=File.ReadAllText(path),needle="m_Sprite: {fileID: 21300000, guid: "+guid+",";
            int count=0,index=0;
            while((index=text.IndexOf(needle,index,StringComparison.Ordinal))>=0) {count++;index+=needle.Length;}
            Require(count==expected,"Изменены прежние Sprite-ссылки: path="+path+", guid="+guid+", count="+count+", expected="+expected);
            return new ReferenceBaseline {path="RemainsUnity/"+path,spriteGuid=guid,occurrences=count};
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Векторная графика гранаты/мишеней: "+message);}
    }
}
