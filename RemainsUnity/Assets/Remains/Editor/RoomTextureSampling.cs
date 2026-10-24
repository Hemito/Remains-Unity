using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEditor;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    // Sampling is serialized import data; a builder's in-memory texture change is not a saved contract.
    internal static class RoomTextureSampling
    {
        public static void ConfigureRepeatingArtwork(RoomLayout layout)
        {
            var keys=new Dictionary<string,bool>();
            foreach(var layer in layout.artwork ?? Array.Empty<RoomArtwork>())
                if(layer.repeat)
                {
                    if(string.IsNullOrEmpty(layer.texture)) throw new InvalidOperationException($"Повторяемое оформление комнаты {layout.id}: отсутствует ключ текстуры.");
                    keys[layer.texture]=!layer.stretchY || keys.TryGetValue(layer.texture,out bool vertical) && vertical;
                }
            foreach(var ladder in layout.ladders ?? Array.Empty<RoomLadder>())
                if(!string.IsNullOrEmpty(ladder.artwork)) keys[ladder.artwork]=true;
            foreach(var entry in keys)
            {
                var texture=GameAssets.Load<Texture2D>("Original/Room/"+entry.Key);
                string path=texture==null?null:AssetDatabase.GetAssetPath(texture);
                if(string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    throw new InvalidOperationException($"Повторяемое оформление комнаты {layout.id}: нет импортёра текстуры {entry.Key}.");
                bool changed=importer.wrapModeU!=TextureWrapMode.Repeat || entry.Value && importer.wrapModeV!=TextureWrapMode.Repeat;
                if(!changed) continue;
                importer.wrapModeU=TextureWrapMode.Repeat;
                if(entry.Value) importer.wrapModeV=TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
        }

        public static void ValidateMesh(Mesh mesh,Texture2D texture,string context,bool? repeat=null)
        {
            string path=texture==null?null:AssetDatabase.GetAssetPath(texture);
            if(mesh==null || string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new InvalidOperationException($"Оформление {context}: отсутствует сохранённый меш/импортёр текстуры, texture={path}.");
            var uv=mesh.uv;
            if(uv.Length!=mesh.vertexCount || uv.Length==0)
                throw new InvalidOperationException($"Оформление {context}: неполные UV; mesh={AssetDatabase.GetAssetPath(mesh)}, texture={path}.");
            float minU=float.PositiveInfinity,maxU=float.NegativeInfinity,minV=minU,maxV=maxU;
            foreach(var point in uv)
            {
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y)) throw new InvalidOperationException($"Оформление {context}: нечисловые UV, texture={path}.");
                minU=Mathf.Min(minU,point.x);maxU=Mathf.Max(maxU,point.x);minV=Mathf.Min(minV,point.y);maxV=Mathf.Max(maxV,point.y);
            }
            bool u=minU<-.00001f || maxU>1.00001f,v=minV<-.00001f || maxV>1.00001f;
            if(repeat==false && (u || v))
                throw new InvalidOperationException($"Оформление {context}: repeat выключен, но UV выходят за картинку; texture={path}, U={minU}..{maxU}, V={minV}..{maxV}.");
            // Check the importer even if another builder already changed the loaded Texture object to Repeat.
            if(u && (importer.wrapModeU!=TextureWrapMode.Repeat || texture.wrapModeU!=TextureWrapMode.Repeat) ||
                v && (importer.wrapModeV!=TextureWrapMode.Repeat || texture.wrapModeV!=TextureWrapMode.Repeat))
                throw new InvalidOperationException($"Оформление {context}: UV требуют сохранённый Repeat; texture={path}, U={minU}..{maxU}/import={importer.wrapModeU}/loaded={texture.wrapModeU}, V={minV}..{maxV}/import={importer.wrapModeV}/loaded={texture.wrapModeV}. Временный sampler генератора не исправляет импорт.");
        }
    }
}
