using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Room prefabs from templates (decision 2026-10-10): for every template of a land, a prefab and its
    // mirrored variant ("-m") with the authored content — decor and object pictures (RoomSpriteLayout,
    // shared with the preview), object points, template metadata. Walls, floors, ladders and light
    // occluders are not in the prefab: level preparation builds them from the level grid. The template
    // stays the source of truth; prefabs are regenerated, not edited by hand (a hand edit is overwritten).
    //   Menu «Remains/Комнаты/Создать префабы комнат завода»;
    //   batch -executeMethod Remains.Editor.RoomPrefabGenerator.RunBatch
    public static class RoomPrefabGenerator
    {
        public const int GeneratorVersion=1;
        private const string Templates="Assets/Remains/Rooms/Original/plant";
        private const string Output="Assets/Remains/Rooms/Original/Prefabs/plant";
        private const string Materials="Assets/Remains/Rooms/Original/Prefabs/Materials";
        private const string Definitions="Assets/Remains/Rooms/Original/definitions.json";
        private const string Lands="Assets/Remains/Rooms/Original/lands.json";
        private const string Lighting="Assets/Remains/Rooms/Original/room-lighting.json";

        [MenuItem("Remains/Комнаты/Создать префабы комнат завода")]
        public static void Run() => Generate();

        public static void RunBatch()
        {
            int code=0;
            try {Generate();}
            catch(Exception error) {Debug.LogError("Префабы комнат: прервано: "+error);code=1;}
            EditorApplication.Exit(code);
        }

        private static void Generate()
        {
            var definitions=RoomDefinitionCatalog.Read(Text(Definitions),Definitions);
            var lands=RoomLandCatalog.Read(Text(Lands),Lands);
            var table=RoomLightingTable.Read(Text(Lighting),Lighting);
            var glow=table.Glow.ToDictionary(e=>e.Key,e=>e.Value.Threshold,StringComparer.Ordinal);
            var art=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(RoomTileArtAuthoring.AssetPath)
                ??throw new InvalidOperationException("Нет набора графики клеток: выполните «Remains/Комнаты/Обновить набор графики клеток».");
            var spriteShader=Resources.Load<Shader>(RoomDecorView.SpriteShaderResource)??throw new InvalidOperationException("Нет шейдера "+RoomDecorView.SpriteShaderResource);
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Materials);
            var materials=new Dictionary<string,Material>(StringComparer.Ordinal);
            Material MaterialFor(RoomDefinition d,string context)
            {
                string key=RoomDecorView.MaterialKey(d,glow);
                if(materials.TryGetValue(key,out var cached)) return cached;
                string file=AssetName(d,glow);
                string path=$"{Materials}/{file}.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null) {material=new Material(spriteShader);AssetDatabase.CreateAsset(material,path);}
                material.shader=spriteShader;material.name=file;
                RoomDecorView.Configure(material,d,glow,context);
                EditorUtility.SetDirty(material);
                materials.Add(key,material);
                return material;
            }

            var written=new HashSet<string>(StringComparer.Ordinal);
            int sprites=0,points=0;
            var guids=AssetDatabase.FindAssets("t:"+nameof(RoomTemplateAsset),new[]{Templates});
            foreach(string guid in guids.OrderBy(g=>AssetDatabase.GUIDToAssetPath(g),StringComparer.Ordinal))
            {
                string templatePath=AssetDatabase.GUIDToAssetPath(guid);
                var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath);
                var room=template.Room;
                string fingerprint=Sha256(RoomSerializer.Write(room));
                string fileName=Path.GetFileName(templatePath).Replace(".room.json","");
                foreach(bool mirror in new[]{false,true})
                {
                    var issues=new List<string>();
                    var lightZones=new SortedDictionary<string,int>(StringComparer.Ordinal);
                    var otherObjects=new SortedDictionary<string,int>(StringComparer.Ordinal);
                    var placed=RoomSpriteLayout.Place(room,mirror,definitions,art.Manifest,Number(lands.Option(room,"lon",issues)),issues,lightZones,otherObjects);
                    var root=new GameObject(fileName+(mirror?"-m":""));
                    try
                    {
                        root.AddComponent<RoomTemplateInstance>().Configure(template,fingerprint,GeneratorVersion,mirror);
                        var pictures=Child(root.transform,"Декор и объекты");
                        foreach(var p in placed)
                        {
                            var obj=Child(pictures,(p.Kind==RoomSpriteKind.Decor?"Декор ":"Объект ")+p.Definition.Art);
                            obj.localPosition=new Vector3((float)p.X,(float)p.Y,0);
                            obj.localScale=new Vector3((float)p.ScaleX,(float)p.ScaleY,1);
                            var renderer=obj.gameObject.AddComponent<SpriteRenderer>();
                            renderer.sprite=art.Tile(p.ArtId).sprite;
                            renderer.sharedMaterial=MaterialFor(p.Definition,room.Template);
                            renderer.color=new Color(1,1,1,(float)p.Alpha);
                            renderer.sortingOrder=RoomDecorView.Order(p);
                            obj.gameObject.AddComponent<RoomSpriteAuthoring>().Configure(p,p.EraseArtId==null?null:art.Tile(p.EraseArtId).sprite);
                            sprites++;
                        }
                        var objects=Child(root.transform,"Точки объектов");
                        for(int i=0;i<room.Objects.Count;i++)
                        {
                            var o=room.Objects[i];
                            var d=definitions.Require(o.Type,$"{room.Template} объект {o.Code}");
                            int width=Math.Max(1,d.ObjectSize),cellX=RoomSpriteLayout.ObjectCellX(room,o,d,mirror);
                            var point=Child(objects,$"{d.ObjectType ?? "?"} {o.Code}");
                            point.localPosition=new Vector3((float)((cellX+width/2.0)*room.Cell),(float)(o.Y*room.Cell),0);
                            point.gameObject.AddComponent<RoomTemplateObjectPoint>().Configure(i,o.Code,o.Type,new Vector2Int(cellX,o.Y),width);
                            points++;
                        }
                        foreach(string issue in issues) Debug.LogWarning(issue);
                        string path=$"{Output}/{root.name}.prefab";
                        PrefabUtility.SaveAsPrefabAsset(root,path,out bool saved);
                        if(!saved) throw new IOException("Не сохранён префаб "+path);
                        written.Add(path);
                    }
                    finally {UnityEngine.Object.DestroyImmediate(root);}
                }
            }
            // Prefabs of templates that no longer exist would be silently stale: remove them, saying so.
            foreach(string stale in AssetDatabase.FindAssets("t:Prefab",new[]{Output}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!written.Contains(p)).ToList())
            {
                Debug.LogWarning("Префабы комнат: удалён префаб без шаблона "+stale);
                AssetDatabase.DeleteAsset(stale);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Префабы комнат: {written.Count} ({guids.Length} шаблонов × обычный/зеркальный), картинок {sprites}, точек объектов {points}, материалов {materials.Count}.");
        }

        private static Transform Child(Transform parent,string name)
        {
            var obj=new GameObject(name).transform;
            obj.SetParent(parent,false);
            return obj;
        }

        private static string Text(string path)=>(AssetDatabase.LoadAssetAtPath<TextAsset>(path)??throw new InvalidOperationException("Нет файла "+path)).text;

        private static double Number(string value)=>string.IsNullOrEmpty(value)?0:double.Parse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture);

        // ASCII file name of a material variant (same parts as RoomDecorView.MaterialKey).
        private static string AssetName(RoomDefinition d,IReadOnlyDictionary<string,double> glow)
        {
            double threshold=RoomDecorView.GlowThreshold(d,glow);
            return "decor-"+(d.Blend??"normal")+(d.EraseFirst?"-erase":"")+(threshold<2?"-glow"+threshold.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture):"");
        }

        private static string Sha256(string text)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
        }
    }
}
