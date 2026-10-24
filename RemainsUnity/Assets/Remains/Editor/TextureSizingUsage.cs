using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Remains.Content;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Editor-only evidence, never a runtime sizing rule. Unknown consumers are retained explicitly.
    public static class TextureSizingUsage
    {
        public const int ReferenceHeight=2160;
        public sealed class Measurement
        {
            public float ScreenPixelsPerSourcePixel;
            public bool Complete;
            public string UnmeasuredReason;
            public readonly List<string> Evidence=new();
        }
        public sealed class Result
        {
            public float OrthographicSize,PixelsPerWorldUnit;
            public readonly Dictionary<string,Measurement> Textures=new(StringComparer.Ordinal);
        }

        public static Result Analyze()
        {
            const string scene="Assets/Remains/Scenes/Remains.unity";
            string sceneText=File.ReadAllText(scene);
            var sizes=Regex.Matches(sceneText,@"(?m)^  orthographic size: ([0-9.eE+-]+)\s*$");
            if(sizes.Count!=1 || !float.TryParse(sizes[0].Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float size) || size<=0)
                throw new InvalidOperationException("Размеры текстур: нужна единственная однозначная камера в "+scene+".");
            var result=new Result {OrthographicSize=size,PixelsPerWorldUnit=ReferenceHeight/(2*size)};
            Rooms(result);
            PrefabEvidence(result);
            ExportedEvidence(result);
            return result;
        }

        private static Measurement Entry(Result result,string path)
        {
            if(!result.Textures.TryGetValue(path,out var entry))
            {
                entry=new Measurement {UnmeasuredReason="Не доказаны максимальные масштабы всех потребителей."};
                result.Textures.Add(path,entry);
            }
            return entry;
        }
        private static void Add(Result result,string path,float ratio,string evidence,bool complete=false)
        {
            if(!float.IsFinite(ratio) || ratio<=0) throw new InvalidOperationException("Размеры текстур: неверный масштаб "+path+" / "+evidence);
            var entry=Entry(result,path);
            entry.ScreenPixelsPerSourcePixel=Mathf.Max(entry.ScreenPixelsPerSourcePixel,ratio);
            if(entry.Evidence.Count<12 && !entry.Evidence.Contains(evidence)) entry.Evidence.Add(evidence);
            if(complete) {entry.Complete=true;entry.UnmeasuredReason=null;}
        }
        private static void Unknown(Result result,string path,string reason)
        {var entry=Entry(result,path);entry.Complete=false;entry.UnmeasuredReason=reason;}

        private static void Rooms(Result result)
        {
            string manifestPath=RoomTileArtPostprocessor.ManifestPath;
            var manifest=TileManifest.Read(File.ReadAllText(manifestPath),manifestPath);
            const string definitionsPath="Assets/Remains/Rooms/Original/definitions.json";
            var definitions=RoomDefinitionCatalog.Read(File.ReadAllText(definitionsPath),definitionsPath);
            var without=RoomPrefabGenerator.LoadWithoutPicture();
            var scales=new Dictionary<string,(float scale,string context)>(StringComparer.Ordinal);
            foreach(string guid in AssetDatabase.FindAssets("t:RoomTemplateAsset",new[]{"Assets/Remains/Rooms/Original"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var room=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(path).Room;
                // Include every frame of every authored clip, including lamp on/off/broken variants.
                foreach(var placement in room.Decor)
                {
                    var definition=definitions.Require(placement.Type,path);
                    if(definition.Art==null || without.ContainsKey(definition.Art)) continue;
                    float scale=Mathf.Max(Mathf.Abs(Parameter(placement.Params,"w")),Mathf.Abs(Parameter(placement.Params,"h")));
                    string key="decor:"+definition.Art+":";
                    if(!scales.TryGetValue(key,out var previous) || previous.scale<scale)
                        scales[key]=(scale,path+" / "+definition.Id+" / w,h");
                }
            }
            foreach(var art in manifest.Art.Values)
            {
                string path=RoomTileArtPostprocessor.Root+art.File;
                float ratio=result.PixelsPerWorldUnit/(float)(RoomTileArtPostprocessor.SourcePixelsPerMeter*art.Zoom);
                if(art.Id.StartsWith("texture:",StringComparison.Ordinal))
                {
                    Add(result,path,ratio,"RoomTerrainView._TexelsPerMeter; WorldTerrain._MaterialTex_TexelSize");
                    Unknown(result,path,"Размер импортированной текстуры задаёт период world-space UV. Уменьшение меняет рисунок; сохраняется разрешение.");
                }
                else if(art.Id.StartsWith("front:",StringComparison.Ordinal))
                    Add(result,path,ratio,"RoomTerrainView.Sprite: единичный масштаб; PPU=50×zoom",true);
                else if(art.Id.StartsWith("mask:",StringComparison.Ordinal) || art.Id.StartsWith("decor-erase:",StringComparison.Ordinal))
                {
                    Add(result,path,ratio,"RoomTerrainView.Layer / RoomDecorResources.BuildErase");
                    Unknown(result,path,"Маска геометрии/стирания: требуется точность альфа-канала, а не только экранный размер.");
                }
                else if(art.Id.StartsWith("decor:",StringComparison.Ordinal))
                {
                    int frameSeparator=art.Id.LastIndexOf(':');
                    if(scales.TryGetValue(art.Id.Substring(0,frameSeparator+1),out var scale))
                        Add(result,path,ratio*scale.scale,scale.context+"; все кадры клипа",true);
                    else Unknown(result,path,"Клип декора отсутствует в авторских шаблонах; максимальный масштаб не доказан.");
                }
                else Unknown(result,path,"Рисунок объекта может масштабироваться PropView/DoorVisuals; все runtime-размеры не доказаны.");
            }
        }
        private static float Parameter(IReadOnlyDictionary<string,string> values,string key)
        {
            if(values==null || !values.TryGetValue(key,out string value)) return 1;
            return float.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);
        }

        private static void PrefabEvidence(Result result)
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Remains"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(root==null) continue;
                foreach(var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    var sprite=renderer.sprite;if(sprite==null) continue;
                    string texturePath=AssetDatabase.GetAssetPath(sprite.texture);
                    var importer=AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if(importer==null) continue;
                    importer.GetSourceTextureWidthAndHeight(out int width,out int height);
                    var matrix=root.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    // Source rect fractions are required: already resized imports must not change the measurement.
                    float sx=matrix.MultiplyVector(Vector3.right).magnitude*sprite.bounds.size.x/(sprite.rect.width*width/sprite.texture.width);
                    float sy=matrix.MultiplyVector(Vector3.up).magnitude*sprite.bounds.size.y/(sprite.rect.height*height/sprite.texture.height);
                    Add(result,texturePath,Mathf.Max(sx,sy)*result.PixelsPerWorldUnit,path+" / "+renderer.name+"; авторская матрица, без доказательства runtime-масштаба");
                }
            }
        }

        private static void ExportedEvidence(Result result)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<ExportedContentCatalog>("Assets/Remains/Resources/Content/ExportedContent.asset");
            if(catalog==null) throw new InvalidOperationException("Размеры текстур: нет каталога ExportedContent.");
            var roles=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
            foreach(var item in catalog.items)
                foreach(var binding in item.graphics??Array.Empty<ExportedBinding>())
                {
                    if(string.IsNullOrEmpty(binding.resourceId)) continue;
                    if(!roles.TryGetValue(binding.resourceId,out var values)) roles[binding.resourceId]=values=new HashSet<string>(StringComparer.Ordinal);
                    values.Add(binding.role??"?");
                }
            foreach(var resource in catalog.resources)
                foreach(var frame in resource.frames??Array.Empty<ExportedFrame>())
                {
                    if(string.IsNullOrEmpty(frame.spriteResource)) continue;
                    string file=Path.GetFileName(frame.spriteResource);
                    string path="Assets/Remains/ImportedContent/Art/"+file+".png";
                    if(!File.Exists(path)) continue;
                    string role=roles.TryGetValue(resource.id,out var values)?string.Join(",",values.OrderBy(v=>v,StringComparer.Ordinal)):"без привязок";
                    Entry(result,path).Evidence.Add("ExportedContent: "+resource.id+" / "+role);
                    Unknown(result,path,"Импортированный ресурс: "+role+". UI, динамические следы/взрывы и смена кадров не ограничены одной авторской матрицей; разрешение сохранено.");
                }
        }
    }
}
