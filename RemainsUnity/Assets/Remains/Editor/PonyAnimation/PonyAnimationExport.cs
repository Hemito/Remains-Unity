using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyAnimationExport
    {
        // A standalone clip with explicit geometry reference. Does not overwrite the stock 61-clip bank.
        public static string RigJson(PonyPoseDocument document,PonyAnimationArtwork artwork)
        {
            document.Validate();
            int steps=Mathf.RoundToInt(document.duration*document.fps);
            if(steps<1 || steps>18000)throw new InvalidDataException("Экспорт анимации: требуется от 1 до 18000 интервалов.");
            var frames=new PonyRigFrame[steps+1];
            for(int f=0;f<frames.Length;f++)
            {
                frames[f]=artwork.CompileFrame(document,document.Sample(document.duration*f/steps));
            }
            var clip=new PonyRigClip {id=document.clipId,source="pony-authoring-v1",frames=frames};
            return new JObject { ["schema"]=1,["format"]="pony-authored-rig-clip",["baseSourceHash"]=PonyRenderAsset.SourceHash(),
                ["geometryClip"]=document.sourceClip,["geometryFrame"]=document.sourceFrame,["fps"]=steps/document.duration,
                ["duration"]=document.duration,["loop"]=document.loop,["endpointIncluded"]=true,["clip"]=JObject.Parse(JsonUtility.ToJson(clip))}.ToString(Formatting.Indented);
        }
    }
}
