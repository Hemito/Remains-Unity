using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace Remains.Editor
{
    // Explicit migration only. CaptureBaseline never writes an imported asset.
    public static class MenuButtonVectorReserveAuthoring
    {
        public const string ManifestPath="Tools/Research/MenuButtonVectorReserve67/manifest.json";
        public const string BaselinePath=".reference/tests/menu-button-vector-r67/baseline.json";
        private const string TexturePath="Assets/Remains/Resources/Original/Menu/button-glow.png";
        private const string TextureGuid="708db2221f107e64f9343daffce215b4";
        private const string BeforeHash="82b226f1d5710fb978bb0e7126f34c41c9ea7166f676f98d71df84618e7a6655";
        private static readonly string[] SpritePaths={
            "Assets/Remains/Resources/Presentation/MainMenuArtwork/button-glow.asset",
            "Assets/Remains/Resources/Presentation/InventoryWindowArtwork/button-glow.asset",
            "Assets/Remains/Resources/Presentation/CombatHudArtwork/hud-2.asset"};
        private static readonly string[] SpriteGuids={"9eb3ac1279f86e649aa95eef881c4f36","bc6f535782bb1bb44a6b15fbda158062","f0dd06b1ef54c3049a2026f43ac4474e"};
        [Serializable] public sealed class Baseline
        {
            public int schema=1,width,height;
            public string authority="native Unity before registration; original runtime unverified",textureGuid,pngSha256,importerJson;
            public NativeSprite[] sprites;
            public Reference[] references;
        }
        [Serializable] public sealed class NativeSprite
        {
            public ReserveSprite declaration;
            public string name,metaSha256;
            public float[] vertices,bounds;
            public int[] triangles;
        }
        [Serializable] public sealed class Reference {public string path,spriteGuid;public int occurrences;}
        [Serializable] private sealed class Package {public Input nativeBaseline;}
        [Serializable] private sealed class Input {public string path,sha256;}

        [MenuItem("Remains/Графика/Зафиксировать прежнюю кнопку меню1×")]
        public static void CaptureBaseline()
        {
            string output=Repo(BaselinePath);
            Require(!File.Exists(output),"Исторический baseline уже существует; повторная запись запрещена: "+output);
            Require(VectorArtworkReserveManifest.Hash(TexturePath)==BeforeHash,"Изменены прежние PNG кнопки; native before capture отменён.");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Require(texture!=null && importer!=null && texture.width==431 && texture.height==56 &&
                AssetDatabase.AssetPathToGUID(TexturePath)==TextureGuid,"Изменена native Texture1×/GUID кнопки.");
            var baseline=new Baseline {textureGuid=TextureGuid,pngSha256=BeforeHash,width=431,height=56,
                importerJson=EditorJsonUtility.ToJson(importer),sprites=new NativeSprite[SpritePaths.Length]};
            for(int i=0;i<SpritePaths.Length;i++)
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(SpritePaths[i]);
                Require(sprite!=null && sprite.texture==texture && Identity(sprite,i) &&
                    sprite.rect==new Rect(0,0,431,56) && sprite.pivot==new Vector2(215.5f,28) &&
                    sprite.pixelsPerUnit==100 && sprite.border==new Vector4(25,5,25,5),
                    "Изменена прежняя native Sprite registration: "+SpritePaths[i]);
                var b=sprite.bounds;
                baseline.sprites[i]=new NativeSprite {name=sprite.name,metaSha256=HashAsset(SpritePaths[i]+".meta"),
                    declaration=new ReserveSprite {assetPath=SpritePaths[i],guid=SpriteGuids[i],localId=21300000,
                        priorRect=new[]{0f,0,431,56},priorPivotPixelsBottomLeft=new[]{215.5f,28},priorPixelsPerUnit=100,
                        priorBorder=new[]{25f,5,25,5},worldBounds=new[]{b.min.x,b.min.y,b.max.x,b.max.y}},
                    bounds=new[]{b.min.x,b.min.y,b.max.x,b.max.y},vertices=sprite.vertices.SelectMany(v=>new[]{v.x,v.y}).ToArray(),
                    triangles=sprite.triangles.Select(t=>(int)t).ToArray()};
            }
            baseline.references=References();
            // InventoryWindowArtwork is an existing saved spare: its zero references
            // are preserved too. MainMenu and CombatHud are the actual active bindings.
            Require(new[]{SpriteGuids[0],SpriteGuids[2]}.All(g=>baseline.references.Any(r=>r.spriteGuid==g)),"Нет реальных serialized references активных MainMenu/CombatHud Sprite.");
            // The native JSON freezes every importer setting, not just a sampler subset.
            NormalizeImporter(baseline.importerJson,true);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false))) writer.Write(JsonUtility.ToJson(baseline,true)+"\n");
            Debug.Log("Кнопка меню: native baseline1× создан; Texture/Sprite/префабы не изменены. refs="+baseline.references.Length+", path="+output);
        }

        [MenuItem("Remains/Графика/Установить исходную кнопку меню8×")]
        public static void Install()
        {
            var entry=ReadEntry();var baseline=ReadBaseline();
            VectorArtworkReserveManifest.VerifyStaged(entry);
            CheckReferences(baseline);CheckMetas(baseline);
            string current=VectorArtworkReserveManifest.Hash(TexturePath);
            Require(current==BeforeHash || current==entry.pngSha256,"Ручные пиксели кнопки изменены; установка отменена: "+TexturePath);
            var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Require(importer!=null,"Нет native importer кнопки.");
            CheckImporter(importer,baseline,true);
            using(var serialized=new SerializedObject(importer)) LegacyMaximumTextureSize(serialized);
            var saved=SpritePaths.Select(p=>AssetDatabase.LoadAssetAtPath<Sprite>(p)).ToArray();
            for(int i=0;i<saved.Length;i++) Require(Identity(saved[i],i) &&
                (Registration(saved[i],entry.sprites[i],false) || Registration(saved[i],entry.sprites[i],true)) && MeshMatches(saved[i],baseline.sprites[i]),
                "Изменены прежние/целевые Sprite identity, registration или mesh: "+SpritePaths[i]);
            // Validate all replacements on a detached exact staged texture before
            // any production PNG/importer/Sprite write. No fallback reconstruction.
            var staged=new Texture2D(2,2,TextureFormat.RGBA32,false);
            var prepared=new Sprite[saved.Length];
            try
            {
                Require(staged.LoadImage(File.ReadAllBytes(Repo(entry.png)),false) && staged.width==3448 && staged.height==448,
                    "Не прочитан точный staged Texture8× кнопки.");
                for(int i=0;i<prepared.Length;i++) prepared[i]=Prepare(staged,entry.sprites[i],baseline.sprites[i]);
            }
            finally
            {
                foreach(var sprite in prepared) if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(staged);
            }
            if(current!=entry.pngSha256) File.Copy(Repo(entry.png),TexturePath,true);
            importer.maxTextureSize=4096;
            foreach(string platform in ExistingPlatforms(baseline.importerJson))
            {
                var settings=importer.GetPlatformTextureSettings(platform);settings.maxTextureSize=4096;
                importer.SetPlatformTextureSettings(settings);
            }
            // Unity's maxTextureSize setter updates the default platform on this
            // importer, while the serialized legacy field can stay at 2048.
            // Both are explicit parts of the frozen all-caps quality contract.
            using(var serialized=new SerializedObject(importer))
            {
                serialized.Update();LegacyMaximumTextureSize(serialized).intValue=4096;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            importer.SaveAndReimport();
            var texture=CheckTexture(entry,baseline);
            prepared=new Sprite[saved.Length];int changed=0;
            try
            {
                // Reacquire native assets after reimport; Unity may invalidate old handles.
                for(int i=0;i<saved.Length;i++)
                {
                    saved[i]=AssetDatabase.LoadAssetAtPath<Sprite>(SpritePaths[i]);
                    Require(Identity(saved[i],i) && MeshMatches(saved[i],baseline.sprites[i]),"Reimport изменил saved Sprite identity/mesh: "+SpritePaths[i]);
                    if(Registration(saved[i],entry.sprites[i],true)) {Require(saved[i].texture==texture,"Saved Sprite использует иной native Texture: "+SpritePaths[i]);CheckSprite(saved[i],entry.sprites[i],baseline.sprites[i]);continue;}
                    prepared[i]=Prepare(texture,entry.sprites[i],baseline.sprites[i]);
                }
                for(int i=0;i<saved.Length;i++) if(prepared[i]!=null)
                {
                    EditorUtility.CopySerialized(prepared[i],saved[i]);EditorUtility.SetDirty(saved[i]);
                    Require(Identity(saved[i],i),"CopySerialized изменил saved GUID/localID: "+SpritePaths[i]);
                    Require(saved[i].texture==texture,"CopySerialized изменил actual Texture reference: "+SpritePaths[i]);
                    CheckSprite(saved[i],entry.sprites[i],baseline.sprites[i]);changed++;
                }
                AssetDatabase.SaveAssets();
            }
            finally {foreach(var sprite in prepared) if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);}
            Check();
            Debug.Log("Кнопка меню8× установлена: saved Sprite обновлено="+changed+"; прежние GUID/localID/mesh/UI border/world canvas и references сохранены.");
        }

        public static void Check()
        {
            var entry=ReadEntry();var baseline=ReadBaseline();var texture=CheckTexture(entry,baseline);CheckReferences(baseline);CheckMetas(baseline);
            for(int i=0;i<SpritePaths.Length;i++)
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(SpritePaths[i]);
                Require(Identity(sprite,i) && sprite.texture==texture,"Изменены actual saved Sprite GUID/localID/Texture: "+SpritePaths[i]);
                CheckSprite(sprite,entry.sprites[i],baseline.sprites[i]);
            }
            var main=Resources.Load<Sprite>("Presentation/MainMenuArtwork/button-glow");
            Require(main==AssetDatabase.LoadAssetAtPath<Sprite>(SpritePaths[0]),"Runtime resource кнопки не совпадает с единственным сохранённым источником.");
        }

        private static ReserveEntry ReadEntry()
        {
            var manifest=VectorArtworkReserveManifest.Load(ManifestPath);
            Require(manifest.entries.Length==1,"Ожидалась одна выбранная кнопка.");var entry=manifest.entries[0];
            Require(entry.id=="menu-button-glow" && entry.texturePath==TexturePath && entry.textureGuid==TextureGuid &&
                entry.productionBeforeSha256==BeforeHash && entry.pixelWidth==3448 && entry.pixelHeight==448 &&
                entry.requiredMaxTextureSize==4096 && entry.sprites.Length==3,"Изменён утверждённый canvas/source/coverage кнопки.");
            for(int i=0;i<SpritePaths.Length;i++)
            {
                var d=entry.sprites[i];
                Require(d.assetPath==SpritePaths[i] && d.guid==SpriteGuids[i] && d.localId==21300000 &&
                    d.priorRect.SequenceEqual(new[]{0f,0,431,56}) && d.rect.SequenceEqual(new[]{0f,0,3448,448}) &&
                    d.priorPixelsPerUnit==100 && d.pixelsPerUnit==800 && d.priorPivotPixelsBottomLeft.SequenceEqual(new[]{215.5f,28}) &&
                    d.pivotPixelsBottomLeft.SequenceEqual(new[]{1724f,224}) && d.priorBorder.SequenceEqual(new[]{25f,5,25,5}) &&
                    d.border.SequenceEqual(new[]{200f,40,200,40}),"Изменены единственные Sprite/border declarations: "+SpritePaths[i]);
            }
            return entry;
        }
        private static Baseline ReadBaseline()
        {
            var package=JsonUtility.FromJson<Package>(File.ReadAllText(Repo(ManifestPath)));
            Require(package?.nativeBaseline?.path=="Tools/Research/MenuButtonVectorReserve67/source/native-baseline.json" &&
                package.nativeBaseline.sha256?.Length==64,"Сначала требуется native CaptureBaseline и явное связывание frozen input.");
            string path=Repo(package.nativeBaseline.path);
            Require(VectorArtworkReserveManifest.Hash(path)==package.nativeBaseline.sha256,"Изменён frozen native before baseline.");
            var baseline=JsonUtility.FromJson<Baseline>(File.ReadAllText(path));
            Require(baseline?.schema==1 && baseline.textureGuid==TextureGuid && baseline.pngSha256==BeforeHash &&
                baseline.width==431 && baseline.height==56 && baseline.sprites?.Length==3 && baseline.references?.Length>0,
                "Повреждён native before baseline кнопки.");
            for(int i=0;i<SpritePaths.Length;i++) Require(baseline.sprites[i]?.declaration?.assetPath==SpritePaths[i] &&
                baseline.sprites[i].declaration.guid==SpriteGuids[i] && baseline.sprites[i].declaration.localId==21300000 && baseline.sprites[i].vertices?.Length>=6 &&
                baseline.sprites[i].vertices.Length%2==0 && baseline.sprites[i].triangles?.Length>=3 && baseline.sprites[i].bounds?.Length==4,
                "Нет полной native mesh identity: "+SpritePaths[i]);
            return baseline;
        }
        private static JObject NormalizeImporter(string json,bool allowBefore)
        {
            var value=JObject.Parse(json);int caps=0;
            ExistingPlatforms(json);
            var output=value["m_Output"] as JObject;
            Require(output!=null,"Native importer JSON не содержит derived m_Output.");
            var imported=output["textureImportInstructions"] as JObject;
            var source=output["sourceTextureInformation"] as JObject;
            Require(imported!=null && source!=null,"Нет derived import/source Texture dimensions.");
            int width=(int?)imported["width"]??-1,height=(int?)imported["height"]??-1;
            Require((int?)source["width"]==width && (int?)source["height"]==height &&
                ((width==3448 && height==448) || (allowBefore && width==431 && height==56)),
                "Неизвестные derived Texture dimensions: import="+width+"×"+height+", source="+source["width"]+"×"+source["height"]);
            // m_Output is engine-derived, not an importer setting. Its two exact
            // canvases are checked above; all serialized settings stay compared.
            value.Remove("m_Output");
            foreach(var property in value.Descendants().OfType<JProperty>().ToArray())
                if((property.Name.StartsWith("m_",StringComparison.Ordinal)?property.Name.Substring(2):property.Name).Equals("MaxTextureSize",StringComparison.OrdinalIgnoreCase))
                {
                    int actual=(int)property.Value;
                    Require(actual==4096 || (allowBefore && actual==2048),"Неизвестный importer cap: "+property.Path+"="+actual);
                    property.Value=4096;caps++;
                }
            Require(caps>0,"Native importer JSON не содержит явно проверяемый MaxTextureSize.");return value;
        }
        private static string[] ExistingPlatforms(string json)
        {
            var settings=JObject.Parse(json)["m_PlatformSettings"] as JArray;
            Require(settings!=null && settings.Count>0,"Нет native набора platform settings.");
            var names=settings.Select(s=>(string)s["m_BuildTarget"]).ToArray();
            Require(names.All(n=>!string.IsNullOrEmpty(n)) && names.Distinct(StringComparer.Ordinal).Count()==names.Length &&
                names.Contains("DefaultTexturePlatform"),"Повреждены native platform identities.");
            return names;
        }
        private static SerializedProperty LegacyMaximumTextureSize(SerializedObject serialized)
        {
            var limit=serialized.FindProperty("m_MaxTextureSize");
            Require(limit!=null && limit.propertyType==SerializedPropertyType.Integer &&
                (limit.intValue==2048 || limit.intValue==4096),"Неизвестное native legacy поле m_MaxTextureSize: "+TexturePath);
            return limit;
        }
        private static void CheckImporter(TextureImporter importer,Baseline baseline,bool allowBefore)
        {
            Require(AssetDatabase.AssetPathToGUID(TexturePath)==TextureGuid,"Изменён Texture GUID кнопки.");
            var expected=NormalizeImporter(baseline.importerJson,true);var actual=NormalizeImporter(EditorJsonUtility.ToJson(importer),allowBefore);
            Require(JToken.DeepEquals(expected,actual),"Изменены importer settings кнопки помимо разрешённого cap2048→4096; native before/current JSON не совпадают.");
            Require(importer.textureCompression==TextureImporterCompression.Uncompressed && !importer.mipmapEnabled &&
                importer.npotScale==TextureImporterNPOTScale.None,"Импорт кнопки сжимает/уменьшает source RGBA.");
        }
        private static Texture2D CheckTexture(ReserveEntry entry,Baseline baseline)
        {
            Require(VectorArtworkReserveManifest.Hash(TexturePath)==entry.pngSha256,"Не установлен точный source PNG8× кнопки.");
            var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;Require(importer!=null,"Нет Texture importer.");
            CheckImporter(importer,baseline,false);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Require(texture!=null && texture.width==3448 && texture.height==448,"Unity уменьшила actual Texture8× кнопки.");return texture;
        }
        private static Sprite Prepare(Texture2D texture,ReserveSprite d,NativeSprite before)
        {
            var fresh=Sprite.Create(texture,new Rect(d.rect[0],d.rect[1],d.rect[2],d.rect[3]),new Vector2(.5f,.5f),800,0,
                SpriteMeshType.FullRect,new Vector4(200,40,200,40));
            Require(fresh!=null,"Unity не создала candidate Sprite кнопки.");
            try
            {
                int count=before.vertices.Length/2;
                NativeArray<Vector3> positions=default;
                NativeArray<Vector2> uv=default;
                NativeArray<ushort> indices=default;
                try
                {
                    positions=new NativeArray<Vector3>(count,Allocator.Temp);
                    uv=new NativeArray<Vector2>(count,Allocator.Temp);
                    indices=new NativeArray<ushort>(before.triangles.Length,Allocator.Temp);
                    for(int i=0;i<count;i++)
                    {
                        positions[i]=new Vector3(before.vertices[i*2],before.vertices[i*2+1],0);
                        uv[i]=Uv(new Vector2(positions[i].x,positions[i].y),fresh);
                    }
                    for(int i=0;i<indices.Length;i++) indices[i]=checked((ushort)before.triangles[i]);
                    fresh.SetVertexCount(count);fresh.SetVertexAttribute(VertexAttribute.Position,positions);
                    fresh.SetVertexAttribute(VertexAttribute.TexCoord0,uv);fresh.SetIndices(indices);
                }
                finally
                {
                    if(indices.IsCreated) indices.Dispose();
                    if(uv.IsCreated) uv.Dispose();
                    if(positions.IsCreated) positions.Dispose();
                }
                fresh.name=before.name;CheckSprite(fresh,d,before);return fresh;
            }
            catch {UnityEngine.Object.DestroyImmediate(fresh);throw;}
        }
        private static Vector2 Uv(Vector2 point,Sprite sprite)=>new((point.x*sprite.pixelsPerUnit+sprite.pivot.x+sprite.rect.x)/sprite.texture.width,
            (point.y*sprite.pixelsPerUnit+sprite.pivot.y+sprite.rect.y)/sprite.texture.height);
        private static void CheckSprite(Sprite sprite,ReserveSprite d,NativeSprite before)
        {
            Require(sprite!=null && AssetDatabase.GetAssetPath(sprite.texture)==TexturePath ||
                sprite!=null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sprite.texture)),"Candidate читает иной Texture asset.");
            Require(Registration(sprite,d,true) && MeshMatches(sprite,before),"Изменены Sprite registration/точные native vertices/indices: "+d.assetPath);
            var b=sprite.bounds;float[] bounds={b.min.x,b.min.y,b.max.x,b.max.y};
            Require(bounds.SequenceEqual(before.bounds),"Изменены exact native bounds кнопки: "+d.assetPath+", actual="+string.Join(",",bounds.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            var points=sprite.vertices;var actual=sprite.uv;Require(actual.Length==points.Length,"Изменено число UV кнопки.");
            for(int i=0;i<points.Length;i++) Require(Vector2.Distance(actual[i],Uv(points[i],sprite))<=.000001f,"UV не соответствует8× canvas: "+d.assetPath+", vertex="+i);
        }
        private static bool Registration(Sprite sprite,ReserveSprite d,bool target)
        {
            var r=target?d.rect:d.priorRect;var p=target?d.pivotPixelsBottomLeft:d.priorPivotPixelsBottomLeft;var b=target?d.border:d.priorBorder;
            return sprite!=null && sprite.rect==new Rect(r[0],r[1],r[2],r[3]) && sprite.pivot==new Vector2(p[0],p[1]) &&
                sprite.pixelsPerUnit==(target?d.pixelsPerUnit:d.priorPixelsPerUnit) && sprite.border==new Vector4(b[0],b[1],b[2],b[3]);
        }
        private static bool Identity(Sprite sprite,int i)=>sprite!=null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long localId) && guid==SpriteGuids[i] && localId==21300000;
        private static bool MeshMatches(Sprite sprite,NativeSprite before)=>sprite!=null &&
            sprite.vertices.SelectMany(v=>new[]{v.x,v.y}).SequenceEqual(before.vertices) && sprite.triangles.Select(t=>(int)t).SequenceEqual(before.triangles);
        private static Reference[] References()
        {
            var refs=new List<Reference>();
            foreach(string path in Directory.EnumerateFiles(Application.dataPath,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))
            {
                string extension=Path.GetExtension(path);if(extension!=".prefab" && extension!=".asset" && extension!=".unity") continue;
                string text=File.ReadAllText(path);
                foreach(string guid in SpriteGuids)
                {
                    string needle="guid: "+guid;int count=0,start=0;
                    while((start=text.IndexOf(needle,start,StringComparison.Ordinal))>=0) {count++;start+=needle.Length;}
                    if(count>0) refs.Add(new Reference {path="RemainsUnity/Assets"+path.Substring(Application.dataPath.Length).Replace('\\','/'),spriteGuid=guid,occurrences=count});
                }
            }
            return refs.OrderBy(r=>r.path,StringComparer.Ordinal).ToArray();
        }
        private static void CheckReferences(Baseline baseline)=>Require(JsonUtility.ToJson(new ReferenceList {items=References()})==
            JsonUtility.ToJson(new ReferenceList {items=baseline.references}),"Изменены все serialized Sprite reference paths/counts кнопки.");
        [Serializable] private sealed class ReferenceList {public Reference[] items;}
        private static void CheckMetas(Baseline baseline)
        {for(int i=0;i<SpritePaths.Length;i++) Require(HashAsset(SpritePaths[i]+".meta")==baseline.sprites[i].metaSha256,"Изменена protected Sprite metadata: "+SpritePaths[i]);}
        private static string HashAsset(string path)=>VectorArtworkReserveManifest.Hash(Repo("RemainsUnity/"+path));
        private static string Repo(string path)=>VectorArtworkReserveManifest.RepositoryPath(path);
        private static void Require(bool condition,string reason)=>VectorArtworkReserveManifest.Require(condition,"Кнопка меню: "+reason);
    }
}
