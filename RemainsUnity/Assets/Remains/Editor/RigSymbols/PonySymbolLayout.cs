using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    // C# authoring equivalent of the verified node decomposition. First occurrence defines each symbol's local space.
    public sealed class PonySymbolLayout
    {
        public sealed class Placement {public string Id,Socket;public Matrix4x4 Matrix;public PonyRigTransform Transform;}
        private sealed class Symbol {public PonyPart Anchor;public string Signature;}
        private readonly Dictionary<string,Symbol> symbols=new(StringComparer.Ordinal);
        private readonly Dictionary<(string,int,string),Placement> placements=new();
        public int SymbolCount=>symbols.Count;
        public Placement Get(string clip,int frame,string path)
        {
            int slash=path.IndexOf('/');string top=slash<0?path:path.Substring(0,slash);
            return placements.TryGetValue((clip,frame,top),out var value)?value:
                throw new InvalidDataException($"Нет символа узла: clip={clip}, frame={frame}, path={path}.");
        }
        public PonySymbolLayout(PonyLibrary library)
        {
            foreach(var clip in library.clips)for(int f=0;f<clip.frames.Length;f++)foreach(var group in PonyRigPilotArt.Groups(clip.frames[f]))
            {
                var parts=group.Indices.Select(i=>clip.frames[f].parts[i]).OrderBy(p=>LocalPath(p.path),StringComparer.Ordinal)
                    .ThenBy(p=>p.shape,StringComparer.Ordinal).ThenBy(p=>p.hairStyle).ThenBy(p=>p.eyeStyle).ToArray();
                string id=group.Socket+":"+string.Join(",",parts.Select(p=>p.shape).Distinct(StringComparer.Ordinal).OrderBy(v=>int.Parse(v)));
                string signature=string.Join("|",parts.Select(p=>LocalPath(p.path)+":"+p.shape+":"+p.hairStyle+":"+p.eyeStyle));
                Matrix4x4 matrix;
                if(!symbols.TryGetValue(id,out var symbol))
                {symbol=new Symbol {Anchor=parts[0],Signature=signature};symbols.Add(id,symbol);matrix=Matrix4x4.identity;}
                else
                {
                    if(symbol.Signature!=signature)throw new InvalidDataException($"Состав символа изменился: {clip.id}/{f}/{id}.");
                    matrix=PonyRigPilotArt.Affine(parts[0].matrix)*PonyRigPilotArt.Affine(symbol.Anchor.matrix).inverse;
                }
                placements.Add((clip.id,f,group.Top),new Placement {Id=id,Socket=group.Socket,Matrix=matrix,Transform=PonyRigTransform.FromMatrix(matrix)});
            }
        }
        public static string LocalPath(string path){int slash=path.IndexOf('/');return slash<0?"":path.Substring(slash);}
    }
}
