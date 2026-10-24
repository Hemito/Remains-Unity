using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    /// <summary>Explicit, hash-validated installation of the preserved original content package.</summary>
    public static class ExportedContentAuthoring
    {
        public const string CatalogPath = "Assets/Remains/Resources/Content/ExportedContent.asset";
        private const string OutputRoot = "Assets/Remains/ImportedContent";
        private sealed class PreparedFile
        {
            public JObject metadata;
            public string source, destination, signature;
            public bool image;
        }

        [MenuItem("Remains/Импорт/Оружие, предметы и боеприпасы")]
        public static void Install() => InstallPackage();

        public static void InstallBatch()
        {
            try { InstallPackage(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogError($"Импорт исходного контента не выполнен: {exception}"); EditorApplication.Exit(1); }
        }

        public static ExportedContentCatalog InstallPackage()
        {
            string package = ContentPackage.Directory;
            string catalogFile = Path.Combine(package, "catalog.json");
            string catalogHash = Hash(catalogFile);
            var source = JObject.Parse(File.ReadAllText(catalogFile));
            if ((int?)source["version"] != 2) throw new InvalidDataException("Неподдержанная версия каталога исходного контента.");
            var existing = AssetDatabase.LoadAssetAtPath<ExportedContentCatalog>(CatalogPath);
            // A newer package may extend an installed one it declares (ContentPackage.Replaces); every file already
            // imported is still checked against its SHA256 below (Prepare), so nothing installed changes silently.
            if (existing != null && existing.sourceHash != catalogHash && !ContentPackage.CanReplace(existing.sourceHash))
                throw new InvalidDataException($"Каталог {CatalogPath} принадлежит другому исходному пакету; автоматическая перезапись запрещена.");
            // Creatures of the factory (CreatureSources): their definitions, animation sheets and sounds.
            var unitIds = CreatureSources.FactoryUnitIds(source);
            var definitions = Rows(source["definitions"]).Where(row => new[] { "weapon", "weapon1", "item" }.Contains((string)row["category"]) ||
                (string)row["category"] == "unit" && unitIds.Contains((string)row["id"])).ToArray();
            var skippedAppearances = new List<string>();
            foreach (var row in definitions.Where(IsUnit))
            {
                var skipped = Rows(row["graphics"]).Where(binding => !CreatureSources.Imported(binding, row)).ToArray();
                foreach (var binding in skipped) skippedAppearances.Add($"{row["id"]}: {binding["role"]} {binding["resource"]}");
                // Only imported bindings stay on the entry; the skipped ones are recorded as its issues.
                row["issues"] = new JArray(Strings(row["issues"]).Concat(skipped.Select(binding =>
                    $"Векторный вид {binding["resource"]} ({binding["role"]}) не импортирован кадрами: существо рисуется листом build_creature_sheets.py.")));
                row["graphics"] = new JArray(Rows(row["graphics"]).Where(binding => CreatureSources.Imported(binding, row)));
            }
            var resourceMap = Rows(source["resources"]).ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions) CollectBindings(definition, selected);
            foreach (var resource in resourceMap.Values)
                if ((string)resource["kind"] == "sound" && Strings(resource["names"]).Any(name => CreatureSources.Sounds.Contains(name))) selected.Add((string)resource["id"]);
            foreach (var weapon in Rows(source["weapons"]))
                foreach (var grade in Rows(weapon["grades"])) CollectBindings(grade, selected);
            var pending = new Queue<string>(selected);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!resourceMap.TryGetValue(id, out var resource)) throw new InvalidDataException($"Привязка ссылается на отсутствующий ресурс {id}.");
                foreach (var dependency in resource["dependencies"] as JArray ?? new JArray())
                {
                    string key = (string)resource["archive"] + ":" + dependency.ToString();
                    if (resourceMap.ContainsKey(key) && selected.Add(key)) pending.Enqueue(key);
                }
            }
            var issues = new List<string>();
            var prepared = new Dictionary<string, PreparedFile>(StringComparer.Ordinal);
            var framesByResource = new Dictionary<string, PreparedFile[]>(StringComparer.Ordinal);
            var soundsByResource = new Dictionary<string, PreparedFile[]>(StringComparer.Ordinal);
            // Everything, including pre-existing destinations, is checked before the first mutation.
            foreach (string id in selected.OrderBy(value => value, StringComparer.Ordinal))
            {
                var resource = resourceMap[id];
                foreach (var file in Rows(resource["files"])) ValidateSource(package, file);
                var images = Rows(resource["files"]).Where(file => Extension(file) == ".png")
                    .GroupBy(file => (int?)file["frame"] ?? 1)
                    .Select(group => group.OrderByDescending(file => (float?)file["zoom"] ?? 1).First()).ToArray();
                framesByResource[id] = images.Select(file => Prepare(package, id, file, true, prepared, issues)).Where(file => file != null).ToArray();
                soundsByResource[id] = Rows(resource["files"]).Where(file => new[] { ".wav", ".mp3", ".ogg" }.Contains(Extension(file)))
                    .Select(file => Prepare(package, id, file, false, prepared, issues)).ToArray();
            }
            foreach (var row in definitions) XElement.Parse(Required(row, "rawXml"));
            foreach (var file in prepared.Values.Where(file => file.image)) ValidateLazySprite(file);
            Directory.CreateDirectory(OutputRoot);
            foreach (var file in prepared.Values)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.destination));
                if (File.Exists(file.destination)) continue;
                // Art sides not multiples of 4 would stay uncompressed (BC7 blocks): install a padded copy.
                byte[] bytes = File.ReadAllBytes(file.source);
                File.WriteAllBytes(file.destination, file.image ? ImportedArtPadding.Pad(bytes, out _, out _) ?? bytes : bytes);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var file in prepared.Values) ConfigureImporter(file);
            foreach (var file in prepared.Values.Where(file => file.image)) CreateLazySprite(file);
            var importedResources = selected.OrderBy(value => value, StringComparer.Ordinal).Select(id => new ExportedResourceEntry
            {
                id = id, kind = (string)resourceMap[id]["kind"], issues = Strings(resourceMap[id]["issues"]),
                socketsJson = resourceMap[id]["weaponSockets"]?.ToString(Formatting.None),
                labelsJson = resourceMap[id]["labels"]?.ToString(Formatting.None),
                frames = framesByResource[id].Select(file => new ExportedFrame
                {
                    frame = (int?)file.metadata["frame"] ?? 1, zoom = (float?)file.metadata["zoom"] ?? 1,
                    spriteResource = SpriteResource(file),
                    sourceOrigin = new Vector2((float)file.metadata["originX"], (float)file.metadata["originY"]),
                    sourceHash = Required(file.metadata, "sha256")
                }).ToArray(),
                sounds = soundsByResource[id].Select(file => AssetDatabase.LoadAssetAtPath<AudioClip>(file.destination)).ToArray()
            }).ToArray();
            foreach (var resource in importedResources)
                if (resource.frames.Any(frame => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/" + frame.spriteResource + ".asset") == null) || resource.sounds.Any(sound => sound == null))
                    throw new InvalidDataException($"Unity не импортировал обязательный ресурс {resource.id}.");
            var importedItems = definitions.Select(row => new ExportedItemEntry
            {
                key = Required(row, "category") + ":" + Required(row, "id"), category = Required(row, "category"),
                sourceId = Required(row, "id"), rawXml = Required(row, "rawXml"),
                displayName = XElement.Parse(Required(row, "rawXml")).Element("n")?.Value ?? "",
                description = XElement.Parse(Required(row, "rawXml")).Element("d")?.Value ?? "",
                graphics = Bindings(row["graphics"]), sounds = Bindings(row["sounds"]), issues = Strings(row["issues"])
            }).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            var catalog = existing != null ? existing : ScriptableObject.CreateInstance<ExportedContentCatalog>();
            catalog.Configure(catalogHash, importedItems, importedResources, source["weapons"].ToString(Formatting.None), source["weaponAttachments"].ToString(Formatting.None));
            if (existing == null) AssetDatabase.CreateAsset(catalog, CatalogPath);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            foreach (var issue in issues) Debug.LogWarning(issue);
            if (skippedAppearances.Count > 0)
                Debug.Log($"Существа завода: векторные виды не импортируются как кадры — генератор берёт их листы (Tools/build_creature_sheets.py): {string.Join("; ", skippedAppearances)}.");
            Debug.Log($"Исходный контент установлен: {importedItems.Length} определений, {importedResources.Length} ресурсов, {prepared.Count} файлов, {issues.Count} пропусков с диагностикой; SHA256={catalogHash}.");
            return catalog;
        }

        private static PreparedFile Prepare(string package, string id, JObject metadata, bool image,
            Dictionary<string, PreparedFile> prepared, List<string> issues)
        {
            if (image && (metadata["originX"]?.Type != JTokenType.Float && metadata["originX"]?.Type != JTokenType.Integer ||
                          metadata["originY"]?.Type != JTokenType.Float && metadata["originY"]?.Type != JTokenType.Integer))
            {
                issues.Add($"Ресурс {id}, кадр {metadata["frame"]}, файл {metadata["path"]}: регистрационная точка неизвестна; спрайт пропущен.");
                return null;
            }
            float zoom = (float?)metadata["zoom"] ?? 1;
            int width = (int?)metadata["width"] ?? 0, height = (int?)metadata["height"] ?? 0;
            if (image && (zoom <= 0 || width <= 0 || height <= 0 || width > 16384 || height > 16384))
                throw new InvalidDataException($"Ресурс {id}: размеры {width}×{height} при масштабе {zoom} не поддерживаются без уменьшения.");
            string signature = Required(metadata, "sha256") + (image ? "|" + metadata["originX"] + "|" + metadata["originY"] + "|" + zoom.ToString("R", CultureInfo.InvariantCulture) : "");
            string key = Hash(Encoding.UTF8.GetBytes(signature));
            string destination = OutputRoot + (image ? "/Art/" : "/Audio/") + key + Extension(metadata);
            if (prepared.TryGetValue(signature, out var shared))
                return new PreparedFile { metadata = metadata, source = shared.source, destination = shared.destination, signature = signature, image = image };
            string input = Resolve(package, Required(metadata, "path"));
            var importer = AssetImporter.GetAtPath(destination);
            // A padded copy (ImportedArtPadding) is checked against its recorded hash and re-derived from the source.
            var (installedSignature, paddedHash) = ImportedArtPadding.Parse(importer?.userData);
            if (File.Exists(destination) && Hash(destination) != (paddedHash ?? Required(metadata, "sha256")))
                throw new InvalidDataException($"Существующий импортированный файл изменён: {destination}. Перезапись запрещена.");
            if (paddedHash != null && ImportedArtPadding.Hash(ImportedArtPadding.Pad(File.ReadAllBytes(input), out _, out _) ?? Array.Empty<byte>()) != paddedHash)
                throw new InvalidDataException($"Дополненная копия {destination} не выводится из исходного файла {input}.");
            if (importer != null && !string.IsNullOrEmpty(importer.userData) && installedSignature != signature)
                throw new InvalidDataException($"Параметры происхождения не совпадают: {destination}.");
            var result = new PreparedFile { metadata = metadata, source = input, destination = destination, signature = signature, image = image };
            prepared.Add(signature, result);
            return result;
        }

        private static void ConfigureImporter(PreparedFile file)
        {
            string padded = null;
            var importer = AssetImporter.GetAtPath(file.destination);
            if (importer == null) throw new InvalidDataException($"Импортёр Unity отсутствует: {file.destination}.");
            if (file.image)
            {
                var texture = importer as TextureImporter;
                if (texture == null) throw new InvalidDataException($"Ожидался TextureImporter: {file.destination}.");
                float zoom = (float?)file.metadata["zoom"] ?? 1;
                texture.textureType = TextureImporterType.Sprite;
                texture.spriteImportMode = SpriteImportMode.Single;
                texture.spritePixelsPerUnit = 50 * zoom;
                texture.GetSourceTextureWidthAndHeight(out int textureWidth, out int textureHeight);
                texture.spritePivot = ImportedArtPadding.Pivot((float)file.metadata["originX"], (float)file.metadata["originY"], zoom, textureWidth, textureHeight);
                if (textureWidth != (int)file.metadata["width"] || textureHeight != (int)file.metadata["height"])
                    padded = ImportedArtPadding.UserData(file.signature, (int)file.metadata["width"], (int)file.metadata["height"], Hash(file.destination));
                var settings = new TextureImporterSettings();
                texture.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                texture.SetTextureSettings(settings);
                texture.alphaIsTransparency = true;
                texture.mipmapEnabled = false;
                texture.npotScale = TextureImporterNPOTScale.None;
                texture.maxTextureSize = 16384;
                texture.textureCompression = TextureImporterCompression.Uncompressed;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
            }
            importer.userData = padded ?? "ExportedContent:" + file.signature;
            importer.SaveAndReimport();
        }

        private static string SpriteResource(PreparedFile file) => "Imported/Art/" + Path.GetFileNameWithoutExtension(file.destination);
        private static void CreateLazySprite(PreparedFile file)
        {
            string path = "Assets/Remains/Resources/" + SpriteResource(file) + ".asset";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file.destination);
            if (sprite == null) throw new InvalidDataException($"Спрайт не импортирован: {file.destination}.");
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                if (existing.texture != sprite.texture || existing.pivot != sprite.pivot || !Mathf.Approximately(existing.pixelsPerUnit, sprite.pixelsPerUnit))
                    throw new InvalidDataException($"Геометрия ранее установленного спрайта изменилась: {path}.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var copy = UnityEngine.Object.Instantiate(sprite);
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
        }

        private static void ValidateLazySprite(PreparedFile file)
        {
            string path = "Assets/Remains/Resources/" + SpriteResource(file) + ".asset";
            if (!File.Exists(path)) return;
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var source = AssetDatabase.LoadAssetAtPath<Sprite>(file.destination);
            if (existing == null || source == null || existing.texture != source.texture || existing.pivot != source.pivot ||
                !Mathf.Approximately(existing.pixelsPerUnit, source.pixelsPerUnit))
                throw new InvalidDataException($"Существующий спрайт не соответствует исходному ресурсу: {path}.");
        }

        private static void ValidateSource(string package, JObject file)
        {
            string path = Resolve(package, Required(file, "path"));
            if (!File.Exists(path) || Hash(path) != Required(file, "sha256"))
                throw new InvalidDataException($"Файл пакета отсутствует или SHA256 изменён: {path}.");
        }
        private static string Resolve(string package, string relative)
        {
            string path = Path.GetFullPath(Path.Combine(package, relative));
            if (!path.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Путь выходит за пределы пакета: {relative}.");
            return path;
        }
        private static bool IsUnit(JObject row) => (string)row["category"] == "unit";
        private static void CollectBindings(JObject row, HashSet<string> resources)
        {
            foreach (var binding in Rows(row["graphics"]).Concat(Rows(row["sounds"])))
                if (!string.IsNullOrEmpty((string)binding["resource"])) resources.Add((string)binding["resource"]);
        }
        private static ExportedBinding[] Bindings(JToken value) => Rows(value).Select(row => new ExportedBinding
        {
            role = (string)row["role"], resourceId = (string)row["resource"], rule = (string)row["rule"],
            frame = (int?)row["frame"] ?? 0, label = (string)row["label"], metadataXml = (string)row["metadataXml"],
            sheetJson = row["sheet"]?.Type == JTokenType.Null ? null : row["sheet"]?.ToString(Formatting.None)
        }).ToArray();
        private static IEnumerable<JObject> Rows(JToken value) => (value as JArray ?? new JArray()).OfType<JObject>();
        private static string[] Strings(JToken value) => (value as JArray ?? new JArray()).Select(token => token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None)).ToArray();
        private static string Required(JObject row, string field) => (string)row[field] ?? throw new InvalidDataException($"Отсутствует поле {field}.");
        private static string Extension(JObject row) => Path.GetExtension(Required(row, "path")).ToLowerInvariant();
        private static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
