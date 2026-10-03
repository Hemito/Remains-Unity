using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomTextureChecks
    {
        public static void Run()
        {
            int count=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Remains/Resources/Original/Room"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                importer.GetSourceTextureWidthAndHeight(out int width,out int height);
                if(importer.textureCompression!=TextureImporterCompression.Uncompressed || texture.width!=width || texture.height!=height ||
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat))
                    throw new InvalidOperationException($"Потеря качества текстуры комнаты: {path}, исходник={width}×{height}, импорт={texture.width}×{texture.height}, формат={texture.graphicsFormat}.");
                count++;
            }
            if(count==0) throw new InvalidOperationException("Не найдены текстуры комнат для проверки импорта.");
            Debug.Log($"Текстуры комнат: {count}, исходные размеры сохранены, сжатия GPU нет.");
        }
    }
}
