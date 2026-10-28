using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    internal sealed class PonyWearableArtSource:IDisposable
    {
        public readonly struct Mask
        {public readonly int Id;public readonly Matrix4x4 Matrix;public Mask(int id,Matrix4x4 matrix){Id=id;Matrix=matrix;}}
        private const string Folder="Assets/Remains/Resources/Original/Pony/Wearables/Art";
        private const string Resource="Original/Pony/Wearables/Art/";
        private const string Owner="remains-wearable-art-v1:";
        private readonly PonyWearableXmlSource xml;
        private readonly string rawFolder;
        private readonly Dictionary<string,Texture2D> loaded=new(StringComparer.Ordinal);
        private readonly Dictionary<string,WearableSourceTexture> declarations=new(StringComparer.Ordinal);
        public PonyWearableArtSource(PonyWearableXmlSource source,string folder){xml=source;rawFolder=folder;Directory.CreateDirectory(Folder);}
        public WearableSourceTexture Declaration(string key)=>declarations[key];
        public string Texture(int id,Matrix4x4 transform,IReadOnlyList<Mask> masks)
        {
            var relatives=masks.Select(m=>(Id:m.Id,Matrix:m.Matrix.inverse*transform)).ToArray();
            string signature=string.Join(";",relatives.Select(m=>m.Id+":"+string.Join(",",new[]{m.Matrix.m00,m.Matrix.m10,m.Matrix.m01,m.Matrix.m11,m.Matrix.m03,m.Matrix.m13}
                .Select(v=>Math.Round(v,5).ToString("R",CultureInfo.InvariantCulture)))));
            string key="s"+id+(masks.Count==0?"":"-m"+Hash(Encoding.UTF8.GetBytes(signature)).Substring(0,12));
            if(declarations.ContainsKey(key))return key;
            var original=Load(Path.Combine(rawFolder,id+".png"));Texture2D baked=null;byte[] bytes;
            try
            {
                if(masks.Count==0)bytes=File.ReadAllBytes(Path.Combine(rawFolder,id+".png"));
                else
                {
                    var pixels=original.GetPixels32();var bounds=xml.Bounds(id);
                    for(int y=0;y<original.height;y++)for(int x=0;x<original.width;x++)
                    {
                        int index=y*original.width+x;if(pixels[index].a==0)continue;
                        var local=new Vector3(bounds[0]+(x+.5f)*(bounds[2]-bounds[0])/original.width,bounds[3]-(y+.5f)*(bounds[3]-bounds[1])/original.height,0);
                        float alpha=pixels[index].a/255f;
                        foreach(var mask in relatives)
                        {
                            var image=Load(Path.Combine(rawFolder,mask.Id+".png"));var mb=xml.Bounds(mask.Id);var p=mask.Matrix.MultiplyPoint3x4(local);
                            float u=(p.x-mb[0])/(mb[2]-mb[0]),v=(mb[3]-p.y)/(mb[3]-mb[1]);
                            alpha*=u<0 || u>1 || v<0 || v>1?0:image.GetPixelBilinear(u,v).a;
                        }
                        pixels[index].a=(byte)Mathf.Clamp(Mathf.RoundToInt(alpha*255),0,255);
                    }
                    baked=new Texture2D(original.width,original.height,TextureFormat.RGBA32,false);baked.SetPixels32(pixels);baked.Apply();bytes=baked.EncodeToPNG();
                }
                string path=Folder+"/"+key+".png";WriteOwned(path,bytes,Owner);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=false;importer.mipmapEnabled=false;
                importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.npotScale=TextureImporterNPOTScale.None;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.isReadable=false;importer.sRGBTexture=true;
                importer.maxTextureSize=Math.Max(2048,Mathf.NextPowerOfTwo(Math.Max(original.width,original.height)));
                importer.SaveAndReimport();
                declarations.Add(key,new WearableSourceTexture {key=key,resource=Resource+key,shape=id.ToString(CultureInfo.InvariantCulture),sha256=Hash(bytes),
                    width=original.width,height=original.height,bounds=(float[])xml.Bounds(id).Clone()});
            }
            finally{if(baked!=null)Object.DestroyImmediate(baked);}
            return key;
        }
        public Texture2D ResourceTexture(string resource)=>Load("Assets/Remains/Resources/"+resource+".png");
        private Texture2D Load(string path)
        {
            path=Path.GetFullPath(path);if(loaded.TryGetValue(path,out var existing))return existing;
            if(!File.Exists(path))throw new InvalidDataException("Одежда: нет исходного PNG "+path);
            var value=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!value.LoadImage(File.ReadAllBytes(path))) {Object.DestroyImmediate(value);throw new InvalidDataException("Одежда: PNG не прочитан "+path);}
            value.wrapMode=TextureWrapMode.Clamp;loaded.Add(path,value);return value;
        }
        public static void WriteOwned(string path,byte[] bytes,string owner)
        {
            string hash=Hash(bytes);
            if(File.Exists(path))
            {
                string old=Hash(File.ReadAllBytes(path));if(old==hash)return;
                var prior=AssetImporter.GetAtPath(path);
                if(prior==null || prior.userData!=owner+old)throw new InvalidDataException("Одежда: изменённый/непринадлежащий генератору ресурс не заменён: "+path);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path)??throw new InvalidDataException("Одежда: нет импортёра "+path);
            importer.userData=owner+hash;importer.SaveAndReimport();
        }
        public static string Hash(byte[] bytes){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public void Dispose(){foreach(var image in loaded.Values)Object.DestroyImmediate(image);loaded.Clear();}
    }
}
