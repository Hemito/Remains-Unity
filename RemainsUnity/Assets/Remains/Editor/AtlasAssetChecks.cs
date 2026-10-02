using System;
using Remains.Content;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class AtlasAssetChecks
    {
        public static void Run()
        {
            var asset=Resources.Load<AtlasSpriteAsset>("Original/Raider/artwork");
            if(asset==null) throw new InvalidOperationException("Нет сохранённого атласа рейдера.");
            var data=asset.CreateDefinition();int count=0;
            foreach(var clip in data.clips)
            {
                var frames=asset.Frames(clip.pose);count+=frames.Length;
                for(int i=0;i<frames.Length;i++)
                {
                    var frame=frames[i];
                    if(!AssetDatabase.Contains(frame) || frame.rect!=new Rect(i*data.cellWidth,data.height-(clip.row+1)*data.cellHeight,data.cellWidth,data.cellHeight) ||
                        frame.pixelsPerUnit!=data.pixelsPerUnit)
                        throw new InvalidOperationException("Кадр атласа не сохранён или не соответствует описанию: "+clip.pose+":"+i);
                }
            }
            var copy=UnityEngine.Object.Instantiate(asset);
            try
            {
                data.clips[0].framesPerSecond+=1;copy.Configure(asset.Texture,data);bool rejected=false;
                try {copy.Frames(data.clips[0].pose);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Атлас принял устаревшую нарезку.");
                asset.Frames(data.clips[0].pose);
            }
            finally {UnityEngine.Object.DestroyImmediate(copy);}
            Debug.Log($"Сохранённый атлас: PASS — кадров {count}, прямые ссылки, геометрия и отказ устаревших данных.");
        }
        public static void RebuildAndVerify()
        {
            var asset=Resources.Load<AtlasSpriteAsset>("Original/Raider/artwork");
            var before=asset.Frames("idle")[0];AssetDatabase.TryGetGUIDAndLocalFileIdentifier(before,out string guid,out long id);
            AtlasSpriteAssetEditor.Rebuild(asset);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.Frames("idle")[0],out string afterGuid,out long afterId);
            if(guid!=afterGuid || id!=afterId) throw new InvalidOperationException("Пересборка изменила постоянную ссылку на кадр.");
            Run();
        }
    }
}
