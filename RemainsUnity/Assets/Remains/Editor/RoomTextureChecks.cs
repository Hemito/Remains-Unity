using System;
using Remains.Content;
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
            int meshes=0;
            // Load saved sources directly. Do not call RoomVisual.Build, which can change live texture sampling.
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Remains"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null || prefab.GetComponentInChildren<RoomAuthoring>(true)==null) continue;
                foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var artwork=filter.GetComponent<RoomArtworkAuthoring>();
                    if(artwork==null && filter.GetComponentInParent<RoomLadderAuthoring>(true)==null) continue;
                    var renderer=filter.GetComponent<MeshRenderer>();
                    if(renderer==null || renderer.sharedMaterial==null || renderer.sharedMaterial.mainTexture is not Texture2D texture)
                        throw new InvalidOperationException($"Оформление {path}/{filter.name}: отсутствует материал/текстура.");
                    bool? repeat=artwork==null?null:new SerializedObject(artwork).FindProperty("repeat").boolValue;
                    RoomTextureSampling.ValidateMesh(filter.sharedMesh,texture,path+"/"+filter.name,repeat);
                    meshes++;
                }
            }
            if(meshes==0) throw new InvalidOperationException("Не найдены сохранённые меши оформления комнат для проверки sampler/UV.");
            Debug.Log($"Текстуры комнат: {count}, исходные размеры сохранены, сжатия GPU нет.");
            Debug.Log($"Сохранённое оформление: {meshes} мешей, UV и оси импортированных sampler согласованы без генерации комнаты.");
        }
    }
}
