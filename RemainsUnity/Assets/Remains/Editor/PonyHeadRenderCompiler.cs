using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Cosmetic geometry is prepared using the same authored affine pose as the
    // original head. Raster resolution never determines the character's size.
    public static class PonyHeadRenderCompiler
    {
        public static PonyRenderHeadStyle[] Compile(PonyLibrary library,
            Func<Mesh,Mesh> saveMesh,Func<Material,Material> saveMaterial)
        {
            var styles=new List<PonyRenderHeadStyle>();
            PonyHeadCatalog.Load();
            foreach(var definition in PonyHeadCatalog.Styles)
            {
                if(definition.id==Domain.CharacterAppearance.OriginalHeadStyleId) continue;
                var texture=GameAssets.Load<Texture2D>(definition.texture);
                if(texture==null)
                    throw new InvalidOperationException($"Нет текстуры формы головы: id={definition.id}, texture={definition.texture}.");
                var material=WorldArtworkMaterial.Create(texture);
                material.name="head-material:"+definition.id;material=saveMaterial(material);
                var clips=new PonyRenderClip[library.clips.Length];
                for(int c=0;c<clips.Length;c++)
                {
                    var sourceClip=library.clips[c];
                    var clip=new PonyRenderClip {id=sourceClip.id,frames=new PonyRenderFrame[sourceClip.frames.Length]};
                    for(int f=0;f<clip.frames.Length;f++)
                    {
                        var sourceParts=sourceClip.frames[f].parts;
                        var candidates=sourceParts.Select((part,index)=>(part,index))
                            .Where(pair=>pair.part.shape=="196" && pair.part.path=="head/morda/morda/1").ToArray();
                        if(candidates.Length!=1)
                            throw new InvalidOperationException($"Форма головы не имеет однозначной покадровой привязки: id={definition.id}, clip={clip.id}, frame={f}, matches={candidates.Length}.");
                        var (source,index)=candidates[0];
                        var geometry=new PonyPart {bounds=definition.bounds,matrix=source.matrix};
                        var points=PonyAnimationData.Quad(geometry);
                        var tint=new Color(source.color[0],source.color[1],source.color[2],source.color[3]);
                        var mesh=new Mesh {name=$"head-mesh:{definition.id}:{clip.id}:{f}",vertices=points,
                            uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},
                            triangles=new[]{0,2,1,2,3,1},colors=new[]{tint,tint,tint,tint}};
                        mesh.RecalculateBounds();mesh=saveMesh(mesh);
                        var replacement=new PonyRenderPart {mesh=mesh,material=material,slot=source.slot,visible=true,
                            hairStyle=source.hairStyle,eyeStyle=source.eyeStyle,drawOrder=source.drawOrder,sourceIndex=index,
                            sourcePath=source.path,sourceShape=source.shape,points=points,baseColor=tint};
                        clip.frames[f]=new PonyRenderFrame {parts=new[]{replacement},bounds=mesh.bounds};
                    }
                    clips[c]=clip;
                }
                styles.Add(new PonyRenderHeadStyle {id=definition.id,clips=clips});
            }
            return styles.ToArray();
        }
    }
}
