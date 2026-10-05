using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable] public sealed class ToolStandIconResource
    {public string resource,sha256;public int[] size;}
    [Serializable] public sealed class ToolStandIconEntry
    {public string id,resource;}
    [Serializable] public sealed class ToolStandIconManifest
    {public int schema;public float pixelsPerUnit;public ToolStandIconResource[] resources;public ToolStandIconEntry[] items;}

    // Explicit one-time asset command; never runs during Update or automatically on import.
    public static class ToolStandIconAuthoring
    {
        public const string ManifestResource="Content/tool-stand-icons";
        public const string CatalogPath="Assets/Remains/Resources/Original/Inventory/icons.asset";
        public static ToolStandIconManifest ReadManifest()
        {
            var asset=Resources.Load<TextAsset>(ManifestResource);
            if(asset==null) throw new InvalidOperationException("Нет декларации иконок стенда: "+ManifestResource);
            var manifest=StateJson.FromJson<ToolStandIconManifest>(asset.text);
            if(manifest==null || manifest.schema!=1 || !float.IsFinite(manifest.pixelsPerUnit) || manifest.pixelsPerUnit<=0 ||
                manifest.resources==null || manifest.items==null || manifest.resources.Length==0 || manifest.items.Length==0)
                throw new InvalidOperationException("Некорректная декларация иконок стенда: "+ManifestResource);
            var resources=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in manifest.resources)
                if(entry==null || string.IsNullOrWhiteSpace(entry.resource) || !resources.Add(entry.resource) ||
                    entry.size==null || entry.size.Length!=2 || entry.size[0]<=0 || entry.size[1]<=0 ||
                    entry.sha256==null || entry.sha256.Length!=64 || entry.sha256.Any(c=>!Uri.IsHexDigit(c)))
                    throw new InvalidOperationException("Некорректный ресурс иконки стенда: "+entry?.resource);
            var items=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in manifest.items)
                if(entry==null || string.IsNullOrWhiteSpace(entry.id) || !items.Add(entry.id) || !resources.Contains(entry.resource))
                    throw new InvalidOperationException("Некорректная явная связь иконки стенда: id="+entry?.id);
            return manifest;
        }
        private static string SpriteName(string resource)=>"tool-stand-"+Path.GetFileName(resource);
        private static string AssetPath(string resource)=>"Assets/Remains/Resources/"+resource+".png";
        private static void CheckTexture(ToolStandIconResource entry,Texture2D texture)
        {
            string path=AssetPath(entry.resource);
            if(texture==null || texture.width!=entry.size[0] || texture.height!=entry.size[1])
                throw new InvalidOperationException($"Отсутствует или изменён размер исходной иконки: {path}, expected={entry.size[0]}×{entry.size[1]}, actual={texture?.width}×{texture?.height}.");
            string absolute=Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
            using var sha=SHA256.Create();
            string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(absolute))).Replace("-","").ToLowerInvariant();
            if(hash!=entry.sha256) throw new InvalidOperationException("Изменена исходная PNG иконки стенда: "+path);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null || importer.textureType!=TextureImporterType.Default || importer.textureShape!=TextureImporterShape.Texture2D ||
                importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.npotScale!=TextureImporterNPOTScale.None ||
                importer.mipmapEnabled || importer.alphaIsTransparency || importer.wrapMode!=TextureWrapMode.Clamp)
                throw new InvalidOperationException("Изменена явная политика 2D/RGBA импорта иконки: "+path);
        }
        [MenuItem("Remains/Контент/Добавить иконки стенда инструментов")]
        public static void Run()
        {
            var manifest=ReadManifest();var text=ItemTextCatalog.Load();
            var catalog=AssetDatabase.LoadAssetAtPath<InventoryIconAsset>(CatalogPath);
            if(catalog==null) throw new InvalidOperationException("Нет прежнего каталога иконок: "+CatalogPath);
            var saved=catalog.CreateLookup();
            var sprites=AssetDatabase.LoadAllAssetsAtPath(CatalogPath).OfType<Sprite>().ToDictionary(s=>s.name,StringComparer.Ordinal);
            var prepared=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
            // Validate every declared file before changing the catalog.
            foreach(var entry in manifest.resources)
            {
                string path=AssetPath(entry.resource);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null) throw new InvalidOperationException("Нет импортера иконки стенда: "+path);
                importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;
                importer.wrapMode=TextureWrapMode.Clamp;importer.alphaIsTransparency=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.sRGBTexture=true;
                importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=2048;importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);CheckTexture(entry,texture);prepared.Add(entry.resource,texture);
                if(sprites.TryGetValue(SpriteName(entry.resource),out var existing) &&
                    (existing.texture!=texture || existing.rect!=new Rect(0,0,texture.width,texture.height) || existing.pixelsPerUnit!=manifest.pixelsPerUnit))
                    throw new InvalidOperationException("Уже зарегистрированный спрайт стенда отличается от декларации: "+existing.name);
            }
            foreach(var entry in manifest.items)
            {
                if(!text.Contains(entry.id)) throw new InvalidOperationException("Нет русских текстов предмета перед регистрацией иконки: "+entry.id);
                if(saved.TryGetValue(entry.id,out var existing) &&
                    (!sprites.TryGetValue(SpriteName(entry.resource),out var expected) || existing!=expected))
                    throw new InvalidOperationException("Предмет уже использует другую иконку; автоматическая замена запрещена: "+entry.id);
            }
            foreach(var entry in manifest.resources)
            {
                string name=SpriteName(entry.resource);if(sprites.ContainsKey(name)) continue;
                var texture=prepared[entry.resource];
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),manifest.pixelsPerUnit,0,SpriteMeshType.FullRect);
                sprite.name=name;AssetDatabase.AddObjectToAsset(sprite,catalog);sprites.Add(name,sprite);
            }
            var serialized=new SerializedObject(catalog);var entries=serialized.FindProperty("entries");int added=0;
            if(entries==null || !entries.isArray) throw new InvalidOperationException("Не найдены записи прежнего каталога иконок.");
            foreach(var entry in manifest.items)
            {
                if(saved.ContainsKey(entry.id)) continue;
                int index=entries.arraySize;entries.InsertArrayElementAtIndex(index);var row=entries.GetArrayElementAtIndex(index);
                row.FindPropertyRelative("Id").stringValue=entry.id;
                row.FindPropertyRelative("Sprite").objectReferenceValue=sprites[SpriteName(entry.resource)];added++;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalog);catalog.CreateLookup();AssetDatabase.SaveAssets();
            Debug.Log($"Иконки стенда зарегистрированы: добавлено={added}, явных ID={manifest.items.Length}. Прежние записи и спрайты сохранены.");
        }
        public static void CheckAuthored()
        {
            var manifest=ReadManifest();var catalog=Resources.Load<InventoryIconAsset>("Original/Inventory/icons");
            if(catalog==null) throw new InvalidOperationException("Нет каталога иконок для проверки стенда.");
            var lookup=catalog.CreateLookup();
            foreach(var entry in manifest.resources) CheckTexture(entry,Resources.Load<Texture2D>(entry.resource));
            foreach(var entry in manifest.items)
            {
                if(!lookup.TryGetValue(entry.id,out var sprite) || sprite.name!=SpriteName(entry.resource) ||
                    sprite.texture!=Resources.Load<Texture2D>(entry.resource) || sprite.pixelsPerUnit!=manifest.pixelsPerUnit ||
                    sprite.rect!=new Rect(0,0,sprite.texture.width,sprite.texture.height) || sprite.pivot!=new Vector2(sprite.texture.width*.5f,sprite.texture.height*.5f))
                    throw new InvalidOperationException("Не зарегистрирована точная иконка стенда: id="+entry.id+", resource="+entry.resource);
            }
        }
    }
}
