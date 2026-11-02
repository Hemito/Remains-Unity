using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class PonyRenderCompiler
    {
        [MenuItem("Remains/Пересобрать графику пони")]
        public static void Build()
        {
            const string path="Assets/Remains/Resources/Original/Pony/render.asset";
            var asset=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>(path);
            if(asset==null) {asset=ScriptableObject.CreateInstance<PonyRenderAsset>();AssetDatabase.CreateAsset(asset,path);}
            var old=AssetDatabase.LoadAllAssetsAtPath(path).Where(value=>value is Mesh || value is Material).ToDictionary(value=>value.name);
            // The game library: pony.json plus the authored bank (PonyAuthoredClips).
            PonyAnimationData.Load();var library=PonyAnimationData.Library;
            var layout=new PonySymbolLayout(library);var retained=new HashSet<UnityEngine.Object>();
            var clips=new List<PonyRenderClip>();var materials=new Dictionary<string,Material>();int meshCount=0;
            T Save<T>(T value) where T:UnityEngine.Object
            {
                if(old.TryGetValue(value.name,out var saved))
                {
                    if(saved is not T) throw new InvalidOperationException("Конфликт типа ресурса пони: "+value.name);
                    EditorUtility.CopySerialized(value,saved);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(saved);retained.Add(saved);return (T)saved;
                }
                AssetDatabase.AddObjectToAsset(value,asset);retained.Add(value);return value;
            }
            var symbols=new PonySymbolMeshCompiler(mesh=>Save(mesh));
            foreach(var clip in library.clips)
            {
                var result=new PonyRenderClip {id=clip.id,frames=new PonyRenderFrame[clip.frames.Length]};
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var parts=clip.frames[f].parts;var frame=new PonyRenderFrame {parts=new PonyRenderPart[parts.Length]};bool hasBounds=false;
                    for(int p=0;p<parts.Length;p++)
                    {
                        var part=parts[p];
                        if(part.slot is not ("none" or "fur" or "hair" or "hairSecondary" or "eye" or "magic"))
                            throw new InvalidOperationException($"Неизвестная область пони: {part.slot}, {part.path}.");
                        if(!materials.TryGetValue(part.texture,out var material))
                        {
                            var texture=GameAssets.Load<Texture2D>("Original/Pony/"+part.texture);
                            if(texture==null) throw new InvalidOperationException("Нет текстуры пони: "+part.texture);
                            material=WorldArtworkMaterial.Create(texture);material.name="material:"+part.texture;material=Save(material);materials.Add(part.texture,material);
                        }
                        var tint=new Color(part.color[0],part.color[1],part.color[2],part.color[3]);
                        var points=PonyAnimationData.Quad(part);
                        string top=part.path.Split('/')[0];var placement=layout.Get(clip.id,f,top);
                        var mesh=symbols.Piece(placement,PonySymbolLayout.LocalPath(part.path)+":"+part.shape+":"+part.hairStyle+":"+part.eyeStyle,points,tint);meshCount++;
                        bool visible=!part.path.StartsWith("pip2/");frame.parts[p]=new PonyRenderPart {mesh=mesh,material=material,slot=part.slot,visible=visible,
                            hairStyle=part.hairStyle,eyeStyle=part.eyeStyle,drawOrder=part.drawOrder,sourceIndex=p,
                            sourcePath=part.path,sourceShape=part.shape,points=points,baseColor=tint,symbolId=placement.Id,symbolPose=placement.Transform};
                        if(visible) {var bounds=frame.parts[p].ActorBounds;if(!hasBounds){frame.bounds=bounds;hasBounds=true;}else frame.bounds.Encapsulate(bounds);}
                    }
                    if(!hasBounds) throw new InvalidOperationException($"Нет видимых частей: {clip.id}:{f}.");
                    result.frames[f]=frame;
                }
                clips.Add(result);
            }
            PonyHeadCatalog.Reset();
            var heads=PonyHeadRenderCompiler.Compile(library,layout,symbols,material=>Save(material));
            asset.Configure(clips.ToArray(),heads);asset.ConfigureSymbols(symbols.Symbols);EditorUtility.SetDirty(asset);
            foreach(var previous in old.Values)if(!retained.Contains(previous))UnityEngine.Object.DestroyImmediate(previous,true);
            AssetDatabase.SaveAssets();asset.Validate();
            Debug.Log($"Библиотека символов пони: узловых символов={layout.SymbolCount}, общих meshes={symbols.MeshCount}, ссылок частей={meshCount}, материалов={materials.Count}, поз={clips.Count}, форм головы={heads.Length}, max error={symbols.MaximumError:G6} м.");
        }
    }
}
