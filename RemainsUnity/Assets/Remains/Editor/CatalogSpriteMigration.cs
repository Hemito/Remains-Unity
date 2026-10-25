using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class CatalogSpriteMigration
    {
        private const string Weapons="Assets/Remains/Prefabs/Weapons/Original";
        private const string Shots="Assets/Remains/Resources/Presentation/Shots/Original";
        private const string Casings="Assets/Remains/Prefabs/Weapons/ShotEffects/Original";
        private static readonly string[] Groups={DroppedItemShapeAuthoring.Path,Weapons,Shots,Casings};
        public static void RunBatch()
        {
            try {Migrate();Check();FirearmChecks.Run();WeaponShotChecks.Run();WeaponReloadChecks.Run();DroppedItemShapeChecks.Run();
                VectorArtLibraryChecks.Run(40);Debug.Log("Ключи графики — этап 2: PASS (полные контракты запускаются при сборке).");EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void CheckBatch()
        {try {Check();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void BuildBatch()
        {try {Check();PrototypeBuild.BuildTo("Builds/VectorStage2/Remains.exe");EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        // Only for iterations on the opt-in acceptance driver after the complete BuildBatch passed.
        public static void RebuildAcceptanceBatch()
        {
            try
            {
                var result=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                    Path.GetFullPath("Builds/VectorStage2/Remains.exe"),BuildTarget.StandaloneWindows64,BuildOptions.Development);
                if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException("Не собрана диагностическая итерация: "+result.summary.result);
                Debug.Log("Сборка диагностической итерации: PASS; полные контракты — предыдущий BuildBatch.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Migrate()
        {
            if(File.Exists("Temp/UnityLockfile") && !Application.isBatchMode)
                throw new InvalidOperationException("Миграция графики разрешена только отдельным batch-процессом при закрытом пользовательском Editor.");
            var catalog=AssetDatabase.LoadAssetAtPath<DroppedItemShapeCatalog>(DroppedItemShapeAuthoring.Path);
            var shapes=catalog.CreateLookup().Values.ToArray();
            // Preflight before any serialized write: the migration must not silently repair physics.
            var measured=new Dictionary<Sprite,Rect>();
            foreach(var shape in shapes)
            {
                var sprite=shape.sprite;
                if(!measured.TryGetValue(sprite,out var actual)) measured.Add(sprite,actual=MeasureSource(sprite));
                if(!Near(actual,shape.opaqueBounds,2/sprite.pixelsPerUnit+.00001f))
                    throw new InvalidOperationException($"Форма {shape.id} расходится с текущей регистрацией: authored={shape.opaqueBounds.ToString("F6")}, measured={actual.ToString("F6")}; миграция остановлена.");
            }
            int dropped=0,weapons=0,shots=0,casings=0;
            foreach(var shape in shapes)
                if(string.IsNullOrEmpty(shape.Reference.Key) && CatalogSpriteAuthoring.Key(shape.sprite)!=null)
                {shape.ConfigureReference(CatalogSpriteAuthoring.Reference(shape.sprite));dropped++;}
            if(dropped>0) EditorUtility.SetDirty(catalog);
            foreach(string path in Paths(Weapons,"*.prefab"))
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view=root.GetComponent<WeaponView>();
                    if(view==null || !string.IsNullOrEmpty(view.RestReference?.Key) || view.Artwork.sprite==null || CatalogSpriteAuthoring.Key(view.Artwork.sprite)==null) continue;
                    string rig=JsonUtility.ToJson(view.ReadRig());
                    view.ConfigureRest(CatalogSpriteAuthoring.Reference(view.Artwork.sprite));
                    if(JsonUtility.ToJson(view.ReadRig())!=rig) throw new InvalidOperationException("Миграция изменила позу оружия: "+path);
                    PrefabUtility.SaveAsPrefabAsset(root,path);weapons++;
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            foreach(string path in Paths(Shots,"*.asset"))
            {
                var shot=AssetDatabase.LoadAssetAtPath<ShotArtwork>(path);if(shot==null) continue;
                if(!string.IsNullOrEmpty(shot.TraceReference?.Key)) continue;
                var trace=CatalogSpriteAuthoring.Reference(shot.trace);
                var impacts=shot.impact.Select(CatalogSpriteAuthoring.Reference).ToArray();
                shots+=(string.IsNullOrEmpty(trace.Key)?0:1)+impacts.Count(r=>!string.IsNullOrEmpty(r.Key));
                shot.ConfigureReferences(trace,impacts);EditorUtility.SetDirty(shot);
            }
            foreach(string path in Paths(Casings,"*.asset"))
            {
                var casing=AssetDatabase.LoadAssetAtPath<CasingEffectProfile>(path);
                if(casing==null || !string.IsNullOrEmpty(casing.Reference?.Key) || CatalogSpriteAuthoring.Key(casing.Sprite)==null) continue;
                casing.ConfigureReference(CatalogSpriteAuthoring.Reference(casing.Sprite));EditorUtility.SetDirty(casing);casings++;
            }
            AssetDatabase.SaveAssets();
            // Centered trace Sprite assets were only adapters over a catalog texture. All consumers now store that geometry.
            foreach(string path in Paths(Shots,"*-trace.asset"))
            {
                if(AssetDatabase.GetMainAssetTypeAtPath(path)!=typeof(Sprite)) continue;
                string guid=AssetDatabase.AssetPathToGUID(path);
                if(Paths(Shots,"*.asset").Any(p=>p!=path && File.ReadAllText(p).Contains(guid)))
                    throw new InvalidOperationException("Осталась ссылка на прежний след: "+path);
                AssetDatabase.DeleteAsset(path);
            }
            DroppedItemShapeCatalog.Reset();
            Debug.Log($"Миграция ключей графики: формы={dropped}, оружие={weapons}, следы/попадания={shots}, гильзы={casings}; проверено форм={shapes.Length}, уникальных alpha={measured.Count}.");
        }
        public static void Check()
        {
            var artGuids=new HashSet<string>(Paths(CatalogSpriteAuthoring.Textures,"*.meta").Concat(Paths(CatalogSpriteAuthoring.Lazy,"*.meta"))
                .Select(path=>Regex.Match(File.ReadAllText(path),@"(?m)^guid: ([0-9a-f]+)").Groups[1].Value),StringComparer.Ordinal);
            int direct=0;
            foreach(string group in Groups)
                foreach(string path in File.Exists(group)?new[]{group}:Paths(group,"*.*").Where(p=>p.EndsWith(".asset") || p.EndsWith(".prefab")))
                    foreach(Match match in Regex.Matches(File.ReadAllText(path),@"guid: ([0-9a-f]{32})"))
                        if(artGuids.Contains(match.Groups[1].Value)) {Debug.LogError("Прямая ссылка на графику каталога: "+path+", GUID="+match.Groups[1].Value);direct++;}
            if(direct!=0) throw new InvalidOperationException("Остались прямые ссылки на графику: "+direct);
            var references=new List<CatalogSpriteReference>();
            foreach(var shape in AssetDatabase.LoadAssetAtPath<DroppedItemShapeCatalog>(DroppedItemShapeAuthoring.Path).CreateLookup().Values) references.Add(shape.Reference);
            foreach(string path in Paths(Weapons,"*.prefab")) references.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<WeaponView>().RestReference);
            foreach(string path in Paths(Shots,"*.asset"))
            {var shot=AssetDatabase.LoadAssetAtPath<ShotArtwork>(path);if(shot!=null){references.Add(shot.TraceReference);references.AddRange(shot.ImpactReferences);}}
            foreach(string path in Paths(Casings,"*.asset"))
            {var casing=AssetDatabase.LoadAssetAtPath<CasingEffectProfile>(path);if(casing!=null) references.Add(casing.Reference);}
            var report=new List<string>();int keyed=0;
            foreach(var reference in references)
            {
                reference.Validate("проверка миграции");
                if(string.IsNullOrEmpty(reference.Key)) continue;keyed++;
                var sprite=reference.Resolve();
                if(sprite==null) throw new InvalidOperationException("Не разрешён ключ: "+reference.Key);
                if(!Near(sprite.rect,reference.Rect,.001f) || Vector2.Distance(sprite.pivot,reference.Pivot)>.001f || Mathf.Abs(sprite.pixelsPerUnit-reference.PixelsPerUnit)>.001f)
                    throw new InvalidOperationException("Изменена геометрия PNG-потребителя: "+reference.Key+", rect="+sprite.rect+", source="+reference.Rect);
                string reason=CatalogSpriteAuthoring.RasterReason(reference.Key);
                if((reason??"")!=(reference.RasterReason??"")) throw new InvalidOperationException("Устарела причина растра: "+reference.Key);
                report.Add(reference.Key+"\t"+(reason??"vector"));
            }
            string output=Path.GetFullPath("../.reference/vector-stage2");Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output,"references.tsv"),report.Distinct().OrderBy(x=>x,StringComparer.Ordinal));
            Debug.Log($"Ключи графики: прямых ссылок={direct}, ссылок по ключам={keyed}, уникальных={report.Distinct().Count()}, растровых={report.Distinct().Count(r=>!r.EndsWith("\tvector",StringComparison.Ordinal))}; PASS.");
        }
        private static Rect MeasureSource(Sprite sprite)
        {
            var image=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                string path=AssetDatabase.GetAssetPath(sprite.texture);
                if(!image.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Не прочитана alpha: "+path);
                // Imported textures may be resized by the sizing policy. Compare source alpha in the sprite's
                // actual coordinate system, with two imported texels of tolerance for filtering/rounding.
                float sx=(float)sprite.texture.width/image.width,sy=(float)sprite.texture.height/image.height;
                var r=sprite.rect;
                var source=SpriteOpaqueBoundsBaker.Measure(image.GetPixels32(),image.width,
                    new Rect(r.x/sx,r.y/sy,Mathf.Min(image.width-r.x/sx,r.width/sx),Mathf.Min(image.height-r.y/sy,r.height/sy)),Vector2.zero,1);
                return Rect.MinMaxRect((source.xMin*sx-sprite.pivot.x)/sprite.pixelsPerUnit,
                    (source.yMin*sy-sprite.pivot.y)/sprite.pixelsPerUnit,(source.xMax*sx-sprite.pivot.x)/sprite.pixelsPerUnit,
                    (source.yMax*sy-sprite.pivot.y)/sprite.pixelsPerUnit);
            }
            finally {UnityEngine.Object.DestroyImmediate(image);}
        }
        private static bool Near(Rect a,Rect b,float tolerance)=>Mathf.Abs(a.x-b.x)<tolerance && Mathf.Abs(a.y-b.y)<tolerance && Mathf.Abs(a.width-b.width)<tolerance && Mathf.Abs(a.height-b.height)<tolerance;
        private static IEnumerable<string> Paths(string folder,string pattern)=>Directory.GetFiles(folder,pattern,SearchOption.AllDirectories).Select(p=>p.Replace('\\','/'));
    }
}
