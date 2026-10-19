using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class DroppedItemShapeAuthoring
    {
        public const string Path="Assets/Remains/Resources/Content/DroppedItemShapes.asset";
        public static void RunBatch()
        {
            try {Run();DroppedItemShapeChecks.Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var artwork=new InventoryArtwork();var source=ExportedContentCatalog.Load();
            foreach(var item in source.items)
            foreach(var binding in item.graphics??Array.Empty<ExportedBinding>())
            {
                string key;
                if(binding.role=="item-image") key=item.key;
                else if(binding.role?.StartsWith("item-image-grade-",StringComparison.Ordinal)==true)
                    key=item.key+":grade:"+binding.role.Substring("item-image-grade-".Length);
                else continue;
                if(!string.IsNullOrEmpty(binding.resourceId)) artwork.TryGet(key,out _);
            }
            var bounds=new Dictionary<Sprite,Rect>();var rows=new List<DroppedItemShape>();
            foreach(var pair in artwork.PreparedSprites.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                Rect opaque;
                try
                {
                    if(!bounds.TryGetValue(pair.Value,out opaque)) bounds.Add(pair.Value,opaque=SpriteOpaqueBoundsBaker.Read(pair.Value));
                }
                catch(EmptySpriteArtworkException error)
                {
                    Debug.LogWarning($"Форма предмета не создана: definition={pair.Key}, sprite={pair.Value.name}, path={AssetDatabase.GetAssetPath(pair.Value.texture)}, rect={pair.Value.rect}; нет непрозрачных пикселей. {error.Message}",pair.Value);
                    continue;
                }
                catch(Exception error)
                {throw new InvalidOperationException($"Не удалось запечь форму: definition={pair.Key}, sprite={pair.Value.name}, path={AssetDatabase.GetAssetPath(pair.Value.texture)}, rect={pair.Value.rect}.",error);}
                rows.Add(new DroppedItemShape {id=pair.Key,sprite=pair.Value,opaqueBounds=opaque});
            }
            var catalog=AssetDatabase.LoadAssetAtPath<DroppedItemShapeCatalog>(Path);bool fresh=catalog==null;
            if(fresh)catalog=ScriptableObject.CreateInstance<DroppedItemShapeCatalog>();
            catalog.Configure(rows.ToArray());
            if(fresh)AssetDatabase.CreateAsset(catalog,Path);else EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();DroppedItemShapeCatalog.Reset();
            Debug.Log($"Формы брошенных предметов: подготовлено ID={rows.Count}, спрайтов={bounds.Count}; alpha/pivot/scale — общий источник физики и изображения.");
        }
    }
}
