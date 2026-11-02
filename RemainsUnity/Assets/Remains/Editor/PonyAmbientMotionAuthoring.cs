using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Takes only the original free1/free2/free3 node motion, not the study export's unmasked textures. Every part
    // must remain the same rigid inner drawing as idle/0; otherwise reusing the idle meshes would be incorrect.
    public static class PonyAmbientMotionAuthoring
    {
        public const string AssetPath="Assets/Remains/Resources/Original/Pony/ambient-motion.asset";
        private const float GeometryTolerance=.00005f;

        [MenuItem("Remains/Пони/Подготовить движения ожидания")]
        public static void Build()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/original/pony-extra/pony-extra.json"));
            if(!File.Exists(path))throw new InvalidDataException("Ожидание пони: нет исходных дополнительных клипов "+path+".");
            byte[] source=File.ReadAllBytes(path);
            var extra=JsonUtility.FromJson<PonyLibrary>(Encoding.UTF8.GetString(source));
            var baseline=JsonUtility.FromJson<PonyLibrary>((GameAssets.Load<TextAsset>("Original/Pony/pony")??
                throw new InvalidDataException("Ожидание пони: нет исходного банка pony.json.")).text);
            var idle=baseline?.clips?.SingleOrDefault(c=>c.id=="idle")?.frames?.FirstOrDefault()??
                throw new InvalidDataException("Ожидание пони: нет исходной стойки idle/0.");
            if(extra?.clips==null)throw new InvalidDataException("Ожидание пони: нет списка клипов в "+path+".");
            var rest=PonyRigPilotArt.Groups(idle).ToArray();RequireSockets(rest,"idle/0");
            var result=new List<PonyAmbientMotionClip>();float maximumGeometryError=0,maximumMatrixError=0;
            foreach(string id in new[]{"free1","free2","free3"})
            {
                var clip=extra.clips.SingleOrDefault(c=>c.id=="x_"+id)??throw new InvalidDataException($"Ожидание пони: нет x_{id} в {path}.");
                if(clip.frames==null || clip.frames.Length<2 || !float.IsFinite(clip.fps) || clip.fps<=0)
                    throw new InvalidDataException("Ожидание пони: неполный исходный клип "+clip.id+".");
                var frames=new PonyAmbientMotionFrame[clip.frames.Length];
                for(int f=0;f<frames.Length;f++)
                {
                    string context=clip.id+"/"+f;var frame=clip.frames[f];
                    if(frame?.parts==null)throw new InvalidDataException("Ожидание пони: нет частей "+context+".");
                    var groups=PonyRigPilotArt.Groups(frame).ToArray();RequireSockets(groups,context);
                    var current=groups.ToDictionary(g=>g.Socket,StringComparer.Ordinal);
                    var nodes=new PonyAmbientMotionNode[rest.Length];
                    for(int n=0;n<rest.Length;n++)
                    {
                        var reference=rest[n];var group=current[reference.Socket];string location=context+"/"+reference.Socket;
                        var before=Parts(idle,reference);var after=Parts(frame,group);
                        if(before.Length!=after.Length || !before.Select(Key).SequenceEqual(after.Select(Key)))
                            throw new InvalidDataException("Ожидание пони: меняется состав рисунка, требуется отдельная библиотека символов: "+location+".");
                        // Registration by a stable local part, independent of the outer timeline's layer numbers.
                        var delta=PonyRigPilotArt.Affine(after[0].matrix)*PonyRigPilotArt.Affine(before[0].matrix).inverse;
                        for(int p=0;p<before.Length;p++)
                        {
                            var a=before[p];var b=after[p];
                            if(a.slot!=b.slot || !a.color.SequenceEqual(b.color) || !a.bounds.SequenceEqual(b.bounds))
                                throw new InvalidDataException($"Ожидание пони: меняется рисунок/цвет {location}/{Key(b)}.");
                            var predicted=PonyAnimationData.Quad(a);var expected=PonyAnimationData.Quad(b);
                            for(int v=0;v<predicted.Length;v++)
                            {
                                float error=Vector3.Distance(delta.MultiplyPoint3x4(predicted[v]),expected[v]);
                                maximumGeometryError=Math.Max(maximumGeometryError,error);
                                if(error>GeometryTolerance)throw new InvalidDataException($"Ожидание пони: внутренний рисунок меняется отдельно от гнезда {location}/{Key(b)}, vertex={v}, error={error:G6} м.");
                            }
                        }
                        var transform=PonyRigTransform.FromMatrix(delta);var rebuilt=transform.Matrix;
                        for(int k=0;k<16;k++)maximumMatrixError=Math.Max(maximumMatrixError,Math.Abs(rebuilt[k]-delta[k]));
                        if(maximumMatrixError>GeometryTolerance)throw new InvalidDataException($"Ожидание пони: потеря affine/skew {location}, error={maximumMatrixError:G6}.");
                        nodes[n]=new PonyAmbientMotionNode {socket=reference.Socket,transform=transform};
                    }
                    frames[f]=new PonyAmbientMotionFrame {nodes=nodes};
                }
                result.Add(new PonyAmbientMotionClip {id=id,fps=clip.fps,frames=frames});
            }
            string baselineHash=PonyRenderAsset.SourceHash();
            using var hash=SHA256.Create();string sourceHash=Convert.ToBase64String(hash.ComputeHash(source));
            var candidate=ScriptableObject.CreateInstance<PonyAmbientMotionAsset>();
            candidate.Configure(sourceHash,baselineHash,result.ToArray());
            try
            {
                candidate.Validate();var asset=AssetDatabase.LoadAssetAtPath<PonyAmbientMotionAsset>(AssetPath);
                if(asset==null){AssetDatabase.CreateAsset(candidate,AssetPath);asset=candidate;candidate=null;}
                else EditorUtility.CopySerialized(candidate,asset);
                EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            }
            finally{if(candidate!=null)UnityEngine.Object.DestroyImmediate(candidate);}
            Debug.Log($"Ожидание пони: 3 клипа, кадров={result.Sum(c=>c.frames.Length)}, 17 гнёзд; внутренняя геометрия={maximumGeometryError:G6} м, affine={maximumMatrixError:G6}. Общие meshes, маски и снаряжение idle/0 сохранены.");
        }

        public static void BuildBatch()
        {
            try{Build();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static PonyPart[] Parts(PonyFrame frame,PonyRigPilotArt.Group group)=>group.Indices.Select(i=>frame.parts[i]).OrderBy(Key,StringComparer.Ordinal).ToArray();
        private static string Key(PonyPart part)=>PonySymbolLayout.LocalPath(part.path)+":"+part.shape+":"+part.hairStyle+":"+part.eyeStyle;
        private static void RequireSockets(PonyRigPilotArt.Group[] groups,string context)
        {
            var sockets=groups.Select(g=>g.Socket).ToArray();
            if(sockets.Length!=PonyAmbientMotionAsset.Sockets.Length || sockets.Distinct(StringComparer.Ordinal).Count()!=sockets.Length ||
                sockets.Any(s=>Array.IndexOf(PonyAmbientMotionAsset.Sockets,s)<0))
                throw new InvalidDataException($"Ожидание пони: {context}, требуется 17 известных уникальных гнёзд; получены {string.Join(",",sockets)}.");
        }
    }
}
