using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class NativePonyMeshAudit
    {
        public static JObject Measure()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(NativePonyAuthoring.Root+"/PonyAnimation.prefab");
            if(prefab==null)throw new InvalidOperationException("Проверка геометрии: отсутствует префаб образца.");
            var root=Object.Instantiate(prefab);var records=new JArray();int flippedMax=0,collapsedMax=0;float minArea=1,maxStretch=1;
            try
            {
                root.GetComponent<Animator>().enabled=false;var library=root.GetComponent<SpriteLibrary>();
                var resolvers=root.GetComponentsInChildren<SpriteResolver>();var skins=root.GetComponentsInChildren<SpriteSkin>();
                foreach(string variant in NativePonyAuthoring.Variants)
                {
                    library.spriteLibraryAsset=AssetDatabase.LoadAssetAtPath<SpriteLibraryAsset>(NativePonyAuthoring.Root+"/"+variant+".asset");
                    foreach(var resolver in resolvers)resolver.ResolveSpriteToSpriteRenderer();
                    var limbs=skins.Where(s=>s.name.StartsWith("front",StringComparison.Ordinal) || s.name.StartsWith("hind",StringComparison.Ordinal) || s.name=="pipbuckNear").ToArray();
                    var probes=limbs.Select(s=>new NativePonyDeformationMetrics.Probe(s.GetComponent<SpriteRenderer>().sprite)).ToArray();
                    foreach(string name in new[]{"Walk","LegCurl"})
                    {
                        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(NativePonyAuthoring.Root+"/"+name+".anim");
                        var flipped=new int[limbs.Length];var collapsed=new int[limbs.Length];var minimum=Enumerable.Repeat(1f,limbs.Length).ToArray();
                        for(int f=0;f<=30;f++)
                        {
                            clip.SampleAnimation(root,f/30f);NativePonyChecks.Deform(skins);
                            for(int l=0;l<limbs.Length;l++)
                            {
                                var result=probes[l].Measure(limbs[l].GetDeformedVertexPositionData());
                                if(limbs[l].name=="pipbuckNear" && (Mathf.Abs(result.MinAreaRatio-1)>.0001f || Mathf.Abs(result.MaxEdgeStretch-1)>.0001f))
                                    throw new InvalidOperationException($"Проверка жёсткого ПипБака: деформация {variant}/{name}/{f}.");
                                flipped[l]=Math.Max(flipped[l],result.FlippedTriangles);collapsed[l]=Math.Max(collapsed[l],result.CollapsedTriangles);minimum[l]=Mathf.Min(minimum[l],result.MinAreaRatio);
                                flippedMax=Math.Max(flippedMax,result.FlippedTriangles);collapsedMax=Math.Max(collapsedMax,result.CollapsedTriangles);
                                minArea=Mathf.Min(minArea,result.MinAreaRatio);maxStretch=Mathf.Max(maxStretch,result.MaxEdgeStretch);
                            }
                        }
                        for(int l=0;l<limbs.Length;l++)records.Add(new JObject { ["variant"]=variant,["clip"]=name,["layer"]=limbs[l].name,["maxFlippedTriangles"]=flipped[l],["maxCollapsedTriangles"]=collapsed[l],["minSignedAreaRatio"]=minimum[l]});
                    }
                }
                return new JObject { ["framesPerClip"]=31,["variants"]=4,["maxFlippedTriangles"]=flippedMax,["maxCollapsedTriangles"]=collapsedMax,["minSignedAreaRatio"]=minArea,["maxEdgeStretch"]=maxStretch,["records"]=records};
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
