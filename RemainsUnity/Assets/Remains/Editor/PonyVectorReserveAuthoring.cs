using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Serialization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit authoring migration. Runtime continues to read the same textures,
    // prepared meshes and registration; raster density never defines body size.
    public static class PonyVectorReserveAuthoring
    {
        private const string ManifestPath="Tools/Research/PonyVectorReserve63/manifest.json";
        private const string PonyRoot="Assets/Remains/Resources/Original/Pony/";
        private const string BankPath=PonyRoot+"Wearables/antirad.json";
        private const string RenderPath=PonyRoot+"Wearables/antirad-render.asset";
        private static readonly int[] Shapes={56,125,196,224,234,238};
        [Serializable] private sealed class Manifest {public int schema;public Entry[] entries;public BankMigration wearableBank;}
        [Serializable] private sealed class BankMigration {public string path,beforeSha256,afterSha256;}
        [Serializable] private sealed class Entry
        {
            public string id,sourceKind,png,pngSha256;public int shapeId,pixelWidth,pixelHeight,requiredMaxTextureSize;
            public float exportZoom;public float[] sourceBoundsTwips;public Destination[] destinations;
        }
        [Serializable] private sealed class Destination
        {
            public string texturePath,productionBeforeSha256,textureGuid,metaBeforeSha256;
            public int priorWidth,priorHeight;
        }
        private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        private static string Repository(string path)
        {
            string absolute=Path.GetFullPath(Path.Combine(Root,path));
            Require(absolute.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Путь вне проекта: "+path);
            return absolute;
        }
        private static string AssetFile(string path)=>Repository("RemainsUnity/"+path);
        private static string Hash(string path)
        {using var stream=File.OpenRead(path);using var sha=SHA256.Create();return Hex(sha.ComputeHash(stream));}
        private static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Векторная графика пони: "+reason);}
        private static Manifest Read(bool preserveOriginalMetadata=false)
        {
            var manifest=StateJson.FromJson<Manifest>(File.ReadAllText(Repository(ManifestPath)));
            Require(manifest?.schema==1 && manifest.entries?.Length==Shapes.Length,"Нужен явный manifest шести рисунков: "+ManifestPath);
            Require(manifest.entries.All(e=>e!=null) && manifest.entries.Select(e=>e.shapeId).OrderBy(i=>i).SequenceEqual(Shapes),"Повтор/неизвестный shape ID: "+ManifestPath);
            Require(manifest.wearableBank!=null && Hash(Repository(manifest.wearableBank.path))==manifest.wearableBank.afterSha256,"Подготовленный antirad bank изменён.");
            var destinations=manifest.entries.SelectMany(e=>e.destinations??Array.Empty<Destination>()).ToArray();
            Require(destinations.Length==10 && destinations.All(d=>d!=null) && destinations.Select(d=>d.texturePath).Distinct(StringComparer.Ordinal).Count()==10,"Нужны шесть базовых текстур и четыре доказанных alias.");
            foreach(var entry in manifest.entries)
            {
                string context=ManifestPath+"/"+entry.id;
                var b=entry.sourceBoundsTwips;
                Require(entry.sourceKind=="true-vector" && entry.exportZoom==8 && b?.Length==4 && b.All(float.IsFinite) && b[2]>b[0] && b[3]>b[1],"Не объявлен настоящий8× вектор/bounds: "+context);
                Require(entry.pixelWidth==(int)Math.Ceiling((b[2]-b[0])/20d*entry.exportZoom) && entry.pixelHeight==(int)Math.Ceiling((b[3]-b[1])/20d*entry.exportZoom) &&
                    entry.requiredMaxTextureSize>=Math.Max(entry.pixelWidth,entry.pixelHeight),"Размер PNG не соответствует source canvas: "+context);
                Require(Hash(Repository(entry.png))==entry.pngSha256,"Подготовленный PNG изменён: "+entry.png);
                string bare=PonyRoot+entry.shapeId+".png",alias=PonyRoot+"Wearables/shape-"+entry.shapeId+".png";
                int count=entry.shapeId is 196 or 224 or 234 or 238?2:1;
                Require(entry.destinations?.Length==count && entry.destinations.Any(d=>d.texturePath==bare) &&
                    entry.destinations.All(d=>d.texturePath==bare || count==2 && d.texturePath==alias),"Необъявленное назначение рисунка: "+context);
                foreach(var destination in entry.destinations)
                {
                    Require(AssetDatabase.AssetPathToGUID(destination.texturePath)==destination.textureGuid,"GUID текстуры изменён: "+destination.texturePath);
                    if(preserveOriginalMetadata) Require(Hash(AssetFile(destination.texturePath)+".meta")==destination.metaBeforeSha256,"Регистрация/importer изменены до миграции: "+destination.texturePath);
                    var importer=AssetImporter.GetAtPath(destination.texturePath) as TextureImporter;
                    Require(importer!=null && importer.textureType==TextureImporterType.Default && importer.npotScale==TextureImporterNPOTScale.None &&
                        !importer.mipmapEnabled && importer.textureCompression==TextureImporterCompression.Uncompressed && importer.maxTextureSize>=Math.Max(entry.pixelWidth,entry.pixelHeight),
                        "Импорт изменит плотность или тип текстуры: "+destination.texturePath);
                    var standalone=importer.GetPlatformTextureSettings("Standalone");
                    Require(!standalone.overridden || standalone.maxTextureSize>=Math.Max(entry.pixelWidth,entry.pixelHeight) && standalone.textureCompression==TextureImporterCompression.Uncompressed,
                        "Standalone уменьшит/сожмёт рисунок: "+destination.texturePath);
                }
            }
            return manifest;
        }
        public static void Run()
        {
            var manifest=Read();int references=0;
            foreach(var entry in manifest.entries) foreach(var destination in entry.destinations)
            {
                Require(Hash(AssetFile(destination.texturePath))==entry.pngSha256,"Не установлены проверенные8× пиксели: "+destination.texturePath);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(destination.texturePath);
                var importer=(TextureImporter)AssetImporter.GetAtPath(destination.texturePath);
                importer.GetSourceTextureWidthAndHeight(out int width,out int height);
                Require(texture!=null && width==entry.pixelWidth && height==entry.pixelHeight && texture.width==width && texture.height==height,
                    "Фактическая Unity Texture2D отличается от экспорта: "+destination.texturePath);
                var render=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>(PonyRoot+"render.asset");
                foreach(var clip in render.Clips) foreach(var frame in clip.frames) foreach(var part in frame.parts)
                    if(part.sourceShape==entry.shapeId.ToString() && destination.texturePath==PonyRoot+entry.shapeId+".png")
                    {Require(part.material.mainTexture==texture,"Материал пони не читает объявленную текстуру: "+entry.id+"/"+clip.id);references++;}
                if(destination.texturePath.Contains("/Wearables/"))
                {
                    var wearable=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>(RenderPath);int aliasReferences=0;
                    foreach(var clip in wearable.Clips) foreach(var frame in clip.frames) foreach(var part in frame.parts)
                        if(part.sourceShape==entry.shapeId.ToString())
                        {Require(part.material.mainTexture==texture,"Материал antirad не читает доказанный alias: "+entry.id+"/"+clip.id);aliasReferences++;}
                    Require(aliasReferences>0,"Не используется заявленный alias: "+destination.texturePath);references+=aliasReferences;
                }
            }
            Require(Hash(AssetFile(BankPath))==manifest.wearableBank.afterSha256,"Декларация antirad не соответствует новым пикселям.");
            Debug.Log($"Векторная графика пони: PASS — {manifest.entries.Length} source shapes,10 Texture2D8×,{references} прежних ссылок; GUID/importer/геометрия сохранены.");
        }
        public static void InstallAndBuild()
        {
            var manifest=Read(true);
            // Check all destinations and bank input before publishing any change.
            foreach(var entry in manifest.entries) foreach(var destination in entry.destinations)
            {
                string current=Hash(AssetFile(destination.texturePath));
                Require(current==destination.productionBeforeSha256 || current==entry.pngSha256,"Авторский PNG изменён; замена запрещена: "+destination.texturePath);
            }
            string currentBankHash=Hash(AssetFile(BankPath));
            Require(currentBankHash==manifest.wearableBank.beforeSha256 || currentBankHash==manifest.wearableBank.afterSha256,"Авторский antirad bank изменён; замена запрещена.");
            string stagedBank=Repository(manifest.wearableBank.path);
            var bank=StateJson.FromJson<WearableSourceBank>(File.ReadAllText(stagedBank));
            Require(bank?.schema==1 && bank.visualId=="pony.armor.antirad" && bank.baseSourceHash==PonyRenderAsset.SourceHash(),"Не подготовлен согласованный antirad bank.");
            string bareBefore=Hash(AssetFile(PonyRoot+"render.asset"));
            string geometryBefore=GeometryFingerprint(RenderPath);
            foreach(var entry in manifest.entries) foreach(var destination in entry.destinations)
                if(Hash(AssetFile(destination.texturePath))!=entry.pngSha256) File.Copy(Repository(entry.png),AssetFile(destination.texturePath),true);
            File.Copy(stagedBank,AssetFile(BankPath),true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            WearableRenderCompiler.BuildAsset(File.ReadAllText(AssetFile(BankPath)),RenderPath,"Original/Pony/Wearables/antirad");
            AssetDatabase.SaveAssets();
            Require(Hash(AssetFile(PonyRoot+"render.asset"))==bareBefore,"Базовая сохранённая геометрия/материалы изменились.");
            Require(GeometryFingerprint(RenderPath)==geometryBefore,"Геометрия, слои или постоянные ссылки antirad изменились.");
            foreach(var entry in manifest.entries) foreach(var destination in entry.destinations)
                Require(Hash(AssetFile(destination.texturePath)+".meta")==destination.metaBeforeSha256,"Миграция изменила сохранённый importer: "+destination.texturePath);
            Debug.Log("Векторная графика пони: миграция сохранила точный geometry fingerprint antirad и полный базовый render.asset; все10 .meta неизменны.");
            Run();PrototypeBuild.BuildTo("Builds/PonyVectorReserve/Remains.exe");Run();
            string evidence=Repository(".reference/tests/pony-vector-reserve-r63/native-import.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidence));
            var textures=manifest.entries.SelectMany(e=>e.destinations.Select(d=>new {
                d.texturePath,d.textureGuid,sourceWidth=e.pixelWidth,sourceHeight=e.pixelHeight,
                width=AssetDatabase.LoadAssetAtPath<Texture2D>(d.texturePath).width,
                height=AssetDatabase.LoadAssetAtPath<Texture2D>(d.texturePath).height,
                pngSha256=Hash(AssetFile(d.texturePath)),metaUnchanged=Hash(AssetFile(d.texturePath)+".meta")==d.metaBeforeSha256
            })).ToArray();
            using var output=new FileStream(evidence,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            using var writer=new StreamWriter(output,new UTF8Encoding(false));
            writer.Write(JsonConvert.SerializeObject(new {success=true,manifestSha256=Hash(Repository(ManifestPath)),textures,
                bareRenderUnchanged=Hash(AssetFile(PonyRoot+"render.asset"))==bareBefore,
                wearableGeometryBefore=geometryBefore,wearableGeometryAfter=GeometryFingerprint(RenderPath),
                runtimeBuild="RemainsUnity/Builds/PonyVectorReserve/Remains.exe",contractsPassed=true},Formatting.Indented));
        }
        private static string GeometryFingerprint(string path)
        {
            var asset=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>(path);
            Require(asset!=null,"Нет скомпилированного antirad: "+path);
            var parts=asset.Clips.Select(clip=>new {clip.id,frames=clip.frames.Select(frame=>new {
                frame.hiddenBasePartIndices,baseOrder=frame.baseOrder.Select(o=>o.path).ToArray(),bounds=new[]{frame.bounds.center.x,frame.bounds.center.y,frame.bounds.center.z,frame.bounds.size.x,frame.bounds.size.y,frame.bounds.size.z},parts=frame.parts.Select(part=>new {
                    part.sourceShape,part.sourcePath,part.sourceIndex,part.drawOrder,part.slot,part.visible,part.hairStyle,part.eyeStyle,part.orderPath,points=part.points.Select(V3).ToArray(),baseColor=Rgba(part.baseColor),
                    mesh=Identity(part.mesh),vertices=part.mesh.vertices.Select(V3).ToArray(),uv=part.mesh.uv.Select(v=>new[]{v.x,v.y}).ToArray(),triangles=part.mesh.triangles,colors=part.mesh.colors.Select(Rgba).ToArray(),
                    material=Identity(part.material),texture=Identity(part.material.mainTexture),shader=Identity(part.material.shader),
                    queue=part.material.renderQueue,keywords=part.material.shaderKeywords.OrderBy(s=>s,StringComparer.Ordinal).ToArray(),
                    textureTransform=new[]{part.material.mainTextureScale.x,part.material.mainTextureScale.y,part.material.mainTextureOffset.x,part.material.mainTextureOffset.y},
                    materialColor=part.material.HasProperty("_Color")?Rgba(part.material.GetColor("_Color")):Array.Empty<float>(),
                    emission=part.material.HasProperty("_Emission")?new[]{part.material.GetFloat("_Emission")}:Array.Empty<float>()
                }).ToArray()}).ToArray()}).ToArray();
            using var sha=SHA256.Create();return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(parts))));
        }
        private static float[] V3(Vector3 value)=>new[]{value.x,value.y,value.z};
        private static float[] Rgba(Color value)=>new[]{value.r,value.g,value.b,value.a};
        private static string Identity(UnityEngine.Object value)
        {Require(value!=null,"Нет постоянного объекта.");Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string guid,out long id),"Нет постоянной ссылки: "+value.name);return guid+":"+id;}
    }
}
