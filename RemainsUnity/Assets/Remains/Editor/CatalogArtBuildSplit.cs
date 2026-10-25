using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Remains.Content;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class CatalogArtBuildSplit : IPreprocessBuildWithReport,IPostprocessBuildWithReport
    {
        public const string EditorFolder="Assets/Remains/Editor/VectorArtSources/Art/";
        public const string ResourceFolder="Assets/Remains/Resources/Imported/Art/";
        public const string CoveragePath="Assets/Remains/Resources/Content/VectorCoverage.json";
        [Serializable] private sealed class Coverage {public int version=1;public string manifestSha256;public string[] keys;}
        private static HashSet<string> covered;
        private static string manifestHash;
        public int callbackOrder=>10000;
        public static void ResetCoverage() {covered=null;manifestHash=null;}
        private static void Prepare()
        {
            if(covered!=null) return;
            var package=VectorArtPackage.Load(VectorArtPackaging.Destination);
            using var library=new VectorArtLibrary(package,ExportedContentCatalog.Load(),1);
            covered=new HashSet<string>(library.Keys,StringComparer.Ordinal);
            if(covered.Count==0) throw new InvalidDataException("Пакет не покрывает ни одного ключа; разделение ресурсов остановлено.");
            manifestHash=VectorArtPackage.Sha256(File.ReadAllBytes(VectorArtPackage.ManifestPath(VectorArtPackaging.Destination)));
        }
        public static bool IsCovered(string key) {Prepare();return covered.Contains(key);}
        public static string SpritePath(string key)=>(IsCovered(key)?EditorFolder:ResourceFolder)+Name(key)+".asset";
        public static string ExistingSpritePath(string key)
        {
            // Import bootstrap precedes publishing the new catalog. Locate existing assets without deriving coverage
            // from a stale/absent catalog; EnsureSplit assigns the final location after publication.
            string editor=EditorFolder+Name(key)+".asset",resource=ResourceFolder+Name(key)+".asset";
            if(File.Exists(editor) && File.Exists(resource)) throw new InvalidDataException("Две копии спрайта: "+key);
            return File.Exists(editor)?editor:resource;
        }
        private static string Name(string key)
        {
            const string prefix="Imported/Art/";
            if(key==null || !key.StartsWith(prefix,StringComparison.Ordinal) || !Regex.IsMatch(key.Substring(prefix.Length),"^[a-f0-9]{64}$"))
                throw new InvalidDataException("Некорректный ключ графики: "+key);
            return key.Substring(prefix.Length);
        }
        public static void EnsureSplit()
        {
            Prepare();Directory.CreateDirectory(EditorFolder);AssetDatabase.Refresh();int moved=0;
            foreach(string path in Directory.GetFiles(CatalogSpriteAuthoring.Textures,"*.png"))
            {
                string key="Imported/Art/"+Path.GetFileNameWithoutExtension(path),target=SpritePath(key),existing=ExistingSpritePath(key);
                if(!File.Exists(existing)) throw new InvalidDataException("Нет установленного спрайта: "+key);
                if(target==existing) continue;
                string guid=AssetDatabase.AssetPathToGUID(existing);
                string error=AssetDatabase.MoveAsset(existing,target);
                if(!string.IsNullOrEmpty(error) || AssetDatabase.AssetPathToGUID(target)!=guid)
                    throw new InvalidDataException("Не перемещён спрайт с сохранением GUID: "+key+", "+error);
                moved++;
            }
            var index=new Coverage {manifestSha256=manifestHash,keys=covered.OrderBy(k=>k,StringComparer.Ordinal).ToArray()};
            File.WriteAllText(CoveragePath,JsonUtility.ToJson(index,true));AssetDatabase.ImportAsset(CoveragePath);AssetDatabase.SaveAssets();
            Debug.Log($"Разделение графики: перемещено={moved}, векторных ключей={covered.Count}, manifest SHA={manifestHash}.");
        }
        public static void Validate()
        {
            ResetCoverage();Prepare();
            var index=File.Exists(CoveragePath)?JsonUtility.FromJson<Coverage>(File.ReadAllText(CoveragePath)):null;
            if(index==null || index.version!=1 || index.manifestSha256!=manifestHash || index.keys==null || !covered.SetEquals(index.keys) || index.keys.Length!=covered.Count)
                throw new BuildFailedException("Индекс покрытия не совпадает с проверенным пакетом/каталогом. Выполните CatalogArtBuildSplit.EnsureSplit.");
            int rasters=0,editor=0;
            foreach(string file in Directory.GetFiles(CatalogSpriteAuthoring.Textures,"*.png"))
            {
                string key="Imported/Art/"+Path.GetFileNameWithoutExtension(file),path=SpritePath(key);
                if(AssetDatabase.LoadAssetAtPath<Sprite>(path)==null || GameAssets.Load<Sprite>(key)==null)
                    throw new BuildFailedException("Не разрешён исходный ключ: "+key+", path="+path);
                string wrong=(IsCovered(key)?ResourceFolder:EditorFolder)+Name(key)+".asset";
                if(File.Exists(wrong)) throw new BuildFailedException("Спрайт в неверной группе: "+wrong);
                if(IsCovered(key)) editor++;else rasters++;
            }
            var references=Scan();
            if(references.Count>0) throw new BuildFailedException("Прямые ссылки на покрытую графику: "+string.Join("; ",references.Take(10)));
            Debug.Log($"Разделение графики: PASS — Editor={editor}, Resources={rasters}, прямых ссылок=0, SHA пакета совпадает.");
        }
        private static HashSet<string> CoveredPaths()
        {
            Prepare();var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(string key in covered)
            {result.Add(CatalogSpriteAuthoring.Textures+Name(key)+".png");result.Add(EditorFolder+Name(key)+".asset");result.Add(ResourceFolder+Name(key)+".asset");}
            return result;
        }
        public static List<string> Scan()
        {
            var guids=new HashSet<string>(CoveredPaths().Select(p=>AssetDatabase.AssetPathToGUID(p)).Where(g=>!string.IsNullOrEmpty(g)),StringComparer.Ordinal);
            var extensions=new HashSet<string>(new[]{".prefab",".asset",".unity",".mat",".controller",".anim"});var result=new List<string>();
            foreach(string raw in Directory.GetFiles("Assets","*",SearchOption.AllDirectories))
            {
                string path=raw.Replace('\\','/');
                if(path.Contains("/Editor/") || !extensions.Contains(Path.GetExtension(path))) continue;
                foreach(Match match in Regex.Matches(File.ReadAllText(path),@"guid: ([a-f0-9]{32})"))
                    if(guids.Contains(match.Groups[1].Value)) result.Add(path+" → "+match.Groups[1].Value);
            }
            string output=Path.GetFullPath("../.reference/vector-stage3");Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output,"reference-scan.txt"),result.Count==0?new[]{"PASS: 0 references (Editor sources excluded)"}:result);
            return result;
        }
        public void OnPreprocessBuild(BuildReport report) {Validate();CatalogWeaponMaterialChecks.Run();}
        public void OnPostprocessBuild(BuildReport report)
        {
            var excluded=CoveredPaths();var found=new List<string>();var details=new List<string>();var shipped=new HashSet<string>(StringComparer.Ordinal);
            ulong rasterBytes=0,bc7Bytes=0,rgbaSaving=0;var formats=new Dictionary<string,int>();var expectedRasterPaths=new HashSet<string>(StringComparer.Ordinal);
            foreach(var pack in report.packedAssets)
                foreach(var asset in pack.contents)
                {
                    shipped.Add(asset.sourceAssetPath);
                    if(excluded.Contains(asset.sourceAssetPath)) found.Add(asset.sourceAssetPath);
                    if(asset.sourceAssetPath.StartsWith(CatalogSpriteAuthoring.Textures,StringComparison.Ordinal))
                    {
                        details.Add(asset.sourceAssetPath+"\t"+asset.packedSize);rasterBytes+=asset.packedSize;
                    }
                }
            foreach(string file in Directory.GetFiles(ResourceFolder,"*.asset"))
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(file);var texture=sprite.texture;
                expectedRasterPaths.Add(file.Replace('\\','/'));expectedRasterPaths.Add(AssetDatabase.GetAssetPath(texture));
                // BC7 block-size estimate only; no importer mutation. No mipmaps are used by these assets.
                ulong blocks=(ulong)((texture.width+3)/4)*((ulong)(texture.height+3)/4)*16;bc7Bytes+=blocks;
                string format=texture.format.ToString();formats.TryGetValue(format,out int count);formats[format]=count+1;
                if(texture.format==TextureFormat.RGBA32 || texture.format==TextureFormat.ARGB32 || texture.format==TextureFormat.BGRA32)
                {ulong bytes=(ulong)texture.width*(ulong)texture.height*4;if(bytes>blocks) rgbaSaving+=bytes-blocks;}
            }
            string output=Path.GetFullPath("../.reference/vector-stage3");Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output,"build-raster-assets.tsv"),details);
            var data=Path.Combine(Path.GetDirectoryName(report.summary.outputPath),"Remains_Data");
            long total=Directory.GetFiles(data,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
            var missing=expectedRasterPaths.Where(p=>!shipped.Contains(p)).ToArray();
            File.WriteAllText(Path.Combine(output,"build-size.txt"),$"Remains_Data={total}\nresources.assets={new FileInfo(Path.Combine(data,"resources.assets")).Length}\nresources.assets.resS={new FileInfo(Path.Combine(data,"resources.assets.resS")).Length}\ncoveredAssetsInBuild={found.Count}\nmissingRasterAssetsInBuild={missing.Length}\nremainingRasterPackedBytes={rasterBytes}\nremainingRasterBC7BlockEstimate={bc7Bytes}\nRGBA32ToBC7PotentialSaving={rgbaSaving}\nformats={string.Join(", ",formats.Select(p=>p.Key+":"+p.Value))}\nBC7 estimate excludes headers/alignment and assumes current sizes, no mipmaps.\n");
            if(details.Count==0) throw new BuildFailedException("BuildReport не содержит данных о растровых ассетах: отсутствие покрытых PNG не доказано.");
            if(found.Count>0) throw new BuildFailedException("Покрытые PNG/спрайты попали в сборку: "+string.Join(", ",found.Take(10)));
            if(missing.Length>0) throw new BuildFailedException("Не все растровые PNG/спрайты попали в сборку: "+string.Join(", ",missing.Take(10)));
            Debug.Log($"Содержимое сборки: PASS — покрытых PNG/спрайтов=0, растровых записей={details.Count}, Remains_Data={total} байт.");
        }
        public static void MigrateBatch()
        {
            try {FirearmAuthoring.RegenerateSpriteMaterials();EnsureSplit();Validate();CatalogWeaponMaterialChecks.Run();
                VectorArtLibraryChecks.Run(40);CatalogSpriteMigration.Check();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void BuildBatch()
        {
            try
            {
                Validate();CheckManifestGuard();ContractChecks.Run();
                var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),Path.GetFullPath("Builds/VectorStage3/Remains.exe"),
                    BuildTarget.StandaloneWindows64,BuildOptions.Development|BuildOptions.DetailedBuildReport);
                if(report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Сборка этапа 3: "+report.summary.result);
                Debug.Log("Сборка этапа 3: PASS.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void CheckManifestGuard()
        {
            byte[] original=File.ReadAllBytes(CoveragePath);bool rejected=false;
            try
            {
                var bad=JsonUtility.FromJson<Coverage>(File.ReadAllText(CoveragePath));bad.manifestSha256=new string('0',64);
                File.WriteAllText(CoveragePath,JsonUtility.ToJson(bad));
                try {Validate();}catch(BuildFailedException error) {if(!error.Message.StartsWith("Индекс покрытия")) throw;rejected=true;}
            }
            finally {File.WriteAllBytes(CoveragePath,original);ResetCoverage();}
            if(!rejected) throw new BuildFailedException("Повреждённый SHA индекса не остановил сборку.");
            Debug.Log("Защита сборки: PASS — несовпадающий SHA индекса отклонён; исходные байты восстановлены.");
        }
    }
}
