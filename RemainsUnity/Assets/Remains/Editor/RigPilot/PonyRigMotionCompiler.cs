using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Serialization;
using Remains.Presentation;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyRigMotionCompiler
    {
        private const string PathAsset="Assets/Remains/Resources/Original/Pony/rig-motion.asset";
        [MenuItem("Remains/Собрать игровые движения куклы")]
        public static void Build()
        {
            PonyAnimationData.Load();
            var rig=new GodotRigPilotData(Path.GetFullPath("../.reference/rig-pilot/source"),includeRise:true);
            var bank=StateJson.FromJson<WearableSourceBank>(GameAssets.Load<TextAsset>("Original/Pony/Wearables/pip").text);
            var own=PonyAnimationData.Library.clips;var idle=own.Single(c=>c.id=="idle").frames[0];
            var restGroups=PonyRigPilotArt.Groups(idle).ToDictionary(g=>g.Socket,StringComparer.Ordinal);
            var idleWear=bank.clips.Single(c=>c.id=="idle").frames[0];
            var result=new List<PonyRigClip>();float maxError=0;int imported=0,libraryDifferences=0;
            foreach(var clip in own)
            {
                var wear=bank.clips.Single(c=>c.id==clip.id);
                var output=new PonyRigClip {id=clip.id,source=Source(clip.id),frames=new PonyRigFrame[clip.frames.Length]};
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var groups=PonyRigPilotArt.Groups(clip.frames[f]).ToArray();var nodes=new List<PonyRigNode>();
                    var donor=Sample(rig,clip.id,f,clip.frames.Length);
                    if(donor!=null)imported++;
                    foreach(var group in groups)
                    {
                        var delta=Matrix4x4.identity;
                        if(donor!=null)
                        {
                            int bone=rig.Indices[GodotRigPilotData.SocketBones[group.Socket]];
                            var reference=restGroups[group.Socket];
                            var current=wear.frames[f].instances.SingleOrDefault(i=>i.basePath==group.Top);
                            var rest=idleWear.instances.SingleOrDefault(i=>i.basePath==reference.Top);
                            Matrix4x4 registration;
                            if(current!=null && rest!=null)registration=PonyRigPilotArt.Affine(rest.matrix)*PonyRigPilotArt.Affine(current.matrix).inverse;
                            else
                            {
                                // Hair, face and tail preserve their current inner playhead; cancel only the shared outer motion.
                                var anchor=reference.Indices.Select(i=>idle.parts[i]).First(p=>p.hairStyle<=1 && p.eyeStyle<=1 &&
                                    group.Indices.Any(i=>SameLocalPath(clip.frames[f].parts[i],p)));
                                var part=group.Indices.Select(i=>clip.frames[f].parts[i]).Single(p=>SameLocalPath(p,anchor));
                                registration=PonyRigPilotArt.Affine(anchor.matrix)*PonyRigPilotArt.Affine(part.matrix).inverse;
                            }
                            delta=donor.Delta[bone]*registration;
                            bool front=group.Socket.StartsWith("frontLower",StringComparison.Ordinal),hind=group.Socket.StartsWith("hindLower",StringComparison.Ordinal);
                            if(front || hind)
                            {
                                bool alternate=group.Indices.Any(i=>clip.frames[f].parts[i].shape==(front?"102":"298"));
                                if(alternate!=(donor.Libraries[bone]==(front?"fleg3":"sleg3")))
                                {libraryDifferences++;Debug.Log($"Выбор символа 1.04 сохранён: {clip.id}/{f}/{group.Socket}; source={donor.Libraries[bone]}, alternate={alternate}.");}
                            }
                        }
                        var transform=PonyRigTransform.FromMatrix(delta);var rebuilt=transform.Matrix;
                        for(int i=0;i<16;i++)maxError=Math.Max(maxError,Math.Abs(rebuilt[i]-delta[i]));
                        nodes.Add(new PonyRigNode {top=group.Top,socket=group.Socket,transform=transform});
                    }
                    output.frames[f]=new PonyRigFrame {nodes=nodes.ToArray()};
                }
                result.Add(output);
            }
            if(maxError>.00005f)throw new InvalidDataException("Игровая кукла теряет affine/skew: "+maxError);
            var asset=AssetDatabase.LoadAssetAtPath<PonyRigMotionAsset>(PathAsset);
            if(asset==null){asset=ScriptableObject.CreateInstance<PonyRigMotionAsset>();AssetDatabase.CreateAsset(asset,PathAsset);}
            asset.Configure(PonyRenderAsset.SourceHash(),GodotRigPilotData.SourceCommit,result.ToArray());asset.Validate();
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();PonyRigDrawPlan.Reset();
            AuthorPrefab();
            Debug.Log($"Игровая кукла: поз={result.Count}, кадров={result.Sum(c=>c.frames.Length)}, импортированных кадров={imported}, узлов=17, affine error={maxError:G6}, различий symbol selection={libraryDifferences}. Выбор рисунков, вложенные playheads и порядок частей остаются из нашего полного банка 1.04.");
        }
        private static void AuthorPrefab()
        {
            ActorTypes.Load();
            if(!EntityAssetCatalog.Load().TryGetActorPrefab(ActorTypes.Get("pony"),out var prefab))throw new InvalidDataException("Нет префаба пони для игровых гнёзд.");
            string path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var data=new SerializedObject(root.GetComponent<PonyVisual>());var outer=data.FindProperty("rigOuter");var inner=data.FindProperty("rigInner");
                outer.arraySize=inner.arraySize=17;
                for(int n=0;n<17;n++)
                {
                    var a=(Transform)outer.GetArrayElementAtIndex(n).objectReferenceValue;
                    var b=(Transform)inner.GetArrayElementAtIndex(n).objectReferenceValue;
                    if(a==null){a=new GameObject("Гнездо куклы "+n).transform;a.SetParent(root.transform,false);}
                    if(b==null){b=new GameObject("Наклон гнезда").transform;b.SetParent(a,false);}
                    outer.GetArrayElementAtIndex(n).objectReferenceValue=a;inner.GetArrayElementAtIndex(n).objectReferenceValue=b;
                }
                // The saved pool already lives below sockets, so the first Draw does not change the root structure.
                var filters=data.FindProperty("filters");var initial=(Transform)inner.GetArrayElementAtIndex(0).objectReferenceValue;
                for(int p=0;p<filters.arraySize;p++)
                {
                    var filter=(MeshFilter)filters.GetArrayElementAtIndex(p).objectReferenceValue;
                    if(filter==null)throw new InvalidDataException("Префаб куклы: повреждён pool parts "+p);
                    if(filter.transform.parent==root.transform)filter.transform.SetParent(initial,false);
                }
                data.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        private static bool SameLocalPath(PonyPart a,PonyPart b)=>a.shape==b.shape &&
            a.path.Substring(a.path.IndexOf('/'))==b.path.Substring(b.path.IndexOf('/'));
        private static string Source(string id)=>id switch
        {
            "idle"=>"body_stay","walk"=>"body_trot","crouch"=>"body_sit","crouchLower"=>"body_sit_down",
            "crouchRise"=>"body_sit_up","climb"=>"body_laz",
            _ when id.StartsWith("edge",StringComparison.Ordinal)=>"body_shield",
            _=>"own-1.04"
        };
        private static GodotRigPilotData.Frame Sample(GodotRigPilotData rig,string id,int frame,int count)
        {
            string key=id switch {"idle"=>"body_stay","walk"=>"body_trot","crouch"=>"body_sit", "crouchLower"=>"body_sit_down","crouchRise"=>"body_sit_up",_=>null};
            if(id=="climb")return frame==0?rig.Clips["ladder_hold"].Frames[0]:rig.Clips["ladder_up"].Frames[frame-1];
            if(id.StartsWith("edge",StringComparison.Ordinal))
            {
                string section=id.StartsWith("edgeStandFront",StringComparison.Ordinal)?"edge_stand_front":id.StartsWith("edgeStandBack",StringComparison.Ordinal)?"edge_stand_back":
                    id.StartsWith("edgeCrouchFront",StringComparison.Ordinal)?"edge_crouch_front":"edge_crouch_back";
                return rig.Clips[section].Frames[int.Parse(id.Substring(id.Length-2))];
            }
            if(key==null)return null;
            var frames=rig.Clips[key].Frames;
            // The complete 1.04 bank has explicit rest endpoints in addition to the source timeline.
            if(id=="walk")return frame==0?null:frames[frame-1]; // Own anticipatory intro, then the exact 16-key cycle.
            if(id=="crouchLower")return frame==count-1?rig.Clips["body_sit"].Frames[0]:frames[frame];
            if(id=="crouchRise")return frame==count-1?rig.Clips["body_stay"].Frames[0]:frames[frame];
            return frames[0];
        }
        public static void RunBatch()
        {
            try{Build();PonyRigRuntimeChecks.Run();EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void BuildPlayer()
        {Build();PonyRigRuntimeChecks.Run();PonyAppearanceChecks.Run();PonyPrefabChecks.Run();PrototypeBuild.Build();}
    }
}
