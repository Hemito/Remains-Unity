using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // A rest-node library and a single suit. Each symbol mesh is constructed once, never once per animation frame.
    public sealed class PonyRigPilotArt:IDisposable
    {
        public sealed class Piece
        {
            public Mesh Mesh;public Material Material;public Color Tint;public string Slot;public int[] Order;
            public bool BodyVisibleWhenWorn;public string Name;
        }
        public sealed class Bundle {public Piece[] Body,Worn;public string Socket,Library;}
        public readonly Dictionary<string,Bundle> Bundles=new(StringComparer.Ordinal);
        private readonly List<Object> owned=new();
        public int MeshCount {get;private set;}
        public int WearableTemplateCount {get;private set;}
        public float RestError {get;private set;}
        public float WearableRestError {get;private set;}
        private readonly Dictionary<string,Material> materials=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Matrix4x4> ownRest=new(StringComparer.Ordinal);
        private readonly WearableSourceBank bank;
        private readonly Dictionary<string,WearableSourceTemplate> templates;
        private readonly Dictionary<string,WearableSourceTexture> textures;
        public const string Outfit="pip";

        public PonyRigPilotArt(GodotRigPilotData rig)
        {
            PonyAnimationData.Load();PonyAppearance.Prepare();
            var compiled=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>("Assets/Remains/Resources/Original/Pony/render.asset")??throw new InvalidDataException("Нет compiled pony render.");
            var compiledWear=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>("Assets/Remains/Resources/Original/Pony/Wearables/"+Outfit+"-render.asset")??throw new InvalidDataException("Нет compiled wearable для проверки регистрации.");
            var referenceWear=compiledWear.Clips.Single(c=>c.id=="idle").frames[0].parts.ToDictionary(p=>p.sourcePath,StringComparer.Ordinal);
            bank=StateJson.FromJson<WearableSourceBank>(GameAssets.Load<TextAsset>("Original/Pony/Wearables/"+Outfit).text);
            templates=bank.templates.ToDictionary(t=>t.id,StringComparer.Ordinal);textures=bank.textures.ToDictionary(t=>t.key,StringComparer.Ordinal);
            WearableTemplateCount=templates.Count;
            var rest=bank.clips.Single(c=>c.id=="idle").frames[0];
            foreach(var group in Groups(PonyAnimationData.Library.clips.Single(c=>c.id=="idle").frames[0]))
            {
                var instance=rest.instances.SingleOrDefault(i=>i.basePath==group.Top);
                if(instance!=null)ownRest.Add(group.Socket,Affine(instance.matrix));
            }
            var needed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var socket in GodotRigPilotData.SocketBones)
            {
                int bone=rig.Indices[socket.Value];
                foreach(var clip in rig.Clips.Values)foreach(var frame in clip.Frames)needed.Add(socket.Key+"/"+ArtworkLibrary(socket.Key,frame.Libraries[bone]));
            }
            foreach(var clip in PonyAnimationData.Library.clips)
            {
                if(Bundles.Count==needed.Count)break;
                var meshes=compiled.Clips.Single(c=>c.id==clip.id);var wear=bank.clips.Single(c=>c.id==clip.id);
                for(int f=0;f<clip.frames.Length && Bundles.Count<needed.Count;f++)foreach(var group in Groups(clip.frames[f]))
                {
                    string donorBone=GodotRigPilotData.SocketBones[group.Socket];
                    string lib=Library(group.Socket,group.Indices.Select(i=>clip.frames[f].parts[i]),ArtworkLibrary(group.Socket,rig.RestLibraries[donorBone]));
                    string key=group.Socket+"/"+lib;if(!needed.Contains(key) || Bundles.ContainsKey(key))continue;
                    var state=wear.frames[f];var instances=state.instances.Where(i=>i.basePath==group.Top).ToArray();
                    var hidden=new HashSet<int>(state.hiddenBasePartIndices);
                    foreach(var instance in state.instances)foreach(int index in instance.basePartIndices)hidden.Add(index);
                    Matrix4x4 registration=Matrix4x4.identity;
                    if(ownRest.TryGetValue(group.Socket,out var outerRest))
                    {
                        if(instances.Length!=1)throw new InvalidDataException($"Пилот: нет единственного symbol placement {clip.id}/{f}/{group.Socket}.");
                        registration=outerRest*Affine(instances[0].matrix).inverse;
                    }
                    else if(clip.id!="idle" || f!=0)throw new InvalidDataException("Пилот: новый рисунок требует регистрации "+key);
                    var body=new List<Piece>();var worn=new List<Piece>();
                    foreach(int index in group.Indices)
                    {
                        var part=meshes.frames[f].parts[index];if(!part.visible || !PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,1,1,false))continue;
                        var mesh=Clone(part.mesh,registration);var order=state.baseDepthPaths[index];
                        var piece=new Piece {Mesh=mesh,Material=part.material,Tint=PonyAppearance.Tint(part.slot,1),Slot=part.slot,Order=order,
                            BodyVisibleWhenWorn=!hidden.Contains(index),Name=part.sourcePath};body.Add(piece);
                        if(clip.id=="idle")for(int p=0;p<mesh.vertexCount;p++)RestError=Math.Max(RestError,(mesh.vertices[p]-part.mesh.vertices[p]).magnitude);
                    }
                    foreach(var instance in instances)
                    {
                        var template=templates[instance.templateId];var outer=registration*Affine(instance.matrix);
                        foreach(var part in template.parts)
                        {
                            if(!PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,1,1,false))continue;
                            var declared=new PonyPart {bounds=part.bounds,matrix=part.matrix};
                            var color=ColorOf(instance.color)*ColorOf(part.color);var vertices=PonyAnimationData.Quad(declared);
                            var mesh=new Mesh {name="Узел костюма "+group.Socket+"/"+part.shape,vertices=vertices,
                                uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},triangles=new[]{0,2,1,2,3,1},colors=new[]{color,color,color,color}};
                            mesh.RecalculateBounds();owned.Add(mesh);MeshCount++;
                            // Bake only the local registration once; animation changes a single draw matrix per node.
                            var registered=Clone(mesh,outer);owned.Remove(mesh);Object.DestroyImmediate(mesh);MeshCount--;
                            string sourcePath=instance.basePath+"/wear:"+template.id+"/"+part.path;
                            if(clip.id=="idle")
                            {
                                if(!referenceWear.TryGetValue(sourcePath,out var reference))throw new InvalidDataException("Пилот: нет контрольной детали костюма "+sourcePath);
                                for(int p=0;p<registered.vertexCount;p++)WearableRestError=Math.Max(WearableRestError,(registered.vertices[p]-reference.mesh.vertices[p]).magnitude);
                            }
                            worn.Add(new Piece {Mesh=registered,Material=Material(part.texture),Tint=PonyAppearance.Tint(part.slot,1),Slot=part.slot,
                                Order=instance.depthPath.Concat(part.depthPath).ToArray(),Name=sourcePath});
                        }
                    }
                    Bundles.Add(key,new Bundle {Body=body.ToArray(),Worn=worn.ToArray(),Socket=group.Socket,Library=lib});
                }
            }
            foreach(string key in needed)if(!Bundles.ContainsKey(key))throw new InvalidDataException("Пилот: нет собственного artwork для "+key);
            if(RestError>1e-5f)throw new InvalidDataException("Пилот: нарушена rest регистрация: "+RestError);
            if(WearableRestError>1e-5f)throw new InvalidDataException("Пилот: костюм не совпал с существующим компилятором: "+WearableRestError);
        }
        public static string ArtworkLibrary(string socket,string imported)=>socket.StartsWith("pipbuck",StringComparison.Ordinal)?"pipbuck":imported;
        private Mesh Clone(Mesh source,Matrix4x4 transform)
        {
            var mesh=Object.Instantiate(source);mesh.name="Rig-local "+source.name;
            mesh.vertices=source.vertices.Select(transform.MultiplyPoint3x4).ToArray();mesh.RecalculateBounds();owned.Add(mesh);MeshCount++;return mesh;
        }
        private Material Material(string key)
        {
            if(materials.TryGetValue(key,out var found))return found;
            if(!textures.TryGetValue(key,out var declaration))throw new InvalidDataException("Пилот: нет wearable texture "+key);
            var texture=GameAssets.Load<Texture2D>(declaration.resource)??throw new InvalidDataException("Пилот: не загружена "+declaration.resource);
            var material=WorldArtworkMaterial.Create(texture);owned.Add(material);materials.Add(key,material);return material;
        }
        private static Color ColorOf(float[] c)=>new(c[0],c[1],c[2],c[3]);
        private static Matrix4x4 Affine(float[] m)
        {
            if(m==null || m.Length!=6 || m.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Пилот: неверная own affine matrix.");
            var result=Matrix4x4.identity;result.m00=m[0];result.m10=-m[1];result.m01=-m[2];result.m11=m[3];result.m03=m[4]/1000;result.m13=-m[5]/1000;
            if(Math.Abs(result.determinant)<1e-12)throw new InvalidDataException("Пилот: вырожденная own matrix.");return result;
        }
        private static string Library(string socket,IEnumerable<PonyPart> parts,string normal)
        {
            if(socket.StartsWith("frontLower",StringComparison.Ordinal))return parts.Any(p=>p.shape=="102")?"fleg3":"fleg3a";
            if(socket.StartsWith("hindLower",StringComparison.Ordinal))return parts.Any(p=>p.shape=="298")?"sleg3":"sleg3a";
            return normal;
        }
        private sealed class Group {public string Top,Socket;public int[] Indices;}
        private static IEnumerable<Group> Groups(PonyFrame frame)
        {
            var paired=new HashSet<string>(StringComparer.Ordinal);
            foreach(var grouping in frame.parts.Select((p,i)=>(Part:p,Index:i)).GroupBy(p=>p.Part.path.Split('/')[0]).OrderBy(g=>g.Min(p=>p.Part.drawOrder)))
            {
                string role=grouping.Key;
                if(role is not ("head" or "mane" or "tail"))
                {
                    var shapes=new HashSet<string>(grouping.Where(p=>p.Part.hairStyle<=1 && p.Part.eyeStyle<=1).Select(p=>p.Part.shape),StringComparer.Ordinal);
                    role=shapes.SetEquals(new[]{"137"})?"torso":shapes.SetEquals(new[]{"160"})?"neck":shapes.SetEquals(new[]{"670"})?"pipbuck":
                        shapes.SetEquals(new[]{"25","27"})?"hindUpper":shapes.SetEquals(new[]{"3"}) || shapes.SetEquals(new[]{"298"})?"hindLower":
                        shapes.SetEquals(new[]{"56"})?"frontUpper":shapes.SetEquals(new[]{"79"})?"frontMiddle":
                        shapes.SetEquals(new[]{"275"}) || shapes.SetEquals(new[]{"102"})?"frontLower":throw new InvalidDataException("Пилот: неизвестный узел "+grouping.Key+": "+string.Join(",",shapes));
                    if(role is not ("torso" or "neck"))role+=paired.Add(role)?"Far":"Near";
                }
                yield return new Group {Top=grouping.Key,Socket=role,Indices=grouping.Select(p=>p.Index).ToArray()};
            }
        }
        public void Dispose(){foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}
    }
}
