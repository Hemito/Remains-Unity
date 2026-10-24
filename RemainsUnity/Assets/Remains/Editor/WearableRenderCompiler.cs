using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class WearableRenderCompiler
    {
        public const string CatalogPath="Assets/Remains/Resources/Content/wearable-render.asset";
        [MenuItem("Remains/Снаряжение/Подготовить рисунки")]
        public static void Build()
        {
            PonyRenderCompiler.Build();PonyAnimationData.Load();
            var assets=new List<WearableRenderAsset>();
            foreach(string name in new[]{"pip","antirad"})
            {
                string resource="Original/Pony/Wearables/"+name;
                var json=GameAssets.Load<TextAsset>(resource);
                if(json==null) throw new InvalidOperationException("Нет исходного банка снаряжения: "+resource+".");
                assets.Add(BuildAsset(json.text,"Assets/Remains/Resources/Original/Pony/Wearables/"+name+"-render.asset",resource));
            }
            var catalog=AssetDatabase.LoadAssetAtPath<WearableRenderCatalogAsset>(CatalogPath);
            if(catalog==null) {catalog=ScriptableObject.CreateInstance<WearableRenderCatalogAsset>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            catalog.Configure(assets.ToArray());EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            Debug.Log("Графика визуального снаряжения подготовлена: банков="+assets.Count+". Базовые кадры/анатомия не изменены.");
        }
        public static void BuildAndPrepare()
        {
            Build();WeaponCatalog.Load();PonyAppearanceAuthoring.PrepareCompiled();CharacterInterfaceAuthoring.RefreshSavedPortraits();
            WearablePresentationChecks.Run();
        }
        // Content-pack tooling may compile its own declared bank into an AssetBundle.
        public static WearableRenderAsset BuildAsset(string json,string outputPath,string source)
        {
            PonyAnimationData.Load();var library=PonyAnimationData.Library;
            var bank=StateJson.FromJson<WearableSourceBank>(json);
            if(bank==null || bank.schema!=1 || bank.baseSourceHash!=PonyRenderAsset.SourceHash() || string.IsNullOrWhiteSpace(bank.visualId) ||
                bank.anatomy!="pony" || bank.textures==null || bank.templates==null || bank.clips==null || bank.clips.Length!=library.clips.Length)
                throw new InvalidOperationException("Некорректный банк визуального снаряжения: source="+source+".");
            var templates=new Dictionary<string,WearableSourceTemplate>(StringComparer.Ordinal);
            foreach(var template in bank.templates)
                if(template==null || string.IsNullOrWhiteSpace(template.id) || template.parts==null || !templates.TryAdd(template.id,template))
                    throw new InvalidOperationException("Повтор/пустой шаблон снаряжения: source="+source+".");
            var textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
            foreach(var declaration in bank.textures)
            {
                if(declaration==null || string.IsNullOrWhiteSpace(declaration.key) || string.IsNullOrWhiteSpace(declaration.resource) || textures.ContainsKey(declaration.key))
                    throw new InvalidOperationException("Повтор/пустая текстура снаряжения: source="+source+".");
                var texture=GameAssets.Load<Texture2D>(declaration.resource);
                if(texture==null || texture.width!=declaration.width || texture.height!=declaration.height ||
                    Hash(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)))!=declaration.sha256)
                    throw new InvalidOperationException($"Текстура снаряжения отсутствует/изменена: source={source}, key={declaration.key}, resource={declaration.resource}.");
                textures.Add(declaration.key,texture);
            }
            var prepared=new List<WearableRenderClip>();var meshes=new List<Mesh>();var materials=new Dictionary<string,Material>(StringComparer.Ordinal);
            try
            {
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var clip in bank.clips)
                {
                    var baseline=Array.Find(library.clips,value=>value.id==clip?.id);
                    if(clip==null || baseline==null || !seen.Add(clip.id) || clip.frames==null || clip.frames.Length!=baseline.frames.Length)
                        throw new InvalidOperationException($"Не совпали кадры снаряжения: source={source}, clip={clip?.id}.");
                    var output=new WearableRenderClip {id=clip.id,frames=new WearableRenderFrame[clip.frames.Length]};
                    for(int f=0;f<clip.frames.Length;f++)
                    {
                        var frame=clip.frames[f];var baseParts=baseline.frames[f].parts;
                        if(frame==null || frame.baseDepthPaths==null || frame.baseDepthPaths.Length!=baseParts.Length || frame.hiddenBasePartIndices==null || frame.instances==null)
                            throw new InvalidOperationException($"Нет полного порядка/привязок: source={source}, clip={clip.id}, frame={f}.");
                        var hidden=new HashSet<int>();var parts=new List<PonyRenderPart>();var orders=new WearableRenderOrder[baseParts.Length];
                        for(int p=0;p<orders.Length;p++) {WearableRenderAsset.ValidatePath(frame.baseDepthPaths[p],source+"/"+clip.id+"/"+f);orders[p]=new WearableRenderOrder {path=(int[])frame.baseDepthPaths[p].Clone()};}
                        void Hide(int index)
                        {
                            if(index<0 || index>=baseParts.Length) throw new InvalidOperationException($"Неизвестный hide ID: source={source}, clip={clip.id}, frame={f}, part={index}.");
                            hidden.Add(index);
                        }
                        foreach(int index in frame.hiddenBasePartIndices) Hide(index);
                        bool hasBounds=false;Bounds bounds=default;
                        for(int i=0;i<frame.instances.Length;i++)
                        {
                            var instance=frame.instances[i];
                            if(instance==null || string.IsNullOrWhiteSpace(instance.basePath) || instance.basePartIndices==null || !templates.TryGetValue(instance.templateId,out var template))
                                throw new InvalidOperationException($"Нет шаблона/исходной привязки: source={source}, clip={clip.id}, frame={f}, instance={i}.");
                            WearableRenderAsset.ValidatePath(instance.depthPath,source+"/"+instance.basePath);
                            foreach(int index in instance.basePartIndices)
                            {
                                Hide(index);
                                if(!baseParts[index].path.StartsWith(instance.basePath+"/",StringComparison.Ordinal) && baseParts[index].path!=instance.basePath)
                                    throw new InvalidOperationException($"Привязка скрывает чужую часть: source={source}, clip={clip.id}, frame={f}, base={instance.basePath}, part={baseParts[index].path}.");
                            }
                            var outer=Affine(instance.matrix,source+"/"+instance.basePath);var outerColor=ColorOf(instance.color,source);
                            for(int p=0;p<template.parts.Length;p++)
                            {
                                var part=template.parts[p];
                                if(part==null || !textures.TryGetValue(part.texture,out var texture) || part.bounds==null || part.bounds.Length!=4 ||
                                    part.bounds[2]<=part.bounds[0] || part.bounds[3]<=part.bounds[1] || Array.Exists(part.bounds,value=>!float.IsFinite(value)))
                                    throw new InvalidOperationException($"Нет графики части: source={source}, template={template.id}, part={p}.");
                                WearableRenderAsset.ValidatePath(part.depthPath,source+"/"+template.id+"/"+p);
                                if(!materials.TryGetValue(part.texture,out var material))
                                {material=WorldArtworkMaterial.Create(texture);material.name="material:"+part.texture;materials.Add(part.texture,material);}
                                var matrix=outer*Affine(part.matrix,source+"/"+template.id+"/"+p);
                                var declared=new PonyPart {bounds=part.bounds,matrix=new[]{matrix.m00,matrix.m10,matrix.m01,matrix.m11,matrix.m03,matrix.m13}};
                                var points=PonyAnimationData.Quad(declared);var color=outerColor*ColorOf(part.color,source);
                                var mesh=new Mesh {name=$"wearable:{bank.visualId}:{clip.id}:{f}:{i}:{p}",vertices=points,uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},
                                    triangles=new[]{0,2,1,2,3,1},colors=new[]{color,color,color,color}};
                                mesh.RecalculateBounds();meshes.Add(mesh);
                                var path=new int[instance.depthPath.Length+part.depthPath.Length];instance.depthPath.CopyTo(path,0);part.depthPath.CopyTo(path,instance.depthPath.Length);
                                parts.Add(new PonyRenderPart {mesh=mesh,material=material,slot=part.slot,visible=true,hairStyle=part.hairStyle,eyeStyle=part.eyeStyle,
                                    sourceIndex=-1,sourcePath=instance.basePath+"/wear:"+template.id+"/"+part.path,sourceShape=part.shape,points=points,baseColor=color,orderPath=path});
                                if(!hasBounds) {bounds=mesh.bounds;hasBounds=true;}else bounds.Encapsulate(mesh.bounds);
                            }
                        }
                        output.frames[f]=new WearableRenderFrame {parts=parts.ToArray(),hiddenBasePartIndices=hidden.OrderBy(value=>value).ToArray(),baseOrder=orders,bounds=bounds};
                    }
                    prepared.Add(output);
                }
                string sourceHash=Hash(Encoding.UTF8.GetBytes(json));var candidate=ScriptableObject.CreateInstance<WearableRenderAsset>();
                candidate.Configure(bank.visualId,bank.anatomy,sourceHash,bank.baseSourceHash,Hash(Encoding.UTF8.GetBytes("wearable-compiler-v1\n"+sourceHash)),prepared.ToArray());
                try {candidate.Validate(library,bank.baseSourceHash,source);} catch {UnityEngine.Object.DestroyImmediate(candidate);throw;}
                var target=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>(outputPath);
                if(target==null) {target=candidate;AssetDatabase.CreateAsset(target,outputPath);}else
                {EditorUtility.CopySerialized(candidate,target);UnityEngine.Object.DestroyImmediate(candidate);}
                var saved=AssetDatabase.LoadAllAssetsAtPath(outputPath).Where(value=>value is Mesh || value is Material).ToDictionary(value=>value.name,StringComparer.Ordinal);
                T Save<T>(T value) where T:UnityEngine.Object
                {
                    if(saved.TryGetValue(value.name,out var previous))
                    {
                        if(previous is not T) throw new InvalidOperationException("Конфликт ресурса снаряжения: "+value.name+".");
                        EditorUtility.CopySerialized(value,previous);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(previous);return (T)previous;
                    }
                    AssetDatabase.AddObjectToAsset(value,target);return value;
                }
                var savedMaterials=new Dictionary<Material,Material>();foreach(var material in materials.Values) savedMaterials.Add(material,Save(material));
                foreach(var clip in target.Clips) foreach(var frame in clip.frames) foreach(var part in frame.parts)
                {part.mesh=Save(part.mesh);part.material=savedMaterials[part.material];}
                EditorUtility.SetDirty(target);AssetDatabase.SaveAssets();target.Validate(library,bank.baseSourceHash,source);return target;
            }
            catch
            {
                foreach(var mesh in meshes) if(mesh!=null && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
                foreach(var material in materials.Values) if(material!=null && !EditorUtility.IsPersistent(material)) UnityEngine.Object.DestroyImmediate(material);
                throw;
            }
        }
        private static Matrix4x4 Affine(float[] value,string context)
        {
            if(value==null || value.Length!=6 || Array.Exists(value,item=>!float.IsFinite(item))) throw new InvalidOperationException("Нет affine-регистрации снаряжения: "+context+".");
            var matrix=Matrix4x4.identity;matrix.m00=value[0];matrix.m10=value[1];matrix.m01=value[2];matrix.m11=value[3];matrix.m03=value[4];matrix.m13=value[5];return matrix;
        }
        private static Color ColorOf(float[] value,string context)
        {
            if(value==null || value.Length!=4 || Array.Exists(value,item=>!float.IsFinite(item))) throw new InvalidOperationException("Нет цветовой регистрации снаряжения: "+context+".");
            return new Color(value[0],value[1],value[2],value[3]);
        }
        private static string Hash(byte[] value) {using var algorithm=SHA256.Create();return BitConverter.ToString(algorithm.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
    }
}
