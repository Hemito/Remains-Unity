using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Explicit mini-preview of this outfit, matching the existing pip/antirad icon contract.
    // This is not an original inventory icon, a gameplay actor, or a fallback to another suit.
    internal static class PonyWearableIconAuthoring
    {
        private sealed class Piece {public Texture2D Texture;public Matrix4x4 Matrix;public Color Tint;public float[] Bounds;public int[] Order;}
        public static Sprite Build(string id,WearableSourceBank bank,PonyWearableArtSource art)
        {
            var raw=PonyAnimationData.Library.clips.Single(c=>c.id=="idle").frames[0].parts;
            var frame=bank.clips.Single(c=>c.id=="idle").frames[0];var hidden=new HashSet<int>(frame.hiddenBasePartIndices);
            foreach(var instance in frame.instances)foreach(int index in instance.basePartIndices)hidden.Add(index);
            var pieces=new List<Piece>();
            bool Visible(string slot,int hair,int eyes)=>slot!="magic" && PonyAppearance.Matches(hair,eyes,slot,1,1,false);
            for(int p=0;p<raw.Length;p++)
            {
                var part=raw[p];
                if(hidden.Contains(p) || part.path.StartsWith("pip2/",StringComparison.Ordinal) || !Visible(part.slot,part.hairStyle,part.eyeStyle))continue;
                pieces.Add(new Piece {Texture=art.ResourceTexture("Original/Pony/"+part.texture),Matrix=Matrix(part.matrix),Bounds=part.bounds,
                    Tint=ColorOf(part.color)*PonyAppearance.Tint(part.slot,1),Order=frame.baseDepthPaths[p]});
            }
            var textures=bank.textures.ToDictionary(t=>t.key,StringComparer.Ordinal);
            foreach(var instance in frame.instances)
            {
                var template=bank.templates.Single(t=>t.id==instance.templateId);
                foreach(var part in template.parts)
                {
                    if(!Visible(part.slot,part.hairStyle,part.eyeStyle))continue;
                    pieces.Add(new Piece {Texture=art.ResourceTexture(textures[part.texture].resource),Matrix=Matrix(instance.matrix)*Matrix(part.matrix),Bounds=part.bounds,
                        Tint=ColorOf(instance.color)*ColorOf(part.color)*PonyAppearance.Tint(part.slot,1),Order=instance.depthPath.Concat(part.depthPath).ToArray()});
                }
            }
            pieces.Sort((a,b)=>WearableDrawPlan.ComparePath(a.Order,b.Order));
            if(pieces.Count==0)throw new InvalidDataException("Одежда: пустая миниатюра "+id);
            var minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var maximum=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var piece in pieces)for(int corner=0;corner<4;corner++)
            {
                var p=piece.Matrix.MultiplyPoint3x4(new Vector3((corner&1)==0?piece.Bounds[0]:piece.Bounds[2],(corner&2)==0?piece.Bounds[1]:piece.Bounds[3],0));
                var point=new Vector2(p.x/1000,-p.y/1000);minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);
            }
            const float ppu=200;const int padding=8;
            int width=Mathf.CeilToInt((maximum.x-minimum.x)*ppu)+padding*2,height=Mathf.CeilToInt((maximum.y-minimum.y)*ppu)+padding*2;
            if(width<1 || height<1 || width>2048 || height>2048)throw new InvalidDataException($"Одежда: недопустимая миниатюра {id}: {width}×{height}.");
            var canvas=new Color[width*height];
            foreach(var piece in pieces)
            {
                var inverse=piece.Matrix.inverse;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float wx=minimum.x+(x-padding+.5f)/ppu,wy=minimum.y+(y-padding+.5f)/ppu;
                    var local=inverse.MultiplyPoint3x4(new Vector3(wx*1000,-wy*1000,0));
                    float u=(local.x-piece.Bounds[0])/(piece.Bounds[2]-piece.Bounds[0]),v=(piece.Bounds[3]-local.y)/(piece.Bounds[3]-piece.Bounds[1]);
                    if(u<0 || u>1 || v<0 || v>1)continue;
                    var color=piece.Texture.GetPixelBilinear(u,v)*piece.Tint;
                    if(color.a<=0)continue;
                    int index=y*width+x;var old=canvas[index];
                    canvas[index]=new Color(color.r*color.a+old.r*(1-color.a),color.g*color.a+old.g*(1-color.a),color.b*color.a+old.b*(1-color.a),color.a+old.a*(1-color.a));
                }
            }
            for(int i=0;i<canvas.Length;i++)if(canvas[i].a>0){canvas[i].r/=canvas[i].a;canvas[i].g/=canvas[i].a;canvas[i].b/=canvas[i].a;}
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            string path="Assets/Remains/Resources/Original/Pony/Wearables/Icons/"+id+".png";
            try{image.SetPixels(canvas);image.Apply();PonyWearableArtSource.WriteOwned(path,image.EncodeToPNG(),"remains-wearable-icon-v1:");}
            finally{Object.DestroyImmediate(image);}
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=ppu;importer.alphaIsTransparency=false;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path)??throw new InvalidDataException("Одежда: не импортирована миниатюра "+id);
        }
        public static void Register(string definition,Sprite sprite)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<InventoryIconAsset>("Assets/Remains/Resources/Original/Inventory/icons.asset")??throw new InvalidDataException("Одежда: нет inventory icons.asset.");
            var serialized=new SerializedObject(catalog);var entries=serialized.FindProperty("entries");
            int index=-1;
            for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue==definition){index=i;break;}
            if(index<0){index=entries.arraySize;entries.InsertArrayElementAtIndex(index);}
            var entry=entries.GetArrayElementAtIndex(index);entry.FindPropertyRelative("Id").stringValue=definition;entry.FindPropertyRelative("Sprite").objectReferenceValue=sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalog);
        }
        private static Color ColorOf(float[] c)=>new(c[0],c[1],c[2],c[3]);
        private static Matrix4x4 Matrix(float[] m)
        {var value=Matrix4x4.identity;value.m00=m[0];value.m10=m[1];value.m01=m[2];value.m11=m[3];value.m03=m[4];value.m13=m[5];return value;}
    }
}
