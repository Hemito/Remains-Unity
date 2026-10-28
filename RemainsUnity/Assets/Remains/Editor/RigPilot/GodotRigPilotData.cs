using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Editor-only adapter of exported data, not a Godot/Flash runtime. Source remains outside Assets.
    // Samples authored frame keys; does not execute source events or silently substitute missing libraries.
    public sealed class GodotRigPilotData
    {
        public const string SourceCommit="0a04b34314352d52951590c09674f2c28b15008c";
        public sealed class Frame {public Matrix4x4[] Delta;public string[] Libraries;public float[] Brightness;public bool CoverInFront;}
        public sealed class Clip {public string Id;public bool Loop;public Frame[] Frames;public string[] Order;public string Context="plain";}
        public static readonly string[] SourceIds={"body_stay","body_trot","body_sit","body_sit_down","body_laz","body_shield",
            "body_lurk1","body_lurk1_un","body_lurk2","body_lurk2_un","body_lurk3","body_lurk3_un"};
        public readonly Dictionary<string,Clip> Clips=new(StringComparer.Ordinal);
        public readonly string[] Bones;
        public readonly Dictionary<string,int> Indices=new(StringComparer.Ordinal);
        public readonly Matrix4x4[] Rest;
        public readonly Dictionary<string,string> RestLibraries=new(StringComparer.Ordinal);
        public readonly float RootScale;
        private readonly float[] restBrightness;
        public int SkewedKeys {get;private set;}
        public float HierarchyMatrixMaxError {get;private set;}
        public int InterpolatedSamples {get;private set;}

        // Names are semantic source bindings, not guesses based on the left/right suffix of a mesh.
        public static readonly Dictionary<string,string> SocketBones=new(StringComparer.Ordinal)
        {
            {"torso","korpus"},{"neck","neck"},{"head","head"},{"mane","mane"},{"tail","tail"},
            {"hindUpperFar","sleg1_l"},{"hindLowerFar","sleg3a_l"},{"hindUpperNear","sleg1_r"},{"hindLowerNear","sleg3a_r"},
            {"frontUpperFar","fleg1_l"},{"frontMiddleFar","fleg2_l"},{"frontLowerFar","flleg3"},
            {"frontUpperNear","fleg1_r"},{"frontMiddleNear","fleg2_r"},{"frontLowerNear","frleg3"},
            // Source player_rig.gd synchronises PipBuck at runtime; static pip timeline keys do not follow the legs.
            {"pipbuckNear","frleg3"},{"pipbuckFar","flleg3"}
        };

        public GodotRigPilotData(string folder)
        {
            var index=Read(folder,"index.json");
            var hierarchy=Read(folder,"hierarchy.json");RootScale=Required(hierarchy,"scale");
            if(RootScale<=0)throw new InvalidDataException("Пилот rig: неположительный root scale.");
            var rest=Read(folder,"body_stay.json");var restBones=(JObject)rest["bones"];
            Bones=SocketBones.Values.Distinct(StringComparer.Ordinal).ToArray();Rest=new Matrix4x4[Bones.Length];restBrightness=new float[Bones.Length];
            for(int i=0;i<Bones.Length;i++)
            {
                Indices.Add(Bones[i],i);var key=Sample(restBones,Bones[i],0);
                Rest[i]=Placement(key);RestLibraries.Add(Bones[i],(string)key["lib"]??throw new InvalidDataException("Пилот rig: нет lib у "+Bones[i]));
                restBrightness[i]=Required(key,"br");
                // Independent affine golden: full raw XFL matrices from hierarchy, before donor decomposition.
                var raw=hierarchy["bones"].Single(b=>(string)b["node_name"]==Bones[i])["matrix"];
                var expected=Matrix4x4.identity;
                expected.m00=Required(raw,"a")*RootScale;expected.m10=-Required(raw,"b")*RootScale;
                expected.m01=-Required(raw,"c")*RootScale;expected.m11=Required(raw,"d")*RootScale;
                expected.m03=Required(raw,"tx")*RootScale/50;expected.m13=-Required(raw,"ty")*RootScale/50;
                for(int p=0;p<16;p++)HierarchyMatrixMaxError=Math.Max(HierarchyMatrixMaxError,Math.Abs(expected[p]-Rest[i][p]));
            }
            if(HierarchyMatrixMaxError>.00005f)throw new InvalidDataException("Пилот rig: разложенные матрицы расходятся с raw hierarchy: "+HierarchyMatrixMaxError);
            foreach(string id in SourceIds)
            {
                var data=Read(folder,id+".json");var tracks=(JObject)data["bones"];
                int count=checked((int)data["frame_end"]-(int)data["frame_start"]+1);
                if(count<1 || count>200)throw new InvalidDataException("Пилот rig: недопустимая длина "+id);
                var order=(index["precomputed"]?["bone_orders"]?[id] as JArray)?.Values<string>().ToArray()??
                    throw new InvalidDataException("Пилот rig: нет порядка частей "+id);
                foreach(string bone in Bones)if(!order.Contains(bone,StringComparer.Ordinal))throw new InvalidDataException("Пилот rig: узел отсутствует в порядке "+id+"/"+bone);
                var clip=new Clip {Id=id,Loop=(string)data["loop_mode"]=="linear",Order=order,Frames=new Frame[count]};
                foreach(var track in tracks.Properties())foreach(var key in (JArray)track.Value["keys"])
                    if(Math.Abs((double?)key["skew"]??0)>.00001)SkewedKeys++;
                for(int frame=0;frame<count;frame++)
                {
                    var delta=new Matrix4x4[Bones.Length];var libraries=new string[Bones.Length];var brightness=new float[Bones.Length];
                    for(int i=0;i<Bones.Length;i++)
                    {
                        var key=Sample(tracks,Bones[i],frame/30d);libraries[i]=(string)key["lib"];
                        bool lowerFront=Bones[i] is "flleg3" or "frleg3",lowerHind=Bones[i].StartsWith("sleg3a",StringComparison.Ordinal);
                        if(libraries[i]!=RestLibraries[Bones[i]] && !(lowerFront && libraries[i] is "fleg3" or "fleg3a") && !(lowerHind && libraries[i] is "sleg3" or "sleg3a"))
                            throw new InvalidDataException($"Пилот rig: неизвестная смена рисунка {id}/{frame}/{Bones[i]}: {RestLibraries[Bones[i]]} → {libraries[i]}.");
                        delta[i]=Placement(key)*Rest[i].inverse;
                        float br=Required(key,"br");
                        if(br>0 || br< -1 || restBrightness[i]<= -1)throw new InvalidDataException("Пилот rig: нужен адаптер положительного brightness/вырожденного rest для "+Bones[i]);
                        brightness[i]=(1+br)/(1+restBrightness[i]);
                    }
                    clip.Frames[frame]=new Frame {Delta=delta,Libraries=libraries,Brightness=brightness};
                }
                Clips.Add(id,clip);
            }
            PonyRigPilotSets.Expand(Clips);
        }
        private Matrix4x4 Placement(JToken key)
        {
            double rotation=Required(key,"rot"),skew=Required(key,"skew"),sx=Required(key,"sx"),sy=Required(key,"sy");
            var m=Matrix4x4.identity;
            // F * (rootScale * FlashPixelMatrix) * F^-1, pixels/50 metres and one Y flip.
            m.m00=(float)(Math.Cos(rotation)*sx*RootScale);m.m10=(float)(-Math.Sin(rotation)*sx*RootScale);
            m.m01=(float)(Math.Sin(rotation+skew)*sy*RootScale);m.m11=(float)(Math.Cos(rotation+skew)*sy*RootScale);
            m.m03=Required(key,"px")*RootScale/50;m.m13=-Required(key,"py")*RootScale/50;
            if(Math.Abs(m.m00*m.m11-m.m01*m.m10)<1e-10)throw new InvalidDataException("Пилот rig: вырожденная матрица.");
            return m;
        }
        private static JObject Read(string root,string name)=>JObject.Parse(File.ReadAllText(Path.Combine(root,name)));
        private static float Required(JToken value,string key)
        {
            var number=(float?)value[key];
            if(number==null || !float.IsFinite(number.Value))throw new InvalidDataException("Пилот rig: нет конечного числа "+key);
            return number.Value;
        }
        private JToken Sample(JObject tracks,string bone,double time)
        {
            var keys=tracks[bone]?["keys"] as JArray??throw new InvalidDataException("Пилот rig: нет keys у "+bone);
            JToken selected=null,next=null;double before=-1;
            foreach(var key in keys)
            {
                double at=Required(key,"time");if(at<before)throw new InvalidDataException("Пилот rig: keys вне порядка "+bone);before=at;
                if(at<=time+.000001)selected=key;else if(next==null)next=key;
            }
            if(selected==null)throw new InvalidDataException("Пилот rig: нет первого ключа "+bone);
            if(next==null || !((bool?)selected["tween"]??false))return selected;
            if(((int?)selected["accel"]??0)!=0)throw new InvalidDataException("Пилот rig: easing требует отдельного адаптера: "+bone);
            double blend=(time-Required(selected,"time"))/(Required(next,"time")-Required(selected,"time"));
            if(blend<=.000001)return selected;
            var mixed=selected.DeepClone();
            // Rotation is authored unwrapped (may exceed a full turn). Never shortest-angle interpolation.
            foreach(string name in new[]{"px","py","rot","sx","sy","skew","br"})
                mixed[name]=Required(selected,name)+(Required(next,name)-Required(selected,name))*Math.Min(1,blend);
            InterpolatedSamples++;return mixed;
        }
    }
}
