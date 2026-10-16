using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    [CustomEditor(typeof(AtlasSpriteAsset))]
    public sealed class AtlasSpriteAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if(GUILayout.Button("Пересобрать кадры"))
            {
                try {Rebuild((AtlasSpriteAsset)target);}
                catch(Exception error) {Debug.LogError(error.Message,target);}
            }
        }
        public static void Rebuild(AtlasSpriteAsset asset)
        {
            var data=asset.CreateDefinition();var texture=asset.Texture;
            if(texture==null || data==null || data.clips==null || data.clips.Length==0 || data.cellWidth<=0 || data.cellHeight<=0 ||
                texture.width!=data.width || texture.height!=data.height || data.width%data.cellWidth!=0 || data.height%data.cellHeight!=0 ||
                !float.IsFinite(data.pixelsPerUnit) || data.pixelsPerUnit<=0 || !float.IsFinite(data.pivotX) || !float.IsFinite(data.pivotY) ||
                data.pivotX<0 || data.pivotX>1 || data.pivotY<0 || data.pivotY>1)
                throw new InvalidOperationException($"Атлас {asset.name}: неверная текстура, сетка, масштаб или точка привязки.");
            var poses=new HashSet<string>();
            foreach(var clip in data.clips)
            {
                if(clip==null || string.IsNullOrWhiteSpace(clip.pose) || !poses.Add(clip.pose) || clip.row<0 || clip.row>=data.height/data.cellHeight ||
                    clip.count<1 || clip.column<0 || clip.column+clip.count>data.width/data.cellWidth || !float.IsFinite(clip.framesPerSecond) || clip.framesPerSecond<=0 ||
                    clip.loopStartFrame<0 || clip.loopStartFrame>=clip.count || (!clip.loop && clip.loopStartFrame!=0))
                    throw new InvalidOperationException($"Атлас {asset.name}: неверная поза {clip?.pose}.");
                if(clip.UsesMotion) MotionFrameSampling.Validate(clip.verticalFrames,clip.count,asset.name+":"+clip.pose);
                AtlasWeaponSockets.ValidateClip(clip,asset.name);
            }
            string path=AssetDatabase.GetAssetPath(asset);
            if(string.IsNullOrEmpty(path)) throw new InvalidOperationException("Сначала сохраните атлас в проект.");
            var existing=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(sprite=>sprite.name);
            var frames=new List<Sprite>();
            foreach(var clip in data.clips)
            for(int index=0;index<clip.count;index++)
            {
                string id=clip.pose+":"+index;
                var sprite=Sprite.Create(texture,new Rect((clip.column+index)*data.cellWidth,data.height-(clip.row+1)*data.cellHeight,data.cellWidth,data.cellHeight),
                    new Vector2(data.pivotX,data.pivotY),data.pixelsPerUnit);sprite.name=id;
                if(existing.TryGetValue(id,out var saved))
                {EditorUtility.CopySerialized(sprite,saved);UnityEngine.Object.DestroyImmediate(sprite);sprite=saved;EditorUtility.SetDirty(sprite);}
                else AssetDatabase.AddObjectToAsset(sprite,asset);
                frames.Add(sprite);
            }
            asset.SetFrames(frames.ToArray());EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            Debug.Log($"Атлас {asset.name}: сохранено кадров {frames.Count}.",asset);
        }
    }
}

